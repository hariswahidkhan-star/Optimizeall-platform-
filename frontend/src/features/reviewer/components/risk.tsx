import { ShieldAlert } from 'lucide-react';
import { Badge, type Tone } from '@/components/ui';
import { humanize } from '@/lib/format/text';
import type { FlagType } from '../api/types';

const FLAG_LABELS: Record<string, string> = {
  DuplicateScreenshot: 'Duplicate screenshot',
  RepeatedContent: 'Repeated content',
  OutsideCampaignWindow: 'Outside campaign window',
  AfterSubmissionDeadline: 'After deadline',
  AccountBelowMinimumAge: 'Account too new',
  AccountNotVerified: 'Account not verified',
  PlatformMismatch: 'Platform mismatch',
  UrlHostMismatch: 'URL host mismatch',
  HighSubmissionVelocity: 'High submission velocity',
  NewParticipant: 'New participant',
  SharedDeviceOrIp: 'Shared device or IP',
};

/** Flags that point at copied or fabricated evidence get the strongest colour. */
const SEVERE_FLAGS = new Set([
  'DuplicateScreenshot',
  'RepeatedContent',
  'SharedDeviceOrIp',
  'UrlHostMismatch',
]);

export function flagLabel(type: FlagType): string {
  return FLAG_LABELS[type] ?? humanize(type);
}

export function flagTone(type: FlagType): Tone {
  return SEVERE_FLAGS.has(type) ? 'danger' : 'warning';
}

export type RiskLevel = 'low' | 'medium' | 'high';

/** Severity bands of the additive risk score (flag weights: duplicate screenshot 40, window 30, content 20, …). */
export function riskLevel(score: number): RiskLevel {
  if (score >= 40) return 'high';
  if (score >= 15) return 'medium';
  return 'low';
}

const LEVEL_META: Record<RiskLevel, { tone: Tone; label: string }> = {
  low: { tone: 'success', label: 'Low' },
  medium: { tone: 'warning', label: 'Medium' },
  high: { tone: 'danger', label: 'High' },
};

export function RiskBadge({ score, size }: { score: number; size?: 'sm' | 'md' }) {
  const meta = LEVEL_META[riskLevel(score)];
  return (
    <Badge tone={meta.tone} size={size} icon={meta.tone === 'danger' ? <ShieldAlert /> : undefined}>
      <span className="tabular">{score}</span>
      <span>· {meta.label} risk</span>
    </Badge>
  );
}

export function FlagChips({ flags, max }: { flags: FlagType[]; max?: number }) {
  if (flags.length === 0) return <span className="text-muted text-small">No flags</span>;
  const shown = max ? flags.slice(0, max) : flags;
  const hidden = flags.length - shown.length;
  return (
    <ul className="rv-chips" aria-label="Risk flags">
      {shown.map((flag) => (
        <li key={flag}>
          <Badge size="sm" tone={flagTone(flag)}>
            {flagLabel(flag)}
          </Badge>
        </li>
      ))}
      {hidden > 0 && (
        <li>
          <Badge size="sm" tone="neutral" title={flags.slice(shown.length).map(flagLabel).join(', ')}>
            +{hidden} more
          </Badge>
        </li>
      )}
    </ul>
  );
}
