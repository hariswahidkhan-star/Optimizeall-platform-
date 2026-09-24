import { Badge } from '@/components/ui';
import type { RateCardStatus, RateCardVersionStatus } from '../api/types';
import { levelLabel, levelTone } from '../labels';

export function CardStatusBadge({ status }: { status: RateCardStatus }) {
  const tone = status === 'Active' ? 'success' : status === 'Draft' ? 'neutral' : 'warning';
  return (
    <Badge size="sm" tone={tone} dot>
      {status}
    </Badge>
  );
}

export function VersionStatusBadge({ status }: { status: RateCardVersionStatus }) {
  if (status === 'Approved') return null;
  return (
    <Badge size="sm" tone={status === 'PendingApproval' ? 'warning' : 'danger'}>
      {status === 'PendingApproval' ? 'Awaiting approval' : 'Rejected'}
    </Badge>
  );
}

export function LevelBadge({ level }: { level: string }) {
  return (
    <Badge size="sm" tone={levelTone(level)}>
      {levelLabel(level)}
    </Badge>
  );
}
