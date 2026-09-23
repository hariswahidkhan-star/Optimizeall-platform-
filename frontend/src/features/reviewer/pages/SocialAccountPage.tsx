import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ShieldCheck } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  DateTime,
  ErrorState,
  FormField,
  Input,
  KeyValueList,
  PageHeader,
  RadioGroup,
  SkeletonText,
  StatusBadge,
  Textarea,
  Timeline,
  useToast,
} from '@/components/ui';
import { useAuth } from '@/lib/auth/useAuth';
import { formatNumber } from '@/lib/format/money';
import { humanize, pluralize } from '@/lib/format/text';
import { describeReviewError } from '../api/errors';
import { reviewApi, reviewKeys } from '../api/reviewApi';
import type { ReviewSocialAccountDetail, SocialDecisionRequest } from '../api/types';
import { ActionError, ExternalLink } from '../components/common';

const NOTE_MAX = 500;

function DecisionForm({ data, self }: { data: ReviewSocialAccountDetail; self: boolean }) {
  const a = data.account;
  const [decision, setDecision] = useState<'Verified' | 'Rejected' | null>(null);
  const [note, setNote] = useState('');
  const [createdAt, setCreatedAt] = useState('');
  const [followers, setFollowers] = useState('');
  const [showErrors, setShowErrors] = useState(false);
  const toast = useToast();
  const queryClient = useQueryClient();

  const decide = useMutation({
    mutationFn: (body: SocialDecisionRequest) => reviewApi.socialDecision(a.id, body),
    onSuccess: (result) => {
      toast.success(
        result.account.verificationStatus === 'Verified' ? 'Profile verified' : 'Profile rejected',
        'The participant was notified.',
      );
      queryClient.setQueryData(reviewKeys.socialDetail(a.id), result);
      void queryClient.invalidateQueries({ queryKey: reviewKeys.socialRoot() });
      void queryClient.invalidateQueries({ queryKey: reviewKeys.stats() });
    },
    onError: (error) => toast.error(describeReviewError(error).title, describeReviewError(error).description),
  });

  const today = new Date().toISOString().slice(0, 10);
  const errors = {
    decision: decision ? null : 'Choose Verified or Rejected.',
    note: decision === 'Rejected' && !note.trim() ? 'Explain why the profile was rejected.' : null,
    createdAt:
      createdAt && (createdAt > today || createdAt < '2004-01-01')
        ? 'Use a date between 2004-01-01 and today.'
        : null,
    followers:
      followers && !/^\d{1,10}$/.test(followers.trim()) ? 'Enter a whole number of followers.' : null,
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (decide.isPending || self) return;
    if (Object.values(errors).some(Boolean)) {
      setShowErrors(true);
      return;
    }
    decide.mutate({
      decision: decision!,
      note: note.trim() || undefined,
      verifiedAccountCreatedAt: createdAt ? `${createdAt}T00:00:00Z` : undefined,
      verifiedFollowerCount: followers ? Number(followers) : undefined,
      concurrencyStamp: a.concurrencyStamp,
    });
  };

  if (a.verificationStatus !== 'PendingReview') {
    return (
      <Alert tone="info" title="Not waiting for review">
        This profile is {humanize(a.verificationStatus).toLowerCase()}
        {data.verifiedBy ? ` (decided by ${data.verifiedBy.displayName})` : ''}. Participants request
        verification again after changing their profile.
      </Alert>
    );
  }

  if (self)
    return (
      <Alert tone="warning" title="You can’t verify your own profile">
        Leave this profile for another reviewer.
      </Alert>
    );

  return (
    <form className="stack" onSubmit={submit} noValidate>
      {decide.isError && (
        <ActionError
          error={decide.error}
          onRefresh={() => {
            decide.reset();
            void queryClient.invalidateQueries({ queryKey: reviewKeys.socialDetail(a.id) });
          }}
        />
      )}
      <RadioGroup
        legend="Decision"
        required
        variant="cards"
        orientation="horizontal"
        value={decision}
        onChange={(v) => setDecision(v as 'Verified' | 'Rejected')}
        error={showErrors ? errors.decision : null}
        options={[
          {
            value: 'Verified',
            label: 'Verified',
            description: 'The profile exists, is theirs and the facts hold.',
          },
          {
            value: 'Rejected',
            label: 'Rejected',
            description: 'It can’t be verified. The note tells them why.',
          },
        ]}
      />
      <FormField
        label="Note to the participant"
        required={decision === 'Rejected'}
        optional={decision !== 'Rejected'}
        hint={`Up to ${NOTE_MAX} characters.`}
        error={showErrors ? errors.note : null}
      >
        <Textarea rows={3} maxLength={NOTE_MAX} value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
      <fieldset className="rv-fieldset">
        <legend className="rv-subheading">Verified facts (only if they differ from the declaration)</legend>
        <div className="rv-two">
          <FormField
            label="Account created"
            optional
            hint={`Declared: ${a.accountCreatedAt.slice(0, 10)}`}
            error={showErrors ? errors.createdAt : null}
          >
            <Input
              type="date"
              value={createdAt}
              max={today}
              min="2004-01-01"
              onChange={(e) => setCreatedAt(e.target.value)}
            />
          </FormField>
          <FormField
            label="Followers"
            optional
            hint={`Declared: ${formatNumber(a.followerCount)}`}
            error={showErrors ? errors.followers : null}
          >
            <Input inputMode="numeric" value={followers} onChange={(e) => setFollowers(e.target.value)} />
          </FormField>
        </div>
      </fieldset>
      <div>
        <Button type="submit" leadingIcon={<ShieldCheck />} loading={decide.isPending}>
          Save decision
        </Button>
      </div>
    </form>
  );
}

export function SocialAccountPage() {
  const { accountId = '' } = useParams();
  const { user } = useAuth();
  const query = useQuery({
    queryKey: reviewKeys.socialDetail(accountId),
    queryFn: ({ signal }) => reviewApi.socialAccount(accountId, signal),
    enabled: !!accountId,
  });

  if (query.isPending) return <SkeletonText lines={8} />;
  if (query.isError)
    return (
      <ErrorState error={query.error} onRetry={() => void query.refetch()} retrying={query.isFetching} />
    );

  const data = query.data;
  const a = data.account;
  const self = !!user && a.owner.id === user.id;

  return (
    <>
      <PageHeader
        title={`@${a.handle}`}
        eyebrow={a.platform}
        breadcrumbs={[
          { label: 'Review', to: '/review' },
          { label: 'Social verification', to: '/review/social-verification' },
          { label: `@${a.handle}` },
        ]}
        meta={<StatusBadge kind="socialVerification" status={a.verificationStatus} />}
      />
      <div className="rv-workspace">
        <div className="rv-workspace__left">
          <Card as="section" aria-labelledby="rv-soc-decision">
            <CardHeader
              title="Verification decision"
              titleId="rv-soc-decision"
              description="Open the profile and compare it with what the participant declared."
            />
            <CardBody>
              <DecisionForm data={data} self={self} />
            </CardBody>
          </Card>
        </div>
        <div className="rv-workspace__right stack">
          <Card as="section" aria-labelledby="rv-soc-facts">
            <CardHeader
              title="Declared profile"
              titleId="rv-soc-facts"
              actions={
                <Badge tone={data.qualifies ? 'success' : 'warning'}>
                  {data.qualifies ? 'Meets platform rules' : 'Doesn’t qualify yet'}
                </Badge>
              }
            />
            <CardBody className="stack">
              <KeyValueList
                layout="inline"
                items={[
                  {
                    label: 'Profile',
                    value: <ExternalLink href={a.profileUrl}>{a.profileUrl}</ExternalLink>,
                  },
                  {
                    label: 'Created',
                    value: (
                      <>
                        <DateTime value={a.accountCreatedAt} format="date" /> (
                        {pluralize(a.accountAgeDays, 'day')})
                      </>
                    ),
                  },
                  {
                    label: 'Followers',
                    value: <span className="tabular">{formatNumber(a.followerCount)}</span>,
                  },
                  { label: 'Language', value: a.primaryLanguage ?? '—' },
                  { label: 'Audience country', value: a.audienceCountryCode ?? '—' },
                  { label: 'Active', value: a.isActive ? 'Yes' : 'No (deactivated)' },
                  { label: 'Added', value: <DateTime value={a.createdAt} /> },
                  ...(a.verifiedAt
                    ? [
                        {
                          label: 'Last decision',
                          value: (
                            <>
                              <DateTime value={a.verifiedAt} /> by {data.verifiedBy?.displayName ?? '—'}
                              {a.verificationNote && <p className="text-small">{a.verificationNote}</p>}
                            </>
                          ),
                        },
                      ]
                    : []),
                ]}
              />
              {data.reasons.length > 0 && (
                <Alert tone="warning" title="Eligibility notes">
                  <ul>
                    {data.reasons.map((r) => (
                      <li key={r.code}>{r.message}</li>
                    ))}
                  </ul>
                  {data.eligibleFrom && (
                    <p>
                      Qualifies from <DateTime value={data.eligibleFrom} format="date" />.
                    </p>
                  )}
                </Alert>
              )}
            </CardBody>
          </Card>
          <Card as="section" aria-labelledby="rv-soc-owner">
            <CardHeader title="Owner" titleId="rv-soc-owner" />
            <CardBody>
              {self && (
                <Alert tone="warning" title="This is your own profile">
                  You can’t verify it.
                </Alert>
              )}
              <KeyValueList
                layout="inline"
                items={[
                  { label: 'Name', value: a.owner.displayName },
                  { label: 'Email', value: <span className="rv-break">{a.owner.email}</span> },
                  { label: 'Country', value: a.owner.countryCode },
                  { label: 'Active profiles', value: data.ownerActiveAccountCount },
                ]}
              />
            </CardBody>
          </Card>
          <Card as="section" aria-labelledby="rv-soc-history">
            <CardHeader title="History" titleId="rv-soc-history" />
            <CardBody>
              {data.history.length === 0 ? (
                <p className="text-muted">No history yet.</p>
              ) : (
                <Timeline
                  label="Profile history"
                  items={data.history.map((h, i) => ({
                    id: `${h.at}-${i}`,
                    title: humanize(h.action.replace(/^social\./, '')),
                    timestamp: h.at,
                    actor: h.actorDisplayName ?? 'System',
                    description: h.reason ?? undefined,
                    tone: h.action.endsWith('verified')
                      ? 'success'
                      : h.action.endsWith('rejected')
                        ? 'danger'
                        : 'neutral',
                  }))}
                />
              )}
            </CardBody>
          </Card>
        </div>
      </div>
    </>
  );
}
