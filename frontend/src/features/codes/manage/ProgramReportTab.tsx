import { Download } from 'lucide-react';
import { useState } from 'react';
import {
  Button,
  DataTable,
  ErrorState,
  FormField,
  Input,
  Money,
  Select,
  Stat,
  type DataTableColumn,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatNumber, formatPercent } from '@/lib/format/money';
import { useCodeReport } from '../api/queries';
import type { CodeProgram, CodeReportGrouping, CodeReportRow } from '../api/types';

const GROUPINGS: { value: CodeReportGrouping; label: string }[] = [
  { value: 'Person', label: 'By person' },
  { value: 'Code', label: 'By code' },
  { value: 'Group', label: 'By rate group' },
];

/** Uses, sales, discount and commissions per person / code / group, with CSV export. */
export function ProgramReportTab({ program }: { program: CodeProgram }) {
  const toast = useToast();
  const [groupBy, setGroupBy] = useState<CodeReportGrouping>('Person');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const params = {
    programId: program.id,
    groupBy,
    from: from ? `${from}T00:00:00Z` : undefined,
    to: to ? `${to}T23:59:59Z` : undefined,
  };
  const query = useCodeReport(params);
  const c = program.currency;
  const r = query.data;
  const columns: DataTableColumn<CodeReportRow>[] = [
    {
      id: 'label',
      header: GROUPINGS.find((g) => g.value === groupBy)!.label.replace('By ', ''),
      primary: true,
      cell: (row) => (
        <span className="stack dc-stack-xs">
          <span className={groupBy === 'Code' ? 'dc-mono dc-strong' : 'dc-strong'}>{row.label}</span>
          {row.detail && <span className="text-small text-muted">{row.detail}</span>}
        </span>
      ),
    },
    { id: 'uses', header: 'Uses', align: 'right', cell: (row) => formatNumber(row.uses) },
    { id: 'approved', header: 'Approved', align: 'right', cell: (row) => formatNumber(row.approved) },
    {
      id: 'gross',
      header: 'Gross sales',
      align: 'right',
      cell: (row) => <Money amount={row.grossSales} currency={c} />,
    },
    {
      id: 'discount',
      header: 'Discount given',
      align: 'right',
      hideOnMobile: true,
      cell: (row) => <Money amount={row.discountGiven} currency={c} />,
    },
    {
      id: 'owed',
      header: 'Pending (est.)',
      align: 'right',
      cell: (row) => <Money amount={row.commissionPending} currency={c} />,
    },
    {
      id: 'approvedC',
      header: 'Approved, unpaid',
      align: 'right',
      cell: (row) => <Money amount={row.commissionApproved} currency={c} />,
    },
    {
      id: 'paid',
      header: 'Paid',
      align: 'right',
      cell: (row) => <Money amount={row.commissionPaid} currency={c} />,
    },
    {
      id: 'reversed',
      header: 'Reversed',
      align: 'right',
      hideOnMobile: true,
      cell: (row) => <Money amount={row.commissionReversed} currency={c} />,
    },
    ...(groupBy === 'Person' && r?.totals.clicks !== null && r?.totals.clicks !== undefined
      ? [
          {
            id: 'clicks',
            header: 'Link clicks',
            align: 'right' as const,
            cell: (row: CodeReportRow) => (row.clicks === null ? '—' : formatNumber(row.clicks)),
          },
          {
            id: 'conv',
            header: 'Conversion',
            align: 'right' as const,
            cell: (row: CodeReportRow) =>
              row.conversionRate === null ? '—' : formatPercent(row.conversionRate / 100),
          },
        ]
      : []),
  ];
  return (
    <div className="stack">
      <div className="dc-form-grid" style={{ alignItems: 'end' }}>
        <FormField label="Group by">
          <Select
            value={groupBy}
            onChange={(e) => setGroupBy(e.target.value as CodeReportGrouping)}
            options={GROUPINGS}
          />
        </FormField>
        <FormField label="Orders from" optional>
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </FormField>
        <FormField label="Orders until" optional>
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </FormField>
        <div>
          <Button
            variant="secondary"
            leadingIcon={<Download />}
            onClick={() =>
              void api
                .download('/admin/code-reports/export.csv', 'code-report.csv', { query: params })
                .catch((e: unknown) => toast.error('Export failed', errorMessage(e)))
            }
          >
            Export CSV
          </Button>
        </div>
      </div>
      {r && (
        <div className="dc-stats">
          <Stat label="Uses" measurement="Count" value={formatNumber(r.totals.uses)} />
          <Stat label="Gross sales" value={<Money amount={r.totals.grossSales} currency={c} />} />
          <Stat label="Discount given" value={<Money amount={r.totals.discountGiven} currency={c} />} />
          <Stat
            label="Commission approved"
            value={<Money amount={r.totals.commissionApproved + r.totals.commissionPaid} currency={c} />}
          />
          <Stat label="Paid" value={<Money amount={r.totals.commissionPaid} currency={c} />} />
        </div>
      )}
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <DataTable
          caption="Discount-code report"
          columns={columns}
          rows={r?.rows ?? []}
          getRowId={(row) => row.id ?? row.label}
          loading={query.isLoading}
        />
      )}
      {r && <p className="text-small text-muted">{r.note}</p>}
    </div>
  );
}
