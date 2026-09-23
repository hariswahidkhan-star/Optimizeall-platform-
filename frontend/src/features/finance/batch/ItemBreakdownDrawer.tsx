import type { ReactNode } from 'react';
import {
  Badge,
  DataTable,
  DateTime,
  KeyValueList,
  Money,
  Skeleton,
  StatusBadge,
  type DataTableColumn,
} from '@/components/ui';
import { humanize } from '@/lib/format/text';
import { useBatchItem } from '../api/hooks';
import type { ItemEarning, PaymentAttempt, PayoutItem } from '../api/types';
import { QueryError } from '../components/common';
import { FinanceDrawer } from '../components/FinanceDrawer';

const earningColumns: DataTableColumn<ItemEarning>[] = [
  {
    id: 'what',
    header: 'Earning',
    primary: true,
    cell: (e) => (
      <span className="fin-person">
        <span className="fin-person__name">{e.campaign?.title ?? e.description}</span>
        <span className="fin-person__email">
          {humanize(e.type)}
          {e.campaign ? ` · ${e.description}` : ''}
        </span>
      </span>
    ),
  },
  {
    id: 'original',
    header: 'Original',
    align: 'right',
    cell: (e) => <Money amount={e.originalAmount} currency={e.originalCurrency} />,
  },
  {
    id: 'rate',
    header: 'Rate',
    align: 'right',
    cell: (e) =>
      e.originalCurrency === e.settlementCurrency ? (
        <span className="text-muted">—</span>
      ) : (
        <span
          className="tabular"
          title={`1 ${e.originalCurrency} = ${e.exchangeRate} ${e.settlementCurrency}`}
        >
          {e.exchangeRate}
        </span>
      ),
  },
  {
    id: 'settlement',
    header: 'Settlement',
    align: 'right',
    cell: (e) => <Money amount={e.settlementAmount} currency={e.settlementCurrency} colored />,
  },
  { id: 'status', header: 'Status', cell: (e) => <StatusBadge kind="earning" status={e.status} size="sm" /> },
  { id: 'available', header: 'Available', cell: (e) => <DateTime value={e.availableAt} format="date" /> },
];

const ATTEMPT_TONE: Record<string, 'neutral' | 'info' | 'success' | 'danger' | 'warning'> = {
  Created: 'neutral',
  Submitted: 'info',
  Succeeded: 'success',
  Failed: 'danger',
  RequiresManualAction: 'warning',
};

function Attempt({ attempt }: { attempt: PaymentAttempt }) {
  return (
    <li className="fin-attempt">
      <div className="cluster">
        <Badge tone={ATTEMPT_TONE[attempt.status] ?? 'neutral'} dot>
          {humanize(attempt.status)}
        </Badge>
        <span className="text-small">Provider: {attempt.provider}</span>
      </div>
      {attempt.message && <p className="text-small">{attempt.message}</p>}
      <p className="text-muted text-small">
        {attempt.providerReference && <>Reference {attempt.providerReference} · </>}
        Created <DateTime value={attempt.createdAt} />
        {attempt.updatedAt && (
          <>
            {' '}
            · updated <DateTime value={attempt.updatedAt} />
          </>
        )}
      </p>
      <p className="text-muted text-small fin-mono">{attempt.idempotencyKey}</p>
    </li>
  );
}

/** Participant-level breakdown of one payout item: every included earning and the payment attempts. */
export function ItemBreakdownDrawer({
  batchId,
  itemId,
  onClose,
  actions,
}: {
  batchId: string;
  itemId: string | null;
  onClose: () => void;
  /** Action buttons for the loaded item (hold, record payment, ...). */
  actions?: (item: PayoutItem) => ReactNode;
}) {
  const query = useBatchItem(batchId, itemId);
  const detail = query.data;
  const item = detail?.item;

  return (
    <FinanceDrawer
      open={!!itemId}
      onClose={onClose}
      title={item ? item.user.displayName : 'Payout item'}
      description={detail ? `${detail.batchReference} · period ${detail.periodKey}` : undefined}
      footer={item && actions ? <div className="cluster">{actions(item)}</div> : undefined}
    >
      {query.isPending ? (
        <div className="stack">
          <Skeleton height={120} />
          <Skeleton height={200} />
        </div>
      ) : query.isError ? (
        <QueryError error={query.error} onRetry={() => query.refetch()} />
      ) : detail && item ? (
        <div className="stack">
          <KeyValueList
            items={[
              { label: 'Participant', value: `${item.user.displayName} · ${item.user.email}` },
              { label: 'Country', value: item.user.country || '—' },
              { label: 'Status', value: <StatusBadge kind="payoutItem" status={item.status} /> },
              { label: 'Amount', value: <Money amount={item.amount} currency={item.currency} /> },
              {
                label: 'Sum of earnings',
                value: <Money amount={detail.earningsTotal} currency={item.currency} />,
              },
              { label: 'Earnings', value: item.earningCount },
              { label: 'Destination', value: item.destinationHint ?? 'No payout details' },
              { label: 'Payment provider', value: item.paymentProvider },
              { label: 'Payment reference', value: item.paymentReference ?? '—' },
              { label: 'Paid at', value: <DateTime value={item.paidAt} /> },
              ...(item.holdReason ? [{ label: 'Hold reason', value: item.holdReason }] : []),
              ...(item.failureReason ? [{ label: 'Failure reason', value: item.failureReason }] : []),
            ]}
          />
          <section aria-labelledby="item-earnings-title" className="stack">
            <h3 id="item-earnings-title" className="fin-subheading">
              Included earnings
            </h3>
            <DataTable
              caption={`Earnings included for ${item.user.displayName}`}
              columns={earningColumns}
              rows={detail.earnings}
              getRowId={(e) => e.id}
              emptyState={
                <p className="text-muted">
                  No earnings are linked. Earnings of failed or cancelled items return to the participant’s
                  balance.
                </p>
              }
            />
          </section>
          <section aria-labelledby="item-attempts-title" className="stack">
            <h3 id="item-attempts-title" className="fin-subheading">
              Payment attempts
            </h3>
            {detail.paymentAttempts.length === 0 ? (
              <p className="text-muted text-small">
                No payment attempts yet (created when the batch is finalized).
              </p>
            ) : (
              <ul className="fin-plain-list stack">
                {detail.paymentAttempts.map((a) => (
                  <Attempt key={a.id} attempt={a} />
                ))}
              </ul>
            )}
          </section>
        </div>
      ) : null}
    </FinanceDrawer>
  );
}
