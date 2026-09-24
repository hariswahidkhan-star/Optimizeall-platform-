import type { Locator, Page } from '@playwright/test';
import {
  ApiSession,
  FINANCE_LANDING,
  accounts,
  expect,
  modal,
  money,
  raw,
  shared,
  state,
  test,
  toast,
  watchErrors,
} from './support/finance';
import { closePeriodNow } from './support/schedule';

/**
 * Finance journey, part 3 — the next period: what was carried over or re-queued is paid in the next batch.
 *   finance 1 releases Cat's payout hold → Ben gets a 6.00 credit (approved by finance 2), so his carried-over 4.00 +
 *   6.00 is exactly the 10.00 minimum → the next period closes → the new batch holds Ben 10.00 (two earnings), Cat 20.00
 *   and Dan's returned 15.50 (re-queued), Eve is held again, the test account is excluded again and Ana is not in it →
 *   finance 1 cancels the draft (every earning goes back to Approved) → the period can be prepared again deliberately,
 *   under a new reference (-R2), with exactly the same items.
 */
test.describe.configure({ mode: 'serial' });

const s = () => state();

function itemRow(page: Page, text: string): Locator {
  return page.getByRole('table', { name: /items/i }).getByRole('row').filter({ hasText: text });
}

test('finance 1 releases Cat’s hold; the hold moves to “Released” with its note', async ({ as }) => {
  const { cat } = s().participants;
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto('/finance/holds');
  await finance1.getByRole('button', { name: `Release hold on ${cat.displayName}` }).click();
  const dialog = modal(finance1, `Release the hold on ${cat.displayName}?`);
  await expect(dialog).toContainText('Reason: KYC documents under review');
  await dialog.getByLabel('Release note').fill('KYC documents verified');
  await dialog.getByRole('button', { name: 'Release hold' }).click();
  await expect(toast(finance1, 'Hold released')).toBeVisible();
  await finance1.getByRole('tab', { name: 'Released' }).click();
  await expect(
    finance1.getByRole('table', { name: 'Released payout holds' }).getByRole('row').filter({ hasText: cat.displayName }),
  ).toContainText('KYC documents verified');

  // Releasing twice is refused.
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const holds = await api.get<{ items: { id: string; user: { id: string } }[] }>(`/finance/holds?userId=${cat.id}`);
  const again = await raw(api.token, 'POST', `/finance/holds/${holds.items[0]!.id}/release`, { note: 'again' });
  expect(again.status).toBe(409);
  expect(again.body).toMatchObject({ code: 'payout.hold_not_active' });
  errors.expectClean('the holds page');
});

test('the next batch pays what was carried over (Ben at exactly the minimum), released (Cat) and re-queued (Dan)', async ({
  as,
}) => {
  const { ana, ben, cat, dan, eve } = s().participants;
  // Arrangement (API): Ben's second credit, created by finance 1 and approved by finance 2.
  const f1 = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const f2 = await ApiSession.login(accounts.finance2.email, accounts.finance2.password);
  await f1.post('/finance/adjustments', {
    requestId: crypto.randomUUID(),
    userId: ben.id,
    amount: 6,
    currency: 'USD',
    reason: 'Second bonus: brings Ben to the minimum',
    confirm: true,
  });
  const pending = await f2.get<{ items: { id: string; concurrencyStamp: string }[] }>(
    `/finance/pending-earnings?search=${encodeURIComponent(ben.email)}`,
  );
  await f2.post(`/finance/pending-earnings/${pending.items[0]!.id}/approve`, {
    concurrencyStamp: pending.items[0]!.concurrencyStamp,
  });

  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  const periodKey = await closePeriodNow(finance1, 'Etc/GMT+12', `Finance journey ${s().runId}: close the next period`);
  expect(periodKey).not.toBe(shared<{ batch1Reference: string }>().batch1Reference.replace('PB-', ''));

  await finance1.goto('/finance/batches');
  await finance1.getByRole('button', { name: 'Prepare batch' }).click();
  await modal(finance1, 'Prepare payout batch').getByRole('button', { name: 'Prepare batch' }).click();
  await expect(finance1).toHaveURL(/\/finance\/batches\/[0-9a-f-]{36}$/);
  const batchId = finance1.url().split('/').pop()!;
  const reference = `PB-${periodKey}`;
  await expect(finance1.getByRole('heading', { level: 1, name: reference })).toBeVisible();

  await expect(itemRow(finance1, ben.email)).toContainText(money(10));
  await expect(itemRow(finance1, ben.email)).toContainText('Pending');
  await expect(itemRow(finance1, cat.email)).toContainText(money(20));
  await expect(itemRow(finance1, dan.email)).toContainText(money(15.5));
  await expect(itemRow(finance1, eve.email)).toContainText('No payout details on file');
  await expect(itemRow(finance1, ana.email)).toHaveCount(0);
  await expect(
    finance1.getByRole('region', { name: 'Excluded participants' }).getByRole('listitem').filter({ hasText: s().tess.displayName }),
  ).toContainText(money(30));

  // Ben's item: his carried-over 4.00 and the new 6.00.
  await itemRow(finance1, ben.email).getByRole('button', { name: new RegExp(`^${ben.displayName}`) }).click();
  const drawer = modal(finance1, ben.displayName);
  const earnings = drawer.getByRole('table', { name: `Earnings included for ${ben.displayName}` });
  const rows = earnings.getByRole('rowgroup').nth(1).getByRole('row');
  await expect(rows).toHaveCount(2);
  await expect(rows.filter({ hasText: money(4) })).toHaveCount(1);
  await expect(rows.filter({ hasText: money(6) })).toHaveCount(1);
  await finance1.keyboard.press('Escape');

  // ---------------------------------------------------------------- cancel → every earning is released
  await finance1.getByRole('button', { name: 'Cancel batch' }).click();
  const cancel = modal(finance1, `Cancel ${reference}?`);
  await cancel.getByRole('textbox').first().fill('Prepared before the bank holiday; redo it');
  await cancel.getByLabel(`Type ${reference} to confirm`).fill(reference);
  await cancel.getByRole('button', { name: /Cancel batch/ }).click();
  await expect(finance1.getByText('Cancelled', { exact: true }).first()).toBeVisible();
  const danLedger = await f1.get<{ items: { status: string; payoutItemId: string | null }[] }>(
    `/finance/ledger?userId=${dan.id}`,
  );
  expect(danLedger.items).toEqual([expect.objectContaining({ status: 'Approved', payoutItemId: null })]);

  // ---------------------------------------------------------------- the period can be prepared again, deliberately
  const again = await raw<{ created: boolean; batch: { id: string; reference: string; totalAmount: number } }>(
    f1.token,
    'POST',
    '/finance/payout-batches/prepare',
    { periodKey },
  );
  expect(again.status).toBe(201);
  expect(again.body.created).toBe(true);
  expect(again.body.batch.id).not.toBe(batchId);
  expect(again.body.batch.reference).toBe(`${reference}-R2`);
  const detail = await f1.get<{ items: { items: { user: { id: string }; amount: number; status: string }[] } }>(
    `/finance/payout-batches/${again.body.batch.id}?pageSize=200`,
  );
  const amountOf = (id: string) => detail.items.items.find((i) => i.user.id === id)?.amount;
  expect([amountOf(ben.id), amountOf(cat.id), amountOf(dan.id), amountOf(eve.id)]).toEqual([10, 20, 15.5, 12]);
  expect(amountOf(ana.id)).toBeUndefined();
  errors.expectClean('the next batch');
});
