import { useEffect, useRef, useState } from 'react';
import { Alert, Checkbox, FormField, Input, Money, Select, Textarea, useToast } from '@/components/ui';
import { useCreateAdjustment, useSchedule } from '../api/hooks';
import { FormDialog } from '../components/FormDialog';
import { UserPicker, type PickedUser } from '../components/UserPicker';
import { GUID_RE, currencyOptions, newRequestId } from '../lib/format';

export interface AdjustmentDialogProps {
  open: boolean;
  onClose: () => void;
  /** Pre-selected participant (e.g. from the balance page). */
  user?: PickedUser | null;
}

/**
 * Manual credit/debit. A `requestId` is generated once each time the dialog opens and reused for every retry, so a
 * timeout followed by "try again" can never create two adjustments (the server answers 200 `created:false`).
 */
export function AdjustmentDialog({ open, onClose, user }: AdjustmentDialogProps) {
  const create = useCreateAdjustment();
  const schedule = useSchedule(open);
  const toast = useToast();
  const [requestId, setRequestId] = useState('');
  const [picked, setPicked] = useState<PickedUser | null>(null);
  const [amount, setAmount] = useState('');
  const [currency, setCurrency] = useState('');
  const [reason, setReason] = useState('');
  const [submissionId, setSubmissionId] = useState('');
  const [ticketId, setTicketId] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [touched, setTouched] = useState(false);
  const userRef = useRef(user);
  userRef.current = user;

  // One requestId per opening of the dialog: every retry of this adjustment reuses it.
  useEffect(() => {
    if (open) {
      setRequestId(newRequestId());
      setPicked(userRef.current ?? null);
      setAmount('');
      setCurrency('');
      setReason('');
      setSubmissionId('');
      setTicketId('');
      setConfirmed(false);
      setTouched(false);
    }
  }, [open]);

  const effectiveCurrency = currency || schedule.data?.current.settlementCurrency || 'USD';
  const numeric = Number(amount);
  const userError =
    !picked || !GUID_RE.test(picked.id) ? 'Choose a participant or paste a valid user id.' : null;
  const amountError =
    amount.trim() === '' || !Number.isFinite(numeric)
      ? 'Enter an amount (negative for a debit).'
      : numeric === 0
        ? 'The amount can’t be zero.'
        : Math.abs(numeric) > 1_000_000
          ? 'The amount must be within ±1,000,000.'
          : null;
  const reasonError = reason.trim().length < 10 ? 'Give a reason of at least 10 characters.' : null;
  const optionalIdError = (v: string) =>
    v.trim() && !GUID_RE.test(v.trim()) ? 'Enter a valid id or leave empty.' : null;
  const valid =
    !userError &&
    !amountError &&
    !reasonError &&
    !optionalIdError(submissionId) &&
    !optionalIdError(ticketId) &&
    confirmed;

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      sensitive
      title="New adjustment"
      description="Creates an audited Adjustment entry. Credits become payable after the hold period; debits apply immediately."
      submitLabel="Create adjustment"
      onSubmit={async () => {
        setTouched(true);
        if (!valid) return false;
        const result = await create.mutateAsync({
          requestId,
          userId: picked!.id,
          amount: numeric,
          currency: effectiveCurrency,
          reason: reason.trim(),
          submissionId: submissionId.trim() || null,
          supportTicketId: ticketId.trim() || null,
          confirm: true,
        });
        if (result.created) toast.success('Adjustment created', `For ${result.earning.user.displayName}.`);
        else
          toast.info(
            'Adjustment already recorded',
            'This request was already processed; no duplicate was created.',
          );
      }}
    >
      <UserPicker value={picked} onChange={setPicked} error={touched ? userError : null} />
      <div className="fin-form-row">
        <FormField
          label="Amount"
          hint="Positive to credit, negative to debit."
          required
          error={touched ? amountError : null}
        >
          <Input
            type="number"
            inputMode="decimal"
            step="any"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
        </FormField>
        <FormField label="Currency" required>
          <Select
            value={effectiveCurrency}
            onChange={(e) => setCurrency(e.target.value)}
            options={currencyOptions}
          />
        </FormField>
      </div>
      {!amountError && (
        <p className="text-small" aria-live="polite">
          {numeric > 0 ? 'Credit' : 'Debit'} of{' '}
          <Money amount={Math.abs(numeric)} currency={effectiveCurrency} />
          {picked?.label ? ` to ${picked.label}` : ''}.
        </p>
      )}
      <FormField
        label="Reason"
        hint="At least 10 characters. Recorded in the audit log."
        required
        error={touched ? reasonError : null}
      >
        <Textarea rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
      <details className="fin-details">
        <summary>Link to a submission or support ticket (optional)</summary>
        <div className="stack">
          <FormField label="Submission id" optional error={optionalIdError(submissionId)}>
            <Input
              value={submissionId}
              spellCheck={false}
              onChange={(e) => setSubmissionId(e.target.value)}
            />
          </FormField>
          <FormField label="Support ticket id" optional error={optionalIdError(ticketId)}>
            <Input value={ticketId} spellCheck={false} onChange={(e) => setTicketId(e.target.value)} />
          </FormField>
        </div>
      </details>
      {effectiveCurrency !== (schedule.data?.current.settlementCurrency ?? effectiveCurrency) && (
        <Alert tone="info">
          The amount is converted to {schedule.data?.current.settlementCurrency} with the exchange rate in
          force now, and that rate is stored with the entry.
        </Alert>
      )}
      <Checkbox
        label="I confirm this adjustment is correct and authorised."
        checked={confirmed}
        invalid={touched && !confirmed}
        onChange={(e) => setConfirmed(e.target.checked)}
      />
    </FormDialog>
  );
}
