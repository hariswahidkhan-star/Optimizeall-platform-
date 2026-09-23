import { isApiError } from '@/lib/api/errors';
import { formatDateTime } from '@/lib/format/dates';

export interface ReviewErrorInfo {
  /** Short headline. */
  title: string;
  /** Human explanation of what happened and what to do. */
  description: string;
  /** The data on screen is stale: offer a "Refresh" action. */
  refresh: boolean;
  code: string;
}

/** Reads `errors.claimedBy` / `errors.claimExpiresAt` of a 409 review.claimed_by_other. */
export function claimHolder(error: unknown): { name: string; until: string | null } | null {
  if (!isApiError(error) || error.code !== 'review.claimed_by_other') return null;
  const name = error.fieldError('claimedBy') ?? 'Another reviewer';
  const until = error.fieldError('claimExpiresAt') ?? null;
  return { name, until };
}

/**
 * Maps the backend's stable error codes for reviewer actions to plain-language messages. Unknown codes fall back to
 * the server's own title, so new server rules still show something meaningful.
 */
export function describeReviewError(error: unknown): ReviewErrorInfo {
  if (!isApiError(error)) {
    return {
      title: 'Something went wrong',
      description: error instanceof Error && error.message ? error.message : 'Please try again.',
      refresh: false,
      code: 'unknown',
    };
  }
  const code = error.code;
  const make = (title: string, description: string, refresh = false): ReviewErrorInfo => ({
    title,
    description,
    refresh,
    code,
  });

  switch (code) {
    case 'review.claimed_by_other': {
      const holder = claimHolder(error);
      const until = holder?.until ? ` until ${formatDateTime(holder.until)}` : '';
      return make(
        'Someone else is reviewing this',
        `${holder?.name ?? 'Another reviewer'} holds this submission${until}. Pick another one or refresh later.`,
        true,
      );
    }
    case 'review.already_decided':
      return make(
        'Already decided',
        'Another reviewer decided this submission (or it changed) while you had it open. Refresh to see the current state.',
        true,
      );
    case 'review.not_claimed':
      return make(
        'Claim no longer held',
        'Your claim expired or was released. Claim the submission again to continue.',
        true,
      );
    case 'review.self_review':
      return make('You can’t review your own submission', 'Leave it for another reviewer.');
    case 'participant.not_active':
      return make(
        'Participant is suspended',
        'Suspended participants can’t be approved. Reject the submission or leave it until the account is reinstated.',
      );
    case 'review.reason_required':
      return make('Reason needed', 'Explain the decision to the participant in at least 5 characters.');
    case 'review.quality_bonus_requires_approval':
      return make('Quality bonus only on approval', 'Remove the quality bonus or approve the submission.');
    case 'reward.quality_bonus_not_configured':
      return make(
        'No quality bonus for this campaign',
        'The campaign’s reward rules have no quality bonus. Approve without one.',
      );
    case 'fx.rate_missing':
      return make(
        'Exchange rate missing',
        'The reward can’t be converted to the settlement currency yet. Try again later or tell the finance team.',
      );
    case 'review.live_check_not_due':
      return make('Live check not due yet', error.title);
    case 'review.live_check_not_pending':
      return make(
        'Live check already recorded',
        'Someone already recorded this live check. Refresh the list.',
        true,
      );
    case 'review.not_approved':
      return make(
        'Not approved',
        'Only approved submissions can be reversed. Refresh to see its current status.',
        true,
      );
    case 'ledger.in_payout_batch':
      return make(
        'Earnings are in a payout batch',
        'At least one earning is part of a payout batch that is being paid. Ask finance to remove it first.',
      );
    case 'confirmation.required':
      return make('Confirmation required', 'Confirm the action to continue.');
    case 'appeal.same_reviewer':
      return make(
        'A different reviewer must resolve this appeal',
        'You made the original decision on this submission. Appeals need a second pair of eyes — leave it for a colleague.',
      );
    case 'appeal.already_resolved':
      return make(
        'Appeal already resolved',
        'Another reviewer resolved this appeal. Refresh to see the outcome.',
        true,
      );
    case 'appeal.submission_changed':
      return make(
        'Submission changed',
        'The submission’s status changed after the appeal was filed. Refresh and review it again.',
        true,
      );
    case 'review.not_a_reviewer':
      return make('Not a reviewer', 'The selected person can’t review submissions. Pick someone else.');
    case 'social.self_verification':
      return make('You can’t verify your own profile', 'Leave this profile for another reviewer.');
    case 'social.not_pending':
      return make(
        'Profile is no longer waiting for review',
        'It was decided by someone else or changed by its owner. Refresh to see the current state.',
        true,
      );
    case 'social.note_required':
      return make('Note needed', 'Explain why the profile was rejected; the participant will see this note.');
    case 'social.invalid_created_at':
      return make('Check the creation date', error.fieldError('verifiedAccountCreatedAt') ?? error.title);
    case 'concurrency.conflict':
      return make(
        'Changed by someone else',
        'This record changed since you opened it. Refresh and try again.',
        true,
      );
    case 'auth.forbidden':
      return make('Not allowed', 'You don’t have permission to do this.');
    default:
      return make(
        error.status === 409 ? 'Couldn’t save' : 'Something went wrong',
        error.title,
        error.status === 409,
      );
  }
}

/** Throws an Error whose message is the human description — for ConfirmDialog, which shows `error.message`. */
export function humanError(error: unknown): Error {
  const info = describeReviewError(error);
  return new Error(`${info.title}. ${info.description}`);
}
