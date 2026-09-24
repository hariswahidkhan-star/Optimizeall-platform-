import type { Locator, Page } from '@playwright/test';
import {
  ApiSession,
  CLIENT_LANDING,
  FINANCE_LANDING,
  accounts,
  expect,
  latestMail,
  modal,
  money,
  raw,
  state,
  sum,
  test,
  toast,
  watchErrors,
} from './support/finance';

/**
 * Finance journey, part 4 — the payments hub, incoming money, per currency:
 *   finance 1 issues a GBP invoice (3 × 333.33 = 999.99) for Wanderly and two USD invoices for Nimbus → records a
 *   400.00 GBP bank transfer with a double click (recorded once) → an overpayment and an amount with too many decimals
 *   are refused → she corrects the method (with a reason) → she may not refund her own payment; finance 2 refunds it
 *   (four-eyes) → the invoice balance is back to 999.99 and she marks it paid in full → a stale screen gets "changed by
 *   someone else" → Nimbus reports "I've paid" 250.00; the bank shows 249.50, so finance confirms 249.50 (balance
 *   0.50) → the client's second report is rejected with a reason the client sees → "send reminder now" emails the
 *   client once (a second one within the hour is refused) → the reminder job sends each due stage once → the KPIs moved
 *   by exactly these amounts, per currency (GBP never mixed with USD).
 */
test.describe.configure({ mode: 'serial' });

const s = () => state();
const today = () => new Date().toISOString().slice(0, 10);

interface Invoice {
  id: string;
  number: string;
  balance: number;
  amountPaid: number;
  status: string;
  concurrencyStamp: string;
  payments: { id: string; amount: number; reference: string; method: string; reversalKind: string | null }[];
}

let wanderly: Invoice;
let nimbusA: Invoice;
let nimbusB: Invoice;
const kpiBefore: Record<string, number> = {};

async function issue(api: ApiSession, clientAccountId: string, reference: string, quantity: number, unitPrice: number) {
  const draft = await api.post<Invoice>('/agency/billing/invoices', {
    clientAccountId,
    reference,
    paymentTermsDays: 14,
    lines: [{ description: `Journey services ${reference}`, quantity, unitPrice }],
  });
  return api.post<Invoice>(`/agency/billing/invoices/${draft.id}/issue`, { concurrencyStamp: draft.concurrencyStamp });
}

function paymentRow(page: Page, text: string | RegExp): Locator {
  return page.getByRole('table', { name: 'Payments' }).getByRole('row').filter({ hasText: text });
}

async function search(page: Page, term: string) {
  const box = page.getByRole('searchbox', { name: 'Search payments' });
  if ((await box.inputValue()) === term) return;
  const loaded = page.waitForResponse((res) => {
    const url = new URL(res.url());
    return url.pathname === '/api/v1/admin/payments' && (url.searchParams.get('search') ?? '') === term && res.ok();
  });
  await box.fill(term);
  await loaded;
}

async function rowAction(page: Page, text: string | RegExp, action: string) {
  const row = paymentRow(page, text);
  await expect(row).toHaveCount(1);
  await row.getByRole('button', { name: /^Actions for / }).click();
  await page.getByRole('menuitem', { name: action }).click();
}

/** The amount a KPI tile shows for `currency` (0 when it has no line for it). */
async function kpi(page: Page, tile: string, currency: 'USD' | 'GBP'): Promise<number> {
  const group = page.getByRole('group', { name: 'Payment totals' }).getByRole('group', { name: tile });
  await expect(group).not.toHaveAttribute('aria-busy', 'true');
  const value = (await group.locator('.ui-stat__value').textContent()) ?? '';
  const re = currency === 'USD' ? /(?<![A-Z])\$([\d,]+\.\d{2})/ : /£([\d,]+\.\d{2})/;
  const match = re.exec(value);
  return match ? Number(match[1]!.replace(/,/g, '')) : 0;
}

async function invoice(api: ApiSession, id: string) {
  return api.get<Invoice>(`/agency/billing/invoices/${id}`);
}

test('arrange: finance 1 issues a GBP invoice for Wanderly and two USD invoices for Nimbus; KPIs before', async ({
  as,
}) => {
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  wanderly = await issue(api, s().clientIds.wanderly, `W-${s().runId}`, 3, 333.33);
  nimbusA = await issue(api, s().clientIds.nimbus, `NA-${s().runId}`, 1, 800);
  nimbusB = await issue(api, s().clientIds.nimbus, `NB-${s().runId}`, 1, 250);
  expect(wanderly.balance).toBe(999.99);
  expect([nimbusA.balance, nimbusB.balance]).toEqual([800, 250]);

  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  await finance1.goto('/finance/payments');
  for (const c of ['USD', 'GBP'] as const) kpiBefore[`received:${c}`] = await kpi(finance1, 'Received this month', c);
  for (const c of ['USD', 'GBP'] as const)
    kpiBefore[`outstanding:${c}`] = await kpi(finance1, 'Outstanding receivables', c);
});

test('a double-clicked bank transfer is recorded once; overpayment and sub-penny amounts are refused', async ({ as }) => {
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  errors.ignore(/HTTP 409 POST .*\/invoices\/.*\/payments$/);
  errors.ignore(/HTTP 400 POST .*\/invoices\/.*\/payments$/);
  await finance1.goto('/finance/payments');
  await search(finance1, wanderly.number);
  await expect(paymentRow(finance1, 'Invoice balance due')).toContainText('£999.99');

  let posts = 0;
  finance1.on('request', (r) => {
    if (r.method() === 'POST' && /\/admin\/payments\/invoices\/.*\/payments$/.test(r.url())) posts++;
  });
  await rowAction(finance1, 'Invoice balance due', 'Record payment');
  let dialog = modal(finance1, 'Record a payment');
  await dialog.getByLabel('Amount (GBP)').fill('400');
  await dialog.getByLabel('Reference').fill(`BT-${s().runId}-W1`);
  await dialog.getByRole('button', { name: 'Record payment' }).dblclick();
  await expect(toast(finance1, /^Payment (recorded|was already recorded)/)).toBeVisible();
  await expect(dialog).toBeHidden();
  let fresh = await invoice(await ApiSession.login(accounts.finance1.email, accounts.finance1.password), wanderly.id);
  expect(fresh.payments).toHaveLength(1);
  expect(fresh.balance).toBe(599.99);
  expect(posts).toBeGreaterThanOrEqual(1);
  await expect(paymentRow(finance1, 'Invoice balance due')).toContainText('£599.99');

  // Overpayment: one penny more than the balance.
  await rowAction(finance1, 'Invoice balance due', 'Record payment');
  dialog = modal(finance1, 'Record a payment');
  await dialog.getByLabel('Amount (GBP)').fill('600.00');
  await dialog.getByLabel('Reference').fill(`BT-${s().runId}-W2`);
  await dialog.getByRole('button', { name: 'Record payment' }).click();
  await expect(dialog.getByRole('alert')).toContainText('That is more than the outstanding balance.');
  // Sub-penny: GBP has two decimals.
  await dialog.getByLabel('Amount (GBP)').fill('10.005');
  await dialog.getByRole('button', { name: 'Record payment' }).click();
  await expect(dialog.getByRole('alert')).toContainText('Enter a valid amount for this currency.');
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  fresh = await invoice(await ApiSession.login(accounts.finance1.email, accounts.finance1.password), wanderly.id);
  expect(fresh.payments).toHaveLength(1);
  expect(fresh.balance).toBe(599.99);
  errors.expectClean('recording a payment');
});

test('edit details with a reason; the recorder cannot refund; finance 2 refunds (four-eyes); mark paid in full', async ({
  as,
}) => {
  const ref = `BT-${s().runId}-W1`;
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto('/finance/payments');
  await search(finance1, wanderly.number);
  await rowAction(finance1, ref, 'Edit details');
  const edit = modal(finance1, 'Edit payment details');
  await edit.getByLabel('Method').selectOption('Cheque');
  await edit.getByLabel('Reason for the change').fill('It was a cheque, not a transfer');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(toast(finance1, 'Payment updated')).toBeVisible();
  await expect(paymentRow(finance1, ref)).toContainText(/Cheque/i);

  await paymentRow(finance1, ref).getByRole('button', { name: /^Actions for / }).click();
  await expect(finance1.getByRole('menuitem', { name: 'Edit details' })).toBeVisible();
  await expect(finance1.getByRole('menuitem', { name: 'Record refund' })).toHaveCount(0);
  await finance1.keyboard.press('Escape');

  const finance2 = await as(accounts.finance2, FINANCE_LANDING);
  const errors2 = watchErrors(finance2);
  await finance2.goto('/finance/payments');
  await search(finance2, ref);
  await rowAction(finance2, ref, 'Record refund');
  const refund = modal(finance2, 'Refund this payment');
  await expect(refund.getByRole('radio', { name: /Refunded to the client/ })).toBeChecked();
  await refund.getByLabel('Reason').fill('Client paid twice; the second transfer went back');
  await refund.getByRole('button', { name: 'Record refund' }).click();
  await expect(toast(finance2, /refund/i)).toBeVisible();
  await expect(paymentRow(finance2, ref).filter({ hasText: 'Refunded' }).first()).toBeVisible();
  errors2.expectClean('refund (finance 2)');

  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const afterRefund = await invoice(api, wanderly.id);
  expect(afterRefund.balance).toBe(999.99);
  expect(afterRefund.amountPaid).toBe(0);

  await finance1.reload();
  await search(finance1, wanderly.number);
  await rowAction(finance1, 'Invoice balance due', 'Mark paid in full');
  const markPaid = modal(finance1, 'Mark invoice as paid in full');
  await markPaid.getByLabel('Reference').fill(`BT-${s().runId}-W3`);
  await markPaid.getByRole('button', { name: 'Mark as paid' }).click();
  await expect(toast(finance1, 'Invoice marked as paid')).toBeVisible();
  await expect(paymentRow(finance1, `BT-${s().runId}-W3`)).toContainText('£999.99');
  await expect(paymentRow(finance1, 'Invoice balance due')).toHaveCount(0);
  const paid = await invoice(api, wanderly.id);
  expect(paid).toMatchObject({ status: 'Paid', balance: 0, amountPaid: 999.99 });
  errors.expectClean('edit / mark paid');
});

test('a stale screen: another user recorded a payment meanwhile → "changed by someone else", nothing recorded', async ({
  as,
}) => {
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  errors.ignore(/HTTP 409 POST .*\/invoices\/.*\/payments$/);
  await finance1.goto('/finance/payments');
  await search(finance1, nimbusA.number);
  await expect(paymentRow(finance1, 'Invoice balance due')).toContainText(money(800));

  // Finance 2 records 100.00 through the API while finance 1's screen still shows the old invoice.
  const f2 = await ApiSession.login(accounts.finance2.email, accounts.finance2.password);
  await f2.post(`/admin/payments/invoices/${nimbusA.id}/payments`, {
    requestId: crypto.randomUUID(),
    amount: 100,
    method: 'BankTransfer',
    reference: `BT-${s().runId}-NA1`,
    paidOn: today(),
    concurrencyStamp: nimbusA.concurrencyStamp,
  });

  await rowAction(finance1, 'Invoice balance due', 'Record payment');
  const dialog = modal(finance1, 'Record a payment');
  await dialog.getByLabel('Amount (USD)').fill('700');
  await dialog.getByLabel('Reference').fill(`BT-${s().runId}-NA2`);
  await dialog.getByRole('button', { name: 'Record payment' }).click();
  await expect(dialog.getByRole('alert')).toContainText(/changed/i);
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  const fresh = await invoice(f2, nimbusA.id);
  expect(fresh.payments.map((p) => p.reference)).toEqual([`BT-${s().runId}-NA1`]);
  expect(fresh.balance).toBe(700);
  errors.expectClean('stale record');
});

test('client "I’ve paid": a corrected confirmation (249.50) and a rejected report the client sees', async ({ as }) => {
  const client = await as(accounts.nimbusBilling, CLIENT_LANDING);
  const clientErrors = watchErrors(client);
  await client.goto(`/client/billing/invoices/${nimbusB.id}`);
  await client.getByRole('button', { name: 'I’ve paid' }).click();
  let claim = modal(client, 'I’ve paid this invoice');
  await expect(claim.getByLabel('Amount paid (USD)')).toHaveValue('250');
  await claim.getByLabel('Transfer reference').fill(`CL-${s().runId}-1`);
  await claim.getByRole('button', { name: 'Send' }).click();
  await expect(toast(client, 'Thanks! We’ll confirm your payment shortly.')).toBeVisible();

  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  await finance1.goto('/finance/payments');
  await search(finance1, `CL-${s().runId}-1`);
  await rowAction(finance1, `CL-${s().runId}-1`, 'Confirm payment');
  const confirm = modal(finance1, 'Confirm the client’s payment');
  await confirm.getByLabel('Amount received (USD)').fill('249.50');
  await confirm.getByRole('button', { name: 'Confirm and record' }).click();
  await expect(toast(finance1, 'Payment confirmed and recorded')).toBeVisible();
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  let fresh = await invoice(api, nimbusB.id);
  expect(fresh).toMatchObject({ amountPaid: 249.5, balance: 0.5 });

  // The client reports the remaining 0.50, but no such transfer arrived: rejected with a reason.
  await client.reload();
  await client.getByRole('button', { name: 'I’ve paid' }).click();
  claim = modal(client, 'I’ve paid this invoice');
  await expect(claim.getByLabel('Amount paid (USD)')).toHaveValue('0.5');
  await claim.getByLabel('Transfer reference').fill(`CL-${s().runId}-2`);
  await claim.getByRole('button', { name: 'Send' }).click();
  await expect(toast(client, 'Thanks! We’ll confirm your payment shortly.')).toBeVisible();

  await search(finance1, `CL-${s().runId}-2`);
  await rowAction(finance1, `CL-${s().runId}-2`, 'Reject report');
  const reject = modal(finance1, 'Reject the client’s payment report');
  await reject.getByLabel('Reason (shown to the client)').fill('No transfer with this reference on our statement');
  await reject.getByRole('button', { name: 'Reject report' }).click();
  await expect(toast(finance1, 'Report rejected')).toBeVisible();
  await expect(paymentRow(finance1, `CL-${s().runId}-2`)).toContainText('Voided');
  fresh = await invoice(api, nimbusB.id);
  expect(fresh.balance).toBe(0.5);

  await client.reload();
  const reported = client.getByRole('table', { name: 'Payments you reported' });
  await expect(reported.getByRole('row').filter({ hasText: `CL-${s().runId}-2` })).toContainText('Not matched');
  await expect(reported.getByRole('row').filter({ hasText: `CL-${s().runId}-2` })).toContainText(
    'No transfer with this reference on our statement',
  );
  await expect(reported.getByRole('row').filter({ hasText: `CL-${s().runId}-1` })).toContainText('Confirmed');
  await expect(
    client.getByRole('table', { name: 'Payments received' }).getByRole('row').filter({ hasText: `CL-${s().runId}-1` }),
  ).toContainText(money(249.5));
  clientErrors.expectClean('the client portal');
  errors.expectClean('claims');
});

test('reminders: "send now" emails the client once per hour; the reminder job sends each due stage once', async ({
  as,
}) => {
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  const errors = watchErrors(finance1);
  errors.ignore(/HTTP 409 POST .*\/reminders$/);
  await finance1.goto('/finance/payments');
  await search(finance1, nimbusB.number);
  await rowAction(finance1, 'Invoice balance due', 'Send reminder now');
  let dialog = modal(finance1, 'Send a payment reminder now');
  await dialog.getByRole('button', { name: 'Send reminder' }).click();
  await expect(toast(finance1, 'Reminder sent')).toBeVisible();
  const mail = await latestMail(accounts.nimbusBilling.email, new RegExp(nimbusB.number));
  expect(mail.text).toContain(nimbusB.number);

  await rowAction(finance1, 'Invoice balance due', 'Send reminder now');
  dialog = modal(finance1, 'Send a payment reminder now');
  await dialog.getByRole('button', { name: 'Send reminder' }).click();
  await expect(dialog.getByRole('alert')).toContainText('A reminder was sent for this invoice less than an hour ago.');
  await dialog.getByRole('button', { name: 'Cancel' }).click();

  // The scheduled job (run by the admin): every reminder the preview lists is sent once, and a second run sends none.
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const admin = await ApiSession.login(accounts.admin.email, accounts.admin.password);
  type Preview = { invoiceId: string; kind: string; alreadySent: boolean }[];
  const before = await api.get<Preview>('/admin/payments/reminders/preview');
  const due = before.filter((r) => !r.alreadySent);
  const history = async (id: string) => (await api.get<unknown[]>(`/admin/payments/invoices/${id}/reminders`)).length;
  const counts = new Map<string, number>();
  for (const r of before) counts.set(r.invoiceId, await history(r.invoiceId));
  const run1 = await raw<{ status: string }>(admin.token, 'POST', '/admin/jobs/billing.overdue-and-reminders/run');
  expect(run1.status).toBe(200);
  const after = await api.get<Preview>('/admin/payments/reminders/preview');
  for (const r of due) {
    expect(after.find((x) => x.invoiceId === r.invoiceId && x.kind === r.kind)?.alreadySent).toBe(true);
    expect(await history(r.invoiceId)).toBe(counts.get(r.invoiceId)! + 1);
  }
  await raw(admin.token, 'POST', '/admin/jobs/billing.overdue-and-reminders/run');
  for (const r of due) expect(await history(r.invoiceId)).toBe(counts.get(r.invoiceId)! + 1);
  errors.expectClean('reminders');
});

test('KPIs moved by exactly these amounts, per currency', async ({ as }) => {
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  await finance1.goto('/finance/payments');
  // GBP: 400 recorded − 400 refunded + 999.99 marked paid. USD: 100 + 249.50.
  await expect
    .poll(() => kpi(finance1, 'Received this month', 'GBP'))
    .toBe(sum([kpiBefore['received:GBP']!, 999.99]));
  await expect
    .poll(() => kpi(finance1, 'Received this month', 'USD'))
    .toBe(sum([kpiBefore['received:USD']!, 100, 249.5]));
  // Outstanding: the new GBP invoice is settled; the USD invoices add 700.00 + 0.50.
  await expect
    .poll(() => kpi(finance1, 'Outstanding receivables', 'GBP'))
    .toBe(kpiBefore['outstanding:GBP']!);
  await expect
    .poll(() => kpi(finance1, 'Outstanding receivables', 'USD'))
    .toBe(sum([kpiBefore['outstanding:USD']!, 700, 0.5]));
  await expect(
    finance1.getByRole('group', { name: 'Payment totals' }).getByRole('group', { name: 'Received this month' }),
  ).toContainText('Refunded: £400.00');
  // The API agrees, one figure per currency.
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const summary = await api.get<{ incoming: { refundedThisMonth: { currency: string; amount: number }[] } }>(
    '/admin/payments/summary',
  );
  expect(summary.incoming.refundedThisMonth.find((r) => r.currency === 'GBP')?.amount).toBeGreaterThanOrEqual(400);
});
