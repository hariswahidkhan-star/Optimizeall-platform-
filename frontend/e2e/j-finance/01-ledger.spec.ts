import { readFileSync } from 'node:fs';
import type { Page } from '@playwright/test';
import {
  ApiSession,
  FINANCE_LANDING,
  accounts,
  confirmBox,
  expect,
  localMinute,
  modal,
  money,
  pickUser,
  raw,
  round,
  share,
  state,
  test,
  toast,
  watchErrors,
} from './support/finance';

/**
 * Finance journey, part 1 — the ledger and its four-eyes controls:
 *   finance 1 changes the payout schedule (no earning hold, 10 USD minimum) → adds two exchange rates (a direct
 *   PKR→USD rate and a newer inverse USD→AED rate) → credits Ana 25.00 USD, 2,801 PKR and 100 AED and debits 5.25 USD
 *   through the ledger UI (the FX conversions are checked to the cent, and the rate stored with each entry) → is
 *   refused an adjustment on her own account → a credit whose response is lost is retried from the same dialog and
 *   recorded once (idempotency key) → credits stay "Pending approval" and finance 1 cannot approve her own → finance 2
 *   approves them → Ana's balance buckets and the ledger CSV show exactly the expected amounts.
 */
test.describe.configure({ mode: 'serial' });

const s = () => state();

/** Opens Ledger → New adjustment and fills it (the dialog is left open for the caller to submit). */
async function fillAdjustment(page: Page, userId: string, amount: string, currency: string, reason: string) {
  await page.getByRole('button', { name: 'New adjustment' }).click();
  const dialog = modal(page, 'New adjustment');
  await pickUser(dialog, userId);
  await dialog.getByLabel('Amount', { exact: true }).fill(amount);
  await dialog.getByLabel('Currency').selectOption(currency);
  await dialog.getByLabel('Reason').fill(reason);
  await confirmBox(dialog, 'I confirm this adjustment is correct and authorised.');
  return dialog;
}

interface LedgerRow {
  id: string;
  type: string;
  status: string;
  originalAmount: number;
  originalCurrency: string;
  exchangeRate: number;
  settlementAmount: number;
  settlementCurrency: string;
  availableAt: string | null;
  createdByUserId: string | null;
  approvedByUserId: string | null;
  concurrencyStamp: string;
}

async function ledgerOf(api: ApiSession, userId: string) {
  return (await api.get<{ items: LedgerRow[] }>(`/finance/ledger?userId=${userId}&pageSize=100`)).items;
}

test('finance 1 sets the payout schedule: no earning hold, 10 USD minimum, no auto-prepare', async ({
  as,
}) => {
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto('/finance/schedule');
  await expect(finance1.getByRole('heading', { level: 1, name: 'Payout schedule' })).toBeVisible();
  await finance1.getByRole('button', { name: 'Change schedule' }).click();
  const dialog = modal(finance1, 'Change payout schedule');
  await dialog.getByLabel('Earning hold (days)').fill('0');
  await dialog.getByLabel('Minimum payout (USD)').fill('10');
  await dialog.getByRole('switch', { name: 'Prepare batches automatically after each cutoff' }).uncheck();
  await dialog.getByLabel('Effective from').fill(localMinute());
  await dialog
    .getByLabel('Reason')
    .fill(`Finance journey ${s().runId}: pay approved earnings without a hold`);
  await confirmBox(dialog, 'I confirm this schedule change.');
  await dialog.getByRole('button', { name: 'Save schedule' }).click();
  await expect(toast(finance1, 'Payout schedule saved')).toBeVisible();

  const current = finance1.getByRole('region', { name: 'Current schedule' });
  await expect(current).toContainText('Earning hold0 days');
  await expect(current).toContainText(`Minimum payout${money(10)}`);
  await expect(current).toContainText('Auto-prepare batchesOff');
  errors.expectClean('the payout schedule');
});

test('exchange rates: finance 1 adds a direct PKR→USD rate and a newer inverse USD→AED rate', async ({
  as,
}) => {
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto('/finance/exchange-rates');
  await expect(finance1.getByRole('heading', { level: 1, name: 'Exchange rates' })).toBeVisible();

  for (const [base, quote, rate] of [
    ['PKR', 'USD', '0.00357'],
    ['USD', 'AED', '3.6725'],
  ] as const) {
    await finance1.getByRole('button', { name: 'Add rate' }).click();
    const dialog = modal(finance1, 'Add exchange rate');
    await dialog.getByLabel('Base currency').selectOption(base);
    await dialog.getByLabel('Quote currency').selectOption(quote);
    await dialog.getByLabel('Rate', { exact: true }).fill(rate);
    await dialog.getByLabel('Source').fill(`journey-${s().runId}`);
    await dialog.getByLabel('Reason').fill(`Finance journey ${s().runId}: bank rate of the day`);
    await confirmBox(dialog, 'I confirm this rate is correct.');
    await dialog.getByRole('button', { name: 'Add rate' }).click();
    await expect(toast(finance1, `1 ${base} = ${rate} ${quote}`)).toBeVisible();
    await expect(dialog).toBeHidden();
  }
  const rates = finance1.getByRole('table', { name: 'Exchange rates, newest effective first' });
  await expect(rates.getByRole('row').filter({ hasText: `journey-${s().runId}` })).toHaveCount(2);
  errors.expectClean('the exchange rates page');
});

test('adjustments: credits in USD, PKR and AED convert to the cent, a debit applies at once, nobody adjusts themselves', async ({
  as,
}) => {
  const { ana } = s().participants;
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  await finance1.goto('/finance/ledger');
  await expect(finance1.getByRole('heading', { level: 1, name: 'Ledger' })).toBeVisible();

  // ---------------------------------------------------------------- 25.00 USD credit
  let dialog = await fillAdjustment(finance1, ana.id, '25', 'USD', 'Goodwill credit for the launch week');
  await expect(dialog.getByText(`Credit of ${money(25)}`)).toBeVisible();
  await dialog.getByRole('button', { name: 'Create adjustment' }).click();
  await expect(toast(finance1, 'Adjustment created')).toBeVisible();
  await expect(dialog).toBeHidden();

  // ---------------------------------------------------------------- 2,801 PKR credit → 2,801 × 0.00357 = 9.99957 → 10.00 USD
  dialog = await fillAdjustment(finance1, ana.id, '2801', 'PKR', 'Karachi Eats bonus paid in rupees');
  await expect(
    dialog.getByText('The amount is converted to USD with the exchange rate in force now'),
  ).toBeVisible();
  await dialog.getByRole('button', { name: 'Create adjustment' }).click();
  await expect(toast(finance1, 'Adjustment created')).toBeVisible();
  await expect(dialog).toBeHidden();

  // ---------------------------------------------------------------- 100 AED credit: the newer inverse rate wins
  // 1 / 3.6725 = 0.272294077… → stored rate 0.27229408 (8 dp); 100 × 0.27229408 = 27.229408 → 27.23 USD.
  dialog = await fillAdjustment(finance1, ana.id, '100', 'AED', 'Desert Bloom bonus paid in dirhams');
  await dialog.getByRole('button', { name: 'Create adjustment' }).click();
  await expect(toast(finance1, 'Adjustment created')).toBeVisible();
  await expect(dialog).toBeHidden();

  // ---------------------------------------------------------------- a 5.25 USD debit is approved at once
  dialog = await fillAdjustment(finance1, ana.id, '-5.25', 'USD', 'Duplicate bonus paid last month');
  await expect(dialog.getByText(`Debit of ${money(5.25)}`)).toBeVisible();
  await dialog.getByRole('button', { name: 'Create adjustment' }).click();
  await expect(toast(finance1, 'Adjustment created')).toBeVisible();
  await expect(dialog).toBeHidden();

  const entries = await ledgerOf(api, ana.id);
  const byCurrency = (c: string, sign: 1 | -1 = 1) =>
    entries.find((e) => e.originalCurrency === c && Math.sign(e.originalAmount) === sign)!;
  expect(entries).toHaveLength(4);
  expect(byCurrency('USD')).toMatchObject({
    status: 'PendingApproval',
    originalAmount: 25,
    exchangeRate: 1,
    settlementAmount: 25,
    settlementCurrency: 'USD',
  });
  expect(byCurrency('PKR')).toMatchObject({
    status: 'PendingApproval',
    originalAmount: 2801,
    exchangeRate: 0.00357,
    settlementAmount: round(2801 * 0.00357),
  });
  expect(round(2801 * 0.00357)).toBe(10);
  expect(byCurrency('AED')).toMatchObject({
    status: 'PendingApproval',
    originalAmount: 100,
    exchangeRate: 0.27229408,
    settlementAmount: 27.23,
  });
  expect(byCurrency('USD', -1)).toMatchObject({
    status: 'Approved',
    originalAmount: -5.25,
    settlementAmount: -5.25,
  });
  for (const e of entries) expect(e.createdByUserId).toBe(api.user.id);

  // The ledger table shows the original amount, the stored rate and the settlement amount of each entry.
  await finance1.goto(`/finance/ledger?userId=${ana.id}`);
  const ledger = finance1.getByRole('table', { name: 'Ledger entries' });
  await expect(ledger.getByRole('row').filter({ hasText: 'PKR' })).toContainText(money(10));
  await expect(ledger.getByRole('row').filter({ hasText: 'AED' })).toContainText(money(27.23));
  await expect(ledger.getByRole('row').filter({ hasText: money(-5.25) })).toContainText('Approved');

  // ---------------------------------------------------------------- segregation of duties: no self-adjustment
  await finance1.goto('/finance/ledger');
  dialog = await fillAdjustment(finance1, api.user.id, '50', 'USD', 'Trying to credit my own account');
  await dialog.getByRole('button', { name: 'Create adjustment' }).click();
  await expect(dialog.getByRole('alert')).toContainText(/own account/i);
  errors.ignore(/HTTP 403 POST .*\/finance\/adjustments/);
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  expect(await ledgerOf(api, api.user.id)).toHaveLength(0);

  // ---------------------------------------------------------------- cross-currency without a rate is refused
  dialog = await fillAdjustment(finance1, ana.id, '10', 'EUR', 'Bonus agreed in euros last week');
  await dialog.getByRole('button', { name: 'Create adjustment' }).click();
  await expect(dialog.getByRole('alert')).toContainText(
    'There is no exchange rate from this currency to the settlement currency.',
  );
  errors.ignore(/HTTP 409 POST .*\/finance\/adjustments/);
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  expect(await ledgerOf(api, ana.id)).toHaveLength(4);
  errors.expectClean('the ledger');
});

test('a credit whose response is lost is retried from the same dialog and recorded once', async ({ as }) => {
  const { dan } = s().participants;
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  await finance1.goto('/finance/ledger');

  // The first POST reaches the server (which records the credit) but its response never reaches the browser.
  let requests = 0;
  const requestIds = new Set<string>();
  await finance1.route('**/api/v1/finance/adjustments', async (route) => {
    requests++;
    requestIds.add((route.request().postDataJSON() as { requestId: string }).requestId);
    if (requests === 1) {
      await route.fetch();
      await route.abort('connectionreset');
    } else await route.continue();
  });
  const dialog = await fillAdjustment(finance1, dan.id, '15.50', 'USD', 'Referral campaign bonus for Dan');
  await dialog.getByRole('button', { name: 'Create adjustment' }).click();
  await expect(dialog.getByRole('alert')).toBeVisible();
  await expect(dialog).toBeVisible();
  // Retry: same dialog, same idempotency key → the server answers with the credit it already recorded.
  await dialog.getByRole('button', { name: 'Create adjustment' }).click();
  await expect(toast(finance1, 'Adjustment already recorded')).toBeVisible();
  expect(requests).toBe(2);
  expect(requestIds.size).toBe(1);

  const entries = await ledgerOf(api, dan.id);
  expect(entries).toHaveLength(1);
  expect(entries[0]).toMatchObject({
    originalAmount: 15.5,
    settlementAmount: 15.5,
    status: 'PendingApproval',
  });
});

test('four-eyes: finance 1 cannot approve her own credits; finance 2 approves them', async ({ as }) => {
  const { ana, ben, cat, eve } = s().participants;
  const finance1Api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  // Arrangement (API): the other participants' credits, created by finance 1 like Ana's.
  for (const [who, amount, reason] of [
    [ben, 4, 'Small bonus (below the payout minimum)'],
    [cat, 20, 'Bonus for the autumn campaign'],
    [eve, 12, 'Bonus without payout details on file'],
    [s().tess, 30, 'Bonus credited to a test account'],
  ] as const)
    await finance1Api.post('/finance/adjustments', {
      requestId: crypto.randomUUID(),
      userId: who.id,
      amount,
      currency: 'USD',
      reason,
      confirm: true,
    });

  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  await finance1.goto('/finance/approvals');
  await expect(finance1.getByRole('heading', { level: 1, name: 'Pending approvals' })).toBeVisible();
  const own = finance1
    .getByRole('row')
    .filter({ hasText: ana.displayName })
    .filter({ hasText: money(25) });
  await expect(own.getByRole('button', { name: /^Approve/ })).toBeDisabled();
  await expect(own).toContainText('You created this earning, so a different finance user must decide it.');
  // …and the server refuses it too.
  const pending = await finance1Api.get<{
    items: { id: string; user: { id: string }; concurrencyStamp: string }[];
  }>(`/finance/pending-earnings?search=${encodeURIComponent(ana.email)}&pageSize=50`);
  const refused = await raw(
    finance1Api.token,
    'POST',
    `/finance/pending-earnings/${pending.items[0]!.id}/approve`,
    {
      concurrencyStamp: pending.items[0]!.concurrencyStamp,
    },
  );
  expect(refused.status).toBe(403);
  expect(refused.body).toMatchObject({ code: 'ledger.self_approval' });

  const finance2 = await as(accounts.finance2, FINANCE_LANDING);
  const errors = watchErrors(finance2);
  await finance2.goto('/finance/approvals');
  for (const amount of [25, 10, 27.23]) {
    const pendingRow = finance2
      .getByRole('row')
      .filter({ hasText: ana.displayName })
      .filter({ hasText: money(amount) });
    await pendingRow.getByRole('button', { name: /^Approve/ }).click();
    const confirm = modal(finance2, 'Approve this earning?');
    await expect(confirm).toContainText(money(amount));
    await confirm.getByRole('button', { name: 'Approve' }).click();
    await expect(toast(finance2, 'Earning approved')).toBeVisible();
    await expect(pendingRow).toHaveCount(0);
  }
  errors.expectClean('the approvals queue');

  // Arrangement (API): finance 2 approves the other journey credits the same way.
  const finance2Api = await ApiSession.login(accounts.finance2.email, accounts.finance2.password);
  for (const who of [ben, cat, eve, s().participants.dan, s().tess]) {
    const list = await finance2Api.get<{ items: { id: string; concurrencyStamp: string }[] }>(
      `/finance/pending-earnings?search=${encodeURIComponent(who.email)}&pageSize=50`,
    );
    expect(list.items, `${who.displayName} has one pending credit`).toHaveLength(1);
    await finance2Api.post(`/finance/pending-earnings/${list.items[0]!.id}/approve`, {
      concurrencyStamp: list.items[0]!.concurrencyStamp,
    });
  }
  share({ approvedAt: new Date().toISOString() });
});

test('Ana’s balance and the ledger CSV show exactly the approved amounts', async ({ as }) => {
  const { ana } = s().participants;
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  // 25.00 + 10.00 + 27.23 − 5.25 = 56.98 USD, available at once (no earning hold).
  const expected = round(25 + 10 + 27.23 - 5.25);
  expect(expected).toBe(56.98);

  await finance1.goto(`/finance/ledger/users/${ana.id}`);
  await expect(finance1.getByRole('heading', { level: 1, name: new RegExp(ana.displayName) })).toBeVisible();
  const stat = (label: string) => finance1.getByRole('group', { name: label, exact: true });
  await expect(stat('Approved')).toContainText(money(expected));
  await expect(stat('Available for next payout')).toContainText(money(expected));
  await expect(stat('Pending')).toContainText(money(0));
  await expect(stat('Scheduled')).toContainText(money(0));

  await finance1.goto(`/finance/ledger?userId=${ana.id}`);
  const [download] = await Promise.all([
    finance1.waitForEvent('download'),
    finance1.getByRole('button', { name: 'Export CSV' }).click(),
  ]);
  expect(download.suggestedFilename()).toMatch(/^ledger-.*\.csv$/);
  const csv = readFileSync((await download.path())!, 'utf8')
    .trim()
    .split(/\r?\n/);
  expect(csv).toHaveLength(5); // header + Ana's four entries only (the export honours the filter)
  const settlements = csv.slice(1).map((line) => line.split(',')[13]);
  expect(settlements.map(Number).sort((a, b) => a - b)).toEqual([-5.25, 10, 25, 27.23]);
  expect(csv.join('\n')).toContain('0.27229408');
  errors.expectClean('the participant balance');
});
