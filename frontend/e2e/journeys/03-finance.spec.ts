import { readFileSync } from 'node:fs';
import { type Page, expect, test } from '@playwright/test';
import { ApiSession } from './support/api';
import { fixtures, participantFor } from './support/fixtures';
import { modal, signedInPage } from './support/ui';

/**
 * Finance journey: the participant adds payout details → finance 1 prepares the batch (items + breakdown) and is
 * blocked from finalizing it (four-eyes) → finance 2 finalizes with the typed reference ("no money has been sent")
 * and downloads the payment instructions → finance 1 records the payment → a stale second attempt says "already
 * recorded" → reconciliation balances → the participant sees Paid → preparing again returns the same batch.
 *
 * Arrangement (API, no database edits): global setup set the earning hold to 0 days, so the approvals of
 * 02-reviewer are payable immediately. Here the payout schedule is moved so that its last completed cutoff is one
 * second ago (weekly, anchored on today's date at that time) — after every approved earning's `availableAt`.
 */
test.describe.serial('finance journey', () => {
  const participant = participantFor('desktop-chromium');
  const paypal = `pat.desktop.${Date.now().toString(36)}@example.com`;
  let finance1: Page;
  let finance2: Page;
  let pat: Page;
  let batchUrl = '';
  let reference = '';
  let periodKey = '';

  test.beforeAll(async ({ browser }) => {
    finance1 = await signedInPage(browser, fixtures().finance1, /\/finance$/);
    finance2 = await signedInPage(browser, fixtures().finance2, /\/finance$/);
    pat = await signedInPage(browser, participant, /\/app$/);
  });
  test.afterAll(async () => {
    for (const page of [finance1, finance2, pat]) await page?.context().close();
  });

  test('the participant saves PayPal payout details on the profile page', async () => {
    await pat.goto('/app/profile/payout-details');
    await pat.getByRole('radio', { name: 'PayPal' }).check();
    await pat.getByLabel('Account holder name').fill(participant.displayName);
    await pat.getByLabel('PayPal email address').fill(paypal);
    await pat.getByLabel('Preferred currency').selectOption('USD');
    await pat.getByRole('button', { name: 'Save payout details' }).click();
    const current = pat.getByRole('region', { name: 'Current payout destination' });
    await expect(current).toContainText('PayPal');
    await expect(current).toContainText('@example.com');
    await expect(current).not.toContainText(paypal);
  });

  test('arrange: the last completed payout period ends one second ago', async () => {
    const financeApi = await ApiSession.login(fixtures().finance1.email, fixtures().finance1.password);
    const patApi = await ApiSession.login(participant.email, participant.password);
    const profile = await patApi.get<{ id: string }>('/me/profile');
    const ledger = await financeApi.get<{
      items: { availableAt: string | null; settlementAmount: number }[];
    }>(`/finance/ledger?userId=${profile.id}&status=Approved&pageSize=50`);
    expect(ledger.items.map((e) => e.settlementAmount).sort()).toEqual([1, 5, 5]);
    const lastAvailable = Math.max(...ledger.items.map((e) => Date.parse(e.availableAt!)));

    let cutoff = 0;
    await expect
      .poll(() => {
        cutoff = Math.floor(Date.now() / 1000) * 1000 - 1000;
        return cutoff > lastAvailable;
      })
      .toBe(true);
    const iso = new Date(cutoff).toISOString();
    periodKey = iso.slice(0, 10);
    await financeApi.put('/finance/payout-schedule', {
      frequency: 'Weekly',
      anchorCutoffDate: periodKey,
      cutoffLocalTime: iso.slice(11, 19),
      timeZone: 'UTC',
      paymentDelayDays: 2,
      minimumPayoutAmount: 1,
      settlementCurrency: 'USD',
      earningHoldDays: 0,
      autoPrepareBatches: false,
      effectiveFrom: new Date().toISOString(),
      reason: 'E2E journeys: close a payout period right after the approvals',
      confirm: true,
    });
    await expect
      .poll(async () => {
        const s = await financeApi.get<{ lastCompletedPeriod: { periodKey: string } }>(
          '/finance/payout-schedule',
        );
        return s.lastCompletedPeriod.periodKey;
      })
      .toBe(periodKey);
  });

  test('finance 1 prepares the batch; the review shows the item and its breakdown', async () => {
    await finance1.goto('/finance/batches');
    await finance1.getByRole('button', { name: 'Prepare batch' }).click();
    const dialog = modal(finance1, 'Prepare payout batch');
    await expect(
      dialog.getByRole('radio', { name: new RegExp(`Last completed period · ${periodKey}`) }),
    ).toBeChecked();
    await dialog.getByRole('button', { name: 'Prepare batch' }).click();

    await expect(finance1).toHaveURL(/\/finance\/batches\/[0-9a-f-]{36}$/);
    batchUrl = new URL(finance1.url()).pathname;
    const title = finance1.getByRole('heading', { level: 1, name: /^PB-/ });
    await expect(title).toBeVisible();
    reference = (await title.textContent())!.trim();
    await expect(finance1.getByText('Draft — nothing has been paid')).toBeVisible();

    const row = finance1.getByRole('row').filter({ hasText: participant.email });
    await expect(row).toContainText('$11.00');
    await expect(row).toContainText('Pending');
    await row.getByRole('button', { name: new RegExp(`^${participant.displayName}`) }).click();

    const drawer = modal(finance1, participant.displayName);
    await expect(drawer).toContainText('$11.00');
    const earnings = drawer.getByRole('table', { name: `Earnings included for ${participant.displayName}` });
    const rows = earnings.getByRole('rowgroup').nth(1).getByRole('row');
    await expect(rows).toHaveCount(3);
    await expect(rows.filter({ hasText: fixtures().campaign.title })).toHaveCount(3);
    await expect(rows.filter({ hasText: '$5.00' })).toHaveCount(2);
    await expect(rows.filter({ hasText: '$1.00' })).toHaveCount(1);
    await finance1.keyboard.press('Escape');
    await expect(drawer).toBeHidden();
  });

  test('finance 1 cannot finalize a batch they prepared', async () => {
    await expect(finance1.getByRole('button', { name: 'Finalize' })).toBeDisabled();
    await expect(finance1.getByRole('button', { name: 'Finalize' })).toHaveAccessibleDescription(
      /You prepared this batch, so a different finance user must finalize it/,
    );
  });

  test('finance 2 finalizes with the typed reference — no money has been sent', async () => {
    await finance2.goto(batchUrl);
    await finance2.getByRole('button', { name: 'Finalize' }).click();
    const dialog = modal(finance2, `Finalize ${reference}?`);
    const submit = dialog.getByRole('button', { name: 'Finalize batch' });
    await expect(submit).toBeDisabled();
    await dialog.getByLabel(`Type ${reference} to confirm`).fill(reference);
    await dialog.getByLabel('Reason').fill('Reviewed the item and the warnings.');
    await submit.click();

    await expect(
      finance2.getByRole('region', { name: 'Batch finalized — no money has been sent' }),
    ).toBeVisible();
    await expect(finance2.getByText('Awaiting manual payment — no money has been sent')).toBeVisible();
    await expect(finance2.getByRole('row').filter({ hasText: participant.email })).toContainText(
      'Awaiting payment',
    );
  });

  test('finance 2 downloads the payment instructions after confirming', async () => {
    await finance2.getByRole('button', { name: 'Payment instructions' }).click();
    const dialog = modal(finance2, 'Download payment instructions?');
    await expect(dialog).toContainText('This download is audited');
    const [download] = await Promise.all([
      finance2.waitForEvent('download'),
      dialog.getByRole('button', { name: 'Download (audited)' }).click(),
    ]);
    expect(download.suggestedFilename()).toMatch(
      new RegExp(`^payment-instructions-${reference}(-\\d+)?\\.csv$`),
    );
    const csv = readFileSync((await download.path())!, 'utf8');
    expect(csv).toContain(participant.email);
    expect(csv).toContain(paypal.toLowerCase());
    await expect(dialog).toBeHidden();
  });

  test('finance 1 records the payment; a stale second attempt says already recorded', async () => {
    // finance 2's page still shows the item awaiting payment (stale after finance 1 records it).
    const recordFor = new RegExp(`Record\\s*payment for ${participant.displayName}`);
    await expect(finance2.getByRole('button', { name: recordFor })).toBeVisible();

    await finance1.reload();
    await finance1.getByRole('button', { name: recordFor }).click();
    const dialog = modal(finance1, 'Record payment');
    await dialog.getByLabel('Payment reference').fill(`PAYPAL-${fixtures().runId}-0001`);
    await dialog.getByRole('button', { name: 'Record payment' }).click();
    await expect(dialog).toBeHidden();
    const row = finance1.getByRole('row').filter({ hasText: participant.email });
    await expect(row).toContainText('Paid');
    await expect(row).toContainText(`PAYPAL-${fixtures().runId}-0001`);
    await expect(finance1.getByText('Completed', { exact: true }).first()).toBeVisible();

    await finance2.getByRole('button', { name: recordFor }).click();
    const stale = modal(finance2, 'Record payment');
    await stale.getByLabel('Payment reference').fill(`PAYPAL-${fixtures().runId}-0002`);
    await stale.getByRole('button', { name: 'Record payment' }).click();
    await expect(stale.getByText('Already recorded by someone else')).toBeVisible();
  });

  test('reconciliation is balanced', async () => {
    await finance1.getByRole('tab', { name: 'Reconciliation' }).click();
    await expect(finance1).toHaveURL(/\/reconciliation$/);
    await expect(finance1.getByText('Balanced', { exact: true })).toBeVisible();
    await expect(finance1.getByText('Not balanced')).toHaveCount(0);
  });

  test('the participant sees the payout as Paid', async () => {
    await pat.goto('/app/payouts');
    const row = pat.getByRole('row').filter({ hasText: reference });
    await expect(row).toContainText('Paid');
    await expect(row).toContainText('$11.00');
  });

  test('retry safety: preparing the same period again opens the existing batch', async () => {
    await finance1.goto('/finance/batches');
    await finance1.getByRole('button', { name: 'Prepare batch' }).click();
    const dialog = modal(finance1, 'Prepare payout batch');
    await dialog.getByRole('button', { name: 'Prepare batch' }).click();
    await expect(finance1.getByText('Batch already exists')).toBeVisible();
    await expect(finance1).toHaveURL(new RegExp(`${batchUrl}$`));
    await expect(finance1.getByRole('heading', { level: 1, name: reference })).toBeVisible();
  });
});
