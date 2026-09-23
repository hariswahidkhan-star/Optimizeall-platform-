import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Send } from 'lucide-react';
import { useRef, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Dialog } from '@/components/ui/Dialog';
import { FileDrop } from '@/components/ui/FileDrop';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Select } from '@/components/ui/Select';
import { Textarea } from '@/components/ui/Textarea';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import { pluralize } from '@/lib/format/text';
import { invalidateAfterSubmission, qk } from '../api/queries';
import type { CampaignDetail, SubmissionDetail } from '../api/types';
import { PlatformTag, platformLabel } from '../components/Platform';
import { focusFirstError, firstMessage } from '../lib/formErrors';
import { utcToZonedLocal, zonedLocalToUtcIso } from '../lib/zonedTime';
import { mapProofErrors, PROOF_FIELDS, type ProofField } from '../submissions/proofErrors';

export const SCREENSHOT_MAX_BYTES = 10 * 1024 * 1024;
const URL_RE = /^https?:\/\/\S+\.\S+/i;
/** The API accepts up to 10 minutes of clock skew. */
const FUTURE_TOLERANCE_MS = 10 * 60_000;

export interface SubmitProofDialogProps {
  open: boolean;
  onClose: () => void;
  campaign: CampaignDetail;
  /** Experiment variant shown to the participant (sent with the submission for attribution). */
  experimentVariantId?: string | null;
}

type Errors = Partial<Record<ProofField, string>>;

/** Proof-of-post form (multipart POST /me/submissions). */
export function SubmitProofDialog({ open, onClose, campaign, experimentVariantId }: SubmitProofDialogProps) {
  const { user } = useAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const client = useQueryClient();
  const zone = user?.timeZone;
  const eligible = campaign.eligibility.accounts.filter((a) => a.isEligible);
  const accountRef = useRef<HTMLSelectElement>(null);

  const [accountId, setAccountId] = useState(eligible.length === 1 ? eligible[0]!.socialAccountId : '');
  const [postUrl, setPostUrl] = useState('');
  const [postedAt, setPostedAt] = useState(() => utcToZonedLocal(new Date(), zone));
  const [caption, setCaption] = useState('');
  const [screenshot, setScreenshot] = useState<File | null>(null);
  const [clientErrors, setClientErrors] = useState<Errors>({});

  const account = campaign.eligibility.accounts.find((a) => a.socialAccountId === accountId);
  const requireScreenshot = campaign.rewardTerms?.requireScreenshot ?? true;
  const liveHours = campaign.rewardTerms?.minPostLiveHours ?? 0;

  const submit = useMutation({
    mutationFn: (form: FormData) => api.upload<SubmissionDetail>('/me/submissions', form),
    onSuccess: async (created) => {
      toast.success('Proof submitted', 'We’ll review your post and let you know the outcome.');
      client.setQueryData(qk.submission(created.id), created);
      await invalidateAfterSubmission(client);
      onClose();
      navigate(`/app/submissions/${created.id}`);
    },
    onError: (error) => {
      const mapped = mapProofErrors(error);
      toast.error('Your proof wasn’t submitted', errorMessage(error));
      if (mapped) focusFirstError('proof', PROOF_FIELDS, mapped.fields);
    },
  });

  const server = submit.isError ? mapProofErrors(submit.error) : null;
  const errorFor = (field: ProofField) => clientErrors[field] ?? firstMessage(server?.fields[field]);

  const validate = (): Errors => {
    const found: Errors = {};
    if (!accountId) found.socialAccountId = 'Choose the profile you posted from.';
    if (!postUrl.trim()) found.postUrl = 'Paste the public link to your post.';
    else if (!URL_RE.test(postUrl.trim())) found.postUrl = 'Enter the full link, starting with https://';
    const postedIso = zonedLocalToUtcIso(postedAt, zone);
    if (!postedIso) found.postedAt = 'Enter when the post went live.';
    else if (new Date(postedIso).getTime() > Date.now() + FUTURE_TOLERANCE_MS)
      found.postedAt = 'The post time can’t be in the future.';
    if (requireScreenshot && !screenshot) found.screenshot = 'This campaign needs a screenshot of your post.';
    return found;
  };

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (submit.isPending) return; // Enter key / double click while a request is running
    const found = validate();
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstError('proof', PROOF_FIELDS, found);
      return;
    }
    const form = new FormData();
    form.append('campaignId', campaign.id);
    form.append('socialAccountId', accountId);
    form.append('platform', account!.platform);
    form.append('postUrl', postUrl.trim());
    form.append('postedAt', zonedLocalToUtcIso(postedAt, zone)!);
    if (caption.trim()) form.append('captionText', caption.trim());
    if (experimentVariantId) form.append('experimentVariantId', experimentVariantId);
    if (screenshot) form.append('screenshot', screenshot, screenshot.name);
    submit.mutate(form);
  };

  const clearFieldError = (field: ProofField) =>
    setClientErrors((current) => (current[field] ? { ...current, [field]: undefined } : current));

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Submit proof of your post"
      description={campaign.title}
      size="lg"
      dismissible={!submit.isPending}
      initialFocusRef={accountRef}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={submit.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="submit-proof-form" loading={submit.isPending} leadingIcon={<Send />}>
            Submit proof
          </Button>
        </>
      }
    >
      <form
        id="submit-proof-form"
        className="pp-form"
        noValidate
        onSubmit={onSubmit}
        aria-label="Submit proof"
      >
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

        <FormField
          id="proof-socialAccountId"
          label="Profile you posted from"
          required
          error={errorFor('socialAccountId')}
          hint={
            account ? <PlatformTag platform={account.platform} /> : 'The platform is set from the profile.'
          }
        >
          <Select
            ref={accountRef}
            value={accountId}
            placeholder="Choose a profile"
            onChange={(e) => {
              setAccountId(e.target.value);
              clearFieldError('socialAccountId');
            }}
            options={campaign.eligibility.accounts.map((a) => ({
              value: a.socialAccountId,
              label: `${platformLabel(a.platform)} · @${a.handle}${a.isEligible ? '' : ' (not eligible)'}`,
              disabled: !a.isEligible,
            }))}
          />
        </FormField>

        <FormField
          id="proof-postUrl"
          label="Link to your post"
          required
          error={errorFor('postUrl')}
          hint="The public URL of the post itself, not your profile."
        >
          <Input
            type="url"
            inputMode="url"
            autoComplete="off"
            placeholder="https://"
            value={postUrl}
            onChange={(e) => {
              setPostUrl(e.target.value);
              clearFieldError('postUrl');
            }}
          />
        </FormField>

        <FormField
          id="proof-postedAt"
          label="When it went live"
          required
          error={errorFor('postedAt')}
          hint={`In your time zone (${zone ?? 'browser time'}).`}
        >
          <Input
            type="datetime-local"
            value={postedAt}
            max={utcToZonedLocal(new Date(Date.now() + FUTURE_TOLERANCE_MS), zone)}
            onChange={(e) => {
              setPostedAt(e.target.value);
              clearFieldError('postedAt');
            }}
          />
        </FormField>

        <FormField id="proof-captionText" label="Caption you used" optional error={errorFor('captionText')}>
          <Textarea value={caption} maxLength={5000} rows={3} onChange={(e) => setCaption(e.target.value)} />
        </FormField>

        <div id="proof-screenshot" tabIndex={-1}>
          <FileDrop
            label={requireScreenshot ? 'Screenshot of your post' : 'Screenshot of your post (optional)'}
            value={screenshot}
            onChange={(file) => {
              setScreenshot(file);
              clearFieldError('screenshot');
            }}
            maxSizeBytes={SCREENSHOT_MAX_BYTES}
            required={requireScreenshot}
            error={errorFor('screenshot')}
            hint="PNG, JPEG or WebP up to 10 MB, at least 200×200 px."
          />
        </div>

        <Alert tone="info" title="Before you submit">
          <ul>
            <li>
              The screenshot is <strong>evidence for the reviewer, not proof on its own</strong> — we check
              the live post at the link you give.
            </li>
            {liveHours > 0 ? (
              <li>
                Keep the post <strong>public for at least {pluralize(liveHours, 'hour')}</strong>. Removing it
                earlier reverses the reward.
              </li>
            ) : (
              <li>Keep the post public while it’s being reviewed.</li>
            )}
            <li>Each post can only be submitted once, by one person.</li>
          </ul>
        </Alert>
      </form>
    </Dialog>
  );
}
