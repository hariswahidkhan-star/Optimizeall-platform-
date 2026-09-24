import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Gavel, RotateCcw, Undo2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DateTime } from '@/components/ui/DateTime';
import { FileDrop } from '@/components/ui/FileDrop';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Textarea } from '@/components/ui/Textarea';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import { invalidateAfterSubmission, qk } from '../api/queries';
import type { SubmissionDetail } from '../api/types';
import { firstMessage, focusFirstError, mapFormErrors } from '../lib/formErrors';
import { utcToZonedLocal, zonedLocalToUtcIso } from '../lib/zonedTime';
import { mapProofErrors, PROOF_FIELDS, type ProofField } from './proofErrors';

const URL_RE = /^https?:\/\/\S+\.\S+/i;
const MAX_BYTES = 10 * 1024 * 1024;

/** "Edit & resubmit" for a submission in NeedsCorrection (multipart PUT). */
export function ResubmitForm({ submission }: { submission: SubmissionDetail }) {
  const { user } = useAuth();
  const zone = user?.timeZone;
  const toast = useToast();
  const client = useQueryClient();
  const [postUrl, setPostUrl] = useState(submission.postUrl);
  const [postedAt, setPostedAt] = useState(() => utcToZonedLocal(submission.postedAt, zone));
  const [caption, setCaption] = useState(submission.captionText ?? '');
  const [screenshot, setScreenshot] = useState<File | null>(null);
  const [clientErrors, setClientErrors] = useState<Partial<Record<ProofField, string>>>({});

  const resubmit = useMutation({
    mutationFn: (form: FormData) => api.put<SubmissionDetail>(`/me/submissions/${submission.id}`, form),
    onSuccess: async (updated) => {
      client.setQueryData(qk.submission(submission.id), updated);
      toast.success('Submission sent back for review', 'We’ll let you know when a reviewer has checked it.');
      await invalidateAfterSubmission(client);
    },
    onError: (error) => {
      toast.error('Your correction wasn’t saved', errorMessage(error));
      const mapped = mapProofErrors(error);
      if (mapped) focusFirstError('resubmit', PROOF_FIELDS, mapped.fields);
    },
  });

  const server = resubmit.isError ? mapProofErrors(resubmit.error) : null;
  const errorFor = (f: ProofField) => clientErrors[f] ?? firstMessage(server?.fields[f]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (resubmit.isPending) return;
    const found: Partial<Record<ProofField, string>> = {};
    if (!URL_RE.test(postUrl.trim()))
      found.postUrl = 'Enter the full link to your post, starting with https://';
    const iso = zonedLocalToUtcIso(postedAt, zone);
    if (!iso) found.postedAt = 'Enter when the post went live.';
    else if (new Date(iso).getTime() > Date.now() + 10 * 60_000)
      found.postedAt = 'The post time can’t be in the future.';
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstError('resubmit', PROOF_FIELDS, found);
      return;
    }
    const form = new FormData();
    form.append('postUrl', postUrl.trim());
    form.append('postedAt', iso!);
    form.append('captionText', caption.trim());
    if (screenshot) form.append('screenshot', screenshot, screenshot.name);
    resubmit.mutate(form);
  };

  return (
    <Card as="section" aria-labelledby="resubmit-title">
      <CardHeader
        titleId="resubmit-title"
        title="Edit & resubmit"
        description="Fix what the reviewer asked for, then send the submission back for review."
      />
      <CardBody>
        <form className="pp-form" noValidate onSubmit={onSubmit} aria-labelledby="resubmit-title">
          {server?.form && (
            <Alert tone="danger" role="alert" title={server.form.title}>
              {server.form.details.length > 0 && (
                <ul>
                  {server.form.details.map((d) => (
                    <li key={d}>{d}</li>
                  ))}
                </ul>
              )}
            </Alert>
          )}
          <FormField id="resubmit-postUrl" label="Link to your post" required error={errorFor('postUrl')}>
            <Input type="url" value={postUrl} onChange={(e) => setPostUrl(e.target.value)} />
          </FormField>
          <FormField
            id="resubmit-postedAt"
            label="When it went live"
            required
            error={errorFor('postedAt')}
            hint={`In your time zone (${zone ?? 'browser time'}).`}
          >
            <Input type="datetime-local" value={postedAt} onChange={(e) => setPostedAt(e.target.value)} />
          </FormField>
          <FormField
            id="resubmit-captionText"
            label="Caption you used"
            optional
            error={errorFor('captionText')}
          >
            <Textarea
              value={caption}
              rows={3}
              maxLength={5000}
              onChange={(e) => setCaption(e.target.value)}
            />
          </FormField>
          <div id="resubmit-screenshot" tabIndex={-1}>
            <FileDrop
              label="New screenshot (optional)"
              value={screenshot}
              onChange={setScreenshot}
              maxSizeBytes={MAX_BYTES}
              error={errorFor('screenshot')}
              hint="Leave empty to keep your current screenshot. PNG, JPEG or WebP up to 10 MB."
            />
          </div>
          <div>
            <Button type="submit" loading={resubmit.isPending} leadingIcon={<RotateCcw />}>
              Resubmit for review
            </Button>
          </div>
        </form>
      </CardBody>
    </Card>
  );
}

const APPEAL_MIN = 20;
const APPEAL_MAX = 2000;

/** Appeal a rejection or reversal (once per decision, within the appeal window). */
export function AppealForm({ submission }: { submission: SubmissionDetail }) {
  const toast = useToast();
  const client = useQueryClient();
  const [reason, setReason] = useState('');
  const [clientError, setClientError] = useState<string | null>(null);

  const appeal = useMutation({
    mutationFn: (text: string) =>
      api.post<SubmissionDetail>(`/me/submissions/${submission.id}/appeal`, { reason: text }),
    onSuccess: async (updated) => {
      client.setQueryData(qk.submission(submission.id), updated);
      toast.success('Appeal submitted', 'A different reviewer will look at your submission again.');
      await invalidateAfterSubmission(client);
    },
    onError: (error) => toast.error('Your appeal wasn’t submitted', errorMessage(error)),
  });

  const server = appeal.isError
    ? mapFormErrors(appeal.error, ['reason'], { 'appeal.reason_too_short': 'reason' })
    : null;
  const error = clientError ?? firstMessage(server?.fields.reason);
  const length = reason.trim().length;

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (appeal.isPending) return;
    if (length < APPEAL_MIN) {
      setClientError(`Explain your appeal in at least ${APPEAL_MIN} characters.`);
      document.getElementById('appeal-reason')?.focus();
      return;
    }
    setClientError(null);
    appeal.mutate(reason.trim());
  };

  return (
    <Card as="section" aria-labelledby="appeal-title">
      <CardHeader
        titleId="appeal-title"
        title="Appeal this decision"
        description={
          submission.appealDeadline ? (
            <>
              You can appeal until <DateTime value={submission.appealDeadline} withZone />.
            </>
          ) : undefined
        }
      />
      <CardBody>
        <form className="pp-form" noValidate onSubmit={onSubmit} aria-labelledby="appeal-title">
          {server?.form && (
            <Alert tone="danger" role="alert">
              {server.form.title}
            </Alert>
          )}
          <FormField
            id="appeal-reason"
            label="Why should the decision be reviewed again?"
            required
            error={error}
            hint={
              <span aria-live="polite">
                {length} / {APPEAL_MAX} characters (minimum {APPEAL_MIN})
              </span>
            }
          >
            <Textarea
              rows={5}
              maxLength={APPEAL_MAX}
              value={reason}
              onChange={(e) => {
                setReason(e.target.value);
                if (clientError) setClientError(null);
              }}
            />
          </FormField>
          <p className="pp-note">
            You can appeal each decision once. A reviewer who didn’t make the original decision will look at
            it.
          </p>
          <div>
            <Button type="submit" loading={appeal.isPending} leadingIcon={<Gavel />}>
              Submit appeal
            </Button>
          </div>
        </form>
      </CardBody>
    </Card>
  );
}

const WITHDRAW_REASON_MAX = 1000;

/**
 * Withdraw a submission that hasn't been decided yet (pending, under review or waiting for a correction). Final: the
 * submission earns nothing and leaves the review queue; the campaign slot and the post are freed.
 */
export function WithdrawAction({ submission }: { submission: SubmissionDetail }) {
  const toast = useToast();
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');

  const withdraw = useMutation({
    mutationFn: () =>
      api.post<SubmissionDetail>(`/me/submissions/${submission.id}/withdraw`, {
        confirm: true,
        reason: reason.trim() || null,
      }),
    onSuccess: async (updated) => {
      client.setQueryData(qk.submission(submission.id), updated);
      toast.success('Submission withdrawn', 'It won’t be reviewed. You can submit the post again if you want.');
      await invalidateAfterSubmission(client);
    },
  });

  return (
    <>
      <Button
        variant="secondary"
        leadingIcon={<Undo2 />}
        onClick={() => {
          setReason('');
          setOpen(true);
        }}
      >
        Withdraw submission
      </Button>
      <ConfirmDialog
        open={open}
        onClose={() => setOpen(false)}
        tone="danger"
        title="Withdraw this submission?"
        description="It won’t be reviewed and won’t earn a reward. You can’t undo this, but you can submit the post again."
        confirmLabel="Withdraw"
        cancelLabel="Keep it"
        onConfirm={async () => {
          try {
            await withdraw.mutateAsync();
          } catch (error) {
            // Shown inside the dialog; refresh the page data (a reviewer may have decided a moment ago).
            await client.invalidateQueries({ queryKey: qk.submission(submission.id) });
            throw error;
          }
        }}
      >
        <FormField
          label="Reason (optional)"
          hint={`Shown to the reviewers. ${reason.length} / ${WITHDRAW_REASON_MAX} characters.`}
        >
          <Textarea
            rows={3}
            maxLength={WITHDRAW_REASON_MAX}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
        </FormField>
      </ConfirmDialog>
    </>
  );
}
