import { Plus } from 'lucide-react';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  ButtonLink,
  Card,
  CardBody,
  DataTable,
  EmptyState,
  ErrorState,
  FilterBar,
  Money,
  PageHeader,
  Pagination,
  type DataTableColumn,
} from '@/components/ui';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useClientOptions, useInvoices } from '../api/hooks';
import type { InvoiceSummary } from '../api/types';
import { INVOICE_STATUS_OPTIONS, InvoiceStatusBadge, formatDateOnly } from '../lib';
import '../billing.css';

const columns: DataTableColumn<InvoiceSummary>[] = [
  {
    id: 'number',
    header: 'Invoice',
    primary: true,
    nowrap: true,
    cell: (i) => (
      <Link className="ui-link bill-strong" to={`/agency/billing/invoices/${i.id}`}>
        {i.number ?? 'Draft'}
      </Link>
    ),
  },
  { id: 'client', header: 'Client', cell: (i) => i.clientName },
  { id: 'status', header: 'Status', cell: (i) => <InvoiceStatusBadge status={i.status} /> },
  { id: 'issued', header: 'Issued', cell: (i) => formatDateOnly(i.issueDate), hideOnMobile: true, nowrap: true },
  {
    id: 'due',
    header: 'Due',
    nowrap: true,
    cell: (i) => (i.daysOverdue > 0 ? `${formatDateOnly(i.dueDate)} (${i.daysOverdue}d late)` : formatDateOnly(i.dueDate)),
  },
  { id: 'total', header: 'Total', align: 'right', cell: (i) => <Money amount={i.total} currency={i.currency} /> },
  { id: 'balance', header: 'Balance', align: 'right', cell: (i) => <Money amount={i.balance} currency={i.currency} /> },
];

export function InvoicesPage() {
  const { hasPermission } = useAuth();
  const [params] = useSearchParams();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<string | undefined>(() => params.get('status') ?? undefined);
  const [clientAccountId, setClient] = useState<string | undefined>(() => params.get('clientAccountId') ?? undefined);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const clients = useClientOptions();
  const query = useInvoices({ search, status, clientAccountId, page, pageSize });

  return (
    <>
      <PageHeader
        title="Invoices"
        breadcrumbs={[{ label: 'Billing', to: '/agency/billing' }, { label: 'Invoices' }]}
        description="Drafts can be edited; issued invoices are final and corrected with credit notes."
        actions={
          hasPermission(Permissions.BillingManage) && (
            <ButtonLink to="/agency/billing/invoices/new" leadingIcon={<Plus />}>
              New invoice
            </ButtonLink>
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
            searchLabel="Search invoices"
            searchPlaceholder="Number, reference or client"
            filters={[
              { id: 'status', label: 'Status', options: INVOICE_STATUS_OPTIONS },
              { id: 'client', label: 'Client', options: (clients.data ?? []).map((c) => ({ value: c.id, label: c.name })) },
            ]}
            values={{ status, client: clientAccountId }}
            onFilterChange={(id, v) => {
              if (id === 'status') setStatus(v);
              else setClient(v);
              setPage(1);
            }}
            onReset={() => {
              setSearch('');
              setStatus(undefined);
              setClient(undefined);
              setPage(1);
            }}
          />
          {query.isError ? (
            <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          ) : (
            <>
              <DataTable
                caption="Invoices"
                columns={columns}
                rows={query.data?.items ?? []}
                getRowId={(i) => i.id}
                loading={query.isPending}
                emptyState={<EmptyState compact headingLevel={3} title="No invoices" description="Invoices you create or that retainers generate appear here." />}
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
                />
              )}
            </>
          )}
        </CardBody>
      </Card>
    </>
  );
}
