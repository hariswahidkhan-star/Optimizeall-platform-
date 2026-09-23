import { humanize } from '@/lib/format/text';
import type { Tone } from './tones';

/** Domain status families rendered by StatusBadge. Values mirror backend enums (serialized as strings). */
export type StatusKind =
  'submission' | 'earning' | 'payout' | 'payoutItem' | 'campaign' | 'socialVerification' | 'ticket';

interface StatusMeta {
  tone: Tone;
  label: string;
}

const MAP: Record<StatusKind, Record<string, StatusMeta>> = {
  submission: {
    Pending: { tone: 'neutral', label: 'Pending' },
    UnderReview: { tone: 'info', label: 'Under review' },
    Approved: { tone: 'success', label: 'Approved' },
    NeedsCorrection: { tone: 'warning', label: 'Needs correction' },
    Rejected: { tone: 'danger', label: 'Rejected' },
    Reversed: { tone: 'danger', label: 'Reversed' },
  },
  earning: {
    PendingApproval: { tone: 'neutral', label: 'Pending approval' },
    Approved: { tone: 'success', label: 'Approved' },
    Scheduled: { tone: 'info', label: 'Scheduled' },
    Paid: { tone: 'brand', label: 'Paid' },
    Reversed: { tone: 'danger', label: 'Reversed' },
    Declined: { tone: 'danger', label: 'Declined' },
  },
  payout: {
    Draft: { tone: 'neutral', label: 'Draft' },
    Finalized: { tone: 'info', label: 'Finalized' },
    Completed: { tone: 'success', label: 'Completed' },
    Cancelled: { tone: 'danger', label: 'Cancelled' },
  },
  payoutItem: {
    Pending: { tone: 'neutral', label: 'Pending' },
    Held: { tone: 'warning', label: 'On hold' },
    AwaitingPayment: { tone: 'info', label: 'Awaiting payment' },
    Paid: { tone: 'success', label: 'Paid' },
    Failed: { tone: 'danger', label: 'Failed' },
    Cancelled: { tone: 'neutral', label: 'Cancelled' },
  },
  campaign: {
    Draft: { tone: 'neutral', label: 'Draft' },
    Scheduled: { tone: 'info', label: 'Scheduled' },
    Active: { tone: 'success', label: 'Active' },
    Paused: { tone: 'warning', label: 'Paused' },
    Ended: { tone: 'neutral', label: 'Ended' },
    Archived: { tone: 'neutral', label: 'Archived' },
  },
  socialVerification: {
    Unverified: { tone: 'neutral', label: 'Unverified' },
    PendingReview: { tone: 'info', label: 'Pending review' },
    Verified: { tone: 'success', label: 'Verified' },
    Rejected: { tone: 'danger', label: 'Rejected' },
  },
  ticket: {
    Open: { tone: 'info', label: 'Open' },
    InProgress: { tone: 'brand', label: 'In progress' },
    AwaitingCustomer: { tone: 'warning', label: 'Awaiting your reply' },
    AwaitingUser: { tone: 'warning', label: 'Awaiting your reply' },
    Resolved: { tone: 'success', label: 'Resolved' },
    Closed: { tone: 'neutral', label: 'Closed' },
  },
};

/** Tone + human label for a domain status. Unknown values fall back to a neutral, humanized label. */
export function statusMeta(kind: StatusKind, status: string): StatusMeta {
  return MAP[kind][status] ?? { tone: 'neutral', label: humanize(status) };
}

/** All known statuses for a kind (for filter dropdowns). */
export function statusOptions(kind: StatusKind): { value: string; label: string }[] {
  const seen = new Set<string>();
  return Object.entries(MAP[kind])
    .filter(([, meta]) => (seen.has(meta.label) ? false : (seen.add(meta.label), true)))
    .map(([value, meta]) => ({ value, label: meta.label }));
}
