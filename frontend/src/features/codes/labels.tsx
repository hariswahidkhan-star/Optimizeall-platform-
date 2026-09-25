import { Badge } from '@/components/ui';
import type { Tone } from '@/components/ui/tones';
import type {
  CodeProgramStatus,
  CodeSaleSource,
  CodeSaleStatus,
  CodeSaleVerification,
  DiscountCodeStatus,
} from './api/types';

const SALE: Record<CodeSaleStatus, { tone: Tone; label: string }> = {
  Pending: { tone: 'neutral', label: 'Pending review' },
  NeedsInfo: { tone: 'warning', label: 'Needs info' },
  Approved: { tone: 'success', label: 'Approved' },
  Rejected: { tone: 'danger', label: 'Rejected' },
  Withdrawn: { tone: 'neutral', label: 'Withdrawn' },
  Cancelled: { tone: 'neutral', label: 'Cancelled' },
  Refunded: { tone: 'danger', label: 'Refunded' },
};

const CODE: Record<DiscountCodeStatus, { tone: Tone; label: string }> = {
  Available: { tone: 'info', label: 'Available' },
  Assigned: { tone: 'success', label: 'Assigned' },
  Paused: { tone: 'warning', label: 'Paused' },
  Expired: { tone: 'neutral', label: 'Expired' },
  Retired: { tone: 'danger', label: 'Retired' },
};

const PROGRAM: Record<CodeProgramStatus, { tone: Tone; label: string }> = {
  Draft: { tone: 'neutral', label: 'Draft' },
  Active: { tone: 'success', label: 'Active' },
  Paused: { tone: 'warning', label: 'Paused' },
  Archived: { tone: 'neutral', label: 'Archived' },
};

const VERIFICATION: Record<CodeSaleVerification, { tone: Tone; label: string }> = {
  Unverified: { tone: 'neutral', label: 'Not in a brand report yet' },
  Matched: { tone: 'success', label: 'Matched by brand' },
  Mismatch: { tone: 'warning', label: 'Differs from brand report' },
  ReportedByBrand: { tone: 'info', label: 'Reported by brand' },
};

export const SOURCE_LABEL: Record<CodeSaleSource, string> = {
  Participant: 'Reported by participant',
  Admin: 'Entered by staff',
  Import: 'Brand report',
};

export const saleStatusLabel = (s: CodeSaleStatus) => SALE[s]?.label ?? s;

export function SaleStatusBadge({ status, size = 'sm' }: { status: CodeSaleStatus; size?: 'sm' | 'md' }) {
  const meta = SALE[status] ?? { tone: 'neutral' as Tone, label: status };
  return (
    <Badge tone={meta.tone} size={size} dot>
      {meta.label}
    </Badge>
  );
}

export function CodeStatusBadge({ status }: { status: DiscountCodeStatus }) {
  const meta = CODE[status] ?? { tone: 'neutral' as Tone, label: status };
  return (
    <Badge tone={meta.tone} size="sm" dot>
      {meta.label}
    </Badge>
  );
}

export function ProgramStatusBadge({ status }: { status: CodeProgramStatus }) {
  const meta = PROGRAM[status] ?? { tone: 'neutral' as Tone, label: status };
  return (
    <Badge tone={meta.tone} size="sm" dot>
      {meta.label}
    </Badge>
  );
}

export function VerificationBadge({ verification }: { verification: CodeSaleVerification }) {
  if (verification === 'Unverified') return null;
  const meta = VERIFICATION[verification];
  return (
    <Badge tone={meta.tone} size="sm">
      {meta.label}
    </Badge>
  );
}

export const SALE_STATUS_OPTIONS = (Object.keys(SALE) as CodeSaleStatus[]).map((s) => ({
  value: s,
  label: SALE[s].label,
}));
export const CODE_STATUS_OPTIONS = (Object.keys(CODE) as DiscountCodeStatus[]).map((s) => ({
  value: s,
  label: CODE[s].label,
}));
export const VERIFICATION_OPTIONS = (Object.keys(VERIFICATION) as CodeSaleVerification[]).map((v) => ({
  value: v,
  label: VERIFICATION[v].label,
}));

/** Participant-facing wording for timeline actions. */
export function eventLabel(action: string): string {
  switch (action) {
    case 'submitted':
      return 'Reported';
    case 'entered':
      return 'Entered by the team';
    case 'imported':
      return 'Found in the brand’s sales report';
    case 'edited':
      return 'Edited';
    case 'resubmitted':
      return 'Sent back for review';
    case 'approved':
      return 'Approved';
    case 'rejected':
      return 'Rejected';
    case 'info_requested':
      return 'More information requested';
    case 'withdrawn':
      return 'Withdrawn';
    case 'cancelled':
      return 'Cancelled (order refunded)';
    case 'refunded':
    case 'refunded_by_report':
      return 'Refunded — commission reversed';
    case 'verified':
      return 'Matched by the brand’s report';
    case 'mismatch_flagged':
      return 'Differs from the brand’s report';
    default:
      return action;
  }
}

/** "Order value" wording used in forms and tables. */
export const NET_HELP = 'What the customer paid, after the discount (without shipping or tax).';
