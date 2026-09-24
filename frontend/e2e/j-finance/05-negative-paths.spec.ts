import type { Page } from '@playwright/test';
import {
  ADMIN_LANDING,
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

/**
 * Finance journey, part 5 — the paths that must not move money:
 *   - a billing-only user (custom role: billing.view + billing.manage) works incoming payments but never sees or
 *     touches payouts (403 on every payout endpoint, no outgoing rows, KPIs or navigation);
 *   - an admin "viewing as" a finance user can look but every money action is refused (payment, payout, adjustment,
 *     finalize, payment instructions), and the dialog says why;
 *   - the session expires while the record-payment dialog is open: the user is sent to sign in, nothing was recorded,
 *     and after signing in again the payment is recorded exactly once;
 *   - a dialog closed half-way records nothing; a request id reused for a different payment is refused;
 *   - retries with the same idempotency key (payout mark-paid with the same reference) are replays, a different
 *     reference is a conflict.
 */
test.describe.configure({ mode: 'serial' });

const s = () => state();
const today = () => new Date().toISOString().slice(0, 10);

interface Invoice {
  id: string;
  number: string;
  balance: number;
  concurrencyStamp: string;
  payments: { reference: string; amount: number }[];
}

async function openInvoice(api: ApiSession, reference: string, amount: number): Promise<Invoice> {
  const draft = await api.post<Invoice>('/agency/billing/invoices', {
    clientAccountId: s().clientIds.nimbus,
    reference,
    lines: [{ description: `Negative paths ${reference}`, quantity: 1, unitPrice: amount }],
  });
  return api.post<Invoice>(`/agency/billing/invoices/${draft.id}/issue`, {
    concurrencyStamp: draft.concurrencyStamp,
  });
}

async function searchPayments(page: Page, term: string) {
  const loaded = page.waitForResponse((res) => {
    const url = new URL(res.url());
    return (
      url.pathname === '/api/v1/admin/payments' && (url.searchParams.get('search') ?? '') === term && res.ok()
    );
  });
  await page.getByRole('searchbox', { name: 'Search payments' }).fill(term);
  await loaded;
}

function balanceRow(page: Page) {
  return page
    .getByRole('table', { name: 'Payments' })
    .getByRole('row')
    .filter({ hasText: 'Invoice balance due' });
}

test('a billing-only user works incoming payments but cannot see or touch payouts', async ({ as }) => {
  const admin = await ApiSession.login(accounts.admin.email, accounts.admin.password);
  const { role } = await admin.post<{ role: { id: string } }>('/admin/roles', {
    name: `Billing clerk ${s().runId}`,
    description: 'Incoming client payments only',
    permissions: ['billing.view', 'billing.manage'],
  });
  const clerk = await admin.post<{ id: string; email: string; password: string; displayName: string }>(
    '/admin/test-users',
    { roles: ['Designer'], displayName: `Billing Clerk ${s().runId}` },
  );
  await admin.put(`/admin/roles/${role.id}/users/${clerk.id}`);
  const api = await ApiSession.login(clerk.email, clerk.password);
  const { batch1Id, danItemId } = shared<{ batch1Id: string; danItemId: string }>();

  const caps = await api.get<{ incoming: boolean; outgoing: boolean; recordPayouts: boolean }>(
    '/admin/payments/capabilities',
  );
  expect(caps).toMatchObject({ incoming: true, outgoing: false, recordPayouts: false });
  for (const [method, path, body] of [
    ['GET', '/finance/payout-batches', undefined],
    ['GET', `/finance/payout-batches/${batch1Id}`, undefined],
    ['GET', `/finance/payout-batches/${batch1Id}/reconciliation`, undefined],
    ['GET', `/finance/payout-batches/${batch1Id}/payment-instructions.csv?confirm=true`, undefined],
    ['GET', '/finance/ledger', undefined],
    [
      'POST',
      `/admin/payments/payouts/${danItemId}/mark-paid`,
      { paymentReference: 'NOPE-123', paidAt: new Date().toISOString() },
    ],
    [
      'POST',
      `/admin/payments/payout-batches/${batch1Id}/mark-paid`,
      { paymentReference: 'NOPE-123', paidAt: new Date().toISOString(), confirm: true },
    ],
    ['POST', '/finance/holds', { userId: s().participants.ben.id, reason: 'Not allowed' }],
  ] as const) {
    const res = await raw(api.token, method, path, body);
    expect(res.status, `${method} ${path}`).toBe(403);
  }
  // The hub's list and export only ever carry incoming rows for this user.
  const outgoing = await api.get<{ items: { direction: string }[]; total: number }>(
    '/admin/payments?direction=Outgoing&pageSize=50',
  );
  expect(outgoing.total).toBe(0);
  const all = await api.get<{ items: { direction: string }[] }>('/admin/payments?pageSize=100');
  expect(all.items.every((r) => r.direction === 'Incoming')).toBe(true);
  const csv = await raw<string>(api.token, 'GET', '/admin/payments/export.csv');
  expect(csv.status).toBe(200);
  expect(csv.body).not.toContain('Outgoing');
  const summary = await api.get<{ outgoing: unknown; incoming: unknown }>('/admin/payments/summary');
  expect(summary.outgoing).toBeNull();
  expect(summary.incoming).not.toBeNull();

  // The UI matches: the finance portal shows Payments only, without payout KPIs or filters.
  const page = await as(
    { email: clerk.email, password: clerk.password, displayName: clerk.displayName },
    /\/(agency|finance)(\/|$)/,
  );
  const errors = watchErrors(page);
  await page.goto('/finance/payments');
  await expect(page.getByRole('heading', { level: 1, name: 'Payments' })).toBeVisible();
  const nav = page.getByRole('navigation', { name: 'Finance navigation' });
  await expect(nav.getByRole('link', { name: 'Payments' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Payout batches' })).toHaveCount(0);
  await expect(nav.getByRole('link', { name: 'Ledger' })).toHaveCount(0);
  const totals = page.getByRole('group', { name: 'Payment totals' });
  await expect(totals.getByRole('group', { name: 'Received this month' })).toBeVisible();
  await expect(totals.getByRole('group', { name: 'Payouts due' })).toHaveCount(0);
  await expect(page.getByRole('combobox', { name: 'Direction' })).toHaveCount(0);
  await expect(
    page.getByRole('table', { name: 'Payments' }).getByRole('row').filter({ hasText: 'Participant payout' }),
  ).toHaveCount(0);
  errors.expectClean('the payments hub (billing clerk)');
});

test('an admin viewing as a finance user is refused every money action, and told why', async ({ as }) => {
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const inv = await openInvoice(api, `IMP-${s().runId}`, 120);
  const { batch1Id, danItemId } = shared<{ batch1Id: string; danItemId: string }>();

  const admin = await as(accounts.admin, ADMIN_LANDING);
  const errors = watchErrors(admin);
  errors.ignore(/HTTP 403 POST .*\/admin\/payments\/invoices\/.*\/payments$/);
  await admin.goto('/admin/users');
  await admin.getByRole('searchbox', { name: 'Search users' }).fill(accounts.finance1.email);
  await admin.getByRole('button', { name: `Log in as ${accounts.finance1.displayName}` }).click();
  const confirm = modal(admin, `Log in as ${accounts.finance1.displayName}?`);
  await confirm.getByLabel(/Why do you need to view this account/).fill('Reproduce a payments hub question');
  await confirm.getByLabel(`Type ${accounts.finance1.email} to confirm`).fill(accounts.finance1.email);
  await confirm.getByRole('button', { name: 'Log in as user' }).click();
  await expect(admin).toHaveURL(FINANCE_LANDING);
  await expect(admin.getByRole('region', { name: 'Impersonation' })).toBeVisible();

  await admin.goto('/finance/payments');
  await searchPayments(admin, inv.number);
  await balanceRow(admin)
    .getByRole('button', { name: /^Actions for / })
    .click();
  await admin.getByRole('menuitem', { name: 'Record payment' }).click();
  const dialog = modal(admin, 'Record a payment');
  await dialog.getByLabel('Amount (USD)').fill('120');
  await dialog.getByLabel('Reference').fill(`IMP-${s().runId}-1`);
  await dialog.getByRole('button', { name: 'Record payment' }).click();
  await expect(dialog.getByRole('alert')).toContainText(
    'not available while you are viewing as another user',
  );
  await dialog.getByRole('button', { name: 'Cancel' }).click();

  // Every other money write is refused for the impersonation token too (403 auth.impersonation_forbidden_action).
  const adminApi = await ApiSession.login(accounts.admin.email, accounts.admin.password);
  const started = await adminApi.post<{ accessToken: string }>(`/admin/users/${api.user.id}/impersonate`, {
    reason: 'Check the refusals of money actions',
    confirm: true,
  });
  const token = started.accessToken;
  const detail = await api.get<{ concurrencyStamp: string }>(`/finance/payout-batches/${batch1Id}`);
  for (const [method, path, body] of [
    [
      'POST',
      `/admin/payments/invoices/${inv.id}/payments`,
      {
        requestId: crypto.randomUUID(),
        amount: 1,
        method: 'BankTransfer',
        reference: 'IMP-2',
        paidOn: today(),
        concurrencyStamp: inv.concurrencyStamp,
      },
    ],
    [
      'POST',
      `/admin/payments/payouts/${danItemId}/mark-paid`,
      { paymentReference: 'IMP-123', paidAt: new Date().toISOString() },
    ],
    [
      'POST',
      '/finance/adjustments',
      {
        requestId: crypto.randomUUID(),
        userId: s().participants.ben.id,
        amount: 5,
        currency: 'USD',
        reason: 'Impersonated credit attempt',
        confirm: true,
      },
    ],
    [
      'POST',
      `/finance/payout-batches/${batch1Id}/finalize`,
      { confirm: true, concurrencyStamp: detail.concurrencyStamp },
    ],
    ['GET', `/finance/payout-batches/${batch1Id}/payment-instructions.csv?confirm=true`, undefined],
  ] as const) {
    const res = await raw(token, method, path, body);
    expect(res.status, `${method} ${path}`).toBe(403);
    expect(res.body, `${method} ${path}`).toMatchObject({ code: 'auth.impersonation_forbidden_action' });
  }
  const fresh = await api.get<Invoice>(`/agency/billing/invoices/${inv.id}`);
  expect(fresh.payments).toHaveLength(0);
  expect(fresh.balance).toBe(120);
  errors.expectClean('impersonation');
});

test('the session expires while the dialog is open: sign in again, nothing was recorded, then it is recorded once', async ({
  as,
}) => {
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const inv = await openInvoice(api, `EXP-${s().runId}`, 75.25);
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  await finance1.goto('/finance/payments');
  await searchPayments(finance1, inv.number);
  await balanceRow(finance1)
    .getByRole('button', { name: /^Actions for / })
    .click();
  await finance1.getByRole('menuitem', { name: 'Record payment' }).click();
  let dialog = modal(finance1, 'Record a payment');
  await dialog.getByLabel('Amount (USD)').fill('75.25');
  await dialog.getByLabel('Reference').fill(`EXP-${s().runId}-1`);

  // While the user was typing the access token expired and the refresh cookie is gone (signed out elsewhere).
  await finance1.context().clearCookies();
  await finance1.route(
    '**/api/v1/admin/payments/invoices/*/payments',
    (route) =>
      route.fulfill({
        status: 401,
        contentType: 'application/problem+json',
        body: JSON.stringify({ status: 401, code: 'auth.token_expired', title: 'Expired' }),
      }),
    { times: 1 },
  );
  await dialog.getByRole('button', { name: 'Record payment' }).click();
  await expect(finance1).toHaveURL(/\/login\?expired=1&next=%2Ffinance%2Fpayments/);
  expect((await api.get<Invoice>(`/agency/billing/invoices/${inv.id}`)).payments).toHaveLength(0);

  // Signing in again returns to the hub; the payment is entered again and recorded exactly once.
  await finance1.getByLabel('Email', { exact: true }).fill(accounts.finance1.email);
  await finance1.getByLabel('Password', { exact: true }).fill(accounts.finance1.password);
  await finance1.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(finance1).toHaveURL(/\/finance\/payments$/);
  await searchPayments(finance1, inv.number);
  await balanceRow(finance1)
    .getByRole('button', { name: /^Actions for / })
    .click();
  await finance1.getByRole('menuitem', { name: 'Record payment' }).click();
  dialog = modal(finance1, 'Record a payment');
  await dialog.getByLabel('Amount (USD)').fill('75.25');
  await dialog.getByLabel('Reference').fill(`EXP-${s().runId}-1`);
  await dialog.getByRole('button', { name: 'Record payment' }).click();
  await expect(toast(finance1, 'Payment recorded')).toBeVisible();
  const fresh = await api.get<Invoice>(`/agency/billing/invoices/${inv.id}`);
  expect(fresh.payments.map((p) => p.amount)).toEqual([75.25]);
  expect(fresh.balance).toBe(0);
});

test('interrupted and replayed requests: a closed dialog records nothing; a reused request id is refused', async ({
  as,
}) => {
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const inv = await openInvoice(api, `INT-${s().runId}`, 300);
  const finance1 = await as(accounts.finance1, FINANCE_LANDING);
  await finance1.goto('/finance/payments');
  await searchPayments(finance1, inv.number);
  await balanceRow(finance1)
    .getByRole('button', { name: /^Actions for / })
    .click();
  await finance1.getByRole('menuitem', { name: 'Record payment' }).click();
  const dialog = modal(finance1, 'Record a payment');
  await dialog.getByLabel('Amount (USD)').fill('300');
  await dialog.getByLabel('Reference').fill(`INT-${s().runId}-1`);
  await finance1.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(balanceRow(finance1)).toContainText(money(300));
  expect((await api.get<Invoice>(`/agency/billing/invoices/${inv.id}`)).payments).toHaveLength(0);

  // API: the same request id replays (200, replayed) — a different payment under that id is refused.
  const requestId = crypto.randomUUID();
  const body = {
    requestId,
    amount: 100,
    method: 'BankTransfer',
    reference: `INT-${s().runId}-2`,
    paidOn: today(),
    concurrencyStamp: inv.concurrencyStamp,
  };
  const first = await raw<{ replayed: boolean }>(
    api.token,
    'POST',
    `/admin/payments/invoices/${inv.id}/payments`,
    body,
  );
  expect(first.status).toBe(201);
  const retry = await raw<{ replayed: boolean }>(
    api.token,
    'POST',
    `/admin/payments/invoices/${inv.id}/payments`,
    body,
  );
  expect(retry.status).toBe(200);
  expect(retry.body.replayed).toBe(true);
  const reused = await raw(api.token, 'POST', `/admin/payments/invoices/${inv.id}/payments`, {
    ...body,
    amount: 150,
  });
  expect(reused.status).toBe(409);
  expect(reused.body).toMatchObject({ code: 'billing.request_id_reused' });
  const fresh = await api.get<Invoice>(`/agency/billing/invoices/${inv.id}`);
  expect(fresh.payments.map((p) => p.amount)).toEqual([100]);
  expect(fresh.balance).toBe(200);
});

test('payout retries: the same reference again is a replay, a different one a conflict; nothing is paid twice', async () => {
  const { anaItemId, anaReference } = shared<{ anaItemId: string; anaReference: string }>();
  const api = await ApiSession.login(accounts.finance1.email, accounts.finance1.password);
  const replay = await raw<{ replayed: boolean }>(
    api.token,
    'POST',
    `/admin/payments/payouts/${anaItemId}/mark-paid`,
    {
      paymentReference: anaReference,
      paidAt: new Date().toISOString(),
    },
  );
  expect(replay.status).toBe(200);
  expect(replay.body.replayed).toBe(true);
  const other = await raw(api.token, 'POST', `/admin/payments/payouts/${anaItemId}/mark-paid`, {
    paymentReference: `${anaReference}-X`,
    paidAt: new Date().toISOString(),
  });
  expect(other.status).toBe(409);
  expect(other.body).toMatchObject({ code: 'payout.already_recorded' });
  // Ana was paid exactly once: 56.98.
  const ana = await api.get<{ paid: number }>(`/finance/users/${s().participants.ana.id}/balance`);
  expect(ana.paid).toBe(56.98);
});
