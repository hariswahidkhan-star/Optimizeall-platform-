import { ConfirmDialog, Money, useToast } from '@/components/ui';
import { humanError } from '../api/errors';
import { useReverseEarning } from '../api/hooks';
import type { LedgerRow } from '../api/types';

/** Reverses an earning (reason ≥ 10). Paid earnings produce a clawback netted in the next batch. */
export function ReverseDialog({ row, onClose }: { row: LedgerRow | null; onClose: () => void }) {
  const reverse = useReverseEarning();
  const toast = useToast();
  const paid = row?.status === 'Paid';
  return (
    <ConfirmDialog
      open={!!row}
      onClose={onClose}
      tone="danger"
      title="Reverse this earning?"
      description={
        paid
          ? 'This earning was already paid. Reversing creates a negative clawback entry that is netted against the participant’s next payout.'
          : 'The earning is marked Reversed and will not be paid.'
      }
      requireReason
      reasonMinLength={10}
      confirmLabel="Reverse earning"
      onConfirm={async ({ reason }) => {
        if (!row) return;
        try {
          await reverse.mutateAsync({ id: row.id, reason });
          toast.success('Earning reversed');
        } catch (e) {
          throw humanError(e);
        }
      }}
    >
      {row && (
        <p>
          {row.user.displayName} · {row.description} ·{' '}
          <Money amount={row.settlementAmount} currency={row.settlementCurrency} />
        </p>
      )}
    </ConfirmDialog>
  );
}
