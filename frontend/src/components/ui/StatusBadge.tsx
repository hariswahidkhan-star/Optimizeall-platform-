import { Badge } from './Badge';
import { statusMeta, type StatusKind } from './statusMap';

export interface StatusBadgeProps {
  kind: StatusKind;
  status: string;
  size?: 'sm' | 'md';
  className?: string;
  /** Audience-specific wording that replaces the shared label (the tone stays the shared one). */
  label?: string;
}

/** Maps a domain status (submission, earning, payout, campaign, ...) to a consistent tone and label. */
export function StatusBadge({ kind, status, size, className, label }: StatusBadgeProps) {
  const meta = statusMeta(kind, status);
  return (
    <Badge tone={meta.tone} size={size} dot className={className}>
      {label ?? meta.label}
    </Badge>
  );
}
