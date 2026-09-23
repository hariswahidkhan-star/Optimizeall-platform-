import { AlertTriangle, CheckCircle2, Info, XCircle } from 'lucide-react';
import { Badge } from '@/components/ui/Badge';
import type { Tone } from '@/components/ui/tones';
import { formatPercent } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import type {
  ApprovalStatus,
  CampaignStatus,
  ChecklistItem,
  ConsentStatus,
  EngagementTier,
  SubscriberStatus,
} from '../api/types';

const campaignTones: Record<CampaignStatus, Tone> = {
  Draft: 'neutral',
  Scheduled: 'info',
  Sending: 'brand',
  Paused: 'warning',
  Sent: 'success',
  Cancelled: 'danger',
};

export function CampaignStatusBadge({ status }: { status: CampaignStatus }) {
  return (
    <Badge tone={campaignTones[status] ?? 'neutral'} dot>
      {status}
    </Badge>
  );
}

export function ApprovalBadge({ status }: { status: ApprovalStatus }) {
  if (status === 'NotRequired') return null;
  const tone: Tone = status === 'Approved' ? 'success' : status === 'Rejected' ? 'danger' : 'warning';
  const label = status === 'Pending' ? 'Awaiting client approval' : status === 'Approved' ? 'Client approved' : 'Changes requested';
  return (
    <Badge tone={tone} size="sm">
      {label}
    </Badge>
  );
}

const subscriberTones: Record<SubscriberStatus, Tone> = {
  Subscribed: 'success',
  Unsubscribed: 'neutral',
  Bounced: 'danger',
  Complained: 'danger',
  Cleaned: 'warning',
};

export function SubscriberStatusBadge({ status }: { status: SubscriberStatus }) {
  return (
    <Badge tone={subscriberTones[status] ?? 'neutral'} size="sm">
      {status}
    </Badge>
  );
}

export function ConsentBadge({ status, channel = 'Email' }: { status: ConsentStatus; channel?: string }) {
  const tone: Tone = status === 'Granted' ? 'success' : status === 'Withdrawn' ? 'danger' : status === 'Pending' ? 'warning' : 'neutral';
  return (
    <Badge tone={tone} size="sm" title={`${channel} consent: ${status}`}>
      {channel === 'Email' ? status : `${channel}: ${status}`}
    </Badge>
  );
}

export function TierBadge({ tier }: { tier: EngagementTier }) {
  const tone: Tone = tier === 'Active' ? 'success' : tier === 'Warm' ? 'info' : tier === 'New' ? 'brand' : 'neutral';
  return (
    <Badge tone={tone} size="sm">
      {tier}
    </Badge>
  );
}

const checkIcons = {
  Pass: <CheckCircle2 aria-hidden="true" />,
  Warning: <AlertTriangle aria-hidden="true" />,
  Fail: <XCircle aria-hidden="true" />,
  Info: <Info aria-hidden="true" />,
};

const checkLabels = { Pass: 'Passed', Warning: 'Warning', Fail: 'Blocking', Info: 'Note' };

/** The pre-send checklist (blocking items stop the send; warnings are advisory). */
export function ChecklistView({ items }: { items: ChecklistItem[] }) {
  return (
    <ul className="email-checklist" aria-label="Pre-send checklist">
      {items.map((item, index) => (
        <li key={`${item.id}-${index}`} className={`email-checklist__item email-checklist__item--${item.status.toLowerCase()}`}>
          <span className="email-checklist__icon">{checkIcons[item.status]}</span>
          <span className="email-checklist__body">
            <span className="email-checklist__label">
              {item.label} <span className="visually-hidden">({checkLabels[item.status]})</span>
            </span>
            {item.detail && <span className="email-checklist__detail">{item.detail}</span>}
          </span>
        </li>
      ))}
    </ul>
  );
}

export function rate(value: number): string {
  return formatPercent(value, { maximumFractionDigits: 1 });
}

export function label(value: string): string {
  return humanize(value);
}

/** Converts an ISO timestamp to the value of a datetime-local input (local time). */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
