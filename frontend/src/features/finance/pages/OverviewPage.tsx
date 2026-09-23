import { useQueries } from '@tanstack/react-query';
import { CalendarClock, ClipboardCheck, PauseCircle, Scale } from 'lucide-react';
import { Link } from 'react-router-dom';
import {
  Badge,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  KeyValueList,
  Money,
  PageHeader,
  Skeleton,
  Stat,
  StatusBadge,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { useAuth } from '@/lib/auth/useAuth';
import { browserTimeZone, greetingFor } from '@/lib/format/dates';
import { firstName } from '@/lib/format/text';
import { financeKeys, useBatches, useHolds, usePendingEarnings, useSchedule } from '../api/hooks';
import type { PayoutBatchSummary, Reconciliation } from '../api/types';
import { Muted, QueryError } from '../components/common';
import { dateOnlyToDisplay, daysUntil } from '../lib/format';
import { useCan } from '../lib/useCan';

const latestColumns: DataTableColumn<PayoutBatchSummary>[] = [
  {
    id: 'ref',
    header: 'Batch',
    primary: true,
    cell: (b) => (
      <Link className="ui-link fin-strong" to={`/finance/batches/${b.id}`}>
        {b.reference}
      </Link>
    ),
  },
  { id: 'status', header: 'Status', cell: (b) => <StatusBadge kind="payout" status={b.status} /> },
  { id: 'payment', header: 'Payment date', cell: (b) => dateOnlyToDisplay(b.paymentDate) },
  {
    id: 'total',
    header: 'Total',
    align: 'right',
    cell: (b) => <Money amount={b.totalAmount} currency={b.currency} />,
  },
  {
    id: 'paid',
    header: 'Paid',
    align: 'right',
    cell: (b) => (
      <span className="fin-stack-right">
        <Money amount={b.paidAmount} currency={b.currency} />
        <Muted>
          {b.paidCount} of {b.itemCount}
        </Muted>
      </span>
    ),
  },
];

/** Reconciliation of the open (Finalized) and most recent completed batches — amounts come from the server. */
function OpenMoney({ batches }: { batches: PayoutBatchSummary[] }) {
  const results = useQueries({
    queries: batches.map((b) => ({
      queryKey: financeKeys.reconciliation(b.id),
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        api.get<Reconciliation>(`/finance/payout-batches/${b.id}/reconciliation`, { signal }),
    })),
  });
  if (batches.length === 0) {
    return <p className="text-muted">No finalized or recently completed batches.</p>;
  }
  return (
    <ul className="fin-open-money">
      {batches.map((b, i) => {
        const r = results[i];
        return (
          <li key={b.id}>
            <div className="fin-open-money__head">
              <Link className="ui-link fin-strong" to={`/finance/batches/${b.id}/reconciliation`}>
                {b.reference}
              </Link>
              <StatusBadge kind="payout" status={b.status} />
              {r?.data &&
                (r.data.isBalanced ? (
                  <Badge tone="success">Balanced</Badge>
                ) : (
                  <Badge tone="danger">Not balanced</Badge>
                ))}
            </div>
            {r?.isPending ? (
              <Skeleton height={16} width="60%" />
            ) : r?.data ? (
              <p className="text-small">
                Awaiting payment <Money amount={r.data.awaiting} currency={r.data.currency} /> (
                {r.data.awaitingCount} {r.data.awaitingCount === 1 ? 'item' : 'items'}) · recorded paid{' '}
                <Money amount={r.data.recordedPaid} currency={r.data.currency} /> of{' '}
                <Money amount={r.data.expected} currency={r.data.currency} />
              </p>
            ) : (
              <Muted>Reconciliation unavailable.</Muted>
            )}
          </li>
        );
      })}
    </ul>
  );
}

export function OverviewPage() {
  const { user } = useAuth();
  const can = useCan();
  const schedule = useSchedule(can.viewPayouts);
  const latest = useBatches({ page: 1, pageSize: 5 }, can.viewPayouts);
  const finalized = useBatches({ status: 'Finalized', page: 1, pageSize: 5 }, can.viewPayouts);
  const completed = useBatches({ status: 'Completed', page: 1, pageSize: 2 }, can.viewPayouts);
  const pending = usePendingEarnings({ page: 1, pageSize: 1 }, can.approveBonus);
  const holds = useHolds({ active: true, page: 1, pageSize: 1 }, can.hold);
  const userZone = user?.timeZone ?? browserTimeZone();

  const openBatches = [...(finalized.data?.items ?? []), ...(completed.data?.items ?? [])].filter(
    (b, i, all) => all.findIndex((x) => x.id === b.id) === i,
  );
  const s = schedule.data;
  const period = s?.currentPeriod;

  return (
    <>
      <PageHeader
        eyebrow="Finance"
        title={`${greetingFor(new Date(), user?.timeZone)}${user ? `, ${firstName(user.displayName)}` : ''}`}
        description="Payouts are paid manually outside Optimize All. Preparing or finalizing a batch never sends money."
      />
      <div className="stack fin-page">
        <div className="fin-stats">
          <Stat
            label="Days until cutoff"
            icon={<CalendarClock />}
            loading={schedule.isPending && can.viewPayouts}
            value={period ? daysUntil(period.cutoffAt) : '—'}
            measurement="Count"
            hint={period ? `Period ${period.periodKey}` : undefined}
          />
          {can.approveBonus && (
            <Stat
              label="Pending approvals"
              icon={<ClipboardCheck />}
              loading={pending.isPending}
              value={
                pending.data ? (
                  <Link to="/finance/approvals" className="ui-link">
                    {pending.data.total}
                  </Link>
                ) : (
                  '—'
                )
              }
              measurement="Count"
            />
          )}
          {can.hold && (
            <Stat
              label="Active holds"
              icon={<PauseCircle />}
              loading={holds.isPending}
              value={
                holds.data ? (
                  <Link to="/finance/holds" className="ui-link">
                    {holds.data.total}
                  </Link>
                ) : (
                  '—'
                )
              }
              measurement="Count"
            />
          )}
          <Stat
            label="Batches awaiting payment"
            icon={<Scale />}
            loading={finalized.isPending && can.viewPayouts}
            value={finalized.data ? finalized.data.total : '—'}
            measurement="Count"
          />
        </div>

        {can.viewPayouts && (
          <div className="fin-two-col">
            <Card as="section" aria-labelledby="ov-period">
              <CardHeader
                titleId="ov-period"
                title="Current payout period"
                actions={
                  <ButtonLink size="sm" variant="secondary" to="/finance/schedule">
                    Schedule
                  </ButtonLink>
                }
              />
              <CardBody>
                {schedule.isPending ? (
                  <Skeleton height={140} />
                ) : schedule.isError ? (
                  <QueryError error={schedule.error} onRetry={() => schedule.refetch()} />
                ) : s && period ? (
                  <KeyValueList
                    items={[
                      { label: 'Period', value: period.periodKey },
                      {
                        label: `Cutoff (${s.current.timeZone})`,
                        value: <DateTime value={period.cutoffAt} timeZone={s.current.timeZone} withZone />,
                      },
                      {
                        label: `Cutoff (your time, ${userZone})`,
                        value: <DateTime value={period.cutoffAt} withZone format="both" />,
                      },
                      { label: 'Payment date', value: dateOnlyToDisplay(period.paymentDate) },
                      {
                        label: 'Days left',
                        value: `${daysUntil(period.cutoffAt)} ${daysUntil(period.cutoffAt) === 1 ? 'day' : 'days'}`,
                      },
                      { label: 'Last completed period', value: s.lastCompletedPeriod.periodKey },
                    ]}
                  />
                ) : null}
              </CardBody>
            </Card>
            <Card as="section" aria-labelledby="ov-schedule">
              <CardHeader titleId="ov-schedule" title="Schedule summary" />
              <CardBody>
                {s ? (
                  <KeyValueList
                    layout="inline"
                    items={[
                      { label: 'Frequency', value: s.current.frequency },
                      { label: 'Cutoff', value: `${s.current.cutoffLocalTime} ${s.current.timeZone}` },
                      { label: 'Payment delay', value: `${s.current.paymentDelayDays} days` },
                      {
                        label: 'Minimum payout',
                        value: (
                          <Money
                            amount={s.current.minimumPayoutAmount}
                            currency={s.current.settlementCurrency}
                          />
                        ),
                      },
                      { label: 'Earning hold', value: `${s.current.earningHoldDays} days` },
                      { label: 'Auto-prepare', value: s.current.autoPrepareBatches ? 'On' : 'Off' },
                      ...(s.scheduledChanges.length > 0
                        ? [
                            {
                              label: 'Scheduled change',
                              value: <DateTime value={s.scheduledChanges[0]!.effectiveFrom} withZone />,
                            },
                          ]
                        : []),
                    ]}
                  />
                ) : (
                  <Skeleton height={140} />
                )}
              </CardBody>
            </Card>
          </div>
        )}

        {can.viewPayouts && (
          <div className="fin-two-col">
            <Card as="section" aria-labelledby="ov-latest">
              <CardHeader
                titleId="ov-latest"
                title="Latest batches"
                actions={
                  <ButtonLink size="sm" variant="secondary" to="/finance/batches">
                    All batches
                  </ButtonLink>
                }
              />
              <CardBody>
                {latest.isError ? (
                  <QueryError error={latest.error} onRetry={() => latest.refetch()} />
                ) : (
                  <DataTable
                    caption="Latest payout batches"
                    columns={latestColumns}
                    rows={latest.data?.items ?? []}
                    loading={latest.isPending}
                    loadingRows={3}
                    getRowId={(b) => b.id}
                    emptyState={<p className="text-muted">No batches yet.</p>}
                  />
                )}
              </CardBody>
            </Card>
            <Card as="section" aria-labelledby="ov-open">
              <CardHeader
                titleId="ov-open"
                title="Awaiting payment & reconciliation"
                description="Server-reconciled totals of finalized and recently completed batches."
              />
              <CardBody>
                {finalized.isPending || completed.isPending ? (
                  <Skeleton height={120} />
                ) : (
                  <OpenMoney batches={openBatches} />
                )}
              </CardBody>
            </Card>
          </div>
        )}
      </div>
    </>
  );
}
