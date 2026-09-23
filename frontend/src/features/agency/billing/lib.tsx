import { Badge, type Tone } from '@/components/ui';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { humanize } from '@/lib/format/text';
import type { ContractStatus, CreditNoteStatus, InvoiceStatus } from './api/types';

/** Human messages for the billing/CRM error codes documented in docs/api/crm-billing.md. */
export const BILLING_ERROR_MESSAGES: Record<string, string> = {
  'auth.forbidden': 'You don’t have permission to do this. Ask an administrator if you need access.',
  'concurrency.conflict': 'Someone else changed this while you were looking at it. Refresh to see the latest version, then try again.',
  'request.confirm_required': 'This action needs an explicit confirmation.',
  'billing.overpayment': 'That’s more than the outstanding balance. Overpayments aren’t accepted — check the amount.',
  'billing.duplicate_reference': 'A payment with this reference was already recorded on this invoice.',
  'billing.request_id_reused': 'This payment was already submitted with different details. Close the dialog and start again.',
  'billing.invoice_not_open': 'This invoice isn’t open for payments or credits any more.',
  'billing.invoice_not_draft': 'Issued invoices can’t be edited. Issue a credit note to correct it.',
  'billing.invoice_has_payments': 'This invoice has payments or credits. Issue a credit note for the balance instead of voiding it.',
  'billing.four_eyes': 'You issued this invoice, so another finance user must do this.',
  'billing.self_payment': 'You belong to this client’s organization, so you can’t record its payments.',
  'billing.credit_exceeds_balance': 'The credit is more than the invoice balance.',
  'billing.credit_exhausted': 'There isn’t that much credit left on this credit note.',
  'billing.credit_mismatch': 'Credits can only be applied to invoices of the same client and currency.',
  'billing.invalid_amount': 'Enter a valid amount for this currency.',
  'billing.paid_on_in_future': 'The payment date can’t be in the future.',
  'billing.zero_total': 'An invoice needs a total above zero before it can be issued.',
  'billing.no_lines': 'Add at least one line.',
  'billing.invalid_tax_rate': 'One of the lines uses a tax rate that no longer exists or is inactive.',
  'billing.contract_invoiced': 'Start date, frequency and currency can’t change after the first invoice.',
  'billing.contract_state': 'The contract can’t make that change in its current status.',
  'crm.lost_reason_required': 'Say why the deal was lost.',
  'crm.duplicate_email': 'A contact with this email already exists.',
  'crm.duplicate_domain': 'A company with this domain already exists.',
  'crm.stage_in_use': 'That stage still has deals. Move them first or deactivate the stage.',
  'crm.invalid_pipeline': 'The pipeline needs exactly one Won stage, one Lost stage and at least one open stage.',
  'proposal.locked': 'Accepted or withdrawn proposals can’t be edited.',
  'proposal.expired': 'This proposal has expired.',
  'proposal.already_accepted': 'This proposal has already been accepted.',
  'proposal.already_declined': 'This proposal was declined.',
  'proposal.version_mismatch': 'A newer version of this proposal was sent. Reload the page to review it.',
  'proposal.being_revised': 'This proposal is being revised. You’ll receive the new version shortly.',
  'proposal.recipient_required': 'Add the recipient’s email before emailing the proposal.',
  'proposal.terms_required': 'Please confirm that you agree to the terms.',
  'client.insufficient_role': 'Billing is available to members with the Billing or Owner role in your organization.',
};

export function billingErrorMessage(error: unknown): string {
  if (isApiError(error)) {
    const known = BILLING_ERROR_MESSAGES[error.code];
    if (known) return known;
    if (error.status === 403) return BILLING_ERROR_MESSAGES['auth.forbidden']!;
    const fields = error.errors ? Object.values(error.errors).flat() : [];
    if (error.status === 400 && fields.length > 0) return fields.join(' ');
    return error.title;
  }
  return errorMessage(error);
}

export function errorCodeOf(error: unknown): string {
  return isApiError(error) ? error.code : '';
}

const INVOICE_TONES: Record<InvoiceStatus, Tone> = {
  Draft: 'neutral',
  Issued: 'info',
  PartiallyPaid: 'warning',
  Paid: 'success',
  Overdue: 'danger',
  Void: 'neutral',
  WrittenOff: 'neutral',
};

const CONTRACT_TONES: Record<ContractStatus, Tone> = {
  Draft: 'neutral',
  Active: 'success',
  Paused: 'warning',
  Cancelled: 'danger',
  Ended: 'neutral',
};

export function InvoiceStatusBadge({ status }: { status: InvoiceStatus }) {
  return <Badge tone={INVOICE_TONES[status] ?? 'neutral'}>{status === 'WrittenOff' ? 'Written off' : humanize(status)}</Badge>;
}

export function ContractStatusBadge({ status }: { status: ContractStatus }) {
  return <Badge tone={CONTRACT_TONES[status] ?? 'neutral'}>{humanize(status)}</Badge>;
}

export function CreditNoteStatusBadge({ status }: { status: CreditNoteStatus }) {
  return <Badge tone={status === 'Open' ? 'info' : 'neutral'}>{status === 'Open' ? 'Credit available' : 'Fully applied'}</Badge>;
}

/** DateOnly ("2026-10-02") as a calendar date without shifting it through a time zone. */
export function formatDateOnly(value: string | null | undefined): string {
  if (!value) return '—';
  const [y, m, d] = value.split('-').map(Number);
  if (!y || !m || !d) return value;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, d)));
}

/** Today as yyyy-MM-dd in the browser's zone (for date inputs). */
export function todayIso(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** yyyy-MM-dd plus a number of days (calendar arithmetic only — never money). */
export function addDaysIso(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y!, m! - 1, d! + days)).toISOString().slice(0, 10);
}

/** A random idempotency key for money-moving requests (payments, credit notes). */
export function newRequestId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

export const PAYMENT_METHODS = [
  { value: 'BankTransfer', label: 'Bank transfer' },
  { value: 'Card', label: 'Card' },
  { value: 'Cash', label: 'Cash' },
  { value: 'Cheque', label: 'Cheque' },
  { value: 'PayPal', label: 'PayPal' },
  { value: 'Stripe', label: 'Stripe' },
  { value: 'Other', label: 'Other' },
];

export const INVOICE_STATUS_OPTIONS: { value: InvoiceStatus; label: string }[] = [
  { value: 'Draft', label: 'Draft' },
  { value: 'Issued', label: 'Issued' },
  { value: 'PartiallyPaid', label: 'Partially paid' },
  { value: 'Overdue', label: 'Overdue' },
  { value: 'Paid', label: 'Paid' },
  { value: 'Void', label: 'Void' },
  { value: 'WrittenOff', label: 'Written off' },
];
