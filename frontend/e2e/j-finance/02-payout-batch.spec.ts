import { readFileSync } from 'node:fs';
import type { Locator, Page } from '@playwright/test';
import {
  ADMIN_LANDING,
  ApiSession,
  FINANCE_LANDING,
  PARTICIPANT_LANDING,
  accounts,
  expect,
  modal,
  money,
  pickUser,
  raw,
  share,
  shared,
  state,
  sum,
  test,
  toast,
  watchErrors,
} from './support/finance';
import { closePeriodNow } from './support/schedule';

/**
 * Finance journey, part 2 — one payout batch from preparation to reconciliation:
 *   finance 1 places a payout hold on Cat → closes the period → prepares the batch: Ana (56.98) and Dan (15.50) are
 *   payable, Eve is held (no payout details), and Ben (4.00, below the 10.00 minimum), Cat (on hold) and the test account
 *   (30.00) are excluded and carried over → she holds and releases Dan's item on the review screen → she cannot finalize
 *   her own batch (four-eyes) → finance 2 is refused too (she approved earnings in it: conflict of interest) → the admin
 *   finalizes → finance 2 downloads the payment instructions (audited) → finance 1 records Ana's payment with its bank
 *   reference; a stale second attempt says "already recorded" → the payments hub marks Dan's transfer returned by the
 *   bank (re-queued) and marks the rest of the batch paid with one bulk reference (a retried click is a replay) →
 *   the batch completes, reconciliation is balanced to the cent, the CSV exports match → Ana sees her payout Paid with
 *   the reference's last four characters, Dan sees his failed.
 */
test.describe.configure({ mode: 'serial' });

const s = () => state();

interface BatchShared {
  batch1Id: string;
  batch1Reference: string;
  anaItemId: string;
  danItemId: string;
  anaReference: string;
}

function itemRow(page: Page, text: string): Locator {
  return page.getByRole('table', { name: /items/i }).getByRole('row').filter({ hasText: text });
}

function exclusion(page: Page, name: string): Locator {
  return page
    .getByRole('region', { name: 'Excluded participants' })
    .getByRole('listitem')
    .filter({ hasText: name });
}

test('finance 1 places a payout hold on Cat from the Holds page', async ({ as }) => {
  const { cat } = s().participants;
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto('/finance/holds');
  await finance1.getByRole('button', { name: 'Place hold' }).click();
  const dialog = modal(finance1, 'Place a payout hold');
  await pickUser(dialog, cat.id);
  await dialog.getByLabel('Reason').fill('KYC documents under review');
  await dialog.getByRole('button', { name: 'Place hold' }).click();
  await expect(
    finance1.getByRole('status').filter({ hasText: `Hold placed on ${cat.displayName}` }),
  ).toContainText('No draft batch items needed holding.');
  await expect(
    finance1
      .getByRole('table', { name: 'Active payout holds' })
      .getByRole('row')
      .filter({ hasText: cat.displayName }),
  ).toContainText('KYC documents under review');

  // A second active hold on the same participant is refused.
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const again = await raw(api.token, 'POST', '/finance/holds', { userId: cat.id, reason: 'Second hold' });
  expect(again.status).toBe(409);
  expect(again.body).toMatchObject({ code: 'payout.hold_exists' });
  errors.expectClean('the holds page');
});

test('finance 1 prepares the batch: payable items, a held item and the exclusions with their amounts', async ({
  as,
}) => {
  const { ana, ben, cat, dan, eve } = s().participants;
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  const periodKey = await closePeriodNow(
    finance1,
    'Pacific/Kiritimati',
    `Finance journey ${s().runId}: close the first period`,
  );

  await finance1.goto('/finance/batches');
  await finance1.getByRole('button', { name: 'Prepare batch' }).click();
  const dialog = modal(finance1, 'Prepare payout batch');
  await expect(
    dialog.getByRole('radio', { name: new RegExp(`Last completed period · ${periodKey}`) }),
  ).toBeChecked();
  await dialog.getByRole('button', { name: 'Prepare batch' }).click();
  await expect(finance1).toHaveURL(/\/finance\/batches\/[0-9a-f-]{36}$/);
  const batchId = finance1.url().split('/').pop()!;
  const reference = `PB-${periodKey}`;
  await expect(finance1.getByRole('heading', { level: 1, name: reference })).toBeVisible();
  await expect(finance1.getByText('Draft — nothing has been paid')).toBeVisible();

  await expect(itemRow(finance1, ana.email)).toContainText(money(56.98));
  await expect(itemRow(finance1, ana.email)).toContainText('Pending');
  await expect(itemRow(finance1, dan.email)).toContainText(money(15.5));
  await expect(itemRow(finance1, eve.email)).toContainText(money(12));
  await expect(itemRow(finance1, eve.email)).toContainText('No payout details on file');

  // Carried over: below the minimum, on hold, test account — each with its amount.
  await expect(exclusion(finance1, ben.displayName)).toContainText('Below minimum');
  await expect(exclusion(finance1, ben.displayName)).toContainText(money(4));
  await expect(exclusion(finance1, cat.displayName)).toContainText('Payout hold');
  await expect(exclusion(finance1, cat.displayName)).toContainText(money(20));
  await expect(exclusion(finance1, s().tess.displayName)).toContainText('Test account');
  await expect(exclusion(finance1, s().tess.displayName)).toContainText(money(30));

  // The batch total is exactly the sum of its payable items (the held item is not payable).
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const detail = await api.get<{
    batch: { totalAmount: number; itemCount: number };
    items: {
      items: { itemId: string; user: { id: string }; amount: number; status: string }[];
      total: number;
    };
  }>(`/finance/payout-batches/${batchId}?pageSize=200`);
  const payable = detail.items.items.filter((i) => i.status === 'Pending');
  expect(detail.batch.itemCount).toBe(payable.length);
  expect(detail.batch.totalAmount).toBe(sum(payable.map((i) => i.amount)));
  const itemOf = (id: string) => detail.items.items.find((i) => i.user.id === id)!;
  for (const excluded of [ben, cat, s().tess])
    expect(detail.items.items.some((i) => i.user.id === excluded.id)).toBe(false);

  // Retry safety: preparing the same period again returns the same batch (200, created: false).
  const again = await raw<{ created: boolean; batch: { id: string } }>(
    api.token,
    'POST',
    '/finance/payout-batches/prepare',
    {},
  );
  expect(again.status).toBe(200);
  expect(again.body).toMatchObject({ created: false, batch: { id: batchId } });

  share({
    batch1Id: batchId,
    batch1Reference: reference,
    anaItemId: itemOf(ana.id).itemId,
    danItemId: itemOf(dan.id).itemId,
  });
  errors.expectClean('the batch review');
});

test('review: finance 1 holds Dan’s item and releases it; the total follows to the cent', async ({ as }) => {
  const { dan } = s().participants;
  const { batch1Id } = shared<BatchShared>();
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto(`/finance/batches/${batch1Id}`);
  const total = finance1.getByRole('region', { name: 'Summary' });
  const before = Number(/Total\$([\d,]+\.\d{2})/.exec((await total.textContent())!)![1]!.replace(/,/g, ''));

  await itemRow(finance1, dan.email)
    .getByRole('button', { name: /^(Actions|More actions)/ })
    .click();
  await finance1.getByRole('menuitem', { name: 'Hold item' }).click();
  const hold = modal(finance1, `Hold ${dan.displayName}’s item?`);
  await hold.getByLabel(/Reason/).fill('Checking the PayPal account name');
  await hold.getByRole('button', { name: /Hold item/ }).click();
  await expect(itemRow(finance1, dan.email)).toContainText('On hold');
  await expect(total).toContainText(`Total${money(before - 15.5)}`);

  await itemRow(finance1, dan.email)
    .getByRole('button', { name: /^(Actions|More actions)/ })
    .click();
  await finance1.getByRole('menuitem', { name: 'Release hold' }).click();
  const release = modal(finance1, /Release/);
  await release.getByRole('button', { name: /Release/ }).click();
  await expect(itemRow(finance1, dan.email)).toContainText('Pending');
  await expect(total).toContainText(`Total${money(before)}`);
  errors.expectClean('the batch review');
});

test('segregation of duties: the preparer and an approver of its earnings cannot finalize; the admin does', async ({
  as,
}) => {
  const { batch1Id, batch1Reference } = shared<BatchShared>();
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  await finance1.goto(`/finance/batches/${batch1Id}`);
  await expect(finance1.getByRole('button', { name: 'Finalize' })).toBeDisabled();
  await expect(finance1.getByRole('button', { name: 'Finalize' })).toHaveAccessibleDescription(
    /You prepared this batch, so a different finance user must finalize it/,
  );
  const f1Api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const detail = await f1Api.get<{ concurrencyStamp: string }>(`/finance/payout-batches/${batch1Id}`);
  const self = await raw(f1Api.token, 'POST', `/finance/payout-batches/${batch1Id}/finalize`, {
    confirm: true,
    concurrencyStamp: detail.concurrencyStamp,
  });
  expect(self.status).toBe(403);
  expect(self.body).toMatchObject({ code: 'payout.self_finalize' });

  // Finance 2 approved Ana's credits: she is conflicted and is told why (not "ask for access").
  const finance2 = await as(accounts.finance2, FINANCE_LANDING);
  const errors2 = watchErrors(finance2);
  errors2.ignore(/HTTP 403 POST .*\/finalize$/);
  await finance2.goto(`/finance/batches/${batch1Id}`);
  await finance2.getByRole('button', { name: 'Finalize' }).click();
  let dialog = modal(finance2, `Finalize ${batch1Reference}?`);
  await dialog.getByLabel(`Type ${batch1Reference} to confirm`).fill(batch1Reference);
  await dialog.getByRole('button', { name: 'Finalize batch' }).click();
  await expect(dialog.getByRole('alert')).toContainText(/you created or approved one of its earnings/i);
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  errors2.expectClean('the batch review (finance 2)');

  // A stale review (the batch changed since it was loaded) is refused; the admin then finalizes the current one.
  const admin = await as(accounts.admin, ADMIN_LANDING);
  const errors = watchErrors(admin);
  const adminApi = await ApiSession.login(accounts.admin.email, accounts.admin.password);
  const stale = await raw(adminApi.token, 'POST', `/finance/payout-batches/${batch1Id}/finalize`, {
    confirm: true,
    concurrencyStamp: crypto.randomUUID(),
  });
  expect(stale.status).toBe(409);
  expect(stale.body).toMatchObject({ code: 'concurrency.conflict' });

  await admin.goto(`/finance/batches/${batch1Id}`);
  await admin.getByRole('button', { name: 'Finalize' }).click();
  dialog = modal(admin, `Finalize ${batch1Reference}?`);
  const submit = dialog.getByRole('button', { name: 'Finalize batch' });
  await expect(submit).toBeDisabled();
  await dialog.getByLabel(`Type ${batch1Reference} to confirm`).fill(batch1Reference);
  await dialog.getByLabel('Reason').fill('Reviewed the items, the held item and the exclusions.');
  await submit.click();
  await expect(admin.getByRole('region', { name: 'Batch finalized — no money has been sent' })).toBeVisible();
  await expect(itemRow(admin, s().participants.ana.email)).toContainText('Awaiting payment');
  // The item without payout details was held at finalize: its earnings went back to Eve's balance.
  await expect(itemRow(admin, s().participants.eve.email)).toContainText('On hold');
  errors.expectClean('finalize (admin)');
});

test('finance 2 downloads the payment instructions: decrypted destinations, audited', async ({ as }) => {
  const { ana, dan, eve } = s().participants;
  const { batch1Id, batch1Reference } = shared<BatchShared>();
  const finance2 = await as(accounts.finance2, FINANCE_LANDING);
  const errors = watchErrors(finance2);
  await finance2.goto(`/finance/batches/${batch1Id}`);
  await finance2.getByRole('button', { name: 'Payment instructions' }).click();
  const dialog = modal(finance2, 'Download payment instructions?');
  await expect(dialog).toContainText('This download is audited');
  const [download] = await Promise.all([
    finance2.waitForEvent('download'),
    dialog.getByRole('button', { name: 'Download (audited)' }).click(),
  ]);
  expect(download.suggestedFilename()).toMatch(
    new RegExp(`^payment-instructions-${batch1Reference}-\\d+\\.csv$`),
  );
  const csv = readFileSync((await download.path())!, 'utf8');
  expect(csv).toContain(`ana.${s().runId}@example.com`);
  expect(csv).toContain(`dan.${s().runId}@example.com`);
  expect(csv).toContain(ana.email);
  expect(csv).not.toContain(eve.email); // held → not payable
  const anaLine = csv.split(/\r?\n/).find((l) => l.includes(ana.email))!;
  expect(anaLine).toContain('56.98');
  void dan;

  const admin = await ApiSession.login(accounts.admin.email, accounts.admin.password);
  const audit = await admin.get<{
    items: { action: string; entityId: string; actor?: { email?: string } | null }[];
  }>(`/admin/audit-logs?action=payout.payment_instructions_exported&pageSize=20`);
  expect(audit.items.some((a) => a.entityId === batch1Id)).toBe(true);
  errors.expectClean('payment instructions');
});

test('finance 1 records Ana’s payment; a stale second attempt is refused as already recorded', async ({
  as,
}) => {
  const { ana } = s().participants;
  const { batch1Id } = shared<BatchShared>();
  const reference = `PP-${s().runId}-ANA1`;
  const finance2 = await as(accounts.finance2, FINANCE_LANDING);
  await finance2.goto(`/finance/batches/${batch1Id}`); // loaded before the payment is recorded → stale

  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto(`/finance/batches/${batch1Id}`);
  const recordFor = new RegExp(`Record\\s*payment for ${ana.displayName}`);
  await finance1.getByRole('button', { name: recordFor }).click();
  const dialog = modal(finance1, 'Record payment');
  await dialog.getByLabel('Payment reference').fill(reference);
  await dialog.getByRole('button', { name: 'Record payment' }).click();
  await expect(dialog).toBeHidden();
  await expect(itemRow(finance1, ana.email)).toContainText('Paid');
  await expect(itemRow(finance1, ana.email)).toContainText(reference);

  await finance2.getByRole('button', { name: recordFor }).click();
  const stale = modal(finance2, 'Record payment');
  await stale.getByLabel('Payment reference').fill(`PP-${s().runId}-ANA2`);
  await stale.getByRole('button', { name: 'Record payment' }).click();
  await expect(stale.getByText('Already recorded by someone else')).toBeVisible();
  share({ anaReference: reference });
  errors.expectClean('record payment');
});

test('payments hub: Dan’s transfer is returned by the bank (re-queued); the rest of the batch is paid in bulk', async ({
  as,
}) => {
  const { dan } = s().participants;
  const { batch1Id, batch1Reference, danItemId } = shared<BatchShared>();
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto('/finance/payments');
  await expect(finance1.getByRole('heading', { level: 1, name: 'Payments' })).toBeVisible();
  const search = finance1.getByRole('searchbox', { name: 'Search payments' });
  await search.fill(dan.displayName);
  const danRow = finance1
    .getByRole('table', { name: 'Payments' })
    .getByRole('row')
    .filter({ hasText: batch1Reference });
  await expect(danRow).toHaveCount(1);
  await expect(danRow).toContainText(money(15.5));
  await danRow.getByRole('button', { name: /^Actions for / }).click();
  await finance1.getByRole('menuitem', { name: 'Mark failed / returned' }).click();
  const failed = modal(finance1, /failed|returned/i);
  await failed.getByRole('radio', { name: 'The bank returned the transfer' }).check();
  await failed.getByLabel('Reason').fill('R04 — invalid account number');
  await failed.getByRole('button', { name: /Mark/ }).click();
  await expect(toast(finance1, 'Payout marked failed and re-queued')).toBeVisible();
  await expect(danRow).toContainText('Failed');

  // Dan's earning is back to Approved and unlinked: it will be picked up by the next batch.
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const danLedger = await api.get<{
    items: { status: string; payoutItemId: string | null; settlementAmount: number }[];
  }>(`/finance/ledger?userId=${dan.id}`);
  expect(danLedger.items).toEqual([
    expect.objectContaining({ status: 'Approved', payoutItemId: null, settlementAmount: 15.5 }),
  ]);
  // Retrying the same "returned" report is a replay, not an error.
  const replay = await raw<{ replayed: boolean; requeued: boolean }>(
    api.token,
    'POST',
    `/admin/payments/payouts/${danItemId}/mark-failed`,
    { kind: 'Returned', reason: 'R04 — invalid account number' },
  );
  expect(replay.status).toBe(200);
  expect(replay.body).toMatchObject({ replayed: true, requeued: true });

  // The rest of the batch: one bulk transfer, from any item still awaiting payment.
  await search.fill(batch1Reference);
  await finance1.getByRole('combobox', { name: 'Status' }).selectOption({ label: 'Pending' });
  const pendingRow = finance1.getByRole('table', { name: 'Payments' }).getByRole('row').nth(1);
  await expect(pendingRow).toContainText(batch1Reference);
  await pendingRow.getByRole('button', { name: /^Actions for / }).click();
  await finance1.getByRole('menuitem', { name: `Mark batch ${batch1Reference} paid` }).click();
  const bulk = modal(finance1, `Mark batch ${batch1Reference} as paid`);
  await bulk.getByLabel('Bulk transfer reference').fill(`BULK-${s().runId}`);
  await bulk.getByRole('button', { name: 'Record all payments' }).click();
  await expect(bulk.getByRole('status')).toContainText(/\d+ recorded, 0 already recorded, 0 not recorded\./);
  await expect(toast(finance1, 'Batch payments recorded')).toBeVisible();

  const batch = await api.get<{ batch: { status: string } }>(`/finance/payout-batches/${batch1Id}`);
  expect(batch.batch.status).toBe('Completed');
  // A retried bulk request finds nothing left awaiting payment.
  const again = await raw(api.token, 'POST', `/admin/payments/payout-batches/${batch1Id}/mark-paid`, {
    paymentReference: `BULK-${s().runId}`,
    paidAt: new Date().toISOString(),
    confirm: true,
  });
  expect(again.status).toBe(409);
  expect(again.body).toMatchObject({ code: 'payout.batch_not_finalized' });
  errors.expectClean('the payments hub (payouts)');
});

test('reconciliation is balanced to the cent; the batch and reconciliation CSVs match', async ({ as }) => {
  const { ana, dan } = s().participants;
  const { batch1Id, batch1Reference, anaReference } = shared<BatchShared>();
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const recon = await api.get<{
    isBalanced: boolean;
    expected: number;
    recordedPaid: number;
    awaiting: number;
    failed: number;
    held: number;
    discrepancies: { type: string; severity: string }[];
    items: {
      user: { id: string };
      status: string;
      amount: number;
      earningsTotal: number;
      linkedEarningCount: number;
      ok: boolean;
    }[];
  }>(`/finance/payout-batches/${batch1Id}/reconciliation`);
  expect(recon.isBalanced).toBe(true);
  expect(recon.discrepancies.filter((d) => d.severity === 'error')).toEqual([]);
  expect(recon.failed).toBe(15.5);
  expect(recon.awaiting).toBe(0);
  // Held items are reported at their amount, but their earnings were released at finalize (Eve's 12.00 is back in
  // her balance): the item is OK with zero linked earnings.
  expect(recon.held).toBe(sum(recon.items.filter((i) => i.status === 'Held').map((i) => i.amount)));
  const eveItem = recon.items.find((i) => i.user.id === s().participants.eve.id)!;
  expect(eveItem).toMatchObject({ status: 'Held', amount: 12, ok: true, linkedEarningCount: 0 });
  expect(recon.recordedPaid).toBe(sum([recon.expected, -15.5]));
  expect(recon.recordedPaid).toBe(sum(recon.items.filter((i) => i.status === 'Paid').map((i) => i.amount)));
  // The bulk reference was used on several items: a warning, never an error.
  if (recon.items.filter((i) => i.status === 'Paid').length > 2)
    expect(
      recon.discrepancies.some((d) => d.type === 'duplicate_payment_reference' && d.severity === 'warning'),
    ).toBe(true);

  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto(`/finance/batches/${batch1Id}/reconciliation`);
  await expect(finance1.getByText('Balanced', { exact: true })).toBeVisible();
  await expect(finance1.getByRole('group', { name: 'Recorded paid', exact: true })).toContainText(
    money(recon.recordedPaid),
  );
  await expect(finance1.getByRole('group', { name: 'Failed', exact: true })).toContainText(money(15.5));

  const [reconCsv] = await Promise.all([
    finance1.waitForEvent('download'),
    finance1
      .getByRole('button', { name: /Export|Download/ })
      .filter({ hasText: /CSV/ })
      .last()
      .click(),
  ]);
  const reconLines = readFileSync((await reconCsv.path())!, 'utf8').split(/\r?\n/);
  expect(reconLines.find((l) => l.includes(ana.email))).toMatch(/Paid,56\.98.*OK/);

  await finance1.getByRole('tab', { name: 'Items' }).click();
  const [batchCsv] = await Promise.all([
    finance1.waitForEvent('download'),
    finance1.getByRole('button', { name: 'Export CSV' }).first().click(),
  ]);
  expect(batchCsv.suggestedFilename()).toBe(`payout-batch-${batch1Reference}.csv`);
  const lines = readFileSync((await batchCsv.path())!, 'utf8').split(/\r?\n/);
  const anaLine = lines.find((l) => l.includes(ana.email))!;
  expect(anaLine).toContain('56.98');
  expect(anaLine).toContain(anaReference);
  expect(lines.find((l) => l.includes(dan.email))).toContain('Failed');
  errors.expectClean('reconciliation');
});

test('participants see their payout status: Ana paid (reference tail only), Dan failed', async ({ as }) => {
  const { ana, dan } = s().participants;
  const { batch1Reference, anaReference } = shared<BatchShared>();
  const anaPage = await as(ana, PARTICIPANT_LANDING);
  const errors = watchErrors(anaPage);
  await anaPage.goto('/app/payouts');
  const anaRow = anaPage
    .getByRole('table', { name: 'Payout history' })
    .getByRole('row')
    .filter({ hasText: batch1Reference });
  await expect(anaRow).toContainText('Paid');
  await expect(anaRow).toContainText(money(56.98));
  await anaRow.getByRole('link').first().click();
  await expect(anaPage.getByRole('heading', { level: 1, name: `Payout ${batch1Reference}` })).toBeVisible();
  await expect(anaPage.getByRole('region', { name: 'Summary' })).toContainText(anaReference.slice(-4));
  await expect(anaPage.getByRole('region', { name: 'Summary' })).not.toContainText(anaReference);
  errors.expectClean('Ana’s payouts');

  const danPage = await as(dan, PARTICIPANT_LANDING);
  await danPage.goto('/app/payouts');
  const danRow = danPage
    .getByRole('table', { name: 'Payout history' })
    .getByRole('row')
    .filter({ hasText: batch1Reference });
  await expect(danRow).toContainText('Failed');
  await expect(danRow).toContainText(money(15.5));
});
