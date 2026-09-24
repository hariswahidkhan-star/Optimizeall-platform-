import { readFileSync } from 'node:fs';
import type { Locator, Page } from '@playwright/test';
import {
  accounts,
  expect,
  test,
  axeViolations,
  clients,
  landing,
  modal,
  runId,
  toast,
  watchErrors,
} from './support/platform';

/**
 * Payments hub (Finance → Payments), incoming and outgoing:
 *   finance issues two Nimbus invoices → on invoice A records a manual bank transfer, corrects its reference, is
 *   refused the refund of their own payment (four-eyes) → a second finance user reverses it → finance marks invoice A
 *   paid in full → the Nimbus billing contact reports "I've paid" invoice B in the client portal → finance confirms it
 *   → a participant payout awaiting payment is marked paid by hand with a reference → the KPIs moved by exactly those
 *   amounts → the CSV export downloads with the new rows.
 */
test.describe.configure({ mode: 'serial' });

const FINANCE_LANDING = /\/(finance|agency)(\/|$)/;

/** Issues a draft invoice for Nimbus Fitness through Agency → Billing; returns its number and id. */
async function issueNimbusInvoice(
  page: Page,
  reference: string,
  description: string,
  quantity: number,
  price: number,
) {
  await page.goto('/agency/billing/invoices/new');
  await expect(page.getByRole('heading', { level: 1, name: 'New invoice' })).toBeVisible();
  await page.getByLabel('Client').selectOption({ label: `${clients.nimbus.name} (USD)` });
  await page.getByLabel('Reference').fill(reference);
  await page.getByLabel('Line 1 description').fill(description);
  await page.getByLabel('Quantity').fill(String(quantity));
  await page.getByLabel(/^Unit price/).fill(String(price));
  await page.getByRole('button', { name: 'Create draft' }).click();
  await expect(toast(page, 'Draft invoice created')).toBeVisible();
  await expect(page).toHaveURL(/\/agency\/billing\/invoices\/[0-9a-f-]{36}$/);
  const invoiceId = page.url().split('/').pop()!;
  await page.getByRole('button', { name: 'Issue', exact: true }).click();
  await expect(toast(page, 'Invoice issued')).toBeVisible();
  const heading = page.getByRole('heading', { level: 1, name: /^Invoice \S+$/ });
  await expect(heading).toBeVisible();
  const number = (await heading.textContent())!.replace('Invoice ', '').trim();
  return { id: invoiceId, number };
}

/** The Payments table row containing `text`. */
function paymentRow(page: Page, text: string | RegExp): Locator {
  return page.getByRole('table', { name: 'Payments' }).getByRole('row').filter({ hasText: text });
}

/** Opens the row menu of the only row matching `text` and picks `action`. */
async function rowAction(page: Page, text: string | RegExp, action: string) {
  const row = paymentRow(page, text);
  await expect(row).toHaveCount(1);
  await row.getByRole('button', { name: /^Actions for / }).click();
  await page.getByRole('menuitem', { name: action }).click();
}

/** Types into the (debounced) search and waits until the list for exactly that search has been loaded. */
async function searchPayments(page: Page, term: string) {
  const search = page.getByRole('searchbox', { name: 'Search payments' });
  if ((await search.inputValue()) === term) return;
  const loaded = page.waitForResponse((res) => {
    const url = new URL(res.url());
    return (
      url.pathname === '/api/v1/admin/payments' && (url.searchParams.get('search') ?? '') === term && res.ok()
    );
  });
  await search.fill(term);
  await loaded;
}

/** Picks a list filter (Type → kind, Status → status, …) and waits until the list with that filter has been loaded. */
async function filterPayments(
  page: Page,
  filter: 'Type' | 'Status',
  label: string,
  param: string,
  value: string,
) {
  const loaded = page.waitForResponse((res) => {
    const url = new URL(res.url());
    return url.pathname === '/api/v1/admin/payments' && url.searchParams.get(param) === value && res.ok();
  });
  await page.getByRole('combobox', { name: filter }).selectOption({ label });
  await loaded;
}

/** The USD amount a KPI tile shows once the summary has loaded (0 when it shows no USD line). */
async function usdIn(page: Page, tile: string): Promise<number> {
  const group = page.getByRole('group', { name: 'Payment totals' }).getByRole('group', { name: tile });
  // While the summary loads the tile is aria-busy and shows a skeleton instead of a value.
  await expect(group).toBeVisible();
  await expect(group).not.toHaveAttribute('aria-busy', 'true');
  const match = /\$([\d,]+\.\d{2})/.exec((await group.textContent()) ?? '');
  return match ? Number(match[1]!.replace(/,/g, '')) : 0;
}

const usd = (n: number) =>
  `$${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

test('finance records, corrects, reverses (four-eyes) and settles client payments; pays a payout by hand', async ({
  as,
}) => {
  const id = runId();
  const refA = `BT-${id}-1`;
  const refAFixed = `BT-${id}-1A`;
  const refA2 = `BT-${id}-2`;
  const claimRef = `CL-${id}`;
  const payoutRef = `PO-${id}`;

  // ---------------------------------------------------------------- finance issues two invoices
  const finance = await as(accounts.finance, FINANCE_LANDING);
  const errors = watchErrors(finance);
  const invoiceA = await issueNimbusInvoice(finance, `PO-A-${id}`, `Retainer A ${id}`, 2, 750); // $1,500.00
  const invoiceB = await issueNimbusInvoice(finance, `PO-B-${id}`, `Retainer B ${id}`, 1, 400); // $400.00

  // ---------------------------------------------------------------- the hub and its KPIs
  await finance.goto('/finance');
  await finance
    .getByRole('navigation', { name: 'Finance navigation' })
    .getByRole('link', { name: 'Payments' })
    .click();
  await expect(finance.getByRole('heading', { level: 1, name: 'Payments' })).toBeVisible();
  const receivedBefore = await usdIn(finance, 'Received this month');
  const paidOutBefore = await usdIn(finance, 'Paid out this month');
  expect(await axeViolations(finance), 'axe violations on the payments hub').toEqual([]);

  // ---------------------------------------------------------------- record a manual bank transfer on invoice A
  await searchPayments(finance, invoiceA.number);
  await expect(paymentRow(finance, 'Invoice balance due')).toContainText(usd(1500));
  await rowAction(finance, 'Invoice balance due', 'Record payment');
  const record = modal(finance, 'Record a payment');
  await record.getByLabel('Amount (USD)').fill('600');
  await expect(record.getByLabel('Method')).toHaveValue('BankTransfer');
  await record.getByLabel('Reference').fill(refA);
  await record.getByRole('button', { name: 'Record payment' }).click();
  await expect(toast(finance, 'Payment recorded')).toBeVisible();
  await expect(record).toBeHidden();
  await expect(paymentRow(finance, refA)).toContainText('Paid');
  await expect(paymentRow(finance, 'Invoice balance due')).toContainText(usd(900));

  // ---------------------------------------------------------------- correct its reference
  await rowAction(finance, refA, 'Edit details');
  const edit = modal(finance, 'Edit payment details');
  await edit.getByLabel('Reference').fill(refAFixed);
  await edit.getByLabel('Reason for the change').fill('Typo in the bank reference');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(toast(finance, 'Payment updated')).toBeVisible();
  await expect(paymentRow(finance, refAFixed)).toBeVisible();
  await expect(paymentRow(finance, new RegExp(`${refA}(?!A)`))).toHaveCount(0);

  // ---------------------------------------------------------------- four-eyes: the recorder cannot refund it
  errors.ignore(/HTTP 403 POST .*\/api\/v1\/admin\/payments\/invoice-payments\/[0-9a-f-]+\/reverse$/);
  await rowAction(finance, refAFixed, 'Record refund');
  const refund = modal(finance, 'Refund this payment');
  await refund.getByLabel('Reason').fill('Client asked for the money back');
  await refund.getByRole('button', { name: 'Record refund' }).click();
  await expect(refund.getByRole('alert')).toHaveText(
    'You recorded this payment, so a different finance user must record its refund.',
  );
  await refund.getByRole('button', { name: 'Cancel' }).click();
  await expect(paymentRow(finance, refAFixed)).toContainText('Paid');

  // ---------------------------------------------------------------- a second finance user reverses it
  const finance2 = await as(accounts.finance2, FINANCE_LANDING);
  const errors2 = watchErrors(finance2);
  await finance2.goto('/finance/payments');
  await searchPayments(finance2, refAFixed);
  await rowAction(finance2, refAFixed, 'Reverse (recorded in error)');
  const reverse = modal(finance2, 'Reverse this payment');
  await expect(reverse.getByRole('radio', { name: /Recorded in error/ })).toBeChecked();
  await reverse.getByLabel('Reason').fill('Recorded against the wrong invoice');
  await reverse.getByRole('button', { name: 'Reverse payment' }).click();
  await expect(toast(finance2, 'Payment reversed')).toBeVisible();
  await expect(paymentRow(finance2, refAFixed).filter({ hasText: 'Voided' }).first()).toBeVisible();
  errors2.expectClean('the second finance user');
  await finance2.context().close();

  // ---------------------------------------------------------------- finance marks invoice A paid in full
  await finance.reload();
  await searchPayments(finance, invoiceA.number);
  await expect(paymentRow(finance, 'Invoice balance due')).toContainText(usd(1500));
  await rowAction(finance, 'Invoice balance due', 'Mark paid in full');
  const markPaid = modal(finance, 'Mark invoice as paid in full');
  await markPaid.getByLabel('Reference').fill(refA2);
  await markPaid.getByRole('button', { name: 'Mark as paid' }).click();
  await expect(toast(finance, 'Invoice marked as paid')).toBeVisible();
  await expect(paymentRow(finance, refA2)).toContainText(usd(1500));
  // Nothing left to collect: the invoice leaves the "balance due" list.
  await expect(paymentRow(finance, 'Invoice balance due')).toHaveCount(0);

  // ---------------------------------------------------------------- the client reports "I've paid" invoice B
  const client = await as(accounts.nimbusBilling, landing.client);
  const clientErrors = watchErrors(client);
  await client.goto(`/client/billing/invoices/${invoiceB.id}`);
  // The page header and the invoice document both title it.
  await expect(
    client.getByRole('heading', { level: 1, name: `Invoice ${invoiceB.number}` }).first(),
  ).toBeVisible();
  await client.getByRole('button', { name: 'I’ve paid' }).click();
  const claim = modal(client, 'I’ve paid this invoice');
  await expect(claim.getByLabel('Amount paid (USD)')).toHaveValue('400');
  await claim.getByLabel('Transfer reference').fill(claimRef);
  await claim.getByRole('button', { name: 'Send' }).click();
  await expect(toast(client, 'Thanks! We’ll confirm your payment shortly.')).toBeVisible();
  const reported = client.getByRole('table', { name: 'Payments you reported' });
  await expect(reported.getByRole('row').filter({ hasText: claimRef })).toContainText(
    'Waiting for confirmation',
  );

  // ---------------------------------------------------------------- finance confirms it
  await finance.reload();
  await searchPayments(finance, claimRef);
  await expect(paymentRow(finance, claimRef)).toContainText('Pending');
  await rowAction(finance, claimRef, 'Confirm payment');
  const confirm = modal(finance, 'Confirm the client’s payment');
  await expect(confirm.getByLabel('Amount received (USD)')).toHaveValue('400');
  await confirm.getByRole('button', { name: 'Confirm and record' }).click();
  await expect(toast(finance, 'Payment confirmed and recorded')).toBeVisible();
  await searchPayments(finance, invoiceB.number);
  await expect(paymentRow(finance, claimRef)).toContainText('Invoice payment');
  await expect(paymentRow(finance, claimRef)).toContainText('Paid');
  await expect(paymentRow(finance, 'Invoice balance due')).toHaveCount(0);

  await client.reload();
  await expect(reported.getByRole('row').filter({ hasText: claimRef })).toContainText('Confirmed');
  await expect(
    client.getByRole('table', { name: 'Payments received' }).getByRole('row').filter({ hasText: claimRef }),
  ).toContainText(usd(400));
  clientErrors.expectClean('the client portal');
  await client.context().close();

  // ---------------------------------------------------------------- outgoing: pay a participant payout by hand
  await searchPayments(finance, '');
  await filterPayments(finance, 'Type', 'Participant payout', 'kind', 'PayoutItem');
  await filterPayments(finance, 'Status', 'Pending', 'status', 'Pending');
  const payoutRow = finance.getByRole('table', { name: 'Payments' }).getByRole('row').nth(1);
  await expect(payoutRow).toContainText('Participant payout');
  const participant = (await payoutRow.getByRole('button').first().textContent())!
    .replace(/^Outgoing/, '')
    .trim();
  const payoutAmount = Number(
    /\$([\d,]+\.\d{2})/.exec((await payoutRow.textContent())!)![1]!.replace(/,/g, ''),
  );
  await payoutRow.getByRole('button', { name: `Actions for Participant payout ${participant}` }).click();
  await finance.getByRole('menuitem', { name: 'Mark paid' }).click();
  const payout = modal(finance, 'Mark payout as paid');
  await payout.getByLabel('Payment reference').fill(payoutRef);
  await payout.getByRole('button', { name: 'Record payment' }).click();
  await expect(toast(finance, 'Payout marked as paid')).toBeVisible();
  await filterPayments(finance, 'Status', 'Paid', 'status', 'Paid');
  await searchPayments(finance, payoutRef);
  await expect(paymentRow(finance, participant)).toContainText(usd(payoutAmount));

  // ---------------------------------------------------------------- the KPIs moved by exactly these amounts
  await finance.reload();
  await expect(finance.getByRole('heading', { level: 1, name: 'Payments' })).toBeVisible();
  // Received: 600 recorded and reversed (net 0) + 1,500 marked paid + 400 confirmed.
  await expect.poll(() => usdIn(finance, 'Received this month')).toBeCloseTo(receivedBefore + 1900, 2);
  await expect.poll(() => usdIn(finance, 'Paid out this month')).toBeCloseTo(paidOutBefore + payoutAmount, 2);

  // ---------------------------------------------------------------- CSV export
  await searchPayments(finance, invoiceA.number);
  await expect(paymentRow(finance, refA2)).toBeVisible();
  const [download] = await Promise.all([
    finance.waitForEvent('download'),
    finance.getByRole('button', { name: 'Export CSV' }).click(),
  ]);
  expect(download.suggestedFilename()).toMatch(/\.csv$/);
  const csv = readFileSync((await download.path())!, 'utf8');
  const lines = csv.trim().split(/\r?\n/);
  expect(lines.length).toBeGreaterThan(1);
  expect(csv).toContain(refA2);
  expect(csv).toContain(refAFixed);
  expect(csv).toContain(invoiceA.number);
  expect(csv).not.toContain(invoiceB.number); // the export honours the search
  errors.expectClean('the payments hub');
});
