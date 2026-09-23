import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Undo2 } from 'lucide-react';
import { useState } from 'react';
import { Button, Card, CardBody, CardHeader, ConfirmDialog, Money, useToast } from '@/components/ui';
import { humanize } from '@/lib/format/text';
import { humanError } from '../api/errors';
import { reviewApi, reviewKeys } from '../api/reviewApi';
import type { ReviewDetail } from '../api/types';

export const REVERSE_CONFIRM_TEXT = 'REVERSE';

/** Reverse an approved submission (submissions.reverse): every live earning is reversed in the ledger. */
export function ReverseAction({ detail }: { detail: ReviewDetail }) {
  const [open, setOpen] = useState(false);
  const toast = useToast();
  const queryClient = useQueryClient();
  const id = detail.submission.id;
  const reverse = useMutation({
    mutationFn: (reason: string) => reviewApi.reverse(id, reason),
    onSuccess: (result) => {
      toast.success(
        'Approval reversed',
        `${result.earnings.filter((e) => e.status === 'Reversed').length} earnings reversed. The participant was notified.`,
      );
      void queryClient.invalidateQueries({ queryKey: reviewKeys.all });
    },
  });
  const live = detail.earnings.filter((e) => e.status !== 'Reversed' && e.status !== 'Declined');

  return (
    <Card as="section" aria-labelledby="rv-reverse-heading">
      <CardHeader
        title="Reverse approval"
        titleId="rv-reverse-heading"
        description="Use when an approved post turns out to be ineligible. The participant is notified."
      />
      <CardBody>
        <Button variant="danger" leadingIcon={<Undo2 />} onClick={() => setOpen(true)}>
          Reverse approval…
        </Button>
      </CardBody>
      <ConfirmDialog
        open={open}
        onClose={() => setOpen(false)}
        tone="danger"
        title="Reverse this approval?"
        description="All live earnings of this submission are reversed (paid ones are clawed back). This is audited and can’t be undone here."
        confirmLabel="Reverse approval"
        requireReason
        reasonMinLength={5}
        reasonLabel="Reason (shown to the participant)"
        reasonHint="At least 5 characters. Recorded in the audit log."
        confirmText={REVERSE_CONFIRM_TEXT}
        onConfirm={async ({ reason }) => {
          try {
            await reverse.mutateAsync(reason);
          } catch (error) {
            throw humanError(error);
          }
        }}
      >
        {live.length > 0 && (
          <ul className="rv-list">
            {live.map((e) => (
              <li key={e.id} className="rv-list__item">
                <span>{humanize(e.type)}</span>
                <Money amount={e.amount} currency={e.currency} />
              </li>
            ))}
          </ul>
        )}
      </ConfirmDialog>
    </Card>
  );
}
