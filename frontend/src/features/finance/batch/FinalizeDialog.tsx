import { useEffect, useState } from 'react';
import { Alert, Button, FormField, Input, KeyValueList, Money, Textarea } from '@/components/ui';
import { financeErrorMessage, errorCode } from '../api/errors';
import { useFinalizeBatch } from '../api/hooks';
import type { FinalizeResponse, PayoutBatchSummary } from '../api/types';
import { FormDialog } from '../components/FormDialog';
import { dateOnlyToDisplay } from '../lib/format';

export interface FinalizeDialogProps {
  open: boolean;
  onClose: () => void;
  batch: PayoutBatchSummary;
  /** Batch concurrency stamp of the reviewed state; a stale stamp is rejected with 409. */
  concurrencyStamp: string;
  heldCount: number;
  onFinalized: (result: FinalizeResponse) => void;
  onRefresh: () => void;
}

/**
 * Four-eyes finalize: summary, type-to-confirm the batch reference, optional reason. Sends the reviewed
 * `concurrencyStamp` so a batch changed after review is never finalized.
 */
export function FinalizeDialog({
  open,
  onClose,
  batch,
  concurrencyStamp,
  heldCount,
  onFinalized,
  onRefresh,
}: FinalizeDialogProps) {
  const finalize = useFinalizeBatch(batch.id);
  const [typed, setTyped] = useState('');
  const [reason, setReason] = useState('');

  useEffect(() => {
    if (open) {
      setTyped('');
      setReason('');
    }
  }, [open]);

  const matches = typed.trim() === batch.reference;

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      sensitive
      title={`Finalize ${batch.reference}?`}
      description="Finalizing freezes the batch and tells participants their payout is scheduled. It does not send any money."
      submitLabel="Finalize batch"
      canSubmit={matches}
      onSubmit={async () => {
        const result = await finalize.mutateAsync({ concurrencyStamp, reason: reason.trim() });
        onFinalized(result);
      }}
      renderError={(error) => {
        const code = errorCode(error);
        const stale = code === 'concurrency.conflict' || code === 'payout.not_draft';
        return (
          <Alert
            tone="danger"
            role="alert"
            title={
              code === 'payout.self_finalize' ? 'Four-eyes rule' : stale ? 'The batch changed' : undefined
            }
            actions={
              stale ? (
                <Button
                  size="sm"
                  variant="secondary"
                  onClick={() => {
                    onRefresh();
                    onClose();
                  }}
                >
                  Refresh batch
                </Button>
              ) : undefined
            }
          >
            {financeErrorMessage(error)}
          </Alert>
        );
      }}
    >
      <KeyValueList
        layout="inline"
        items={[
          { label: 'Payable items', value: batch.itemCount },
          { label: 'Total', value: <Money amount={batch.totalAmount} currency={batch.currency} /> },
          { label: 'Period', value: batch.periodKey },
          { label: 'Payment date', value: dateOnlyToDisplay(batch.paymentDate) },
          { label: 'Prepared by', value: batch.preparedBy?.displayName ?? 'System job' },
        ]}
      />
      <Alert tone="info" title="No money is sent">
        After finalizing, pay each participant outside Optimize All, then record the payment reference for
        each item.
        {heldCount > 0 &&
          ` ${heldCount} held ${heldCount === 1 ? 'item is' : 'items are'} released back to the participants’ balances.`}
      </Alert>
      <FormField
        label={
          <>
            Type <strong>{batch.reference}</strong> to confirm
          </>
        }
        required
      >
        <Input
          value={typed}
          autoComplete="off"
          spellCheck={false}
          onChange={(e) => setTyped(e.target.value)}
        />
      </FormField>
      <FormField label="Reason" optional hint="Recorded in the audit log.">
        <Textarea rows={2} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}
