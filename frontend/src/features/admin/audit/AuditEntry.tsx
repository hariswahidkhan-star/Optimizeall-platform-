import { ChevronDown, ChevronRight } from 'lucide-react';
import { useId, useState } from 'react';
import { Badge } from '@/components/ui/Badge';
import { DateTime } from '@/components/ui/DateTime';
import type { AuditLogEntry } from '../api/types';
import { JsonDiff } from '../shared/JsonDiff';

/** One audit entry: summary row with a disclosure button revealing the reason, metadata and before/after diff. */
export function AuditEntry({ entry, headingLevel = 3 }: { entry: AuditLogEntry; headingLevel?: 3 | 4 }) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const Heading = `h${headingLevel}` as const;
  const actedAs =
    entry.actorDisplayName ?? entry.actorEmail ?? (entry.actorType === 'System' ? 'System' : 'Unknown');
  // Actions taken while impersonating read "Admin X as User Y".
  const actor = entry.impersonatorUserId
    ? `${entry.impersonatorDisplayName ?? 'Unknown staff member'} as ${actedAs}`
    : actedAs;
  const hasData = entry.before != null || entry.after != null;

  return (
    <li className="admin-audit">
      <Heading className="admin-audit__heading">
        <button
          type="button"
          className="admin-audit__toggle"
          aria-expanded={open}
          aria-controls={panelId}
          onClick={() => setOpen((v) => !v)}
        >
          {open ? <ChevronDown aria-hidden="true" /> : <ChevronRight aria-hidden="true" />}
          <code className="admin-audit__action">{entry.action}</code>
          <span className="admin-audit__entity">
            {entry.entityType}
            {entry.entityId ? ` · ${entry.entityId}` : ''}
          </span>
        </button>
      </Heading>
      <p className="admin-audit__meta text-small text-muted">
        <DateTime value={entry.createdAt} /> · {actor}{' '}
        <Badge size="sm" tone="neutral">
          {entry.actorType}
        </Badge>
      </p>
      {entry.reason && (
        <p className="admin-audit__reason text-small">
          <strong>Reason:</strong> {entry.reason}
        </p>
      )}
      <div id={panelId} hidden={!open} className="admin-audit__panel">
        {open && (
          <>
            <dl className="admin-audit__facts text-small">
              <div>
                <dt>Entry</dt>
                <dd className="tabular">#{entry.id}</dd>
              </div>
              {entry.actorEmail && (
                <div>
                  <dt>Actor email</dt>
                  <dd>{entry.actorEmail}</dd>
                </div>
              )}
              {entry.ipAddress && (
                <div>
                  <dt>IP address</dt>
                  <dd>{entry.ipAddress}</dd>
                </div>
              )}
              {entry.correlationId && (
                <div>
                  <dt>Correlation id</dt>
                  <dd>
                    <code>{entry.correlationId}</code>
                  </dd>
                </div>
              )}
            </dl>
            {hasData ? (
              <JsonDiff before={entry.before} after={entry.after} />
            ) : (
              <p className="text-small text-muted">No before/after data was recorded for this action.</p>
            )}
          </>
        )}
      </div>
    </li>
  );
}
