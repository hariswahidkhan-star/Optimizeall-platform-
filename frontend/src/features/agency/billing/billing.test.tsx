import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { ClientBillingPage, ClientInvoicePage } from '@/features/client/billing/ClientBillingPages';
import type { Invoice, PublicInvoice } from './api/types';
import { InvoiceDetailPage } from './pages/InvoiceDetailPage';
import { BillingSettingsPage } from './pages/BillingSettingsPage';
import { PublicInvoicePage } from './PublicInvoicePage';

const financeAdmin = makeUser({
  id: 'admin-2',
  displayName: 'Farah Finance',
  roles: ['Admin'],
  permissions: ['billing.view', 'billing.manage', 'billing.settings', 'clients.view'],
  timeZone: 'UTC',
});
const signedIn = { 'POST /auth/refresh': () => json(200, session(financeAdmin)) };

function invoice(overrides: Partial<Invoice> = {}): Invoice {
  return {
    id: 'inv1',
    number: 'OA-2026-0042',
    clientAccountId: 'client-1',
    clientName: 'Nimbus Fitness',
    clientBillingEmail: 'accounts@nimbus.example',
    status: 'Issued',
    currency: 'USD',
    issueDate: '2026-09-10',
    dueDate: '2026-09-24',
    paymentTermsDays: 14,
    totals: { currency: 'USD', grossTotal: 4500, discountTotal: 0, subtotal: 4500, taxTotal: 0, total: 4500, taxes: [] },
    amountPaid: 0,
    amountCredited: 0,
    amountWrittenOff: 0,
    balance: 4500,
    daysOverdue: 0,
    notes: null,
    reference: 'CT-2026-0001',
    contractId: null,
    proposalId: null,
    periodStart: null,
    periodEnd: null,
    lines: [
      {
        id: 'l1', position: 1, description: 'SEO & content retainer', serviceSlug: 'seo', packageSlug: null, quantity: 1, unitPrice: 4500,
        discountType: 'None', discountValue: 0, taxRateId: null, taxName: null, taxPercent: 0, taxInclusive: false, recurrence: 'OneTime',
        discountAmount: 0, subtotal: 4500, taxAmount: 0, total: 4500,
      },
    ],
    payments: [],
    credits: [],
    reminders: [],
    publicUrl: `http://localhost/i/${'t'.repeat(43)}`,
    issuedAt: '2026-09-10T09:00:00Z',
    sentAt: null,
    paidAt: null,
    voidedAt: null,
    voidReason: null,
    writtenOffAt: null,
    writeOffReason: null,
    issuedByUserId: 'someone-else',
    createdAt: '2026-09-10T08:00:00Z',
    concurrencyStamp: 'stamp-1',
    ...overrides,
  };
}

describe('Record payment', () => {
  it('sends one request per submission even when clicked twice, with a stable request id and the invoice stamp', async () => {
    const user = userEvent.setup();
    let release: (value: Response) => void = () => undefined;
    const { calls } = mockFetch({
      ...signedIn,
      'GET /agency/billing/invoices/inv1': () => json(200, invoice()),
      'POST /agency/billing/invoices/inv1/payments': () =>
        new Promise<Response>((resolve) => {
          release = resolve;
        }),
    });
    renderWithApp(<InvoiceDetailPage />, { route: '/agency/billing/invoices/inv1', path: '/agency/billing/invoices/:invoiceId' });
    await user.click(await screen.findByRole('button', { name: 'Record payment' }));
    const dialog = await screen.findByRole('dialog', { name: 'Record payment' });
    const amount = within(dialog).getByLabelText(/Amount/);
    await user.clear(amount);
    await user.type(amount, '1500');
    await user.type(within(dialog).getByLabelText(/Reference/), 'WIRE-7781');
    const submit = within(dialog).getByRole('button', { name: 'Record payment' });
    await user.dblClick(submit);
    await user.click(submit);
    await waitFor(() => expect(calls.filter((c) => c.method === 'POST' && c.path.endsWith('/payments'))).toHaveLength(1));
    const body = calls.find((c) => c.path.endsWith('/payments'))!.body as Record<string, unknown>;
    expect(body).toMatchObject({ amount: 1500, reference: 'WIRE-7781', method: 'BankTransfer', concurrencyStamp: 'stamp-1' });
    expect(body.requestId).toMatch(/^[0-9a-f-]{36}$/);
    release(json(201, { payment: {}, invoice: invoice({ status: 'PartiallyPaid', amountPaid: 1500, balance: 3000 }), replayed: false }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Record payment' })).not.toBeInTheDocument());
  });

  it('explains a concurrent payment by someone else and offers a refresh', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...signedIn,
      'GET /agency/billing/invoices/inv1': () => json(200, invoice()),
      'POST /agency/billing/invoices/inv1/payments': () => problem(409, 'concurrency.conflict', 'Changed.'),
    });
    renderWithApp(<InvoiceDetailPage />, { route: '/agency/billing/invoices/inv1', path: '/agency/billing/invoices/:invoiceId' });
    await user.click(await screen.findByRole('button', { name: 'Record payment' }));
    const dialog = await screen.findByRole('dialog', { name: 'Record payment' });
    await user.type(within(dialog).getByLabelText(/Reference/), 'WIRE-1');
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }));
    expect(await within(dialog).findByText('This invoice just changed')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Refresh' })).toBeInTheDocument();
  });

  it('shows the overpayment rule from the server', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...signedIn,
      'GET /agency/billing/invoices/inv1': () => json(200, invoice()),
      'POST /agency/billing/invoices/inv1/payments': () => problem(409, 'billing.overpayment', 'Too much.'),
    });
    renderWithApp(<InvoiceDetailPage />, { route: '/agency/billing/invoices/inv1', path: '/agency/billing/invoices/:invoiceId' });
    await user.click(await screen.findByRole('button', { name: 'Record payment' }));
    const dialog = await screen.findByRole('dialog', { name: 'Record payment' });
    await user.type(within(dialog).getByLabelText(/Reference/), 'WIRE-2');
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(/more than the outstanding balance/);
  });

  it('the invoice page has no axe violations and hides void for the issuer', async () => {
    mockFetch({ ...signedIn, 'GET /agency/billing/invoices/inv1': () => json(200, invoice({ issuedByUserId: 'admin-2' })) });
    const { container } = renderWithApp(<InvoiceDetailPage />, { route: '/agency/billing/invoices/inv1', path: '/agency/billing/invoices/:invoiceId' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Invoice OA-2026-0042' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Void' })).toBeDisabled();
    expect(screen.getByText(/You issued this invoice/)).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Billing settings', () => {
  it('is read-only without billing.settings', async () => {
    const viewer = makeUser({ id: 'am', roles: ['AccountManager'], permissions: ['billing.view', 'clients.view'] });
    mockFetch({
      'POST /auth/refresh': () => json(200, session(viewer)),
      'GET /agency/billing/settings': () =>
        json(200, {
          invoicePrefix: 'OA', creditNotePrefix: 'CN', contractPrefix: 'CT', proposalPrefix: 'PR', numberPadding: 4, paymentTermsDays: 14,
          defaultCurrency: 'USD', invoiceOnAcceptance: true, autoIssueInvoices: false, remindersEnabled: true, reminderOffsetsDays: [-3, 0, 7, 14],
          companyName: 'Optimize All', companyAddress: null, companyTaxId: null, companyEmail: null, bankDetails: null, paymentLinkText: null,
          paymentInstructions: null, invoiceFooter: null, defaultTaxRateId: null,
        }),
      'GET /agency/billing/tax-rates': () => json(200, []),
      'GET /meta/currencies': () => json(200, [{ code: 'USD', minorUnits: 2 }]),
    });
    const { container } = renderWithApp(<BillingSettingsPage />);
    expect(await screen.findByText('Read only')).toBeInTheDocument();
    expect(screen.getByLabelText(/Invoice prefix/)).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save settings' })).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Public invoice page', () => {
  const token = 'b'.repeat(43);
  const view: PublicInvoice = {
    number: 'OA-2026-0042', clientName: 'Nimbus Fitness', clientAddress: '1 Main St', clientTaxId: null, status: 'Issued', currency: 'USD',
    issueDate: '2026-09-10', dueDate: '2026-09-24', totals: invoice().totals, amountPaid: 0, amountCredited: 0, balance: 4500, notes: null,
    periodStart: null, periodEnd: null, lines: invoice().lines,
    payment: {
      companyName: 'Optimize All', companyAddress: null, companyTaxId: null, companyEmail: null, bankDetails: 'IBAN GB00 TEST 0000',
      paymentLinkText: null, paymentInstructions: 'Pay within 14 days.', invoiceFooter: null, onlinePaymentAvailable: false,
    },
  };

  it('shows the invoice, payment instructions and no fake online payment', async () => {
    mockFetch({ [`GET /public/invoices/${token}`]: () => json(200, view), 'POST /auth/refresh': () => problem(401, 'x', 'No session') });
    const { container } = renderWithApp(<PublicInvoicePage />, { route: `/i/${token}`, path: '/i/:token' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Invoice OA-2026-0042' })).toBeInTheDocument();
    expect(screen.getByText('IBAN GB00 TEST 0000')).toBeInTheDocument();
    expect(screen.getByText(/Online card payment isn’t available/)).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('in the client portal the document sits below the page heading: one h1, the document title is an h2', async () => {
    const billing = makeUser({ id: 'billing', roles: ['Client'], permissions: ['client.portal'] });
    mockFetch({
      'POST /auth/refresh': () => json(200, session(billing)),
      'GET /client/billing/invoices/inv1': () => json(200, view),
    });
    const { container } = renderWithApp(<ClientInvoicePage />, {
      route: '/client/billing/invoices/inv1',
      path: '/client/billing/invoices/:invoiceId',
    });
    expect(await screen.findByRole('heading', { level: 2, name: 'Invoice OA-2026-0042' })).toBeInTheDocument();
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(screen.getByRole('heading', { level: 3, name: 'Bill to' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('shows a friendly page for unknown links', async () => {
    mockFetch({ [`GET /public/invoices/${token}`]: () => problem(404, 'invoice.not_found', 'Not found'), 'POST /auth/refresh': () => problem(401, 'x', 'No session') });
    renderWithApp(<PublicInvoicePage />, { route: `/i/${token}`, path: '/i/:token' });
    expect(await screen.findByRole('heading', { level: 1, name: 'This invoice link isn’t valid' })).toBeInTheDocument();
  });
});

describe('Client portal billing', () => {
  it('explains that billing needs the Billing or Owner role', async () => {
    const viewer = makeUser({ id: 'viewer', roles: ['Client'], permissions: ['client.portal'] });
    mockFetch({
      'POST /auth/refresh': () => json(200, session(viewer)),
      'GET /client/billing/summary': () => problem(403, 'client.insufficient_role', 'Needs billing role'),
    });
    renderWithApp(<ClientBillingPage />);
    expect(await screen.findByText('Billing isn’t available to you')).toBeInTheDocument();
  });

  it('lists balances and invoices for billing members', async () => {
    const billing = makeUser({ id: 'billing', roles: ['Client'], permissions: ['client.portal'] });
    mockFetch({
      'POST /auth/refresh': () => json(200, session(billing)),
      'GET /client/billing/summary': () =>
        json(200, {
          organizations: [{ clientAccountId: 'client-1', name: 'Nimbus Fitness', currency: 'USD', role: 'Billing' }],
          outstanding: [{ currency: 'USD', amount: 4500 }],
          overdue: [],
          openInvoices: 1,
          proposalsAwaitingResponse: 0,
        }),
      'GET /client/billing/invoices': () =>
        json(200, {
          items: [{ id: 'inv1', number: 'OA-2026-0042', clientAccountId: 'client-1', clientName: 'Nimbus Fitness', status: 'Issued', currency: 'USD', issueDate: '2026-09-10', dueDate: '2026-09-24', total: 4500, amountPaid: 0, balance: 4500, daysOverdue: 0, createdAt: '2026-09-10T08:00:00Z', contractId: null }],
          total: 1, page: 1, pageSize: 100, totalPages: 1,
        }),
    });
    const { container } = renderWithApp(<ClientBillingPage />);
    expect(await screen.findByRole('link', { name: 'OA-2026-0042' })).toHaveAttribute('href', '/client/billing/invoices/inv1');
    expect(screen.getByRole('group', { name: /^Outstanding/ })).toHaveTextContent('$4,500.00');
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Invoice editing actions', () => {
  const invoiceRoutes = [
    { path: '/agency/billing/invoices', element: <p>Invoices</p> },
    { path: '/agency/billing/invoices/:invoiceId/edit', element: <p>Editor</p> },
  ];

  it('explains why an issued invoice is locked and duplicates it into a draft', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /agency/billing/invoices/inv1': () => json(200, invoice()),
      'POST /agency/billing/invoices/inv1/duplicate': () => json(201, invoice({ id: 'inv2', number: null, status: 'Draft' })),
    });
    renderWithApp(<InvoiceDetailPage />, { route: '/agency/billing/invoices/inv1', path: '/agency/billing/invoices/:invoiceId', routes: invoiceRoutes });
    expect(await screen.findByText('Issued invoices are locked')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Duplicate' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/agency/billing/invoices/inv1/duplicate')).toBe(true));
  });

  it('deletes a draft only after confirmation', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /agency/billing/invoices/inv1': () => json(200, invoice({ status: 'Draft', number: null, issueDate: null, dueDate: null })),
      'DELETE /agency/billing/invoices/inv1': () => json(204),
    });
    renderWithApp(<InvoiceDetailPage />, { route: '/agency/billing/invoices/inv1', path: '/agency/billing/invoices/:invoiceId', routes: invoiceRoutes });
    await user.click(await screen.findByRole('button', { name: 'Delete draft' }));
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false);
    const dialog = await screen.findByRole('alertdialog', { name: 'Delete this draft invoice?' });
    expect(await axeViolations(dialog)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Delete draft' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/agency/billing/invoices/inv1')).toBe(true));
  });
});

describe('Service catalog settings', () => {
  it('shows the payment term options and confirms before deleting a catalog item', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /agency/billing/settings': () =>
        json(200, {
          invoicePrefix: 'OA', creditNotePrefix: 'CN', contractPrefix: 'CT', proposalPrefix: 'PR', numberPadding: 4, paymentTermsDays: 14,
          defaultCurrency: 'USD', invoiceOnAcceptance: true, autoIssueInvoices: false, remindersEnabled: true, reminderOffsetsDays: [-3, 0, 7, 14],
          companyName: 'Optimize All', companyAddress: null, companyTaxId: null, companyEmail: null, bankDetails: null, paymentLinkText: null,
          paymentInstructions: null, invoiceFooter: null, defaultTaxRateId: null, paymentTermsOptions: [0, 14, 30],
        }),
      'GET /agency/billing/tax-rates': () => json(200, []),
      'GET /meta/currencies': () => json(200, [{ code: 'USD', minorUnits: 2 }]),
      'GET /agency/billing/catalog': () =>
        json(200, [
          {
            id: 'cat1', name: 'SEO retainer', description: 'Monthly SEO retainer', serviceSlug: 'seo', currency: 'USD', unitPrice: 1500, quantity: 1,
            recurrence: 'Monthly', taxRateId: null, taxName: null, sortOrder: 10, isActive: true, updatedAt: '2026-09-01T00:00:00Z', concurrencyStamp: 's',
          },
        ]),
      'DELETE /agency/billing/catalog/cat1': () => json(204),
    });
    const { container } = renderWithApp(<BillingSettingsPage />);
    expect(await screen.findByLabelText(/Payment terms offered/)).toHaveValue('0, 14, 30');
    const table = await screen.findByRole('table', { name: 'Service catalog' });
    await user.click(await within(table).findByRole('button', { name: /SEO retainer/ }));
    await user.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Delete “SEO retainer”/ });
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/agency/billing/catalog/cat1')).toBe(true));
    expect(await axeViolations(container)).toEqual([]);
  });
});
