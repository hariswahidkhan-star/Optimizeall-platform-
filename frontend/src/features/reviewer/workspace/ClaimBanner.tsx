import { Hand, LogOut, RefreshCw, TimerReset } from 'lucide-react';
import { Alert, Button, DateTime, StatusBadge } from '@/components/ui';
import type { ReviewSubmission } from '../api/types';
import { formatCountdown } from '../components/common';

export type ClaimState = 'mine' | 'other' | 'none' | 'closed';

export function isOpenStatus(status: string): boolean {
  return status === 'Pending' || status === 'UnderReview';
}

/** Where the claim stands right now (a claim of mine past its expiry counts as none). */
export function claimState(submission: ReviewSubmission, now: number): ClaimState {
  if (!isOpenStatus(submission.status)) return 'closed';
  const { claim } = submission;
  const expires = claim.claimExpiresAt ? new Date(claim.claimExpiresAt).getTime() : 0;
  if (!claim.isActive || !claim.claimedBy || expires <= now) return 'none';
  return claim.isMine ? 'mine' : 'other';
}

interface ClaimBannerProps {
  submission: ReviewSubmission;
  state: ClaimState;
  now: number;
  onClaim: () => void;
  onRelease: () => void;
  onRefresh: () => void;
  claiming: boolean;
  releasing: boolean;
  refreshing: boolean;
  /** The claim was mine and has run out while the page was open. */
  expired: boolean;
}

/** Who holds the submission, a countdown for my claim, and claim / extend / release actions. */
export function ClaimBanner({
  submission,
  state,
  now,
  onClaim,
  onRelease,
  onRefresh,
  claiming,
  releasing,
  refreshing,
  expired,
}: ClaimBannerProps) {
  const { claim } = submission;

  if (state === 'closed') {
    return (
      <Alert tone="info" title="This submission has been decided" className="rv-claim">
        <span className="rv-inline">
          <StatusBadge kind="submission" status={submission.status} size="sm" />
          {submission.decidedBy && <span>by {submission.decidedBy.displayName}</span>}
          {submission.decidedAt && <DateTime value={submission.decidedAt} format="relative" />}
        </span>
      </Alert>
    );
  }

  if (state === 'mine' && claim.claimExpiresAt) {
    const remaining = new Date(claim.claimExpiresAt).getTime() - now;
    const low = remaining < 2 * 60_000;
    return (
      <Alert
        tone={low ? 'warning' : 'brand'}
        icon={<Hand />}
        title="You’re reviewing this submission"
        className="rv-claim"
        actions={
          <>
            <Button
              size="sm"
              variant="secondary"
              leadingIcon={<TimerReset />}
              onClick={onClaim}
              loading={claiming}
            >
              Extend claim
            </Button>
            <Button
              size="sm"
              variant="ghost"
              leadingIcon={<LogOut />}
              onClick={onRelease}
              loading={releasing}
            >
              Release
            </Button>
          </>
        }
      >
        <p>
          Claim expires in{' '}
          <strong className="tabular rv-countdown" aria-hidden="true">
            {formatCountdown(remaining)}
          </strong>
          <span className="visually-hidden">
            <DateTime value={claim.claimExpiresAt} format="relative" />
          </span>{' '}
          (at <DateTime value={claim.claimExpiresAt} />
          ). Other reviewers can’t decide it until then.
        </p>
      </Alert>
    );
  }

  if (state === 'other') {
    return (
      <Alert
        tone="warning"
        title={`${claim.claimedBy?.displayName ?? 'Another reviewer'} is reviewing this submission`}
        className="rv-claim"
        actions={
          <Button
            size="sm"
            variant="secondary"
            leadingIcon={<RefreshCw />}
            onClick={onRefresh}
            loading={refreshing}
          >
            Refresh
          </Button>
        }
      >
        {claim.claimExpiresAt && (
          <p>
            Their claim runs until <DateTime value={claim.claimExpiresAt} /> (
            <DateTime value={claim.claimExpiresAt} format="relative" />
            ). You can read everything, but only they can decide it until then.
          </p>
        )}
      </Alert>
    );
  }

  return (
    <Alert
      tone={expired ? 'warning' : 'neutral'}
      title={expired ? 'Your claim expired' : 'Not claimed'}
      className="rv-claim"
      role={expired ? 'alert' : undefined}
      actions={
        <Button size="sm" leadingIcon={<Hand />} onClick={onClaim} loading={claiming}>
          {expired ? 'Claim again' : 'Claim to review'}
        </Button>
      }
    >
      <p>Claim the submission so nobody else decides it while you review. Claims last a limited time.</p>
    </Alert>
  );
}
