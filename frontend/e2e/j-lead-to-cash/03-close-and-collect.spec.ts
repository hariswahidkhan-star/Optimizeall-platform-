import type { Locator, Page } from '@playwright/test';
import {
  API_URL,
  FINANCE_LANDING,
  accounts,
  apiAs,
  expect,
  isoDate,
  landing,
  latestMail,
  modal,
  pathOf,
  prospect,
  recall,
  remember,
  runId,
  statusOf,
  test,
  toast,
  watchErrors,
} from './support/journey';

/**
 * Lead to cash, part 3 — close and collect:
 *   the prospect opens the tokenized proposal (the sales rep is told it was viewed), accepts it with a typed signature
 *   (accepting twice is refused) → a client account is created and the signer is invited as Owner (dev mailbox) → the
 *   retainer contract bills through the recurring invoice job, which issues and emails the invoice (idempotent on a
 *   rerun) → the client opens /i/:token and, signed in, the portal's billing, reports "I've paid" → finance confirms →
 *   the invoice is Paid. Negatives: a withdrawn proposal's link is dead, another organisation's client gets 404s,
 *   sales/finance/account-manager permissions are enforced (403) and stale edits get 409.
 */
test.describe.configure({ mode: 'serial' });

async function openInbox(page: Page) {
  await page.getByRole('button', { name: /^Notifications( \(\d+ unread\))?$/ }).click();
  return page.getByRole('dialog', { name: 'Notifications' });
}

test('the prospect opens and accepts the proposal; the client account and owner invitation follow', async ({ as, anonymous }) => {
  const lead = prospect();
  const proposalLink = recall('proposalLink');
  const client = await anonymous();
  const errors = watchErrors(client);

  await client.goto(pathOf(proposalLink));
  await expect(client.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(client.getByText(`${lead.company} growth retainer`).first()).toBeVisible();

  // The sales rep is told the proposal was opened (first view only).
  const sales = await as(accounts.sales, landing.agency);
  const inbox = await openInbox(sales);
  await expect(inbox.getByRole('link', { name: /^Proposal \S+ was opened$/ }).first()).toBeVisible();
  await sales.keyboard.press('Escape');
  await sales.goto(recall('proposalUrl'));
  await expect(sales.getByText('Viewed', { exact: true }).first()).toBeVisible();

  const accept = client.getByRole('form', { name: 'Accept proposal' });
  await accept.getByRole('button', { name: 'Accept proposal' }).click(); // nothing typed: refused client-side
  await expect(accept.getByRole('checkbox', { name: 'I agree to the terms of this proposal' })).not.toBeChecked();
  await accept.getByLabel('Full name').fill(lead.name);
  await accept.getByLabel('Job title').fill('Head of Marketing');
  await accept.getByRole('checkbox', { name: 'I agree to the terms of this proposal' }).check();
  await accept.getByRole('button', { name: 'Accept proposal' }).dblclick(); // a double click accepts once
  await expect(client.getByRole('status', { name: 'Proposal accepted' })).toContainText(`Accepted by ${lead.name}`);

  // Accepting again (a second tab, a replayed request) is refused.
  const token = pathOf(proposalLink).split('/p/')[1]!;
  const again = await fetch(`${API_URL}/api/v1/public/proposals/${token}/accept`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
    body: JSON.stringify({ version: 1, fullName: lead.name, title: 'CEO', agreeToTerms: true }),
  });
  expect(again.status).toBe(409);
  expect(((await again.json()) as { code: string }).code).toBe('proposal.already_accepted');
  await client.reload();
  await expect(client.getByRole('form', { name: 'Accept proposal' })).toHaveCount(0);
  errors.expectClean('the public proposal page');

  // The deal is Won and the proposal links the new retainer contract (no first invoice: it was switched off).
  await sales.goto(recall('proposalUrl'));
  await expect(sales.getByText(`${lead.name} (Head of Marketing) · v1`, { exact: false })).toBeVisible();
  await expect(sales.getByRole('link', { name: 'Contract 1' })).toBeVisible();
  await expect(sales.getByRole('link', { name: 'Open first invoice' })).toHaveCount(0);
  const contractUrl = new URL((await sales.getByRole('link', { name: 'Contract 1' }).getAttribute('href'))!, 'http://x').pathname;
  await sales.goto(recall('dealUrl'));
  await expect(sales.getByText(/ · Won \(100%\)$/)).toBeVisible();

  // The signer was invited as the new client's Owner: set a password from the email and sign in.
  const invite = await latestMail(lead.email, /password/i);
  const resetLink = invite.links.find((l) => l.includes('/reset-password'));
  expect(resetLink, 'the invitation carries a set-password link').toBeTruthy();
  await client.goto(pathOf(resetLink!));
  const form = client.getByRole('form', { name: 'Choose a new password' });
  await form.getByLabel('New password', { exact: true }).fill(lead.password);
  await form.getByLabel('Confirm new password').fill(lead.password);
  await form.getByRole('button').last().click();
  await expect(client).toHaveURL(/\/login/);
  await client.getByLabel('Email', { exact: true }).fill(lead.email);
  await client.getByLabel('Password', { exact: true }).fill(lead.password);
  await client.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(client).toHaveURL(landing.client);
  await client.goto('/client/billing');
  await expect(client.getByRole('heading', { level: 1, name: 'Billing' })).toBeVisible();
  await client.getByRole('tab', { name: 'Contracts' }).click();
  await expect(client.getByRole('table', { name: 'Contracts' }).getByRole('row').nth(1)).toContainText('Active');

  remember({ contractUrl });
});

test('the recurring invoice job issues the retainer invoice exactly once', async ({ as }) => {
  const lead = prospect();
  const contractUrl = recall('contractUrl');

  // Sales reps can't manage contracts; the account manager can.
  const salesApi = await apiAs(accounts.sales);
  expect(await statusOf(salesApi.get('/agency/contracts'))).toMatchObject({ status: 403 });

  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(contractUrl);
  await expect(am.getByRole('heading', { level: 1, name: new RegExp(`growth retainer — Monthly retainer$`) })).toBeVisible();
  const contractId = contractUrl.split('/').pop()!;
  const amApi = await apiAs(accounts.am);
  const before = await amApi.get<{ concurrencyStamp: string }>(`/agency/contracts/${contractId}`);

  await am.getByRole('link', { name: 'Edit' }).click();
  await am.getByLabel('Generated invoices').selectOption({ label: 'Issue automatically' });
  await am.getByRole('button', { name: 'Save contract' }).click();
  await expect(toast(am, 'Contract saved')).toBeVisible();

  // A stale edit (the stamp read before the save) is a 409.
  const stale = await statusOf(amApi.put(`/agency/contracts/${contractId}`, { ...(before as object), concurrencyStamp: before.concurrencyStamp }));
  expect(stale.status).toBe(409);

  // Account managers see billing but can't issue or record payments (billing.manage).
  expect(await statusOf(amApi.post('/agency/billing/jobs/recurring-invoices/run'))).toMatchObject({ status: 403 });

  // The admin runs the recurring invoice job (Admin → Jobs → Run now), twice.
  const admin = await as(accounts.admin, landing.admin);
  const adminErrors = watchErrors(admin);
  for (let i = 0; i < 2; i++) {
    await admin.goto('/admin/jobs');
    await admin.getByRole('button', { name: 'Run RecurringInvoiceJob now' }).click();
    const ran = admin.waitForResponse((r) => r.request().method() === 'POST' && /\/admin\/jobs\/.+\/run$/.test(new URL(r.url()).pathname));
    await modal(admin, 'Run RecurringInvoiceJob now?').getByRole('button', { name: 'Run now' }).click();
    expect((await ran).ok()).toBe(true);
    await expect(admin.getByRole('row').filter({ hasText: 'RecurringInvoiceJob' })).toContainText(i === 0 ? /Created [1-9]\d* invoice/ : /Created 0 invoice/); // the rerun creates nothing
  }

  const contract = await amApi.get<{ invoices: { id: string; number: string | null; status: string }[] }>(`/agency/contracts/${contractId}`);
  expect(contract.invoices, 'one invoice for the first period, even after two runs').toHaveLength(1);
  const invoice = contract.invoices[0]!;
  expect(invoice.status).toBe('Issued');
  expect(invoice.number).toMatch(/\S+/);

  // The client's Owner (the signer, also the billing address) is notified in-app and by email; the email goes out
  // through the notification outbox (NotificationDispatchJob), run here by hand because jobs are off in E2E.
  const adminApi = await apiAs(accounts.admin);
  await adminApi.post('/admin/jobs/NotificationDispatchJob/run');
  const mail = await latestMail(lead.email, new RegExp(`Invoice ${invoice.number}`));
  expect(mail.links.some((l) => new URL(l).pathname === `/client/billing/invoices/${invoice.id}`), 'the email links the portal invoice').toBe(true);

  // The private /i/ link finance shares ("Client view link").
  const financeApi = await apiAs(accounts.finance);
  const issued = await financeApi.get<{ publicUrl: string | null }>(`/agency/billing/invoices/${invoice.id}`);
  const invoiceLink = issued.publicUrl!;
  expect(invoiceLink).toMatch(/\/i\/[\w-]+$/);

  errors.expectClean('the contract');
  adminErrors.expectClean('the jobs page');
  remember({ invoiceId: invoice.id, invoiceNumber: invoice.number!, invoiceLink });
});

/** The Payments table row containing `text`. */
function paymentRow(page: Page, text: string | RegExp): Locator {
  return page.getByRole('table', { name: 'Payments' }).getByRole('row').filter({ hasText: text });
}

async function searchPayments(page: Page, term: string) {
  const search = page.getByRole('searchbox', { name: 'Search payments' });
  const loaded = page.waitForResponse((res) => {
    const url = new URL(res.url());
    return url.pathname === '/api/v1/admin/payments' && (url.searchParams.get('search') ?? '') === term && res.ok();
  });
  await search.fill(term);
  await loaded;
}

test('the client pays: public invoice, portal "I’ve paid", finance confirms, invoice Paid', async ({ as, anonymous }) => {
  const lead = prospect();
  const invoiceId = recall('invoiceId');
  const invoiceNumber = recall('invoiceNumber');
  const claimRef = `BT-${runId()}`;

  // The private /i/ link works without signing in.
  const viewer = await anonymous();
  const viewerErrors = watchErrors(viewer);
  await viewer.goto(pathOf(recall('invoiceLink')));
  await expect(viewer.getByText(invoiceNumber).first()).toBeVisible();
  await expect(viewer.getByText(/Social media management/).first()).toBeVisible();
  viewerErrors.expectClean('the public invoice page');

  // The client owner reports the payment in the portal (a double click sends one report).
  const client = await as({ email: lead.email, password: lead.password, displayName: lead.name }, landing.client);
  const clientErrors = watchErrors(client);
  await client.goto(`/client/billing/invoices/${invoiceId}`);
  await expect(client.getByRole('heading', { level: 1, name: `Invoice ${invoiceNumber}` }).first()).toBeVisible();
  await client.getByRole('button', { name: 'I’ve paid' }).click();
  const claim = modal(client, 'I’ve paid this invoice');
  const amount = await claim.getByLabel(/^Amount paid/).inputValue();
  expect(Number(amount)).toBeGreaterThan(0);
  await claim.getByLabel('Transfer reference').fill(claimRef);
  await claim.getByRole('button', { name: 'Send' }).dblclick();
  await expect(toast(client, 'Thanks! We’ll confirm your payment shortly.')).toBeVisible();
  const reported = client.getByRole('table', { name: 'Payments you reported' });
  await expect(reported.getByRole('row').filter({ hasText: claimRef })).toHaveCount(1);
  await expect(reported.getByRole('row').filter({ hasText: claimRef })).toContainText('Waiting for confirmation');

  // Finance is notified and confirms it in the payments hub.
  const finance = await as(accounts.finance, FINANCE_LANDING);
  const financeErrors = watchErrors(finance);
  const inbox = await openInbox(finance);
  await expect(inbox.getByText(new RegExp(invoiceNumber)).first()).toBeVisible();
  await finance.keyboard.press('Escape');
  await finance.goto('/finance/payments');
  await searchPayments(finance, claimRef);
  await expect(paymentRow(finance, claimRef)).toContainText('Pending');
  await paymentRow(finance, claimRef).getByRole('button', { name: /^Actions for / }).click();
  await finance.getByRole('menuitem', { name: 'Confirm payment' }).click();
  const confirm = modal(finance, 'Confirm the client’s payment');
  await confirm.getByRole('button', { name: 'Confirm and record' }).click();
  await expect(toast(finance, 'Payment confirmed and recorded')).toBeVisible();
  await searchPayments(finance, invoiceNumber);
  await expect(paymentRow(finance, 'Invoice balance due')).toHaveCount(0);

  await finance.goto(`/agency/billing/invoices/${invoiceId}`);
  await expect(finance.getByText('Paid', { exact: true }).first()).toBeVisible();

  await client.reload();
  await expect(reported.getByRole('row').filter({ hasText: claimRef })).toContainText('Confirmed');
  await expect(client.getByText('Paid', { exact: true }).first()).toBeVisible();

  clientErrors.expectClean('the client portal');
  financeErrors.expectClean('the payments hub');
});

test('tenancy, permissions and dead links', async ({ anonymous }) => {
  const lead = prospect();
  const id = runId();
  const invoiceId = recall('invoiceId');

  // Another organisation's client sees nothing of this client (404, not 403).
  const nimbus = await apiAs(accounts.nimbusOwner);
  expect(await statusOf(nimbus.get(`/client/billing/invoices/${invoiceId}`))).toMatchObject({ status: 404 });
  expect(await statusOf(nimbus.get(`/client/billing/invoices/${invoiceId}/payments`))).toMatchObject({ status: 404 });
  const nimbusInvoices = await nimbus.get<{ items: { id: string }[] }>('/client/billing/invoices');
  expect(nimbusInvoices.items.map((i) => i.id)).not.toContain(invoiceId);
  const proposals = await apiAs(accounts.am).then((am) =>
    am.get<{ items: { id: string; title: string }[] }>(`/agency/proposals?search=${encodeURIComponent(lead.company)}`),
  );
  const proposalId = proposals.items[0]!.id;
  expect(await statusOf(nimbus.get(`/client/billing/proposals/${proposalId}`))).toMatchObject({ status: 404 });
  // …and can't report a payment on it.
  expect(
    await statusOf(
      nimbus.post(`/client/billing/invoices/${invoiceId}/payment-claims`, {
        requestId: crypto.randomUUID(),
        amount: 1,
        method: 'BankTransfer',
        reference: `X-${id}`,
        paidOn: isoDate(0),
      }),
    ),
  ).toMatchObject({ status: 404 });

  // Staff permissions: sales can view invoices but not issue or record payments; finance can't send proposals.
  const sales = await apiAs(accounts.sales);
  expect(await statusOf(sales.get(`/agency/billing/invoices/${invoiceId}`))).toMatchObject({ status: 200 });
  expect(await statusOf(sales.post(`/agency/billing/invoices/${invoiceId}/send`))).toMatchObject({ status: 403 });
  expect(await statusOf(sales.post(`/admin/payments/invoices/${invoiceId}/mark-paid`, {}))).toMatchObject({ status: 403 });
  const finance = await apiAs(accounts.finance);
  expect(await statusOf(finance.post(`/agency/proposals/${proposalId}/withdraw`, { concurrencyStamp: crypto.randomUUID() }))).toMatchObject({ status: 403 });

  // A withdrawn proposal's link stops working.
  const draft = await sales.post<{ id: string; concurrencyStamp: string }>('/agency/proposals', {
    title: `Withdrawn offer ${id}`,
    validUntil: isoDate(10),
    recipientName: lead.name,
    recipientEmail: lead.email,
    terms: 'Standard terms',
    lines: [{ description: 'One-off audit', quantity: 1, unitPrice: 900, recurrence: 'OneTime' }],
  });
  const sent = await sales.post<{ shareUrl: string; proposal: { concurrencyStamp: string } }>(`/agency/proposals/${draft.id}/send`, {
    concurrencyStamp: draft.concurrencyStamp,
    email: false,
  });
  await sales.post(`/agency/proposals/${draft.id}/withdraw`, { concurrencyStamp: sent.proposal.concurrencyStamp, reason: 'Superseded' });
  const visitor = await anonymous();
  const errors = watchErrors(visitor);
  errors.ignore(/HTTP 404 GET .*\/public\/proposals\//);
  await visitor.goto(pathOf(sent.shareUrl));
  await expect(visitor.getByRole('heading', { level: 1, name: 'This proposal link isn’t valid' })).toBeVisible();
  // A tampered token is the same dead end.
  await visitor.goto(`${pathOf(sent.shareUrl)}x`);
  await expect(visitor.getByRole('heading', { level: 1, name: 'This proposal link isn’t valid' })).toBeVisible();
  errors.expectClean('the dead proposal links');
});
