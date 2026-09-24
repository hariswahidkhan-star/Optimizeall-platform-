/**
 * Payments hub API (`/api/v1/admin/payments`, backend `Modules/PaymentsHub`). One list of incoming (client invoice)
 * and outgoing (participant payout) money with KPIs, CSV export and manual actions. Money always travels with its
 * currency and is never converted or summed across currencies here.
 */
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type QueryParams } from '@/lib/api/client';
import type { IsoDateTime, PagedResult } from '@/lib/api/types';

export type DateOnly = string;
export type PaymentDirection = 'Incoming' | 'Outgoing';
export type PaymentHubStatus = 'Scheduled' | 'Pending' | 'Paid' | 'Failed' | 'Refunded' | 'Voided';
export type PaymentRecordKind = 'InvoicePayment' | 'InvoiceDue' | 'PaymentClaim' | 'PayoutItem';
export type IncomingMethod = 'BankTransfer' | 'Card' | 'Cash' | 'Cheque' | 'PayPal' | 'Stripe' | 'Other';
export type ReversalKind = 'Refund' | 'Error';

export const PAYMENT_STATUSES: PaymentHubStatus[] = [
  'Scheduled',
  'Pending',
  'Paid',
  'Failed',
  'Refunded',
  'Voided',
];
export const INCOMING_METHODS: { value: IncomingMethod; label: string }[] = [
  { value: 'BankTransfer', label: 'Bank transfer' },
  { value: 'Cash', label: 'Cash' },
  { value: 'Cheque', label: 'Cheque' },
  { value: 'Card', label: 'Card (offline terminal)' },
  { value: 'PayPal', label: 'PayPal' },
  { value: 'Stripe', label: 'Stripe' },
  { value: 'Other', label: 'Other' },
];
export const KIND_LABELS: Record<PaymentRecordKind, string> = {
  InvoicePayment: 'Invoice payment',
  InvoiceDue: 'Invoice balance due',
  PaymentClaim: 'Client report (“I’ve paid”)',
  PayoutItem: 'Participant payout',
};

export function methodLabel(method: string | null | undefined): string {
  if (!method) return '—';
  return INCOMING_METHODS.find((m) => m.value === method)?.label ?? method;
}

export interface CurrencyAmount {
  currency: string;
  amount: number;
}

export interface PaymentParty {
  type: 'client' | 'participant';
  id: string;
  name: string;
  email: string | null;
}

export interface PaymentRecord {
  key: string;
  kind: PaymentRecordKind;
  id: string;
  direction: PaymentDirection;
  status: PaymentHubStatus;
  sourceStatus: string;
  party: PaymentParty;
  amount: number;
  currency: string;
  method: string | null;
  reference: string | null;
  date: DateOnly;
  dueDate: DateOnly | null;
  paidAt: IsoDateTime | null;
  reversedAt: IsoDateTime | null;
  reversalReason: string | null;
  invoiceId: string | null;
  invoiceNumber: string | null;
  invoiceBalance: number | null;
  batchId: string | null;
  batchReference: string | null;
  notes: string | null;
  recordedBy: string | null;
  daysOverdue: number;
  hasProof: boolean;
  concurrencyStamp: string;
  invoiceConcurrencyStamp: string | null;
  createdAt: IsoDateTime;
  actions: string[];
}

export interface InvoicePayment {
  id: string;
  invoiceId: string;
  invoiceNumber: string | null;
  amount: number;
  currency: string;
  method: IncomingMethod;
  reference: string;
  paidOn: DateOnly;
  notes: string | null;
  recordedBy: string | null;
  createdAt: IsoDateTime;
  reversalOfPaymentId: string | null;
  reversedAt: IsoDateTime | null;
  reversalKind: ReversalKind | null;
  reversalReason: string | null;
  concurrencyStamp: string;
}

export interface PaymentProof {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAt: IsoDateTime;
  url: string;
}

export interface ReminderHistory {
  kind: string;
  sentAt: IsoDateTime;
  manual: boolean;
  sentBy: string | null;
}

export interface HistoryEntry {
  at: IsoDateTime;
  action: string;
  actor: string | null;
  reason: string | null;
  details: string | null;
}

export interface PaymentRecordDetail {
  record: PaymentRecord;
  invoicePayments: InvoicePayment[];
  proofs: PaymentProof[];
  reminders: ReminderHistory[];
  history: HistoryEntry[];
}

export interface AgingRow {
  clientAccountId: string;
  clientName: string;
  currency: string;
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  over90: number;
  total: number;
}

export interface PaymentsSummary {
  today: DateOnly;
  monthStart: DateOnly;
  incoming: {
    receivedThisMonth: CurrencyAmount[];
    refundedThisMonth: CurrencyAmount[];
    outstanding: AgingRow[];
    openInvoices: number;
    overdueInvoices: number;
    pendingClaims: number;
  } | null;
  outgoing: {
    paidOutThisMonth: CurrencyAmount[];
    dueInBatches: CurrencyAmount[];
    awaitingPayment: number;
    failedThisMonth: number;
    nextCycle: {
      periodKey: string;
      cutoffAt: IsoDateTime;
      paymentDate: DateOnly;
      estimatedAvailable: CurrencyAmount[];
    };
  } | null;
}

export interface PaymentsCapabilities {
  incoming: boolean;
  outgoing: boolean;
  manageIncoming: boolean;
  recordPayouts: boolean;
  reminderSettings: boolean;
}

export interface PayoutActionResult {
  record: PaymentRecord;
  batchStatus: string;
  replayed: boolean;
  requeued: boolean;
}

export interface BatchPaidResult {
  batchId: string;
  batchStatus: string;
  recorded: number;
  alreadyRecorded: number;
  invalid: number;
  lines: { itemId: string; outcome: string; message: string }[];
}

const BASE = '/admin/payments';

export const paymentsKeys = {
  all: ['payments-hub'] as const,
  list: (params: QueryParams) => ['payments-hub', 'list', params] as const,
  summary: () => ['payments-hub', 'summary'] as const,
  capabilities: () => ['payments-hub', 'capabilities'] as const,
  detail: (kind: string, id: string) => ['payments-hub', 'detail', kind, id] as const,
};

export function usePaymentsCapabilities() {
  return useQuery({
    queryKey: paymentsKeys.capabilities(),
    queryFn: ({ signal }) => api.get<PaymentsCapabilities>(`${BASE}/capabilities`, { signal }),
    staleTime: 60_000,
  });
}

export function usePayments(params: QueryParams) {
  return useQuery({
    queryKey: paymentsKeys.list(params),
    queryFn: ({ signal }) => api.get<PagedResult<PaymentRecord>>(BASE, { query: params, signal }),
    placeholderData: keepPreviousData,
  });
}

export function usePaymentsSummary() {
  return useQuery({
    queryKey: paymentsKeys.summary(),
    queryFn: ({ signal }) => api.get<PaymentsSummary>(`${BASE}/summary`, { signal }),
  });
}

export function usePaymentDetail(record: Pick<PaymentRecord, 'kind' | 'id'> | null) {
  return useQuery({
    queryKey: paymentsKeys.detail(record?.kind ?? '', record?.id ?? ''),
    queryFn: ({ signal }) =>
      api.get<PaymentRecordDetail>(`${BASE}/records/${record!.kind}/${record!.id}`, { signal }),
    enabled: !!record,
  });
}

/** Every hub mutation refreshes the whole hub (list, KPIs, open drawer) and the finance/billing screens that show the same money. */
function usePaymentsMutation<TBody, TResult>(fn: (body: TBody) => Promise<TResult>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSettled: () =>
      Promise.all([
        client.invalidateQueries({ queryKey: paymentsKeys.all }),
        client.invalidateQueries({ queryKey: ['finance'] }),
        client.invalidateQueries({ queryKey: ['billing'] }),
      ]),
  });
}

export interface RecordPaymentBody {
  invoiceId: string;
  requestId: string;
  amount: number;
  method: IncomingMethod;
  reference: string;
  paidOn: DateOnly;
  notes?: string;
  concurrencyStamp: string;
}

export function useRecordInvoicePayment() {
  return usePaymentsMutation(({ invoiceId, ...body }: RecordPaymentBody) =>
    api.post<{ payment: InvoicePayment; replayed: boolean }>(`${BASE}/invoices/${invoiceId}/payments`, body),
  );
}

export function useMarkInvoicePaid() {
  return usePaymentsMutation(
    ({ invoiceId, ...body }: Omit<RecordPaymentBody, 'amount'> & { expectedBalance: number }) =>
      api.post<{ payment: InvoicePayment; replayed: boolean }>(
        `${BASE}/invoices/${invoiceId}/mark-paid`,
        body,
      ),
  );
}

export function useEditInvoicePayment() {
  return usePaymentsMutation(
    ({
      paymentId,
      ...body
    }: {
      paymentId: string;
      reference?: string;
      paidOn?: DateOnly;
      method?: IncomingMethod;
      notes?: string;
      reason: string;
      concurrencyStamp: string;
    }) => api.patch<InvoicePayment>(`${BASE}/invoice-payments/${paymentId}`, body),
  );
}

export function useReverseInvoicePayment() {
  return usePaymentsMutation(
    ({
      paymentId,
      ...body
    }: {
      paymentId: string;
      requestId: string;
      kind: ReversalKind;
      reason: string;
      reversedOn?: DateOnly;
      concurrencyStamp: string;
    }) =>
      api.post<{ replayed: boolean }>(`${BASE}/invoice-payments/${paymentId}/reverse`, {
        ...body,
        confirm: true,
      }),
  );
}

export function useUploadPaymentProof() {
  return usePaymentsMutation(({ paymentId, file }: { paymentId: string; file: File }) => {
    const form = new FormData();
    form.append('file', file);
    return api.post<PaymentProof>(`${BASE}/invoice-payments/${paymentId}/proofs`, form);
  });
}

export function useSendReminder() {
  return usePaymentsMutation(({ invoiceId, requestId }: { invoiceId: string; requestId: string }) =>
    api.post<{ kind: string; sentAt: IsoDateTime; replayed: boolean }>(
      `${BASE}/invoices/${invoiceId}/reminders`,
      { requestId },
    ),
  );
}

export function useConfirmClaim() {
  return usePaymentsMutation(
    ({
      claimId,
      ...body
    }: {
      claimId: string;
      amount?: number;
      paidOn?: DateOnly;
      notes?: string;
      invoiceConcurrencyStamp: string;
    }) => api.post<{ replayed: boolean }>(`${BASE}/claims/${claimId}/confirm`, body),
  );
}

export function useRejectClaim() {
  return usePaymentsMutation(
    ({ claimId, ...body }: { claimId: string; reason: string; concurrencyStamp: string }) =>
      api.post<{ replayed: boolean }>(`${BASE}/claims/${claimId}/reject`, body),
  );
}

export function useMarkPayoutPaid() {
  return usePaymentsMutation(
    ({
      itemId,
      ...body
    }: {
      itemId: string;
      paymentReference: string;
      paidAt: IsoDateTime;
      note?: string;
      overrideReason?: string;
    }) => api.post<PayoutActionResult>(`${BASE}/payouts/${itemId}/mark-paid`, body),
  );
}

export function useMarkPayoutFailed() {
  return usePaymentsMutation(
    ({ itemId, ...body }: { itemId: string; kind: 'Failed' | 'Returned'; reason: string }) =>
      api.post<PayoutActionResult>(`${BASE}/payouts/${itemId}/mark-failed`, body),
  );
}

export function useMarkBatchPaid() {
  return usePaymentsMutation(
    ({ batchId, ...body }: { batchId: string; paymentReference: string; paidAt: IsoDateTime }) =>
      api.post<BatchPaidResult>(`${BASE}/payout-batches/${batchId}/mark-paid`, { ...body, confirm: true }),
  );
}

/** Human messages for the hub's error codes (others fall back to the finance messages / server title). */
export const PAYMENTS_ERROR_MESSAGES: Record<string, string> = {
  'billing.overpayment':
    'That is more than the outstanding balance. Overpayments aren’t accepted — record the balance and refund the rest.',
  'billing.invalid_amount': 'Enter a valid amount for this currency.',
  'billing.duplicate_reference': 'A payment with this reference is already recorded on this invoice.',
  'billing.invoice_not_open':
    'This invoice has nothing left to pay (it may have just been paid by someone else).',
  'billing.request_id_reused':
    'This request was already used for something else. Close the dialog and start again.',
  'billing.self_payment':
    'You belong to this client’s organization, so another finance user must handle its payments.',
  'billing.four_eyes': 'You recorded this payment, so a different finance user must record its refund.',
  'billing.payment_already_reversed': 'This payment was already reversed.',
  'billing.payment_reversed': 'A reversed payment can’t be edited.',
  'billing.invoice_not_reversible': 'Payments of a void or written-off invoice can’t be reversed.',
  'billing.paid_on_in_future': 'The date can’t be in the future.',
  'payments.reminder_too_soon': 'A reminder was sent for this invoice less than an hour ago.',
  'payments.claim_state': 'This report was already handled by someone else.',
  'payments.claim_rejected': 'This report was rejected. Ask the client to report the payment again.',
  'payments.claim_duplicate': 'A report with this reference is already waiting for confirmation.',
  'payments.too_many_proofs': 'The maximum number of proof files is reached.',
  'file.unsupported_type': 'Upload a PDF, PNG, JPEG or WebP file.',
  'file.too_large': 'Proof files must be 10 MB or smaller.',
  'payout.self_record':
    'This batch was prepared by the system and finalized by you; another finance user must record its payments.',
  'payout.user_on_hold':
    'The participant has an active payout hold. Only record a payment that already left the account, with an override reason.',
  'payout.attempt_in_flight': 'The payment provider is still processing this payout. Wait for its outcome.',
};
