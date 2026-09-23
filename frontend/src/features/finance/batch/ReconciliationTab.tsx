import { CheckCircle2, XCircle } from 'lucide-react';
import {
  Alert,
  Badge,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  Money,
  Skeleton,
  Stat,
  StatusBadge,
  type DataTableColumn,
} from '@/components/ui';
import { humanize } from '@/lib/format/text';
import { useReconciliation } from '../api/hooks';
import type { Reconciliation, ReconciliationItem } from '../api/types';
import { DownloadButton, PersonCell, QueryError } from '../components/common';

function itemColumns(currency: string): DataTableColumn<ReconciliationItem>[] {
  return [
    { id: 'who', header: 'Participant', primary: true, cell: (i) => <PersonCell user={i.user} /> },
    { id: 'status', header: 'Item status', cell: (i) => <StatusBadge kind="payoutItem" status={i.status} /> },
    {
      id: 'amount',
      header: 'Item amount',
      align: 'right',
      cell: (i) => <Money amount={i.amount} currency={currency} />,
    },
    {
      id: 'earnings',
      header: 'Sum of earnings',
      align: 'right',
      cell: (i) => <Money amount={i.earningsTotal} currency={currency} />,
    },
    {
      id: 'count',
      header: 'Earnings (linked)',
      align: 'right',
      cell: (i) => (
        <span className="tabular">
          {i.earningCount} ({i.linkedEarningCount})
        </span>
      ),
    },
    { id: 'ref', header: 'Payment reference', cell: (i) => i.paymentReference ?? '—', hideOnMobile: true },
    { id: 'paid', header: 'Paid at', cell: (i) => <DateTime value={i.paidAt} />, hideOnMobile: true },
    {
      id: 'ok',
      header: 'Check',
      cell: (i) =>
        i.ok ? (
          <Badge tone="success" icon={<CheckCircle2 />}>
            OK
          </Badge>
        ) : (
          <Badge tone="danger" icon={<XCircle />}>
            Discrepancy
          </Badge>
        ),
    },
  ];
}

/** Server-computed reconciliation of one batch (never recomputed in the browser). */
export function ReconciliationView({ data }: { data: Reconciliation }) {
  const c = data.currency;
  const errors = data.discrepancies.filter((d) => d.severity === 'error');
  const warnings = data.discrepancies.filter((d) => d.severity !== 'error');
  return (
    <div className="stack">
      {data.isBalanced ? (
        <Alert tone="success" title="Balanced" icon={<CheckCircle2 />}>
          No error-level discrepancies.{' '}
          {warnings.length > 0
            ? `${warnings.length} ${warnings.length === 1 ? 'warning needs' : 'warnings need'} a look.`
            : 'Compare the recorded paid total with your bank statement.'}
        </Alert>
      ) : (
        <Alert tone="danger" title="Not balanced" icon={<XCircle />}>
          {errors.length} {errors.length === 1 ? 'error' : 'errors'} found. Investigate before archiving this
          batch.
        </Alert>
      )}
      <div className="fin-stats">
        <Stat
          label="Expected"
          value={<Money amount={data.expected} currency={c} />}
          hint="Items not held or cancelled"
        />
        <Stat
          label="Recorded paid"
          value={<Money amount={data.recordedPaid} currency={c} />}
          hint={`${data.paidCount} ${data.paidCount === 1 ? 'item' : 'items'}`}
        />
        <Stat
          label="Awaiting payment"
          value={<Money amount={data.awaiting} currency={c} />}
          hint={`${data.awaitingCount} ${data.awaitingCount === 1 ? 'item' : 'items'}`}
        />
        <Stat label="Failed" value={<Money amount={data.failed} currency={c} />} />
        <Stat label="Held" value={<Money amount={data.held} currency={c} />} />
        <Stat label="Cancelled" value={<Money amount={data.cancelled} currency={c} />} />
      </div>
      <section aria-labelledby="recon-discrepancies" className="stack">
        <h3 id="recon-discrepancies" className="fin-subheading">
          Discrepancies
        </h3>
        {data.discrepancies.length === 0 ? (
          <p className="text-muted">None.</p>
        ) : (
          <ul className="fin-discrepancies">
            {data.discrepancies.map((d, i) => (
              <li key={`${d.type}-${d.itemId ?? ''}-${d.earningId ?? ''}-${i}`} data-severity={d.severity}>
                <Badge tone={d.severity === 'error' ? 'danger' : 'warning'} dot>
                  {d.severity === 'error' ? 'Error' : 'Warning'}
                </Badge>
                <span className="fin-discrepancies__type">{humanize(d.type)}</span>
                <span className="fin-discrepancies__message">{d.message}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
      <DataTable
        caption="Per-item checks"
        showCaption
        columns={itemColumns(c)}
        rows={data.items}
        getRowId={(i) => i.itemId}
      />
    </div>
  );
}

export function ReconciliationTab({ batchId, reference }: { batchId: string; reference: string }) {
  const query = useReconciliation(batchId);
  return (
    <Card as="section" aria-labelledby="recon-title">
      <CardHeader
        titleId="recon-title"
        title="Reconciliation"
        description="Expected vs recorded payments, checked against every linked earning."
        actions={
          <DownloadButton
            path={`/finance/payout-batches/${batchId}/reconciliation.csv`}
            fileName={`reconciliation-${reference}.csv`}
          >
            Export CSV
          </DownloadButton>
        }
      />
      <CardBody>
        {query.isPending ? (
          <Skeleton height={240} />
        ) : query.isError ? (
          <QueryError error={query.error} onRetry={() => query.refetch()} />
        ) : (
          <ReconciliationView data={query.data} />
        )}
      </CardBody>
    </Card>
  );
}
