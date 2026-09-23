import { Plus } from 'lucide-react';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  Button,
  Card,
  CardBody,
  DataTable,
  DateTime,
  EmptyState,
  FilterBar,
  Money,
  PageHeader,
  Pagination,
  StatusBadge,
  statusOptions,
  type DataTableColumn,
} from '@/components/ui';
import { useBatches } from '../api/hooks';
import type { PayoutBatchSummary } from '../api/types';
import { Muted, PersonCell, QueryError } from '../components/common';
import { dateOnlyToDisplay } from '../lib/format';
import { useCan } from '../lib/useCan';
import { PrepareBatchDialog } from './PrepareBatchDialog';

const columns: DataTableColumn<PayoutBatchSummary>[] = [
  {
    id: 'reference',
    header: 'Reference',
    primary: true,
    cell: (b) => (
      <Link className="ui-link fin-strong" to={`/finance/batches/${b.id}`}>
        {b.reference}
      </Link>
    ),
    nowrap: true,
  },
  { id: 'status', header: 'Status', cell: (b) => <StatusBadge kind="payout" status={b.status} /> },
  { id: 'period', header: 'Period', cell: (b) => b.periodKey, nowrap: true },
  { id: 'cutoff', header: 'Cutoff', cell: (b) => <DateTime value={b.cutoffAt} />, hideOnMobile: true },
  { id: 'payment', header: 'Payment date', cell: (b) => dateOnlyToDisplay(b.paymentDate), nowrap: true },
  { id: 'items', header: 'Items', align: 'right', cell: (b) => b.itemCount },
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
  {
    id: 'prepared',
    header: 'Prepared by',
    cell: (b) => <PersonCell user={b.preparedBy} />,
    hideOnMobile: true,
  },
  {
    id: 'finalized',
    header: 'Finalized by',
    cell: (b) => (b.finalizedBy ? <PersonCell user={b.finalizedBy} /> : <Muted>—</Muted>),
    hideOnMobile: true,
  },
];

export function BatchesPage() {
  const can = useCan();
  const [search, setSearch] = useState('');
  const [params] = useSearchParams();
  const [status, setStatus] = useState<string | undefined>(() => params.get('status') ?? undefined);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [preparing, setPreparing] = useState(false);
  const query = useBatches({ search, status, page, pageSize });

  return (
    <>
      <PageHeader
        title="Payout batches"
        description="Prepare a batch after each cutoff, have a second person finalize it, pay participants outside the platform and record each payment reference."
        actions={
          can.prepare && (
            <Button leadingIcon={<Plus />} onClick={() => setPreparing(true)}>
              Prepare batch
            </Button>
          )
        }
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={search}
            onSearchChange={(v) => {
              setSearch(v);
              setPage(1);
            }}
            searchPlaceholder="Search reference or period"
            searchLabel="Search batches"
            filters={[{ id: 'status', label: 'Status', options: statusOptions('payout') }]}
            values={{ status }}
            onFilterChange={(_, v) => {
              setStatus(v);
              setPage(1);
            }}
            onReset={() => {
              setSearch('');
              setStatus(undefined);
              setPage(1);
            }}
          />
          {query.isError ? (
            <QueryError error={query.error} onRetry={() => query.refetch()} />
          ) : (
            <>
              <DataTable
                caption="Payout batches"
                columns={columns}
                rows={query.data?.items ?? []}
                getRowId={(b) => b.id}
                loading={query.isPending}
                emptyState={
                  <EmptyState
                    compact
                    headingLevel={3}
                    title="No payout batches"
                    description={
                      search || status
                        ? 'No batches match these filters.'
                        : 'Batches appear here after a cutoff, or when you prepare one.'
                    }
                  />
                }
              />
              {query.data && query.data.total > 0 && (
                <Pagination
                  page={page}
                  pageSize={pageSize}
                  total={query.data.total}
                  onPageChange={setPage}
                  onPageSizeChange={(s) => {
                    setPageSize(s);
                    setPage(1);
                  }}
                  label="Batches pages"
                />
              )}
            </>
          )}
        </CardBody>
      </Card>
      <PrepareBatchDialog open={preparing} onClose={() => setPreparing(false)} />
    </>
  );
}
