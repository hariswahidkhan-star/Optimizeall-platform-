import type { SelectOption } from '@/components/ui/Select';
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

/** Ticket statuses of the support module (the shared status map predates them). */
export const TICKET_STATUS: Record<TicketStatus, { tone: Tone; label: string }> = {
  Open: { tone: 'info', label: 'Open' },
  AwaitingParticipant: { tone: 'warning', label: 'Awaiting your reply' },
  AwaitingStaff: { tone: 'brand', label: 'With support' },
  Resolved: { tone: 'success', label: 'Resolved' },
  Closed: { tone: 'neutral', label: 'Closed' },
};

export const ticketStatusOptions: SelectOption[] = Object.entries(TICKET_STATUS).map(([value, meta]) => ({
  value,
  label: meta.label,
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

/** Mirrors `Money.SupportedCurrencies` in the backend domain. */
export const SUPPORTED_CURRENCIES = [
  'USD',
  'EUR',
  'GBP',
  'AED',
  'SAR',
  'PKR',
  'INR',
  'CAD',
  'AUD',
  'JPY',
  'KWD',
  'BHD',
  'OMR',
  'QAR',
  'EGP',
  'TRY',
  'NGN',
  'ZAR',
  'BRL',
  'MXN',
];

/**
 * Onboarding/banner links come from the CMS as app paths. The seeded payout step points at `/app/payout-details`,
 * which lives under the profile area in this app.
 */
export function normalizeAppLink(url: string): string {
  if (url === '/app/payout-details') return '/app/profile/payout-details';
  return url;
}

export function isInternalLink(url: string): boolean {
  return url.startsWith('/') && !url.startsWith('//');
}
