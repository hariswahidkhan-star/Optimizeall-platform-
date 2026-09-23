import { AlertTriangle, CheckCircle2 } from 'lucide-react';
import type { ReactNode } from 'react';
import { Badge, Card, CardBody, CardHeader, Money } from '@/components/ui';
import type { BatchWarnings, PayoutExclusion, UserWarning } from '../api/types';
import { PersonCell } from '../components/common';
import { EXCLUSION_LABELS, exclusionLabel } from '../lib/format';

function WarningGroup({
  title,
  help,
  items,
  onOpenItem,
}: {
  title: string;
  help: ReactNode;
  items: UserWarning[];
  onOpenItem?: (itemId: string) => void;
}) {
  if (items.length === 0) return null;
  return (
    <section className="fin-warning-group" aria-label={title}>
      <h3 className="fin-warning-group__title">
        <AlertTriangle aria-hidden="true" />
        {title} <Badge tone="warning">{items.length}</Badge>
      </h3>
      <p className="text-muted text-small">{help}</p>
      <ul className="fin-warning-list">
        {items.map((w, i) => (
          <li key={`${w.user.id}-${w.itemId ?? i}`}>
            <PersonCell user={w.user} />
            <span className="fin-warning-list__detail">{w.detail}</span>
            {w.itemId && onOpenItem && (
              <button type="button" className="ui-link fin-linkbutton" onClick={() => onOpenItem(w.itemId!)}>
                View item<span className="visually-hidden"> of {w.user.displayName}</span>
              </button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

function Exclusions({ exclusions, currency }: { exclusions: PayoutExclusion[]; currency: string }) {
  if (exclusions.length === 0) return null;
  return (
    <section className="fin-warning-group" aria-label="Excluded participants">
      <h3 className="fin-warning-group__title">
        Excluded from this batch <Badge tone="neutral">{exclusions.length}</Badge>
      </h3>
      <p className="text-muted text-small">
        These participants have approved earnings but were not included. Amounts are carried over to a later
        batch.
      </p>
      <ul className="fin-warning-list">
        {exclusions.map((e) => (
          <li key={`${e.user.id}-${e.reason}`}>
            <PersonCell user={e.user} />
            <span className="fin-warning-list__detail">
              <Badge tone={e.reason === 'BelowMinimum' ? 'info' : 'warning'} size="sm">
                {exclusionLabel(e.reason)}
              </Badge>{' '}
              <span className="text-muted text-small">{EXCLUSION_LABELS[e.reason]?.help}</span>
            </span>
            <span className="fin-warning-list__amount">
              <Money amount={e.amount} currency={currency} />{' '}
              <span className="text-muted text-small">
                ({e.earningCount} {e.earningCount === 1 ? 'earning' : 'earnings'} carried over)
              </span>
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Everything finance should look at before finalizing. */
export function WarningsPanel({
  warnings,
  currency,
  onOpenItem,
}: {
  warnings: BatchWarnings;
  currency: string;
  onOpenItem: (itemId: string) => void;
}) {
  const count =
    warnings.missingPayoutDetails.length +
    warnings.openAppeals.length +
    warnings.openDisputes.length +
    warnings.highRiskSubmissions.length;
  return (
    <Card as="section" aria-labelledby="batch-warnings-title">
      <CardHeader
        titleId="batch-warnings-title"
        title="Warnings"
        description="Check these before finalizing. Hold items you are not comfortable paying."
        actions={
          count === 0 ? (
            <Badge tone="success" icon={<CheckCircle2 />}>
              No warnings
            </Badge>
          ) : (
            <Badge tone="warning">{count} to review</Badge>
          )
        }
      />
      <CardBody className="stack">
        <WarningGroup
          title="Missing payout details"
          help="Held automatically: the participant has no payout details on file."
          items={warnings.missingPayoutDetails}
          onOpenItem={onOpenItem}
        />
        <WarningGroup
          title="Open appeals"
          help="The participant has an appeal that is not resolved yet."
          items={warnings.openAppeals}
          onOpenItem={onOpenItem}
        />
        <WarningGroup
          title="Open disputes"
          help="Open dispute or payout support tickets."
          items={warnings.openDisputes}
          onOpenItem={onOpenItem}
        />
        <WarningGroup
          title="High-risk submissions"
          help={`Submissions with a risk score of ${warnings.highRiskThreshold} or more in this period or batch.`}
          items={warnings.highRiskSubmissions}
          onOpenItem={onOpenItem}
        />
        <Exclusions exclusions={warnings.exclusions} currency={currency} />
        {count === 0 && warnings.exclusions.length === 0 && (
          <p className="text-muted">Nothing needs attention in this batch.</p>
        )}
      </CardBody>
    </Card>
  );
}
