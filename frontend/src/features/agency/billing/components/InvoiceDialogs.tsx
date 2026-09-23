import { RefreshCw } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Alert, Button, FormField, Input, KeyValueList, Money, Select, Textarea, useToast } from '@/components/ui';
import { useCreateCreditNote, useRecordPayment } from '../api/hooks';
import type { Invoice, PaymentMethod } from '../api/types';
import { PAYMENT_METHODS, billingErrorMessage, errorCodeOf, newRequestId, todayIso } from '../lib';
import { FormDialog } from './FormDialog';

export interface RecordPaymentDialogProps {
  invoice: Invoice | null;
  onClose: () => void;
  onRefresh: () => void;
}

/**
 * Records a payment received outside the platform. One request id per opening of the dialog makes retries idempotent;
 * the invoice stamp makes a payment recorded meanwhile by someone else fail with a clear message instead of paying twice.
 */
export function RecordPaymentDialog({ invoice, onClose, onRefresh }: RecordPaymentDialogProps) {
  const record = useRecordPayment(invoice?.id ?? '');
  const toast = useToast();
  const open = !!invoice;
  const [requestId, setRequestId] = useState('');
  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState<PaymentMethod>('BankTransfer');
  const [reference, setReference] = useState('');
  const [paidOn, setPaidOn] = useState(todayIso());
  const [notes, setNotes] = useState('');
  const [touched, setTouched] = useState(false);

  const invoiceId = invoice?.id;
  const balance = invoice?.balance;
  useEffect(() => {
    if (open && invoiceId) {
      setRequestId(newRequestId());
      setAmount(balance === undefined ? '' : String(balance));
      setMethod('BankTransfer');
      setReference('');
      setPaidOn(todayIso());
      setNotes('');
      setTouched(false);
    }
    // Reset only when the dialog opens for an invoice, not when its data refetches.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, invoiceId]);

  if (!invoice) return null;
  const numeric = Number(amount);
  const amountError = !amount || !Number.isFinite(numeric) || numeric <= 0 ? 'Enter the amount received.' : null;
  const referenceError = reference.trim().length === 0 ? 'Enter the bank or payment reference.' : null;
  const paidOnError = !paidOn ? 'Enter the payment date.' : paidOn > todayIso() ? 'The payment date can’t be in the future.' : null;
  const valid = !amountError && !referenceError && !paidOnError;

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title="Record payment"
      description="Only record money that has actually been received. The server checks the amount against the balance."
      submitLabel="Record payment"
      onSubmit={async () => {
        setTouched(true);
        if (!valid) return false;
        const result = await record.mutateAsync({
          requestId,
          amount: numeric,
          method,
          reference: reference.trim(),
          paidOn,
          notes: notes.trim() || undefined,
          concurrencyStamp: invoice.concurrencyStamp,
        });
        toast.success(
          result.replayed ? 'Payment already recorded' : 'Payment recorded',
          result.invoice.status === 'Paid' ? 'The invoice is now paid in full.' : undefined,
        );
      }}
      renderError={(error) =>
        errorCodeOf(error) === 'concurrency.conflict' ? (
          <Alert
            tone="warning"
            role="alert"
            title="This invoice just changed"
            actions={
              <Button
                size="sm"
                variant="secondary"
                leadingIcon={<RefreshCw />}
                onClick={() => {
                  onRefresh();
                  onClose();
                }}
              >
                Refresh
              </Button>
            }
          >
            Someone else recorded a payment or credit on this invoice a moment ago. Nothing was recorded — refresh to see the new
            balance before trying again.
          </Alert>
        ) : (
          <Alert tone="danger" role="alert">
            {billingErrorMessage(error)}
          </Alert>
        )
      }
    >
      <KeyValueList
        layout="inline"
        items={[
          { label: 'Invoice', value: `${invoice.number ?? 'Draft'} · ${invoice.clientName}` },
          { label: 'Balance', value: <Money amount={invoice.balance} currency={invoice.currency} /> },
        ]}
      />
      <FormField label={`Amount (${invoice.currency})`} required error={touched ? amountError : null}>
        <Input type="number" inputMode="decimal" min={0} step="any" value={amount} onChange={(e) => setAmount(e.target.value)} />
      </FormField>
      <FormField label="Method" required>
        <Select value={method} options={PAYMENT_METHODS} onChange={(e) => setMethod(e.target.value as PaymentMethod)} />
      </FormField>
      <FormField label="Reference" hint="Bank transaction id, cheque number, …" required error={touched ? referenceError : null}>
        <Input value={reference} maxLength={120} autoComplete="off" onChange={(e) => setReference(e.target.value)} />
      </FormField>
      <FormField label="Paid on" required error={touched ? paidOnError : null}>
        <Input type="date" value={paidOn} max={todayIso()} onChange={(e) => setPaidOn(e.target.value)} />
      </FormField>
      <FormField label="Notes" optional>
        <Textarea rows={2} maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}

/** Issues a credit note against an issued invoice (applied immediately, up to its balance). */
export function CreditNoteDialog({ invoice, onClose }: { invoice: Invoice | null; onClose: () => void }) {
  const create = useCreateCreditNote();
  const toast = useToast();
  const open = !!invoice;
  const [requestId, setRequestId] = useState('');
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('');
  const [touched, setTouched] = useState(false);

  useEffect(() => {
    if (open) {
      setRequestId(newRequestId());
      setAmount('');
      setReason('');
      setTouched(false);
    }
  }, [open]);

  if (!invoice) return null;
  const numeric = Number(amount);
  const amountError = !amount || !Number.isFinite(numeric) || numeric <= 0 ? 'Enter the amount to credit.' : null;
  const reasonError = reason.trim().length < 5 ? 'Explain the correction (at least 5 characters).' : null;

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title="Issue credit note"
      description="Issued invoices never change. A credit note reduces what the client owes and is numbered and audited."
      submitLabel="Issue credit note"
      onSubmit={async () => {
        setTouched(true);
        if (amountError || reasonError) return false;
        const note = await create.mutateAsync({ requestId, invoiceId: invoice.id, amount: numeric, reason: reason.trim() });
        toast.success(`Credit note ${note.number} issued`);
      }}
    >
      <KeyValueList
        layout="inline"
        items={[{ label: 'Balance', value: <Money amount={invoice.balance} currency={invoice.currency} /> }]}
      />
      <FormField label={`Amount (${invoice.currency})`} required error={touched ? amountError : null}>
        <Input type="number" inputMode="decimal" min={0} step="any" value={amount} onChange={(e) => setAmount(e.target.value)} />
      </FormField>
      <FormField label="Reason" required error={touched ? reasonError : null}>
        <Textarea rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}
