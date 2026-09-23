import { Badge } from '@/components/ui/Badge';
import type { Tone } from '@/components/ui/tones';
import { humanize } from '@/lib/format/text';

type Meta = Record<string, { tone: Tone; label?: string }>;

const USER_STATUS: Meta = {
  Active: { tone: 'success' },
  Suspended: { tone: 'danger' },
  Deactivated: { tone: 'neutral' },
};

const PRIORITY: Meta = {
  Low: { tone: 'neutral' },
  Normal: { tone: 'info' },
  High: { tone: 'warning' },
  Urgent: { tone: 'danger' },
};

const DELIVERY: Meta = {
  Pending: { tone: 'neutral' },
  Sending: { tone: 'info' },
  Sent: { tone: 'success' },
  Failed: { tone: 'danger' },
  Skipped: { tone: 'warning' },
};

const JOB_RUN: Meta = {
  Running: { tone: 'info' },
  Succeeded: { tone: 'success' },
  Failed: { tone: 'danger' },
};

const SEVERITY: Meta = {
  Info: { tone: 'info' },
  Success: { tone: 'success' },
  Warning: { tone: 'warning' },
  Critical: { tone: 'danger' },
};

/** Ticket statuses use the shared `StatusBadge kind="ticket"` (components/ui/statusMap). */
export type BadgeKind = 'user' | 'priority' | 'delivery' | 'jobRun' | 'severity';

const MAPS: Record<BadgeKind, Meta> = {
  user: USER_STATUS,
  priority: PRIORITY,
  delivery: DELIVERY,
  jobRun: JOB_RUN,
  severity: SEVERITY,
};

export function badgeLabel(kind: BadgeKind, value: string): string {
  return MAPS[kind][value]?.label ?? humanize(value);
}

/** Status badge for admin-only enums (unknown values fall back to a neutral humanized badge). */
export function AdminBadge({ kind, value }: { kind: BadgeKind; value: string }) {
  const meta = MAPS[kind][value];
  return (
    <Badge tone={meta?.tone ?? 'neutral'} dot>
      {badgeLabel(kind, value)}
    </Badge>
  );
}

export function roleLabel(role: string): string {
  return humanize(role);
}
