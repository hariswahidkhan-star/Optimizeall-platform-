import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  ErrorState,
  PageHeader,
  Skeleton,
  SkeletonText,
  StatusBadge,
  Tabs,
  useToast,
} from '@/components/ui';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatMoney } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import { useIsMobile } from '@/lib/hooks/useMediaQuery';
import { describeReviewError } from '../api/errors';
import { reviewApi, reviewKeys } from '../api/reviewApi';
import type { DecisionRequest, DecisionResult } from '../api/types';
import { useNow } from '../components/common';
import { RiskBadge } from '../components/risk';
import { useClaim, useOpenNext, useRelease, type WorkspaceState } from '../hooks/useReviewActions';
import { ClaimBanner, claimState } from '../workspace/ClaimBanner';
import { DecisionPanel } from '../workspace/DecisionPanel';
import { AccountPanel, ActivityPanel, HistoryPanel, PostPanel, RiskPanel } from '../workspace/Evidence';
import { RequirementsPanel } from '../workspace/RequirementsPanel';
import { ReverseAction } from '../workspace/ReverseAction';

const DECISION_TITLES: Record<string, string> = {
  Approved: 'Submission approved',
  NeedsCorrection: 'Correction requested',
  Rejected: 'Submission rejected',
};

function decisionSummary(result: DecisionResult, requestedBonus?: number): string | undefined {
  const parts: string[] = [];
  if (result.reward) {
    parts.push(`Earnings recorded: ${formatMoney(result.reward.total, result.reward.currency)}.`);
    if (result.reward.appliedCaps.length > 0)
      parts.push(`Caps applied: ${result.reward.appliedCaps.map(humanize).join(', ')}.`);
    const quality = result.reward.lines.find((l) => l.type === 'QualityBonus');
    if (requestedBonus && quality && quality.amount < requestedBonus)
      parts.push(
        `Quality bonus limited to ${formatMoney(quality.amount, result.reward.currency)} ${
          quality.uncappedAmount < requestedBonus ? 'by the campaign’s quality-bonus rule' : 'by caps'
        }.`,
      );
  }
  if (result.liveCheckStatus === 'Pending' && result.liveCheckDueAt)
    parts.push('Earnings stay pending until the live check.');
  return parts.length ? parts.join(' ') : undefined;
}

function WorkspaceSkeleton() {
  return (
    <div className="rv-workspace" aria-busy="true">
      <div className="stack">
        <Skeleton height={28} width="60%" />
        <SkeletonText lines={6} />
      </div>
      <div className="stack">
        <Skeleton height={220} />
        <SkeletonText lines={4} />
      </div>
    </div>
  );
}

export function WorkspacePage() {
  const { submissionId = '' } = useParams();
  const location = useLocation();
  const queueSearch = (location.state as WorkspaceState | null)?.queueSearch ?? '';
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { user, hasPermission } = useAuth();
  const isMobile = useIsMobile();
  const now = useNow(1000);
  const [tab, setTab] = useState('submission');
  const [decisionError, setDecisionError] = useState<unknown>(null);
  const [claimError, setClaimError] = useState<unknown>(null);
  const [advancing, setAdvancing] = useState(false);

  const detailQuery = useQuery({
    queryKey: reviewKeys.detail(submissionId),
    queryFn: ({ signal }) => reviewApi.detail(submissionId, signal),
    enabled: !!submissionId,
  });
  const detail = detailQuery.data;

  useEffect(() => {
    setDecisionError(null);
    setClaimError(null);
  }, [submissionId]);

  const claim = useClaim();
  const release = useRelease();
  const openNext = useOpenNext();

  const decide = useMutation<DecisionResult, unknown, { request: DecisionRequest; next: boolean }>({
    mutationFn: ({ request }) => reviewApi.decide(submissionId, request),
    onSuccess: async (result, { request, next }) => {
      toast.success(
        DECISION_TITLES[result.status] ?? 'Decision saved',
        decisionSummary(result, request.qualityBonusAmount),
      );
      await queryClient.invalidateQueries({ queryKey: reviewKeys.all });
      if (!next) return;
      setAdvancing(true);
      try {
        const found = await openNext(submissionId, queueSearch);
        if (!found) {
          toast.info('Nothing left to review', 'No other open submission matches your queue filters.');
          navigate(`/review/queue${queueSearch ? `?${queueSearch}` : ''}`);
        }
      } catch (error) {
        toast.error('Couldn’t open the next submission', describeReviewError(error).description);
      } finally {
        setAdvancing(false);
      }
    },
    onError: (error) => {
      setDecisionError(error);
      const info = describeReviewError(error);
      toast.error(info.title, info.description);
    },
  });

  if (detailQuery.isPending) return <WorkspaceSkeleton />;
  if (detailQuery.isError || !detail)
    return (
      <ErrorState
        error={detailQuery.error}
        onRetry={() => void detailQuery.refetch()}
        retrying={detailQuery.isFetching}
      />
    );

  const s = detail.submission;
  const state = claimState(s, now);
  // The server still reports my claim as active, but its expiry has passed on this clock.
  const expired = s.claim.isMine && s.claim.isActive && state === 'none';
  const ownSubmission = !!user && detail.participant.id === user.id;
  const canDecide = state === 'mine' && !ownSubmission && !advancing;

  const refresh = () => {
    setDecisionError(null);
    setClaimError(null);
    void detailQuery.refetch();
  };

  const doClaim = () => {
    setClaimError(null);
    claim.mutate(submissionId, {
      onSuccess: () => toast.success(state === 'mine' ? 'Claim extended' : 'Submission claimed'),
      onError: (error) => {
        setClaimError(error);
        toast.error(describeReviewError(error).title, describeReviewError(error).description);
      },
    });
  };

  const doRelease = () =>
    release.mutate(submissionId, {
      onSuccess: () => {
        toast.info('Submission released', 'It’s back in the queue for other reviewers.');
        navigate(`/review/queue${queueSearch ? `?${queueSearch}` : ''}`);
      },
      onError: (error) => {
        setClaimError(error);
        toast.error(describeReviewError(error).title, describeReviewError(error).description);
      },
    });

  const blockedReason = ownSubmission
    ? 'This is your own submission. Self-review isn’t allowed — leave it for another reviewer.'
    : state === 'closed'
      ? `This submission is ${humanize(s.status).toLowerCase()}. There is nothing left to decide.`
      : state === 'other'
        ? `${s.claim.claimedBy?.displayName ?? 'Another reviewer'} holds the claim.`
        : advancing
          ? 'Opening the next submission…'
          : 'Claim this submission to decide it.';

  const decision = (
    <DecisionPanel
      detail={detail}
      canDecide={canDecide}
      blockedReason={blockedReason}
      pending={decide.isPending || advancing}
      error={decisionError}
      onDismissError={() => setDecisionError(null)}
      onRefresh={refresh}
      refreshing={detailQuery.isFetching}
      onSubmit={(request, next) => {
        setDecisionError(null);
        decide.mutate({ request, next });
      }}
    />
  );
  const reverse =
    s.status === 'Approved' && hasPermission(Permissions.SubmissionsReverse) ? (
      <ReverseAction detail={detail} />
    ) : null;
  const requirements = (
    <RequirementsPanel
      requirements={detail.requirements}
      platform={s.platform}
      countryCode={detail.participant.countryCode}
    />
  );

  return (
    <>
      <PageHeader
        title={detail.requirements.campaignTitle}
        breadcrumbs={[
          { label: 'Review', to: '/review' },
          { label: 'Queue', to: `/review/queue${queueSearch ? `?${queueSearch}` : ''}` },
          { label: 'Submission' },
        ]}
        description={`${s.platform} post by ${detail.participant.displayName} (@${detail.account.handle})`}
        meta={
          <>
            <StatusBadge kind="submission" status={s.status} />
            <RiskBadge score={s.riskScore} />
          </>
        }
      />
      <div className="stack">
        {ownSubmission && (
          <Alert tone="danger" title="This is your own submission">
            Self-review isn’t allowed. Leave it for another reviewer.
          </Alert>
        )}
        <ClaimBanner
          submission={s}
          state={state}
          now={now}
          expired={expired}
          onClaim={doClaim}
          onRelease={doRelease}
          onRefresh={refresh}
          claiming={claim.isPending}
          releasing={release.isPending}
          refreshing={detailQuery.isFetching}
        />
        {claimError !== null && (
          <Alert
            tone="warning"
            role="alert"
            title={describeReviewError(claimError).title}
            onDismiss={() => setClaimError(null)}
          >
            {describeReviewError(claimError).description}
          </Alert>
        )}

        {isMobile ? (
          <>
            <Tabs
              label="Submission details"
              value={tab}
              onValueChange={setTab}
              tabs={[
                {
                  id: 'submission',
                  label: 'Post',
                  badge: detail.flags.filter((f) => !f.resolved).length || undefined,
                  content: (
                    <div className="stack">
                      <RiskPanel detail={detail} />
                      <PostPanel detail={detail} />
                    </div>
                  ),
                },
                { id: 'requirements', label: 'Rules', content: requirements },
                {
                  id: 'people',
                  label: 'Account',
                  content: (
                    <div className="stack">
                      <AccountPanel detail={detail} />
                    </div>
                  ),
                },
                {
                  id: 'history',
                  label: 'History',
                  badge: detail.relatedSubmissions.length || undefined,
                  content: (
                    <div className="stack">
                      <HistoryPanel detail={detail} />
                      <ActivityPanel detail={detail} />
                    </div>
                  ),
                },
              ]}
            />
            {decision}
            {reverse}
          </>
        ) : (
          <div className="rv-workspace">
            <div className="rv-workspace__left">
              {decision}
              {reverse}
              {requirements}
            </div>
            <div className="rv-workspace__right stack">
              <RiskPanel detail={detail} />
              <PostPanel detail={detail} />
              <AccountPanel detail={detail} />
              <HistoryPanel detail={detail} />
              <ActivityPanel detail={detail} />
            </div>
          </div>
        )}
      </div>
    </>
  );
}
