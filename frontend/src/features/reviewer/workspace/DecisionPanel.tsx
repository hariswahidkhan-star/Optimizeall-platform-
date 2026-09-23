import { Check, MessageSquareWarning, X } from 'lucide-react';
import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { Button, Card, CardBody, CardHeader, FormField, Input, Switch, Textarea } from '@/components/ui';
import { safeStorage } from '@/lib/hooks/storage';
import type { DecisionRequest, ReviewDecision, ReviewDetail } from '../api/types';
import { ActionError } from '../components/common';
import { useHotkeys } from '../hooks/useHotkeys';

export const REASON_MIN = 5;
export const REASON_MAX = 1000;
const NEXT_KEY = 'oa.review.continueNext';

export function correctionTemplates(detail: ReviewDetail): string[] {
  const r = detail.requirements;
  return [
    `Add the required disclosure "${r.disclosureText}" to the caption.`,
    ...(r.requiredHashtags ? [`Add the required hashtags: ${r.requiredHashtags}.`] : []),
    ...(r.requiredMentions ? [`Mention ${r.requiredMentions} in the post.`] : []),
    'The screenshot doesn’t clearly show the post. Upload a full, uncropped screenshot.',
    'The link doesn’t open the post. Submit the direct link to the post itself.',
    'The posting date doesn’t match the post. Correct the posting date.',
  ];
}

export const REJECT_TEMPLATES = [
  'The post is not public or could not be found.',
  'The post doesn’t use the approved campaign content.',
  'The screenshot appears to be reused from another submission.',
  'The post was published outside the campaign window.',
  'The social account doesn’t meet the campaign’s eligibility rules.',
  'This post was already submitted.',
];

const MODES: { id: ReviewDecision; label: string; key: string; icon: ReactNode }[] = [
  { id: 'Approve', label: 'Approve', key: 'A', icon: <Check /> },
  { id: 'RequestCorrection', label: 'Request correction', key: 'C', icon: <MessageSquareWarning /> },
  { id: 'Reject', label: 'Reject', key: 'R', icon: <X /> },
];

interface DecisionPanelProps {
  detail: ReviewDetail;
  /** False unless I hold an active claim on an open submission. */
  canDecide: boolean;
  /** Why deciding is not possible right now (shown instead of the form). */
  blockedReason?: ReactNode;
  pending: boolean;
  error: unknown;
  onDismissError: () => void;
  onRefresh: () => void;
  refreshing: boolean;
  onSubmit: (request: DecisionRequest, continueNext: boolean) => void;
}

/**
 * Approve / request correction / reject. Correction and rejection need a reason the participant will read;
 * approval can add a quality bonus (the server bounds it by the campaign's quality-bonus rule).
 * Shortcuts A, C and R pick a decision; they are ignored while typing.
 */
export function DecisionPanel({
  detail,
  canDecide,
  blockedReason,
  pending,
  error,
  onDismissError,
  onRefresh,
  refreshing,
  onSubmit,
}: DecisionPanelProps) {
  const [mode, setMode] = useState<ReviewDecision | null>(null);
  const [reason, setReason] = useState('');
  const [bonus, setBonus] = useState('');
  const [showErrors, setShowErrors] = useState(false);
  const [continueNext, setContinueNext] = useState(() => safeStorage.get(NEXT_KEY) !== 'false');
  const [focusTick, setFocusTick] = useState(0);
  const reasonRef = useRef<HTMLTextAreaElement>(null);
  const submitRef = useRef<HTMLButtonElement>(null);
  const inFlight = useRef(false);
  const formId = useId();

  const submissionId = detail.submission.id;
  useEffect(() => {
    // A different submission: start from a clean form.
    setMode(null);
    setReason('');
    setBonus('');
    setShowErrors(false);
  }, [submissionId]);

  useEffect(() => {
    if (!pending) inFlight.current = false;
  }, [pending]);

  useEffect(() => {
    if (focusTick === 0) return;
    if (mode === 'Approve') submitRef.current?.focus();
    else reasonRef.current?.focus();
  }, [focusTick, mode]);

  const choose = (next: ReviewDecision) => {
    setMode(next);
    setShowErrors(false);
    setFocusTick((t) => t + 1);
  };

  useHotkeys(
    { a: () => choose('Approve'), c: () => choose('RequestCorrection'), r: () => choose('Reject') },
    canDecide && !pending,
  );

  const needsReason = mode === 'RequestCorrection' || mode === 'Reject';
  const trimmed = reason.trim();
  const reasonError =
    needsReason && trimmed.length < REASON_MIN
      ? `Explain the decision to the participant (at least ${REASON_MIN} characters).`
      : null;
  const bonusValue = bonus.trim() === '' ? undefined : Number(bonus);
  const bonusError =
    mode === 'Approve' &&
    bonusValue !== undefined &&
    (!Number.isFinite(bonusValue) || bonusValue < 0 || !/^\d+(\.\d{1,2})?$/.test(bonus.trim()))
      ? 'Enter an amount of 0 or more with at most 2 decimals.'
      : null;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!mode || !canDecide || pending || inFlight.current) return;
    if (reasonError || bonusError) {
      setShowErrors(true);
      (reasonError ? reasonRef.current : null)?.focus();
      return;
    }
    inFlight.current = true;
    onSubmit(
      {
        decision: mode,
        reason: trimmed || undefined,
        qualityBonusAmount: mode === 'Approve' && bonusValue ? bonusValue : undefined,
        concurrencyStamp: detail.submission.concurrencyStamp,
      },
      continueNext,
    );
  };

  const applyTemplate = (text: string) => {
    setReason((current) => (current.trim() ? `${current.trimEnd()}\n${text}` : text));
    reasonRef.current?.focus();
  };

  const templates = mode === 'RequestCorrection' ? correctionTemplates(detail) : REJECT_TEMPLATES;
  const submitLabel =
    mode === 'Approve'
      ? 'Confirm approval'
      : mode === 'RequestCorrection'
        ? 'Send correction request'
        : mode === 'Reject'
          ? 'Confirm rejection'
          : 'Decide';

  return (
    <Card as="section" aria-labelledby="rv-decision-heading" className="rv-decision">
      <CardHeader
        title="Decision"
        titleId="rv-decision-heading"
        description={canDecide ? 'Shortcuts: A approve · C correction · R reject' : undefined}
      />
      <CardBody className="stack">
        {error !== null && error !== undefined && (
          <ActionError
            error={error}
            onRefresh={onRefresh}
            refreshing={refreshing}
            onDismiss={onDismissError}
          />
        )}
        {!canDecide ? (
          <p className="text-muted">{blockedReason ?? 'Claim this submission to decide it.'}</p>
        ) : (
          <>
            <div className="rv-modes" role="group" aria-label="Choose a decision">
              {MODES.map((m) => (
                <Button
                  key={m.id}
                  variant={mode === m.id ? (m.id === 'Reject' ? 'danger' : 'primary') : 'secondary'}
                  aria-pressed={mode === m.id}
                  aria-keyshortcuts={m.key}
                  leadingIcon={m.icon}
                  onClick={() => choose(m.id)}
                  disabled={pending}
                  className="rv-mode"
                >
                  {m.label}
                  <kbd className="rv-kbd" aria-hidden="true">
                    {m.key}
                  </kbd>
                </Button>
              ))}
            </div>

            {mode && (
              <form id={formId} className="stack" onSubmit={submit} noValidate>
                {mode === 'Approve' && (
                  <FormField
                    label="Quality bonus"
                    optional
                    hint={`Only for exceptional posts. Limited by the campaign’s quality-bonus rule; the amount actually awarded is shown after approval (${detail.submission.currency}).`}
                    error={showErrors ? bonusError : null}
                  >
                    <Input
                      inputMode="decimal"
                      value={bonus}
                      onChange={(e) => setBonus(e.target.value)}
                      placeholder="0.00"
                      trailing={<span className="text-small">{detail.submission.currency}</span>}
                    />
                  </FormField>
                )}
                {needsReason && (
                  <>
                    <div>
                      <p className="rv-subheading" id={`${formId}-templates`}>
                        Quick reasons
                      </p>
                      <ul className="rv-templates" aria-labelledby={`${formId}-templates`}>
                        {templates.map((t) => (
                          <li key={t}>
                            <Button
                              size="sm"
                              variant="ghost"
                              onClick={() => applyTemplate(t)}
                              disabled={pending}
                            >
                              {t}
                            </Button>
                          </li>
                        ))}
                      </ul>
                    </div>
                    <FormField
                      label={mode === 'Reject' ? 'Reason for rejection' : 'What needs correcting'}
                      required
                      hint="The participant sees this message. Edit the quick reasons as needed."
                      error={showErrors ? reasonError : null}
                    >
                      <Textarea
                        ref={reasonRef}
                        rows={4}
                        maxLength={REASON_MAX}
                        value={reason}
                        onChange={(e) => setReason(e.target.value)}
                      />
                    </FormField>
                  </>
                )}
                <Switch
                  checked={continueNext}
                  onCheckedChange={(checked) => {
                    setContinueNext(checked);
                    safeStorage.set(NEXT_KEY, String(checked));
                  }}
                  label="Open the next submission afterwards"
                />
                <div className="cluster">
                  <Button
                    ref={submitRef}
                    type="submit"
                    variant={mode === 'Reject' ? 'danger' : mode === 'Approve' ? 'highlight' : 'primary'}
                    loading={pending}
                  >
                    {continueNext ? `${submitLabel} & next` : submitLabel}
                  </Button>
                  <Button variant="ghost" onClick={() => setMode(null)} disabled={pending}>
                    Cancel
                  </Button>
                </div>
              </form>
            )}
          </>
        )}
      </CardBody>
    </Card>
  );
}
