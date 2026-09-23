import { RefreshCw } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Alert, Button, FormField, Input, KeyValueList, Money, Textarea, useToast } from '@/components/ui';
import { errorCode, financeErrorMessage } from '../api/errors';
import { useRecordPayment } from '../api/hooks';
import type { PayoutItem } from '../api/types';
import { FormDialog } from '../components/FormDialog';
import { localInputToIso, toLocalInputValue } from '../lib/format';

export interface RecordPaymentDialogProps {
  batchId: string;
  item: PayoutItem | null;
  onClose: () => void;
  onRefresh: () => void;
}

/**
 * Records a payment made outside the platform. The request is guarded against double submission; a 409
 * `payout.already_recorded` means someone else (or an earlier retry) already recorded it.
 */
export function RecordPaymentDialog({ batchId, item, onClose, onRefresh }: RecordPaymentDialogProps) {
  const record = useRecordPayment(batchId);
  const toast = useToast();
  const [reference, setReference] = useState('');
  const [paidAt, setPaidAt] = useState('');
  const [note, setNote] = useState('');
  const [touched, setTouched] = useState(false);
  const open = !!item;

  useEffect(() => {
    if (open) {
      setReference('');
      setPaidAt(toLocalInputValue());
      setNote('');
      setTouched(false);
    }
  }, [open]);

  const ref = reference.trim();
  const paidIso = localInputToIso(paidAt);
  const referenceError =
    ref.length < 3 || ref.length > 120 ? 'Enter the payment reference (3–120 characters).' : null;
  const future = paidIso !== null && new Date(paidIso).getTime() > Date.now() + 5 * 60_000;
  const paidAtError = !paidIso
    ? 'Enter when the payment was made.'
    : future
      ? 'The payment date can’t be in the future.'
      : null;
  const valid = !referenceError && !paidAtError;

  if (!item) return null;

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title="Record payment"
      description="Only record a payment that has actually left the account. A recorded payment can’t be undone."
      submitLabel="Record payment"
      onSubmit={async () => {
        setTouched(true);
        if (!valid) return false;
        const result = await record.mutateAsync({
          itemId: item.itemId,
          paymentReference: ref,
          paidAt: paidIso!,
          note: note.trim() || undefined,
        });
        toast.success(
          'Payment recorded',
          result.batchStatus === 'Completed'
            ? 'That was the last open item — the batch is now completed.'
            : `${item.user.displayName} is marked as paid.`,
        );
      }}
      renderError={(error) =>
        errorCode(error) === 'payout.already_recorded' ? (
          <Alert
            tone="warning"
            role="alert"
            title="Already recorded by someone else"
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
            A payment for this item was already recorded — possibly by another finance user or an earlier
            attempt. Nothing was changed. Refresh to see the recorded reference.
          </Alert>
        ) : (
          <Alert tone="danger" role="alert">
            {financeErrorMessage(error)}
          </Alert>
        )
      }
    >
      <KeyValueList
        layout="inline"
        items={[
          { label: 'Participant', value: `${item.user.displayName} (${item.user.email})` },
          { label: 'Amount', value: <Money amount={item.amount} currency={item.currency} /> },
          { label: 'Destination', value: item.destinationHint ?? '—' },
        ]}
      />
      <FormField
        label="Payment reference"
        hint="The bank, PayPal or wallet transaction reference."
        required
        error={touched ? referenceError : null}
      >
        <Input
          value={reference}
          maxLength={120}
          autoComplete="off"
          spellCheck={false}
          onChange={(e) => setReference(e.target.value)}
          onBlur={() => setTouched(true)}
        />
      </FormField>
      <FormField label="Paid at" hint="Your local time." required error={touched ? paidAtError : null}>
        <Input
          type="datetime-local"
          value={paidAt}
          max={toLocalInputValue()}
          onChange={(e) => setPaidAt(e.target.value)}
        />
      </FormField>
      <FormField label="Note" optional>
        <Textarea rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}
