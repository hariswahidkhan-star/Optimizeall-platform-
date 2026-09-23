import type { SelectOption } from '@/components/ui/Select';
import { statusOptions } from '@/components/ui/statusMap';
import type { Tone } from '@/components/ui/tones';
import { humanize } from '@/lib/format/text';
import {
  EARNING_TYPES,
  SOCIAL_PLATFORMS,
  TICKET_CATEGORIES,
  type SubmissionStatus,
  type TicketStatus,
} from '../api/types';

export const platformOptions: SelectOption[] = SOCIAL_PLATFORMS.map((p) => ({
  value: p,
  label: p === 'X' ? 'X (Twitter)' : p,
}));

export const submissionStatusOptions: SelectOption[] = [
  { value: 'Pending', label: 'Pending' },
  { value: 'UnderReview', label: 'Under review' },
  { value: 'NeedsCorrection', label: 'Needs correction' },
  { value: 'Approved', label: 'Approved' },
  { value: 'Rejected', label: 'Rejected' },
  { value: 'Reversed', label: 'Reversed' },
];

export const earningTypeOptions: SelectOption[] = EARNING_TYPES.map((t) => ({
  value: t,
  label: earningTypeLabel(t),
}));

export function earningTypeLabel(type: string): string {
  switch (type) {
    case 'PostReward':
      return 'Post reward';
    case 'FirstPostBonus':
      return 'First-post bonus';
    case 'TimeLimitedBonus':
      return 'Time-limited bonus';
    case 'QualityBonus':
      return 'Quality bonus';
    case 'ReferralReward':
      return 'Referral reward';
    default:
      return humanize(type);
  }
}

export const ticketCategoryOptions: SelectOption[] = TICKET_CATEGORIES.map((c) => ({
  value: c,
  label: c === 'SocialProfile' ? 'Social profile' : humanize(c),
}));

/** Participant wording for ticket statuses that differs from the shared (staff-neutral) status map. */
const PARTICIPANT_TICKET_LABELS: Partial<Record<TicketStatus, string>> = {
  AwaitingParticipant: 'Awaiting your reply',
};

/** Label override for `<StatusBadge kind="ticket" label=…>` in the participant portal (undefined = shared label). */
export function participantTicketLabel(status: string): string | undefined {
  return PARTICIPANT_TICKET_LABELS[status as TicketStatus];
}

export const ticketStatusOptions: SelectOption[] = statusOptions('ticket').map((o) => ({
  value: o.value,
  label: participantTicketLabel(o.value) ?? o.label,
}));

/** Human names for submission timeline actions. */
export function timelineActionLabel(action: string): string {
  const labels: Record<string, string> = {
    submitted: 'Submitted',
    claimed: 'Review started',
    approved: 'Approved',
    correction_requested: 'Correction requested',
    rejected: 'Rejected',
    resubmitted: 'Resubmitted',
    appealed: 'Appeal filed',
    appeal_overturned: 'Appeal accepted — decision overturned',
    appeal_upheld: 'Appeal reviewed — decision upheld',
    reversed: 'Reversed',
    live_check_confirmed: 'Live check passed',
    live_check_removed: 'Post found removed',
  };
  return labels[action] ?? humanize(action);
}

export function timelineTone(status: SubmissionStatus | null | undefined): Tone {
  switch (status) {
    case 'Approved':
      return 'success';
    case 'NeedsCorrection':
      return 'warning';
    case 'Rejected':
    case 'Reversed':
      return 'danger';
    case 'UnderReview':
      return 'info';
    default:
      return 'neutral';
  }
}

export function qualifyingActionLabel(action: string): string {
  switch (action) {
    case 'EmailVerified':
      return 'verifies their email address';
    case 'FirstApprovedSubmission':
      return 'gets their first post approved';
    case 'FirstPaidPayout':
      return 'receives their first payout';
    default:
      return humanize(action).toLowerCase();
  }
}

export const payoutMethodOptions: SelectOption[] = [
  { value: 'BankTransfer', label: 'Bank transfer' },
  { value: 'PayPal', label: 'PayPal' },
  { value: 'MobileWallet', label: 'Mobile wallet' },
  { value: 'Other', label: 'Other' },
];
