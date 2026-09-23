import { useEffect, useState } from 'react';
import { FormField, Money, Textarea, useToast } from '@/components/ui';
import { useUnholdItem } from '../api/hooks';
import type { PayoutItem } from '../api/types';
import { FormDialog } from '../components/FormDialog';

/** Releases a held draft item back to Pending (optional note). */
export function UnholdDialog({
  batchId,
  item,
  onClose,
}: {
  batchId: string;
  item: PayoutItem | null;
  onClose: () => void;
}) {
  const unhold = useUnholdItem(batchId);
  const toast = useToast();
  const [note, setNote] = useState('');
  useEffect(() => {
    if (item) setNote('');
  }, [item]);
  if (!item) return null;
  return (
    <FormDialog
      open
      onClose={onClose}
      title={`Release the hold on ${item.user.displayName}’s item?`}
      description="The item goes back to Pending and will be paid when the batch is finalized."
      submitLabel="Release hold"
      onSubmit={async () => {
        await unhold.mutateAsync({ itemId: item.itemId, note: note.trim() });
        toast.success('Hold released');
      }}
    >
      <p>
        Amount: <Money amount={item.amount} currency={item.currency} />
        {item.holdReason && <> · held because: {item.holdReason}</>}
      </p>
      <FormField label="Note" optional hint="Recorded in the audit log.">
        <Textarea rows={2} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}
