import { Banknote } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DataTable, type DataTableColumn } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Skeleton } from '@/components/ui/Skeleton';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { pluralize } from '@/lib/format/text';
import { useEarningsSummary, usePayout, usePayouts } from '../api/queries';
import type { Earning, MyPayout } from '../api/types';
import { QueryState } from '../components/QueryState';
import { NextPayoutCard } from '../home/HomeSections';
import { earningTypeLabel } from '../lib/labels';
import '../participant.css';

/** `DateOnly` payment dates are calendar dates: render them without shifting through a time zone. */
export function PaymentDate({ value }: { value: string }) {
  return <DateTime value={`${value}T12:00:00Z`} format="date" timeZone="UTC" />;
}

const columns: DataTableColumn<MyPayout>[] = [
  {
    id: 'period',
    header: 'Payout',
    primary: true,
    cell: (p) => (
      <Link to={`/app/payouts/${p.itemId}`} className="ui-link">
        {p.batchReference}
      </Link>
    ),
  },
  { id: 'status', header: 'Status', cell: (p) => <StatusBadge kind="payoutItem" status={p.status} /> },
  {
    id: 'amount',
    header: 'Amount',
    align: 'right',
    cell: (p) => <Money amount={p.amount} currency={p.currency} />,
  },
  {
    id: 'earnings',
    header: 'Earnings',
    align: 'right',
    cell: (p) => <span className="tabular">{p.earningCount}</span>,
  },
  {
    id: 'paymentDate',
    header: 'Payment date',
    nowrap: true,
    cell: (p) => <PaymentDate value={p.paymentDate} />,
  },
  {
    id: 'paid',
    header: 'Paid',
    nowrap: true,
    cell: (p) => (p.paidAt ? <DateTime value={p.paidAt} format="date" /> : '—'),
  },
];

export function PayoutsPage() {
  const [page, setPage] = useState(1);
  const list = usePayouts(page);
  const summary = useEarningsSummary();

  return (
    <div className="pp-page">
      <PageHeader
        title="Payouts"
        description="Payouts are prepared every two weeks from approved earnings. Finance pays them and records the payment reference."
      />
      {summary.isSuccess && <NextPayoutCard summary={summary.data} />}
      {summary.isPending && <Skeleton height={200} />}

      <section aria-labelledby="payout-history-title" className="pp-section">
        <h2 id="payout-history-title" className="pp-section__title">
          Payout history
        </h2>
        {list.isError ? (
          <Card flat>
            <ErrorState
              error={list.error}
              title="Payouts couldn’t be loaded"
              onRetry={() => void list.refetch()}
            />
          </Card>
        ) : (
          <>
            <DataTable
              caption="Payout history"
              columns={columns}
              rows={list.data?.items ?? []}
              getRowId={(p) => p.itemId}
              loading={list.isPending}
              emptyState={
                <EmptyState
                  headingLevel={3}
                  icon={<Banknote />}
                  title="No payouts yet"
                  description="Your first payout appears here once approved earnings reach the minimum and a payout period closes."
                />
              }
            />
            {list.data && list.data.total > 25 && (
              <Pagination
                page={page}
                pageSize={25}
                total={list.data.total}
                onPageChange={setPage}
                label="Payout pages"
              />
            )}
          </>
        )}
      </section>
    </div>
  );
}

const earningColumns: DataTableColumn<Earning>[] = [
  {
    id: 'description',
    header: 'Earning',
    primary: true,
    cell: (e) => (
      <span className="stack" style={{ ['--stack-gap' as string]: '2px' }}>
        <span>{e.description || earningTypeLabel(e.type)}</span>
        {e.campaign && <span className="text-small pp-muted">{e.campaign.title}</span>}
      </span>
    ),
  },
  { id: 'type', header: 'Type', cell: (e) => earningTypeLabel(e.type) },
  { id: 'date', header: 'Earned', cell: (e) => <DateTime value={e.createdAt} format="date" /> },
  {
    id: 'amount',
    header: 'Amount',
    align: 'right',
    cell: (e) => <Money amount={e.settlementAmount} currency={e.settlementCurrency} colored />,
  },
];

export function PayoutDetailPage() {
  const { itemId = '' } = useParams();
  const query = usePayout(itemId);
  return (
    <QueryState query={query} errorTitle="This payout isn’t available">
      {({ payout, earnings }) => (
        <div className="pp-page">
          <PageHeader
            title={`Payout ${payout.batchReference}`}
            breadcrumbs={[{ label: 'Payouts', to: '/app/payouts' }, { label: payout.batchReference }]}
            meta={<StatusBadge kind="payoutItem" status={payout.status} />}
          />
          {payout.status === 'Failed' && (
            <Alert tone="danger" title="This payment failed">
              The earnings in it went back to your balance and will be included in a later payout. Check your{' '}
              <Link to="/app/profile/payout-details" className="ui-link">
                payout details
              </Link>
              .
            </Alert>
          )}
          <Card as="section" aria-labelledby="payout-summary-title">
            <CardHeader titleId="payout-summary-title" title="Summary" />
            <CardBody>
              <KeyValueList
                items={[
                  { label: 'Amount', value: <Money amount={payout.amount} currency={payout.currency} /> },
                  { label: 'Status', value: <StatusBadge kind="payoutItem" status={payout.status} /> },
                  { label: 'Period cutoff', value: <DateTime value={payout.cutoffAt} withZone /> },
                  { label: 'Scheduled payment date', value: <PaymentDate value={payout.paymentDate} /> },
                  { label: 'Paid on', value: payout.paidAt ? <DateTime value={payout.paidAt} /> : 'Not yet' },
                  {
                    label: 'Payment reference',
                    value: payout.paymentReference ? (
                      <code
                        aria-label={`Payment reference ending ${payout.paymentReference.replace(/\D/g, '')}`}
                      >
                        {payout.paymentReference}
                      </code>
                    ) : (
                      '—'
                    ),
                  },
                ]}
              />
            </CardBody>
          </Card>
          <section aria-labelledby="included-title" className="pp-section">
            <h2 id="included-title" className="pp-section__title">
              Included earnings ({pluralize(earnings.length, 'item')})
            </h2>
            <DataTable
              caption="Earnings included in this payout"
              columns={earningColumns}
              rows={earnings}
              getRowId={(e) => e.id}
              emptyState={
                <EmptyState
                  headingLevel={3}
                  title="No earnings listed"
                  description={
                    payout.status === 'Failed'
                      ? 'The earnings of a failed payout return to your balance.'
                      : 'This payout has no earnings attached.'
                  }
                />
              }
            />
          </section>
        </div>
      )}
    </QueryState>
  );
}
