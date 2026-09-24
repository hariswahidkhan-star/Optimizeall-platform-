import { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  FileDrop,
  FormField,
  Input,
  KeyValueList,
  Money,
  RadioGroup,
  Select,
  Textarea,
  useToast,
} from '@/components/ui';
import { isApiError } from '@/lib/api/errors';
import { FormDialog } from '../components/FormDialog';
import { financeErrorMessage } from '../api/errors';
import { localInputToIso, toLocalInputValue } from '../lib/format';
import {
  INCOMING_METHODS,
  PAYMENTS_ERROR_MESSAGES,
  useConfirmClaim,
  useEditInvoicePayment,
  useMarkBatchPaid,
  useMarkInvoicePaid,
  useMarkPayoutFailed,
  useMarkPayoutPaid,
  useRecordInvoicePayment,
  useRejectClaim,
  useReverseInvoicePayment,
  useSendReminder,
  useUploadPaymentProof,
  type IncomingMethod,
  type PaymentRecord,
  type ReversalKind,
} from './api';

/** Human message for a hub error: hub codes first, then the finance messages / server title. */
export function paymentsErrorMessage(error: unknown): string {
  if (isApiError(error) && PAYMENTS_ERROR_MESSAGES[error.code]) return PAYMENTS_ERROR_MESSAGES[error.code]!;
  return financeErrorMessage(error);
}

function ErrorAlert({ error }: { error: unknown }) {
  return (
    <Alert tone="danger" role="alert">
      {paymentsErrorMessage(error)}
    </Alert>
  );
}

const todayIso = () => new Date().toISOString().slice(0, 10);

/**
 * One idempotency key per opened dialog: a retry after a timeout (or a second click) reuses it, so the server records
 * the action once and answers "replayed".
 */
function useRequestId(open: boolean): string {
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useMemo(() => crypto.randomUUID(), [open]);
}

export type DialogState =
  | { type: 'record'; record: PaymentRecord }
  | { type: 'markPaid'; record: PaymentRecord }
  | { type: 'edit'; record: PaymentRecord }
  | { type: 'reverse'; record: PaymentRecord; kind: ReversalKind }
  | { type: 'proof'; record: PaymentRecord }
  | { type: 'reminder'; record: PaymentRecord }
  | { type: 'confirmClaim'; record: PaymentRecord }
  | { type: 'rejectClaim'; record: PaymentRecord }
  | { type: 'payoutPaid'; record: PaymentRecord }
  | { type: 'payoutFailed'; record: PaymentRecord }
  | { type: 'batchPaid'; batchId: string; batchReference: string }
  | null;

function MethodSelect({ value, onChange }: { value: IncomingMethod; onChange: (m: IncomingMethod) => void }) {
  return (
    <FormField label="Method" required>
      <Select
        value={value}
        onChange={(e) => onChange(e.target.value as IncomingMethod)}
        options={INCOMING_METHODS}
      />
    </FormField>
  );
}

function RecordSummary({ record }: { record: PaymentRecord }) {
  return (
    <KeyValueList
      layout="inline"
      items={[
        { label: record.party.type === 'client' ? 'Client' : 'Participant', value: record.party.name },
        ...(record.invoiceNumber ? [{ label: 'Invoice', value: record.invoiceNumber }] : []),
        ...(record.batchReference ? [{ label: 'Batch', value: record.batchReference }] : []),
        {
          label: record.kind === 'InvoiceDue' ? 'Balance due' : 'Amount',
          value: <Money amount={record.amount} currency={record.currency} />,
        },
      ]}
    />
  );
}

// ---------------------------------------------------------------- Incoming

export function RecordPaymentDialog({
  record,
  onClose,
}: {
  record: PaymentRecord | null;
  onClose: () => void;
}) {
  const open = !!record;
  const requestId = useRequestId(open);
  const mutation = useRecordInvoicePayment();
  const toast = useToast();
  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState<IncomingMethod>('BankTransfer');
  const [reference, setReference] = useState('');
  const [paidOn, setPaidOn] = useState(todayIso());
  const [notes, setNotes] = useState('');
  const [touched, setTouched] = useState(false);
  useEffect(() => {
    if (open) {
      setAmount('');
      setMethod('BankTransfer');
      setReference('');
      setPaidOn(todayIso());
      setNotes('');
      setTouched(false);
    }
  }, [open]);
  const amountValue = Number(amount);
  const amountError =
    !amount || !Number.isFinite(amountValue) || amountValue <= 0 ? 'Enter the amount received.' : null;
  const referenceError = reference.trim().length === 0 ? 'Enter the bank or receipt reference.' : null;
  const dateError = !paidOn ? 'Enter the payment date.' : null;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title="Record a payment"
      description="Record money that reached the agency’s account. It reduces the invoice balance straight away; overpayments are refused."
      submitLabel="Record payment"
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        setTouched(true);
        if (!record || amountError || referenceError || dateError) return false;
        const result = await mutation.mutateAsync({
          invoiceId: record.invoiceId!,
          requestId,
          amount: amountValue,
          method,
          reference: reference.trim(),
          paidOn,
          notes: notes.trim() || undefined,
          concurrencyStamp: record.invoiceConcurrencyStamp!,
        });
        toast.success(result.replayed ? 'Payment was already recorded' : 'Payment recorded');
      }}
    >
      {record && <RecordSummary record={record} />}
      <FormField label={`Amount (${record?.currency ?? ''})`} required error={touched ? amountError : null}>
        <Input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
      </FormField>
      <MethodSelect value={method} onChange={setMethod} />
      <FormField
        label="Reference"
        hint="Bank transaction id, cheque number or receipt number."
        required
        error={touched ? referenceError : null}
      >
        <Input maxLength={120} value={reference} onChange={(e) => setReference(e.target.value)} />
      </FormField>
      <FormField label="Payment date" required error={touched ? dateError : null}>
        <Input type="date" value={paidOn} max={todayIso()} onChange={(e) => setPaidOn(e.target.value)} />
      </FormField>
      <FormField label="Notes" optional>
        <Textarea rows={2} maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

export function MarkPaidInFullDialog({
  record,
  onClose,
}: {
  record: PaymentRecord | null;
  onClose: () => void;
}) {
  const open = !!record;
  const requestId = useRequestId(open);
  const mutation = useMarkInvoicePaid();
  const toast = useToast();
  const [method, setMethod] = useState<IncomingMethod>('BankTransfer');
  const [reference, setReference] = useState('');
  const [paidOn, setPaidOn] = useState(todayIso());
  const [touched, setTouched] = useState(false);
  useEffect(() => {
    if (open) {
      setMethod('BankTransfer');
      setReference('');
      setPaidOn(todayIso());
      setTouched(false);
    }
  }, [open]);
  const referenceError = reference.trim().length === 0 ? 'Enter the bank or receipt reference.' : null;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      sensitive
      title="Mark invoice as paid in full"
      description="Records the whole remaining balance as one payment. Only do this once the money is in the account."
      submitLabel="Mark as paid"
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        setTouched(true);
        if (!record || referenceError || !paidOn) return false;
        const result = await mutation.mutateAsync({
          invoiceId: record.invoiceId!,
          requestId,
          method,
          reference: reference.trim(),
          paidOn,
          expectedBalance: record.invoiceBalance ?? record.amount,
          concurrencyStamp: record.invoiceConcurrencyStamp!,
        });
        toast.success(result.replayed ? 'Invoice was already marked paid' : 'Invoice marked as paid');
      }}
    >
      {record && <RecordSummary record={record} />}
      <MethodSelect value={method} onChange={setMethod} />
      <FormField label="Reference" required error={touched ? referenceError : null}>
        <Input maxLength={120} value={reference} onChange={(e) => setReference(e.target.value)} />
      </FormField>
      <FormField label="Payment date" required>
        <Input type="date" value={paidOn} max={todayIso()} onChange={(e) => setPaidOn(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

export function EditPaymentDialog({
  record,
  onClose,
}: {
  record: PaymentRecord | null;
  onClose: () => void;
}) {
  const open = !!record;
  const mutation = useEditInvoicePayment();
  const toast = useToast();
  const [reference, setReference] = useState('');
  const [paidOn, setPaidOn] = useState('');
  const [method, setMethod] = useState<IncomingMethod>('BankTransfer');
  const [notes, setNotes] = useState('');
  const [reason, setReason] = useState('');
  const [touched, setTouched] = useState(false);
  useEffect(() => {
    if (record) {
      setReference(record.reference ?? '');
      setPaidOn(record.date);
      setMethod((record.method as IncomingMethod) ?? 'BankTransfer');
      setNotes(record.notes ?? '');
      setReason('');
      setTouched(false);
    }
  }, [record]);
  const reasonError = reason.trim().length < 5 ? 'Explain the change (at least 5 characters).' : null;
  const referenceError = reference.trim().length === 0 ? 'Enter the reference.' : null;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title="Edit payment details"
      description="Correct the reference, date, method or notes. The amount can’t be edited: reverse the payment and record it again instead."
      submitLabel="Save changes"
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        setTouched(true);
        if (!record || reasonError || referenceError) return false;
        await mutation.mutateAsync({
          paymentId: record.id,
          reference: reference.trim(),
          paidOn,
          method,
          notes: notes.trim(),
          reason: reason.trim(),
          concurrencyStamp: record.concurrencyStamp,
        });
        toast.success('Payment updated');
      }}
    >
      {record && <RecordSummary record={record} />}
      <FormField label="Reference" required error={touched ? referenceError : null}>
        <Input maxLength={120} value={reference} onChange={(e) => setReference(e.target.value)} />
      </FormField>
      <FormField label="Payment date" required>
        <Input type="date" value={paidOn} max={todayIso()} onChange={(e) => setPaidOn(e.target.value)} />
      </FormField>
      <MethodSelect value={method} onChange={setMethod} />
      <FormField label="Notes" optional>
        <Textarea rows={2} maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)} />
      </FormField>
      <FormField
        label="Reason for the change"
        hint="Recorded in the audit log."
        required
        error={touched ? reasonError : null}
      >
        <Textarea rows={2} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

export function ReversePaymentDialog({
  record,
  initialKind,
  onClose,
}: {
  record: PaymentRecord | null;
  initialKind: ReversalKind;
  onClose: () => void;
}) {
  const open = !!record;
  const requestId = useRequestId(open);
  const mutation = useReverseInvoicePayment();
  const toast = useToast();
  const [kind, setKind] = useState<ReversalKind>(initialKind);
  const [reason, setReason] = useState('');
  const [date, setDate] = useState(todayIso());
  const [touched, setTouched] = useState(false);
  useEffect(() => {
    if (open) {
      setKind(initialKind);
      setReason('');
      setDate(todayIso());
      setTouched(false);
    }
  }, [open, initialKind]);
  const reasonError = reason.trim().length < 5 ? 'Give a reason of at least 5 characters.' : null;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      sensitive
      tone="danger"
      title={kind === 'Refund' ? 'Refund this payment' : 'Reverse this payment'}
      description="The payment stays in the history, a reversal is added and the invoice balance goes back up (a paid invoice reopens)."
      submitLabel={kind === 'Refund' ? 'Record refund' : 'Reverse payment'}
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        setTouched(true);
        if (!record || reasonError) return false;
        const result = await mutation.mutateAsync({
          paymentId: record.id,
          requestId,
          kind,
          reason: reason.trim(),
          reversedOn: date,
          concurrencyStamp: record.concurrencyStamp,
        });
        toast.success(
          result.replayed ? 'Already done' : kind === 'Refund' ? 'Refund recorded' : 'Payment reversed',
        );
      }}
    >
      {record && <RecordSummary record={record} />}
      <RadioGroup
        legend="What happened?"
        value={kind}
        onChange={(v) => setKind(v as ReversalKind)}
        options={[
          {
            value: 'Error',
            label: 'Recorded in error',
            description: 'Wrong invoice, wrong amount, or the money never arrived.',
          },
          {
            value: 'Refund',
            label: 'Refunded to the client',
            description: 'The money was sent back. Needs a different person from the one who recorded it.',
          },
        ]}
      />
      <FormField label={kind === 'Refund' ? 'Refund date' : 'Date'} required>
        <Input type="date" value={date} max={todayIso()} onChange={(e) => setDate(e.target.value)} />
      </FormField>
      <FormField
        label="Reason"
        hint="Recorded in the audit log."
        required
        error={touched ? reasonError : null}
      >
        <Textarea rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

export function UploadProofDialog({
  record,
  onClose,
}: {
  record: PaymentRecord | null;
  onClose: () => void;
}) {
  const open = !!record;
  const mutation = useUploadPaymentProof();
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null);
  useEffect(() => {
    if (open) setFile(null);
  }, [open]);
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title="Attach proof of payment"
      submitLabel="Upload"
      canSubmit={!!file}
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        if (!record || !file) return false;
        await mutation.mutateAsync({ paymentId: record.id, file });
        toast.success('Proof attached');
      }}
    >
      <FileDrop
        label="Bank slip or receipt"
        hint="PDF, PNG, JPEG or WebP, up to 10 MB."
        value={file}
        onChange={setFile}
        accept={['application/pdf', 'image/png', 'image/jpeg', 'image/webp']}
        maxSizeBytes={10 * 1024 * 1024}
      />
    </FormDialog>
  );
}

export function SendReminderDialog({
  record,
  onClose,
}: {
  record: PaymentRecord | null;
  onClose: () => void;
}) {
  const open = !!record;
  const requestId = useRequestId(open);
  const mutation = useSendReminder();
  const toast = useToast();
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title="Send a payment reminder now"
      description="Emails the client’s billing contacts a reminder with a link to the invoice. At most one reminder per invoice per hour."
      submitLabel="Send reminder"
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        if (!record) return false;
        const result = await mutation.mutateAsync({ invoiceId: record.invoiceId!, requestId });
        toast.success(result.replayed ? 'Reminder was already sent' : 'Reminder sent');
      }}
    >
      {record && <RecordSummary record={record} />}
    </FormDialog>
  );
}

export function ConfirmClaimDialog({
  record,
  onClose,
}: {
  record: PaymentRecord | null;
  onClose: () => void;
}) {
  const open = !!record;
  const mutation = useConfirmClaim();
  const toast = useToast();
  const [amount, setAmount] = useState('');
  const [notes, setNotes] = useState('');
  useEffect(() => {
    if (record) {
      setAmount(String(record.amount));
      setNotes('');
    }
  }, [record]);
  const value = Number(amount);
  const invalid = !amount || !Number.isFinite(value) || value <= 0;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title="Confirm the client’s payment"
      description="Only confirm once you have found the transfer on the bank statement. This records the payment on the invoice."
      submitLabel="Confirm and record"
      canSubmit={!invalid}
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        if (!record || invalid) return false;
        await mutation.mutateAsync({
          claimId: record.id,
          amount: value,
          notes: notes.trim() || undefined,
          invoiceConcurrencyStamp: record.invoiceConcurrencyStamp!,
        });
        toast.success('Payment confirmed and recorded');
      }}
    >
      {record && (
        <KeyValueList
          layout="inline"
          items={[
            { label: 'Client', value: record.party.name },
            { label: 'Invoice', value: record.invoiceNumber ?? '—' },
            { label: 'Reported amount', value: <Money amount={record.amount} currency={record.currency} /> },
            { label: 'Reference', value: record.reference ?? '—' },
          ]}
        />
      )}
      <FormField
        label={`Amount received (${record?.currency ?? ''})`}
        required
        hint="Change it if the bank shows a different amount."
      >
        <Input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
      </FormField>
      <FormField label="Notes" optional>
        <Textarea rows={2} maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

export function RejectClaimDialog({
  record,
  onClose,
}: {
  record: PaymentRecord | null;
  onClose: () => void;
}) {
  const open = !!record;
  const mutation = useRejectClaim();
  const toast = useToast();
  const [reason, setReason] = useState('');
  const [touched, setTouched] = useState(false);
  useEffect(() => {
    if (open) {
      setReason('');
      setTouched(false);
    }
  }, [open]);
  const reasonError = reason.trim().length < 5 ? 'Tell the client why (at least 5 characters).' : null;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      tone="danger"
      title="Reject the client’s payment report"
      description="The client is told the payment couldn’t be matched, with your reason."
      submitLabel="Reject report"
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        setTouched(true);
        if (!record || reasonError) return false;
        await mutation.mutateAsync({
          claimId: record.id,
          reason: reason.trim(),
          concurrencyStamp: record.concurrencyStamp,
        });
        toast.success('Report rejected');
      }}
    >
      <FormField label="Reason (shown to the client)" required error={touched ? reasonError : null}>
        <Textarea rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

// ---------------------------------------------------------------- Outgoing

export function PayoutPaidDialog({ record, onClose }: { record: PaymentRecord | null; onClose: () => void }) {
  const open = !!record;
  const mutation = useMarkPayoutPaid();
  const toast = useToast();
  const [reference, setReference] = useState('');
  const [paidAt, setPaidAt] = useState(toLocalInputValue());
  const [note, setNote] = useState('');
  const [override, setOverride] = useState('');
  const [needsOverride, setNeedsOverride] = useState(false);
  const [touched, setTouched] = useState(false);
  useEffect(() => {
    if (open) {
      setReference('');
      setPaidAt(toLocalInputValue());
      setNote('');
      setOverride('');
      setNeedsOverride(false);
      setTouched(false);
    }
  }, [open]);
  const referenceError =
    reference.trim().length < 3 ? 'Enter the transfer reference (at least 3 characters).' : null;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      sensitive
      title="Mark payout as paid"
      description="Only record a payment that has left the account. The participant is told it was sent."
      submitLabel="Record payment"
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        setTouched(true);
        const iso = localInputToIso(paidAt);
        if (!record || referenceError || !iso) return false;
        try {
          const result = await mutation.mutateAsync({
            itemId: record.id,
            paymentReference: reference.trim(),
            paidAt: iso,
            note: note.trim() || undefined,
            overrideReason: override.trim() || undefined,
          });
          toast.success(result.replayed ? 'Payment was already recorded' : 'Payout marked as paid');
        } catch (error) {
          if (isApiError(error) && error.code === 'payout.user_on_hold') setNeedsOverride(true);
          throw error;
        }
      }}
    >
      {record && <RecordSummary record={record} />}
      <FormField label="Payment reference" required error={touched ? referenceError : null}>
        <Input maxLength={120} value={reference} onChange={(e) => setReference(e.target.value)} />
      </FormField>
      <FormField label="Paid at" required>
        <Input type="datetime-local" value={paidAt} onChange={(e) => setPaidAt(e.target.value)} />
      </FormField>
      <FormField label="Note" optional>
        <Textarea rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
      {needsOverride && (
        <FormField
          label="Hold override reason"
          hint="Why the money left although the participant is on hold (audited)."
          required
        >
          <Textarea
            rows={2}
            maxLength={1000}
            value={override}
            onChange={(e) => setOverride(e.target.value)}
          />
        </FormField>
      )}
    </FormDialog>
  );
}

export function PayoutFailedDialog({
  record,
  onClose,
}: {
  record: PaymentRecord | null;
  onClose: () => void;
}) {
  const open = !!record;
  const mutation = useMarkPayoutFailed();
  const toast = useToast();
  const [kind, setKind] = useState<'Failed' | 'Returned'>('Failed');
  const [reason, setReason] = useState('');
  const [touched, setTouched] = useState(false);
  useEffect(() => {
    if (open) {
      setKind('Failed');
      setReason('');
      setTouched(false);
    }
  }, [open]);
  const reasonError = reason.trim().length < 5 ? 'Give the bank’s reason (at least 5 characters).' : null;
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      tone="danger"
      title="Mark payout as failed"
      description="The earnings go back to the participant’s balance and are re-queued into the next payout batch."
      submitLabel="Mark failed"
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        setTouched(true);
        if (!record || reasonError) return false;
        await mutation.mutateAsync({ itemId: record.id, kind, reason: reason.trim() });
        toast.success('Payout marked failed and re-queued');
      }}
    >
      {record && <RecordSummary record={record} />}
      <RadioGroup
        legend="What happened?"
        value={kind}
        onChange={(v) => setKind(v as 'Failed' | 'Returned')}
        options={[
          { value: 'Failed', label: 'The transfer could not be made' },
          { value: 'Returned', label: 'The bank returned the transfer' },
        ]}
      />
      <FormField label="Reason" required error={touched ? reasonError : null}>
        <Textarea rows={3} maxLength={900} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

export function BatchPaidDialog({
  batch,
  onClose,
}: {
  batch: { batchId: string; batchReference: string } | null;
  onClose: () => void;
}) {
  const open = !!batch;
  const mutation = useMarkBatchPaid();
  const toast = useToast();
  const [reference, setReference] = useState('');
  const [paidAt, setPaidAt] = useState(toLocalInputValue());
  const [summary, setSummary] = useState<string | null>(null);
  useEffect(() => {
    if (open) {
      setReference('');
      setPaidAt(toLocalInputValue());
      setSummary(null);
    }
  }, [open]);
  const iso = localInputToIso(paidAt);
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      sensitive
      title={`Mark batch ${batch?.batchReference ?? ''} as paid`}
      description="Records every item awaiting payment as paid with one bulk-transfer reference. Items that can’t be recorded (e.g. participant on hold) are listed and left unpaid."
      submitLabel="Record all payments"
      hideSubmit={summary !== null}
      canSubmit={reference.trim().length >= 3 && !!iso}
      renderError={(e) => <ErrorAlert error={e} />}
      onSubmit={async () => {
        if (!batch || !iso) return false;
        const result = await mutation.mutateAsync({
          batchId: batch.batchId,
          paymentReference: reference.trim(),
          paidAt: iso,
        });
        setSummary(
          `${result.recorded} recorded, ${result.alreadyRecorded} already recorded, ${result.invalid} not recorded.` +
            (result.invalid > 0
              ? ' ' +
                result.lines
                  .filter((l) => l.outcome === 'invalid')
                  .map((l) => l.message)
                  .join(' ')
              : ''),
        );
        toast.success('Batch payments recorded');
        return false;
      }}
    >
      {summary ? (
        <Alert tone="info" role="status">
          {summary}
        </Alert>
      ) : (
        <>
          <FormField label="Bulk transfer reference" required>
            <Input maxLength={120} value={reference} onChange={(e) => setReference(e.target.value)} />
          </FormField>
          <FormField label="Paid at" required>
            <Input type="datetime-local" value={paidAt} onChange={(e) => setPaidAt(e.target.value)} />
          </FormField>
        </>
      )}
    </FormDialog>
  );
}

export function PaymentDialogs({ state, onClose }: { state: DialogState; onClose: () => void }) {
  const r = (type: NonNullable<DialogState>['type']) =>
    state && state.type === type && 'record' in state ? state.record : null;
  return (
    <>
      <RecordPaymentDialog record={r('record')} onClose={onClose} />
      <MarkPaidInFullDialog record={r('markPaid')} onClose={onClose} />
      <EditPaymentDialog record={r('edit')} onClose={onClose} />
      <ReversePaymentDialog
        record={r('reverse')}
        initialKind={state?.type === 'reverse' ? state.kind : 'Error'}
        onClose={onClose}
      />
      <UploadProofDialog record={r('proof')} onClose={onClose} />
      <SendReminderDialog record={r('reminder')} onClose={onClose} />
      <ConfirmClaimDialog record={r('confirmClaim')} onClose={onClose} />
      <RejectClaimDialog record={r('rejectClaim')} onClose={onClose} />
      <PayoutPaidDialog record={r('payoutPaid')} onClose={onClose} />
      <PayoutFailedDialog record={r('payoutFailed')} onClose={onClose} />
      <BatchPaidDialog batch={state?.type === 'batchPaid' ? state : null} onClose={onClose} />
    </>
  );
}
