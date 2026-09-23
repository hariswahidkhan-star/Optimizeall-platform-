import { Download } from 'lucide-react';
import { useState } from 'react';
import {
  Button,
  Card,
  CardBody,
  DataTable,
  EmptyState,
  ErrorState,
  FormField,
  Money,
  PageHeader,
  Select,
  Tabs,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api, type QueryParams } from '@/lib/api/client';
import { useAgingReport, useCollectionsReport, useMrrReport, useRevenueReport } from '../api/hooks';
import type { AgingRow, CollectionsReport, MrrReport, RevenueRow } from '../api/types';
import { billingErrorMessage, formatDateOnly } from '../lib';
import { Amounts } from './BillingOverviewPage';
import '../billing.css';

function CsvButton({ path, fileName, query }: { path: string; fileName: string; query?: QueryParams }) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  return (
    <Button
      size="sm"
      variant="secondary"
      leadingIcon={<Download />}
      loading={busy}
      onClick={async () => {
        setBusy(true);
        try {
          await api.download(path, fileName, { query });
        } catch (error) {
          toast.error('Download failed', billingErrorMessage(error));
        } finally {
          setBusy(false);
        }
      }}
    >
      Export CSV
    </Button>
  );
}

const agingColumns: DataTableColumn<AgingRow>[] = [
  { id: 'client', header: 'Client', primary: true, cell: (r) => <span className={r.clientName === 'Total' ? 'bill-strong' : undefined}>{r.clientName}</span> },
  { id: 'current', header: 'Current', align: 'right', cell: (r) => <Money amount={r.current} currency={r.currency} /> },
  { id: 'd30', header: '1–30', align: 'right', cell: (r) => <Money amount={r.days1To30} currency={r.currency} /> },
  { id: 'd60', header: '31–60', align: 'right', cell: (r) => <Money amount={r.days31To60} currency={r.currency} /> },
  { id: 'd90', header: '61–90', align: 'right', cell: (r) => <Money amount={r.days61To90} currency={r.currency} /> },
  { id: 'over', header: '90+', align: 'right', cell: (r) => <Money amount={r.over90} currency={r.currency} /> },
  { id: 'total', header: 'Total', align: 'right', cell: (r) => <Money amount={r.total} currency={r.currency} /> },
];

function AgingTab() {
  const query = useAgingReport({});
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const rows = [...(query.data?.rows ?? []), ...(query.data?.totals ?? [])];
  return (
    <div className="stack">
      <div className="bill-actions">
        <span className="bill-muted">As of {formatDateOnly(query.data?.asOf)} · days past the due date</span>
        <CsvButton path="/agency/billing/reports/aging.csv" fileName="ar-aging.csv" />
      </div>
      <DataTable
        caption="Accounts receivable aging"
        columns={agingColumns}
        rows={rows}
        getRowId={(r) => `${r.clientAccountId}-${r.currency}-${r.clientName}`}
        loading={query.isPending}
        emptyState={<EmptyState compact headingLevel={3} title="Nothing outstanding" />}
      />
    </div>
  );
}

function RevenueTab() {
  const [groupBy, setGroupBy] = useState<'month' | 'service' | 'client'>('month');
  const query = useRevenueReport({ groupBy });
  const columns: DataTableColumn<RevenueRow>[] = [
    { id: 'label', header: groupBy === 'month' ? 'Month' : groupBy === 'service' ? 'Service' : 'Client', primary: true, cell: (r) => r.label },
    { id: 'currency', header: 'Currency', cell: (r) => r.currency },
    { id: 'invoiced', header: 'Invoiced (net of tax)', align: 'right', cell: (r) => <Money amount={r.invoiced} currency={r.currency} /> },
    { id: 'collected', header: 'Collected', align: 'right', cell: (r) => <Money amount={r.collected} currency={r.currency} /> },
  ];
  return (
    <div className="stack">
      <div className="bill-actions">
        <FormField label="Group by">
          <Select
            value={groupBy}
            options={[
              { value: 'month', label: 'Month' },
              { value: 'service', label: 'Service' },
              { value: 'client', label: 'Client' },
            ]}
            onChange={(e) => setGroupBy(e.target.value as typeof groupBy)}
          />
        </FormField>
        <CsvButton path="/agency/billing/reports/revenue.csv" fileName={`revenue-by-${groupBy}.csv`} query={{ groupBy }} />
      </div>
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <DataTable
          caption={`Revenue by ${groupBy}`}
          columns={columns}
          rows={query.data?.rows ?? []}
          getRowId={(r) => `${r.key}-${r.currency}`}
          loading={query.isPending}
          emptyState={<EmptyState compact headingLevel={3} title="No revenue in this period" />}
        />
      )}
    </div>
  );
}

function MrrTab() {
  const query = useMrrReport();
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const columns: DataTableColumn<MrrReport['rows'][number]>[] = [
    { id: 'client', header: 'Client', primary: true, cell: (r) => r.clientName },
    { id: 'contracts', header: 'Active retainers', align: 'right', cell: (r) => r.activeContracts },
    { id: 'mrr', header: 'MRR', align: 'right', cell: (r) => <Money amount={r.mrr} currency={r.currency} /> },
    { id: 'arr', header: 'ARR', align: 'right', cell: (r) => <Money amount={r.arr} currency={r.currency} /> },
  ];
  return (
    <div className="stack">
      <div className="bill-actions">
        <span>
          MRR <Amounts values={query.data?.mrrByCurrency ?? []} /> · ARR <Amounts values={query.data?.arrByCurrency ?? []} />
        </span>
        <CsvButton path="/agency/billing/reports/mrr.csv" fileName="mrr-arr.csv" />
      </div>
      <DataTable caption="Recurring revenue by client" columns={columns} rows={query.data?.rows ?? []} getRowId={(r) => `${r.clientAccountId}-${r.currency}`} loading={query.isPending} emptyState={<EmptyState compact headingLevel={3} title="No active retainers" />} />
    </div>
  );
}

function CollectionsTab() {
  const query = useCollectionsReport({});
  if (query.isError) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const columns: DataTableColumn<CollectionsReport['rows'][number]>[] = [
    { id: 'month', header: 'Month', primary: true, cell: (r) => r.month },
    { id: 'method', header: 'Method', cell: (r) => r.method },
    { id: 'count', header: 'Payments', align: 'right', cell: (r) => r.payments },
    { id: 'amount', header: 'Amount', align: 'right', cell: (r) => <Money amount={r.amount} currency={r.currency} /> },
  ];
  return (
    <div className="stack">
      <div className="bill-actions">
        <CsvButton path="/agency/billing/reports/collections.csv" fileName="collections.csv" />
      </div>
      <DataTable caption="Collections" columns={columns} rows={query.data?.rows ?? []} getRowId={(r) => `${r.month}-${r.currency}-${r.method}`} loading={query.isPending} emptyState={<EmptyState compact headingLevel={3} title="No payments in this period" />} />
    </div>
  );
}

export function ReportsPage() {
  return (
    <>
      <PageHeader title="Billing reports" breadcrumbs={[{ label: 'Billing', to: '/agency/billing' }, { label: 'Reports' }]} />
      <Card>
        <CardBody>
          <Tabs
            label="Reports"
            tabs={[
              { id: 'aging', label: 'AR aging', content: <AgingTab /> },
              { id: 'revenue', label: 'Revenue', content: <RevenueTab /> },
              { id: 'mrr', label: 'MRR / ARR', content: <MrrTab /> },
              { id: 'collections', label: 'Collections', content: <CollectionsTab /> },
            ]}
          />
        </CardBody>
      </Card>
    </>
  );
}
