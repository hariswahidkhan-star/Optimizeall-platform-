import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { InvoicePaymentsPanel, type ClientInvoicePayments } from './InvoicePaymentsPanel';

const clientUser = makeUser({
  id: 'client-1',
  roles: ['Client'],
  permissions: ['client.portal'],
  timeZone: 'UTC',
});
const signedIn = { 'POST /auth/refresh': () => json(200, session(clientUser)) };

function payments(overrides: Partial<ClientInvoicePayments> = {}): ClientInvoicePayments {
  return {
    invoiceId: 'inv-1',
    number: 'OA-2026-0007',
    status: 'PartiallyPaid',
    currency: 'USD',
    total: 1000,
    amountPaid: 400,
    amountCredited: 0,
    balance: 600,
    payments: [
      {
        id: 'p1',
        amount: 400,
        currency: 'USD',
        method: 'BankTransfer',
        reference: 'HSBC-1',
        paidOn: '2026-09-02',
        status: 'Paid',
        reversedAt: null,
      },
    ],
    claims: [
      {
        id: 'c1',
        invoiceId: 'inv-1',
        amount: 200,
        currency: 'USD',
        method: 'BankTransfer',
        reference: 'HSBC-OLD',
        paidOn: '2026-09-01',
        note: null,
        status: 'Rejected',
        createdAt: '2026-09-01T10:00:00Z',
        reviewedAt: '2026-09-02T10:00:00Z',
        reviewNote: 'No transfer found',
        hasProof: false,
      },
    ],
    canReportPayment: true,
    ...overrides,
  };
}

describe('Client invoice payments', () => {
  it('lists payments and reports, and sends "I’ve paid" once with a reference', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /client/billing/invoices/inv-1/payments': () => json(200, payments()),
      'POST /client/billing/invoices/inv-1/payment-claims': () =>
        json(201, {
          ...payments().claims[0],
          id: 'c2',
          status: 'Pending',
          reference: 'HSBC-2',
          hasProof: false,
        }),
    });
    renderWithApp(<InvoicePaymentsPanel invoiceId="inv-1" />);
    expect(await screen.findByText('HSBC-1')).toBeInTheDocument();
    expect(screen.getByText('No transfer found')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'I’ve paid' }));
    const dialog = await screen.findByRole('dialog', { name: 'I’ve paid this invoice' });
    expect(within(dialog).getByLabelText(/Amount paid/)).toHaveValue('600');
    await user.click(within(dialog).getByRole('button', { name: 'Send' }));
    expect(await within(dialog).findByText(/Enter the transfer reference/)).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(/Transfer reference/), 'HSBC-2');
    await user.dblClick(within(dialog).getByRole('button', { name: 'Send' }));
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'I’ve paid this invoice' })).not.toBeInTheDocument(),
    );
    const posts = calls.filter((c) => c.method === 'POST' && c.path.endsWith('/payment-claims'));
    expect(posts).toHaveLength(1);
    expect(posts[0]!.body).toMatchObject({ amount: 600, reference: 'HSBC-2', method: 'BankTransfer' });
  });

  it('shows the server explanation and hides the button when nothing is due', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...signedIn,
      'GET /client/billing/invoices/inv-1/payments': () => json(200, payments()),
      'POST /client/billing/invoices/inv-1/payment-claims': () =>
        problem(409, 'billing.overpayment', 'The amount is more than the outstanding balance.'),
    });
    renderWithApp(<InvoicePaymentsPanel invoiceId="inv-1" />);
    await user.click(await screen.findByRole('button', { name: 'I’ve paid' }));
    const dialog = await screen.findByRole('dialog', { name: 'I’ve paid this invoice' });
    await user.type(within(dialog).getByLabelText(/Transfer reference/), 'HSBC-3');
    await user.click(within(dialog).getByRole('button', { name: 'Send' }));
    expect(await within(dialog).findByRole('alert')).toBeInTheDocument();
  });

  it('has no "I’ve paid" button for a paid invoice', async () => {
    mockFetch({
      ...signedIn,
      'GET /client/billing/invoices/inv-1/payments': () =>
        json(200, payments({ balance: 0, canReportPayment: false, claims: [] })),
    });
    renderWithApp(<InvoicePaymentsPanel invoiceId="inv-1" />);
    expect(await screen.findByText('HSBC-1')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'I’ve paid' })).not.toBeInTheDocument();
  });
});
