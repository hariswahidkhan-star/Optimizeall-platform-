import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Card,
  CardBody,
  DataTable,
  EmptyState,
  ErrorState,
  FilterBar,
  Money,
  PageHeader,
  Pagination,
  Tabs,
  type DataTableColumn,
} from '@/components/ui';
import { useCreditNotes, usePayments } from '../api/hooks';
import type { CreditNote, Payment } from '../api/types';
import { CreditNoteStatusBadge, formatDateOnly } from '../lib';
import '../billing.css';

const paymentColumns: DataTableColumn<Payment>[] = [
  { id: 'date', header: 'Paid on', primary: true, nowrap: true, cell: (p) => formatDateOnly(p.paidOn) },
  {
    id: 'invoice',
    header: 'Invoice',
    cell: (p) => (
      <Link className="ui-link" to={`/agency/billing/invoices/${p.invoiceId}`}>
        {p.invoiceNumber}
      </Link>
    ),
  },
  { id: 'client', header: 'Client', cell: (p) => p.clientName },
  { id: 'method', header: 'Method', cell: (p) => p.method, hideOnMobile: true },
  { id: 'reference', header: 'Reference', cell: (p) => p.reference, hideOnMobile: true },
  { id: 'amount', header: 'Amount', align: 'right', cell: (p) => <Money amount={p.amount} currency={p.currency} /> },
];

const creditColumns: DataTableColumn<CreditNote>[] = [
  { id: 'number', header: 'Credit note', primary: true, cell: (c) => <span className="bill-strong">{c.number}</span> },
  { id: 'client', header: 'Client', cell: (c) => c.clientName },
  {
    id: 'invoice',
    header: 'Invoice',
    cell: (c) =>
      c.invoiceId ? (
        <Link className="ui-link" to={`/agency/billing/invoices/${c.invoiceId}`}>
          {c.invoiceNumber}
        </Link>
      ) : (
        '—'
      ),
  },
  { id: 'status', header: 'Status', cell: (c) => <CreditNoteStatusBadge status={c.status} /> },
  { id: 'reason', header: 'Reason', cell: (c) => c.reason, hideOnMobile: true },
  { id: 'amount', header: 'Amount', align: 'right', cell: (c) => <Money amount={c.amount} currency={c.currency} /> },
  { id: 'remaining', header: 'Unapplied', align: 'right', cell: (c) => <Money amount={c.remaining} currency={c.currency} /> },
];

function PaymentsTab() {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const query = usePayments({ search, page, pageSize: 25 });
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  return (
    <div className="stack">
      <FilterBar search={search} onSearchChange={(v) => { setSearch(v); setPage(1); }} searchLabel="Search payments" searchPlaceholder="Payment reference" />
      <DataTable
        caption="Payments received"
        columns={paymentColumns}
        rows={query.data?.items ?? []}
        getRowId={(p) => p.id}
        loading={query.isPending}
        emptyState={<EmptyState compact headingLevel={3} title="No payments recorded" />}
      />
      {query.data && query.data.total > 25 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
    </div>
  );
}

function CreditNotesTab() {
  const [page, setPage] = useState(1);
  const query = useCreditNotes({ page, pageSize: 25 });
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  return (
    <div className="stack">
      <DataTable
        caption="Credit notes"
        columns={creditColumns}
        rows={query.data?.items ?? []}
        getRowId={(c) => c.id}
        loading={query.isPending}
        emptyState={<EmptyState compact headingLevel={3} title="No credit notes" description="Credit notes correct issued invoices." />}
      />
      {query.data && query.data.total > 25 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
    </div>
  );
}

export function PaymentsPage() {
  return (
    <>
      <PageHeader
        title="Payments & credits"
        breadcrumbs={[{ label: 'Billing', to: '/agency/billing' }, { label: 'Payments & credits' }]}
        description="Payments are recorded from an invoice. Online card payments appear here once a payment gateway is connected."
      />
      <Card>
        <CardBody>
          <Tabs
            label="Payments and credit notes"
            tabs={[
              { id: 'payments', label: 'Payments', content: <PaymentsTab /> },
              { id: 'credits', label: 'Credit notes', content: <CreditNotesTab /> },
            ]}
          />
        </CardBody>
      </Card>
    </>
  );
}
