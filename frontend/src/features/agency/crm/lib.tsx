import { Badge, type Tone } from '@/components/ui';
import type { ActivityType, ConsentStatus, DealSource, DealStatus, LifecycleStage, ProposalStatus } from './api/types';

export const LIFECYCLE_LABELS: Record<LifecycleStage, string> = {
  Subscriber: 'Subscriber',
  Lead: 'Lead',
  MarketingQualifiedLead: 'MQL',
  SalesQualifiedLead: 'SQL',
  Opportunity: 'Opportunity',
  Customer: 'Customer',
  Evangelist: 'Evangelist',
};

export const LIFECYCLE_OPTIONS = (Object.keys(LIFECYCLE_LABELS) as LifecycleStage[]).map((value) => ({
  value,
  label: LIFECYCLE_LABELS[value],
}));

export const CONSENT_OPTIONS: { value: ConsentStatus; label: string }[] = [
  { value: 'Unknown', label: 'Unknown' },
  { value: 'Subscribed', label: 'Subscribed to marketing' },
  { value: 'Unsubscribed', label: 'Unsubscribed' },
  { value: 'NotGiven', label: 'No marketing consent' },
];

export const SOURCE_LABELS: Record<DealSource, string> = {
  WebsiteInquiry: 'Website inquiry',
  Form: 'Form',
  Referral: 'Referral',
  Outbound: 'Outbound',
  Event: 'Event',
  Other: 'Other',
};

export const SOURCE_OPTIONS = (Object.keys(SOURCE_LABELS) as DealSource[]).map((value) => ({ value, label: SOURCE_LABELS[value] }));

export const ACTIVITY_OPTIONS: { value: ActivityType; label: string }[] = [
  { value: 'Note', label: 'Note' },
  { value: 'Call', label: 'Call' },
  { value: 'Meeting', label: 'Meeting' },
  { value: 'Email', label: 'Email (logged)' },
  { value: 'Task', label: 'Task' },
];

const DEAL_TONES: Record<DealStatus, Tone> = { Open: 'info', Won: 'success', Lost: 'danger' };

export function DealStatusBadge({ status }: { status: DealStatus }) {
  return <Badge tone={DEAL_TONES[status]}>{status}</Badge>;
}

const PROPOSAL_TONES: Record<ProposalStatus, Tone> = {
  Draft: 'neutral',
  Sent: 'info',
  Viewed: 'brand',
  Accepted: 'success',
  Declined: 'danger',
  Expired: 'warning',
  Withdrawn: 'neutral',
};

export function ProposalStatusBadge({ status }: { status: ProposalStatus }) {
  return <Badge tone={PROPOSAL_TONES[status]}>{status}</Badge>;
}

export function LifecycleBadge({ stage }: { stage: LifecycleStage }) {
  const tone: Tone = stage === 'Customer' || stage === 'Evangelist' ? 'success' : stage === 'Opportunity' || stage === 'SalesQualifiedLead' ? 'brand' : 'neutral';
  return <Badge tone={tone}>{LIFECYCLE_LABELS[stage]}</Badge>;
}

/** datetime-local value (browser zone) → UTC ISO string, or null. */
export function localToIso(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** UTC ISO → datetime-local value in the browser zone. */
export function isoToLocal(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function splitTags(value: string): string[] {
  return value
    .split(/[,;]/)
    .map((t) => t.trim().toLowerCase())
    .filter(Boolean);
}
