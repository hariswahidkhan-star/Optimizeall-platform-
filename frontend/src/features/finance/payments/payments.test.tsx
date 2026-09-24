import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { signedIn } from '../test/fixtures';
import type { PaymentRecord, PaymentRecordDetail, PaymentsCapabilities, PaymentsSummary } from './api';
import { PayoutPaidDialog, RecordPaymentDialog, ReversePaymentDialog } from './PaymentDialogs';
import { PaymentsPage, hubActions } from './PaymentsPage';

const allCaps: PaymentsCapabilities = {
  incoming: true,
  outgoing: true,
  manageIncoming: true,
  recordPayouts: true,
  reminderSettings: true,
};

function record(overrides: Partial<PaymentRecord> = {}): PaymentRecord {
  return {
    key: 'InvoiceDue:inv-1',
    kind: 'InvoiceDue',
    id: 'inv-1',
    direction: 'Incoming',
    status: 'Pending',
    sourceStatus: 'Overdue',
    party: { type: 'client', id: 'c1', name: 'Nimbus Fitness', email: null },
    amount: 750,
    currency: 'USD',
    method: null,
    reference: null,
    date: '2026-09-10',
    dueDate: '2026-09-10',
    paidAt: null,
    reversedAt: null,
    reversalReason: null,
    invoiceId: 'inv-1',
    invoiceNumber: 'OA-2026-0007',
    invoiceBalance: 750,
    batchId: null,
    batchReference: null,
    notes: null,
    recordedBy: null,
    daysOverdue: 13,
    hasProof: false,
    concurrencyStamp: 'stamp-inv',
    invoiceConcurrencyStamp: 'stamp-inv',
    createdAt: '2026-09-01T10:00:00Z',
    actions: ['record_payment', 'mark_paid_in_full', 'send_reminder'],
    ...overrides,
  };
}

const payout = record({
  key: 'PayoutItem:item-1',
  kind: 'PayoutItem',
  id: 'item-1',
  direction: 'Outgoing',
  status: 'Pending',
  sourceStatus: 'AwaitingPayment',
  party: { type: 'participant', id: 'p1', name: 'Ayesha Khan', email: 'ayesha@example.test' },
  amount: 40,
  method: 'manual',
  invoiceId: null,
  invoiceNumber: null,
  invoiceBalance: null,
  invoiceConcurrencyStamp: null,
  batchId: 'b1',
  batchReference: 'PB-2026-09-20',
  daysOverdue: 0,
  actions: ['mark_payout_paid', 'mark_payout_failed'],
});

const summary: PaymentsSummary = {
  today: '2026-09-23',
  monthStart: '2026-09-01',
  incoming: {
    receivedThisMonth: [
      { currency: 'USD', amount: 1200 },
      { currency: 'AED', amount: 3000 },
    ],
    refundedThisMonth: [],
    outstanding: [
      {
        clientAccountId: '00000000-0000-0000-0000-000000000000',
        clientName: 'Total',
        currency: 'USD',
        current: 0,
        days1To30: 750,
        days31To60: 0,
        days61To90: 0,
        over90: 0,
        total: 750,
      },
    ],
    openInvoices: 1,
    overdueInvoices: 1,
    pendingClaims: 2,
  },
  outgoing: {
    paidOutThisMonth: [{ currency: 'USD', amount: 90 }],
    dueInBatches: [{ currency: 'USD', amount: 40 }],
    awaitingPayment: 1,
    failedThisMonth: 0,
    nextCycle: {
      periodKey: '2026-10-04',
      cutoffAt: '2026-10-04T23:59:59Z',
      paymentDate: '2026-10-09',
      estimatedAvailable: [],
    },
  },
};

function detail(r: PaymentRecord): PaymentRecordDetail {
  return {
    record: r,
    invoicePayments: [],
    proofs: [],
    reminders: [],
    history: [
      {
        at: '2026-09-02T10:00:00Z',
        action: 'billing.invoice_issued',
        actor: 'Farah Finance',
        reason: null,
        details: null,
      },
    ],
  };
}

function hubRoutes(rows: PaymentRecord[], caps = allCaps) {
  return {
    ...signedIn,
    'GET /admin/payments/capabilities': () => json(200, caps),
    'GET /admin/payments/summary': () => json(200, caps.outgoing ? summary : { ...summary, outgoing: null }),
    'GET /admin/payments': () =>
      json(200, { items: rows, total: rows.length, page: 1, pageSize: 25, totalPages: 1 }),
    'GET /admin/payments/records/InvoiceDue/inv-1': () => json(200, detail(rows[0]!)),
  };
}

describe('Payments hub page', () => {
  it('shows per-currency KPIs (never summed across currencies), the aging table and every record', async () => {
    mockFetch(hubRoutes([record(), payout]));
    const { container } = renderWithApp(<PaymentsPage />, { route: '/finance/payments' });
    expect(await screen.findByRole('heading', { name: 'Payments' })).toBeInTheDocument();
    const kpis = await screen.findByRole('group', { name: 'Payment totals' });
    await waitFor(() => expect(within(kpis).getByText(/AED/)).toBeInTheDocument());
    expect(within(kpis).getByText(/2 client report\(s\) to check/)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Incoming Nimbus Fitness' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Outgoing Ayesha Khan' })).toBeInTheDocument();
    expect(screen.getByText('13 d overdue')).toBeInTheDocument();
    expect(screen.getByRole('table', { name: /Outstanding receivables by age/ })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('opens the detail drawer and records a payment from it with one idempotency key', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...hubRoutes([record()]),
      'POST /admin/payments/invoices/inv-1/payments': () =>
        json(201, { payment: { id: 'pay-1' }, replayed: false }),
    });
    renderWithApp(<PaymentsPage />, { route: '/finance/payments' });
    await user.click(await screen.findByRole('button', { name: 'Incoming Nimbus Fitness' }));
    const drawer = await screen.findByRole('dialog', { name: /Invoice balance due — Nimbus Fitness/ });
    expect(await within(drawer).findByText('billing.invoice_issued')).toBeInTheDocument();
    await user.click(within(drawer).getByRole('button', { name: 'Record payment' }));

    const dialog = await screen.findByRole('dialog', { name: 'Record a payment' });
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }));
    expect(await within(dialog).findByText('Enter the amount received.')).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(/Amount/), '250');
    await user.selectOptions(within(dialog).getByLabelText(/Method/), 'Cheque');
    await user.type(within(dialog).getByLabelText(/Reference/), 'CHQ-000123');
    await user.dblClick(within(dialog).getByRole('button', { name: 'Record payment' }));

    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Record a payment' })).not.toBeInTheDocument(),
    );
    const posts = calls.filter(
      (c) => c.method === 'POST' && c.path === '/admin/payments/invoices/inv-1/payments',
    );
    expect(posts).toHaveLength(1);
    expect(posts[0]!.body).toMatchObject({
      amount: 250,
      method: 'Cheque',
      reference: 'CHQ-000123',
      concurrencyStamp: 'stamp-inv',
    });
    expect((posts[0]!.body as { requestId: string }).requestId).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('hides outgoing filters for billing-only users', async () => {
    mockFetch(hubRoutes([record()], { ...allCaps, outgoing: false, recordPayouts: false }));
    renderWithApp(<PaymentsPage />, { route: '/finance/payments' });
    await screen.findByRole('button', { name: 'Incoming Nimbus Fitness' });
    expect(screen.queryByLabelText('Direction')).not.toBeInTheDocument();
    expect(screen.queryByText('Payouts due')).not.toBeInTheDocument();
  });

  it('maps server-advertised actions only', () => {
    expect(hubActions(record()).map((a) => a.id)).toEqual([
      'record_payment',
      'mark_paid_in_full',
      'send_reminder',
    ]);
    expect(hubActions(record({ actions: [] }))).toEqual([]);
    expect(hubActions(payout).find((a) => a.id === 'mark_payout_failed')?.danger).toBe(true);
  });
});

describe('Payment dialogs', () => {
  it('retries with the same request id after a failure, so the server records the payment once', async () => {
    const user = userEvent.setup();
    let attempt = 0;
    const { calls } = mockFetch({
      ...signedIn,
      'POST /admin/payments/invoices/inv-1/payments': () =>
        ++attempt === 1
          ? problem(503, 'http_503', 'Service unavailable')
          : json(200, { payment: { id: 'pay-1' }, replayed: true }),
    });
    const onClose = vi.fn();
    renderWithApp(<RecordPaymentDialog record={record()} onClose={onClose} />);
    const dialog = await screen.findByRole('dialog', { name: 'Record a payment' });
    await user.type(within(dialog).getByLabelText(/Amount/), '750');
    await user.type(within(dialog).getByLabelText(/Reference/), 'WIRE-9');
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }));
    expect(await within(dialog).findByRole('alert')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    const ids = calls
      .filter((c) => c.path === '/admin/payments/invoices/inv-1/payments')
      .map((c) => (c.body as { requestId: string }).requestId);
    expect(ids).toHaveLength(2);
    expect(ids[0]).toBe(ids[1]);
  });

  it('reversal needs a reason and explains the four-eyes refusal', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'POST /admin/payments/invoice-payments/pay-1/reverse': () =>
        problem(403, 'billing.four_eyes', 'Forbidden'),
    });
    const paid = record({
      kind: 'InvoicePayment',
      id: 'pay-1',
      status: 'Paid',
      concurrencyStamp: 'stamp-pay',
      actions: ['refund'],
    });
    renderWithApp(<ReversePaymentDialog record={paid} initialKind="Refund" onClose={() => {}} />);
    const dialog = await screen.findByRole('alertdialog', { name: 'Refund this payment' });
    await user.click(within(dialog).getByRole('button', { name: 'Record refund' }));
    expect(await within(dialog).findByText(/at least 5 characters/)).toBeInTheDocument();
    expect(calls.filter((c) => c.path.endsWith('/reverse'))).toHaveLength(0);
    await user.type(within(dialog).getByLabelText(/^Reason/), 'Client paid twice');
    await user.click(within(dialog).getByRole('button', { name: 'Record refund' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'a different finance user must record its refund',
    );
    expect(calls.find((c) => c.path.endsWith('/reverse'))?.body).toMatchObject({
      kind: 'Refund',
      confirm: true,
      concurrencyStamp: 'stamp-pay',
      reason: 'Client paid twice',
    });
  });

  it('asks for a hold override reason when the participant is on hold', async () => {
    const user = userEvent.setup();
    let calls = 0;
    const { calls: requests } = mockFetch({
      ...signedIn,
      'POST /admin/payments/payouts/item-1/mark-paid': () =>
        ++calls === 1
          ? problem(409, 'payout.user_on_hold', 'On hold')
          : json(200, { record: payout, batchStatus: 'Completed', replayed: false, requeued: false }),
    });
    const onClose = vi.fn();
    renderWithApp(<PayoutPaidDialog record={payout} onClose={onClose} />);
    const dialog = await screen.findByRole('alertdialog', { name: 'Mark payout as paid' });
    await user.type(within(dialog).getByLabelText(/Payment reference/), 'WISE-1001');
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }));
    const override = await within(dialog).findByLabelText(/Hold override reason/);
    await user.type(override, 'Transfer left before the hold');
    await user.click(within(dialog).getByRole('button', { name: 'Record payment' }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(requests.filter((c) => c.path.endsWith('/mark-paid'))[1]!.body).toMatchObject({
      paymentReference: 'WISE-1001',
      overrideReason: 'Transfer left before the hold',
    });
  });
});
