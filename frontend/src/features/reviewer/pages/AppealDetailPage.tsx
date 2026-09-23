import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Gavel } from 'lucide-react';
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
  KeyValueList,
  PageHeader,
  RadioGroup,
  SkeletonText,
  StatusBadge,
  Textarea,
  useToast,
} from '@/components/ui';
import { useAuth } from '@/lib/auth/useAuth';
import { describeReviewError } from '../api/errors';
import { reviewApi, reviewKeys } from '../api/reviewApi';
import type { AppealDetail } from '../api/types';
import { ActionError } from '../components/common';
import { AccountPanel, ActivityPanel, HistoryPanel, PostPanel, RiskPanel } from '../workspace/Evidence';
import { RequirementsPanel } from '../workspace/RequirementsPanel';
import { APPEAL_TONE } from './AppealsPage';

export const NOTE_MIN = 5;
const NOTE_MAX = 2000;

function ResolveForm({ data, sameReviewer }: { data: AppealDetail; sameReviewer: boolean }) {
  const [outcome, setOutcome] = useState<'Upheld' | 'Overturned' | null>(null);
  const [note, setNote] = useState('');
  const [showErrors, setShowErrors] = useState(false);
  const toast = useToast();
  const queryClient = useQueryClient();
  const appealId = data.appeal.id;

  const resolve = useMutation({
    mutationFn: () =>
      reviewApi.resolveAppeal(appealId, {
        outcome: outcome!,
        note: note.trim(),
        concurrencyStamp: data.appeal.concurrencyStamp,
      }),
    onSuccess: (result) => {
      toast.success(
        result.appeal.status === 'Overturned' ? 'Appeal overturned' : 'Appeal upheld',
        result.appeal.status === 'Overturned'
          ? 'The submission is approved and fresh earnings were recorded. The participant was notified.'
          : 'The original decision stands. The participant was notified.',
      );
      void queryClient.invalidateQueries({ queryKey: reviewKeys.all });
    },
    onError: (error) => toast.error(describeReviewError(error).title, describeReviewError(error).description),
  });

  const noteError =
    note.trim().length < NOTE_MIN ? `Write a resolution note (at least ${NOTE_MIN} characters).` : null;
  const outcomeError = outcome ? null : 'Choose an outcome.';

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (resolve.isPending || sameReviewer) return;
    if (noteError || outcomeError) {
      setShowErrors(true);
      return;
    }
    resolve.mutate();
  };

  if (data.appeal.status !== 'Open') {
    return (
      <Card as="section" aria-labelledby="rv-resolution-heading">
        <CardHeader title="Resolution" titleId="rv-resolution-heading" />
        <CardBody>
          <KeyValueList
            layout="inline"
            items={[
              {
                label: 'Outcome',
                value: <Badge tone={APPEAL_TONE[data.appeal.status]}>{data.appeal.status}</Badge>,
              },
              { label: 'Resolved by', value: data.appeal.resolvedBy?.displayName ?? '—' },
              { label: 'Resolved', value: <DateTime value={data.appeal.resolvedAt} /> },
              { label: 'Note', value: data.appeal.resolutionNote ?? '—' },
            ]}
          />
        </CardBody>
      </Card>
    );
  }

  return (
    <Card as="section" aria-labelledby="rv-resolve-heading" className="rv-decision">
      <CardHeader
        title="Resolve appeal"
        titleId="rv-resolve-heading"
        description="Overturning approves the submission with fresh earnings. Upholding keeps the original decision."
      />
      <CardBody>
        {sameReviewer ? (
          <Alert tone="warning" title="A different reviewer must resolve this appeal">
            You made the original decision on this submission. Appeals need a second pair of eyes — leave it
            for a colleague.
          </Alert>
        ) : (
          <form className="stack" onSubmit={submit} noValidate>
            {resolve.isError && (
              <ActionError
                error={resolve.error}
                onRefresh={() => {
                  resolve.reset();
                  void queryClient.invalidateQueries({ queryKey: reviewKeys.appeal(appealId) });
                }}
              />
            )}
            <RadioGroup
              legend="Outcome"
              required
              variant="cards"
              value={outcome}
              onChange={(v) => setOutcome(v as 'Upheld' | 'Overturned')}
              error={showErrors ? outcomeError : null}
              options={[
                {
                  value: 'Upheld',
                  label: 'Uphold the original decision',
                  description: `The submission stays ${data.appeal.decisionAppealed.toLowerCase()}.`,
                },
                {
                  value: 'Overturned',
                  label: 'Overturn and approve',
                  description: 'The submission is approved and earnings are recorded.',
                },
              ]}
            />
            <FormField
              label="Resolution note"
              required
              hint="The participant sees this note. 5–2000 characters."
              error={showErrors ? noteError : null}
            >
              <Textarea
                rows={4}
                maxLength={NOTE_MAX}
                value={note}
                onChange={(e) => setNote(e.target.value)}
              />
            </FormField>
            <div>
              <Button type="submit" leadingIcon={<Gavel />} loading={resolve.isPending}>
                Resolve appeal
              </Button>
            </div>
          </form>
        )}
      </CardBody>
    </Card>
  );
}

export function AppealDetailPage() {
  const { appealId = '' } = useParams();
  const { user } = useAuth();
  const query = useQuery({
    queryKey: reviewKeys.appeal(appealId),
    queryFn: ({ signal }) => reviewApi.appeal(appealId, signal),
    enabled: !!appealId,
  });

  if (query.isPending) return <SkeletonText lines={8} />;
  if (query.isError)
    return (
      <ErrorState error={query.error} onRetry={() => void query.refetch()} retrying={query.isFetching} />
    );

  const data = query.data;
  const { appeal, review, originalDecidedBy } = data;
  const isAdmin = !!user?.roles.includes('Admin');
  const sameReviewer = !!user && originalDecidedBy?.id === user.id && !isAdmin;

  return (
    <>
      <PageHeader
        title={`Appeal: ${review.requirements.campaignTitle}`}
        breadcrumbs={[
          { label: 'Review', to: '/review' },
          { label: 'Appeals', to: '/review/appeals' },
          { label: 'Appeal' },
        ]}
        description={`${review.participant.displayName} appealed a ${appeal.decisionAppealed.toLowerCase()} decision.`}
        meta={
          <>
            <Badge tone={APPEAL_TONE[appeal.status]}>{appeal.status}</Badge>
            <StatusBadge kind="submission" status={review.submission.status} />
          </>
        }
      />
      <div className="rv-workspace">
        <div className="rv-workspace__left">
          <Card as="section" aria-labelledby="rv-appeal-heading">
            <CardHeader
              title="Participant’s appeal"
              titleId="rv-appeal-heading"
              description={<DateTime value={appeal.createdAt} format="both" />}
            />
            <CardBody>
              <blockquote className="rv-caption rv-prewrap">{appeal.reason}</blockquote>
            </CardBody>
          </Card>
          <Card as="section" aria-labelledby="rv-original-heading">
            <CardHeader title="Original decision" titleId="rv-original-heading" />
            <CardBody>
              <KeyValueList
                layout="inline"
                items={[
                  {
                    label: 'Decision',
                    value: <StatusBadge kind="submission" status={appeal.decisionAppealed} />,
                  },
                  {
                    label: 'Decided by',
                    value: (
                      <span className="rv-inline">
                        {originalDecidedBy?.displayName ?? 'System'}
                        {originalDecidedBy?.id === user?.id && (
                          <Badge size="sm" tone="warning">
                            You
                          </Badge>
                        )}
                      </span>
                    ),
                  },
                  { label: 'Decided', value: <DateTime value={review.submission.decidedAt} /> },
                  { label: 'Reason given', value: review.submission.decisionReason ?? '—' },
                ]}
              />
            </CardBody>
          </Card>
          <ResolveForm data={data} sameReviewer={sameReviewer} />
          <RequirementsPanel
            requirements={review.requirements}
            platform={review.submission.platform}
            countryCode={review.participant.countryCode}
          />
        </div>
        <div className="rv-workspace__right stack">
          <RiskPanel detail={review} />
          <PostPanel detail={review} />
          <AccountPanel detail={review} />
          <HistoryPanel detail={review} />
          <ActivityPanel detail={review} />
        </div>
      </div>
    </>
  );
}
