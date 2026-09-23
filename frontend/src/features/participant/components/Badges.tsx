import { CircleCheck, CircleSlash } from 'lucide-react';
import { Badge } from '@/components/ui/Badge';
import type { Reason, TicketStatus } from '../api/types';
import { TICKET_STATUS } from '../lib/labels';

export function TicketStatusBadge({ status }: { status: TicketStatus | string }) {
  const meta = TICKET_STATUS[status as TicketStatus] ?? { tone: 'neutral' as const, label: status };
  return (
    <Badge tone={meta.tone} dot>
      {meta.label}
    </Badge>
  );
}

/** "Eligible" or "Not eligible" with the first reason next to it (all reasons are server-computed). */
export function EligibilityBadge({
  isEligible,
  reasons,
  showReason = true,
}: {
  isEligible: boolean;
  reasons: Reason[];
  showReason?: boolean;
}) {
  if (isEligible) {
    return (
      <Badge tone="success" icon={<CircleCheck size={14} />}>
        Eligible
      </Badge>
    );
  }
  const first = reasons[0];
  return (
    <span className="pp-eligibility">
      <Badge tone="warning" icon={<CircleSlash size={14} />}>
        Not eligible
      </Badge>
      {showReason && first && <span className="pp-eligibility__reason">{first.message}</span>}
    </span>
  );
}
