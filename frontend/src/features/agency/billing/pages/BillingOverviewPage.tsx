import { AlertTriangle, CircleDollarSign, FileText, Plus, Repeat } from 'lucide-react';
import { Link } from 'react-router-dom';
import {
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  EmptyState,
  ErrorState,
  Money,
  PageHeader,
  Stat,
  type DataTableColumn,
} from '@/components/ui';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { useBillingOverview } from '../api/hooks';
import type { CurrencyAmount, InvoiceSummary, Payment } from '../api/types';
import { InvoiceStatusBadge, formatDateOnly } from '../lib';
import '../billing.css';

export function Amounts({ values, empty = '—' }: { values: CurrencyAmount[]; empty?: string }) {
  if (values.length === 0) return <>{empty}</>;
  return (
    <span className="bill-amounts">
      {values.map((v) => (
        <Money key={v.currency} amount={v.amount} currency={v.currency} />
      ))}
    </span>
  );
}

const overdueColumns: DataTableColumn<InvoiceSummary>[] = [
  {
    id: 'number',
    header: 'Invoice',
    primary: true,
    cell: (i) => (
      <Link className="ui-link bill-strong" to={`/agency/billing/invoices/${i.id}`}>
        {i.number ?? 'Draft'}
      </Link>
    ),
  },
  { id: 'client', header: 'Client', cell: (i) => i.clientName },
  { id: 'due', header: 'Due', cell: (i) => `${formatDateOnly(i.dueDate)} · ${i.daysOverdue} days overdue` },
  { id: 'balance', header: 'Balance', align: 'right', cell: (i) => <Money amount={i.balance} currency={i.currency} /> },
];

const paymentColumns: DataTableColumn<Payment>[] = [
  { id: 'date', header: 'Paid on', cell: (p) => formatDateOnly(p.paidOn), primary: true },
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
  { id: 'amount', header: 'Amount', align: 'right', cell: (p) => <Money amount={p.amount} currency={p.currency} /> },
];

/** Billing home: receivables, overdue, MRR from active retainers and recent collections (all figures from the API). */
export function BillingOverviewPage() {
  const { hasPermission } = useAuth();
  const query = useBillingOverview();
  const data = query.data;
  return (
    <>
      <PageHeader
        title="Billing"
        description="Receivables, retainers and collections. Amounts are shown per currency — they are never converted or mixed."
        actions={
          hasPermission(Permissions.BillingManage) && (
            <ButtonLink to="/agency/billing/invoices/new" leadingIcon={<Plus />}>
              New invoice
            </ButtonLink>
          )
        }
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <div className="stack">
          <div className="bill-grid">
            <Stat label="Outstanding" icon={<CircleDollarSign />} loading={query.isPending} value={<Amounts values={data?.outstanding ?? []} empty="Nothing due" />} />
            <Stat
              label="Overdue"
              icon={<AlertTriangle />}
              loading={query.isPending}
              value={<Amounts values={data?.overdue ?? []} empty="None" />}
              hint={data ? `${data.overdueInvoices} invoice(s)` : undefined}
            />
            <Stat
              label="Monthly recurring revenue"
              icon={<Repeat />}
              loading={query.isPending}
              value={<Amounts values={data?.mrr ?? []} />}
              hint={data ? `${data.activeContracts} active retainer(s)` : undefined}
            />
            <Stat label="Collected (30 days)" icon={<FileText />} loading={query.isPending} value={<Amounts values={data?.collectedLast30Days ?? []} />} hint={data ? `${data.draftInvoices} draft invoice(s) to review` : undefined} />
          </div>
          <div className="bill-two-col">
            <Card>
              <CardHeader title="Overdue invoices" />
              <CardBody>
                <DataTable
                  caption="Overdue invoices"
                  columns={overdueColumns}
                  rows={data?.recentlyOverdue ?? []}
                  getRowId={(i) => i.id}
                  loading={query.isPending}
                  emptyState={<EmptyState compact headingLevel={3} title="Nothing overdue" description="Every issued invoice is within its terms." />}
                />
              </CardBody>
            </Card>
            <Card>
              <CardHeader title="Recent payments" />
              <CardBody>
                <DataTable
                  caption="Recent payments"
                  columns={paymentColumns}
                  rows={data?.recentPayments ?? []}
                  getRowId={(p) => p.id}
                  loading={query.isPending}
                  emptyState={<EmptyState compact headingLevel={3} title="No payments in the last 30 days" />}
                />
              </CardBody>
            </Card>
          </div>
          <p className="bill-muted">
            Status of every invoice: <InvoiceStatusBadge status="Overdue" /> invoices get reminders automatically (3 days before, on the due
            date, 7 and 14 days after).
          </p>
        </div>
      )}
    </>
  );
}
