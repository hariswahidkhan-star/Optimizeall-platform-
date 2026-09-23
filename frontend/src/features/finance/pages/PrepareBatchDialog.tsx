import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, DateTime, FormField, Input, RadioGroup, Skeleton, Textarea, useToast } from '@/components/ui';
import { usePrepareBatch, useSchedule } from '../api/hooks';
import { FormDialog } from '../components/FormDialog';
import { dateOnlyToDisplay } from '../lib/format';

export interface PrepareBatchDialogProps {
  open: boolean;
  onClose: () => void;
}

/**
 * Prepares (or finds) the draft batch of a completed period. The period list comes from the server schedule; other
 * completed periods can be entered by their cutoff date (the server validates it: `payout.invalid_period`).
 */
export function PrepareBatchDialog({ open, onClose }: PrepareBatchDialogProps) {
  const schedule = useSchedule(open);
  const prepare = usePrepareBatch();
  const toast = useToast();
  const navigate = useNavigate();
  const [choice, setChoice] = useState<'last' | 'other'>('last');
  const [otherKey, setOtherKey] = useState('');
  const [note, setNote] = useState('');

  useEffect(() => {
    if (open) {
      setChoice('last');
      setOtherKey('');
      setNote('');
    }
  }, [open]);

  const last = schedule.data?.lastCompletedPeriod;
  const periodKey = choice === 'last' ? last?.periodKey : otherKey;
  const canSubmit = choice === 'last' ? !!last : /^\d{4}-\d{2}-\d{2}$/.test(otherKey);

  const submit = async () => {
    const result = await prepare.mutateAsync({ periodKey, note: note.trim() || undefined });
    if (result.created) {
      toast.success(
        `Batch ${result.batch.reference} prepared`,
        'Review the warnings and items. Nothing has been paid.',
      );
    } else {
      toast.info(
        'Batch already exists',
        `${result.batch.reference} already covers this period — opening it instead of creating another.`,
      );
    }
    navigate(`/finance/batches/${result.batch.id}`);
  };

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title="Prepare payout batch"
      description="Creates a draft batch with every approved earning available at the period’s cutoff. Preparing never pays anyone."
      submitLabel="Prepare batch"
      onSubmit={submit}
      canSubmit={canSubmit}
    >
      {schedule.isPending ? (
        <Skeleton height={96} />
      ) : schedule.isError ? (
        <Alert tone="warning">
          The payout schedule couldn’t be loaded. You can still enter a period’s cutoff date below.
        </Alert>
      ) : null}
      <RadioGroup
        legend="Payout period"
        value={choice}
        onChange={(v) => setChoice(v as 'last' | 'other')}
        variant="cards"
        options={[
          {
            value: 'last',
            label: last ? `Last completed period · ${last.periodKey}` : 'Last completed period',
            description: last ? (
              <>
                Cutoff <DateTime value={last.cutoffAt} withZone /> · payment date{' '}
                {dateOnlyToDisplay(last.paymentDate)}
              </>
            ) : (
              'Default'
            ),
            disabled: !last && !schedule.isError,
          },
          {
            value: 'other',
            label: 'An earlier completed period',
            description: 'Enter the period’s cutoff date (its period key).',
          },
        ]}
      />
      {choice === 'other' && (
        <FormField
          label="Cutoff date (period key)"
          hint="Must be a cutoff date of the current schedule that has already passed."
          required
        >
          <Input
            type="date"
            value={otherKey}
            max={last?.cutoffLocalDate}
            onChange={(e) => setOtherKey(e.target.value)}
          />
        </FormField>
      )}
      {schedule.data && schedule.data.upcoming.length > 0 && (
        <details className="fin-details">
          <summary>Upcoming periods (not preparable yet)</summary>
          <ul className="fin-plain-list">
            {schedule.data.upcoming.map((p) => (
              <li key={p.periodKey}>
                <strong>{p.periodKey}</strong> · cutoff <DateTime value={p.cutoffAt} withZone /> · payment{' '}
                {dateOnlyToDisplay(p.paymentDate)}
              </li>
            ))}
          </ul>
        </details>
      )}
      <FormField label="Note" optional hint="Visible on the batch; recorded in the audit log.">
        <Textarea rows={2} maxLength={2000} value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
    </FormDialog>
  );
}
