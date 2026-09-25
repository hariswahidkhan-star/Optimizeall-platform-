import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CheckCheck, Download, Inbox } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  FilterBar,
  Money,
  Pagination,
  type DataTableColumn,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { invalidateCodes, useCodePrograms, useCodeSales } from '../api/queries';
import type { BulkDecisionResult, CodeSaleListItem } from '../api/types';
import {
  SALE_STATUS_OPTIONS,
  SaleStatusBadge,
  SOURCE_LABEL,
  VERIFICATION_OPTIONS,
  VerificationBadge,
} from '../labels';

export interface CodeSalesTableProps {
  /** Where sale detail pages live, e.g. "/review/code-sales". */
  basePath: string;
  /** Fixed program (program detail page); otherwise a program filter is shown. */
  programId?: string;
  defaultStatus?: string;
  caption?: string;
}

/** Code sales with filters, search, bulk approval of matched sales (sales.review) and CSV export (codes.view). */
export function CodeSalesTable({
  basePath,
  programId,
  defaultStatus,
  caption = 'Code sales',
}: CodeSalesTableProps) {
  const { hasPermission } = useAuth();
  const canReview = hasPermission(Permissions.SalesReview);
  const canExport = hasPermission(Permissions.CodesView);
  const toast = useToast();
  const client = useQueryClient();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({ status: defaultStatus });
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [selected, setSelected] = useState<string[]>([]);
  const [bulkResult, setBulkResult] = useState<BulkDecisionResult | null>(null);
  const programs = useCodePrograms({ page: 1, pageSize: 100 });
  const params = {
    programId: programId ?? filters.program,
    status: filters.status,
    verification: filters.verification,
    source: filters.source,
    search: search || undefined,
    page,
    pageSize,
  };
  const query = useCodeSales(params);
  const bulk = useMutation({
    mutationFn: (saleIds: string[]) =>
      api.post<BulkDecisionResult>('/admin/code-sales/bulk-approve', {
        saleIds,
        onlyMatched: true,
        reason: 'Matched by the brand’s sales report',
      }),
    onSuccess: async (r) => {
      setBulkResult(r);
      setSelected([]);
      toast.success(
        `${r.approved} approved`,
        r.skipped > 0 ? `${r.skipped} skipped — see the details above the table.` : undefined,
      );
      await invalidateCodes(client);
    },
  });

  const columns: DataTableColumn<CodeSaleListItem>[] = [
    {
      id: 'order',
      header: 'Order',
      primary: true,
      cell: (s) => (
        <span className="stack dc-stack-xs">
          <Link className="ui-link dc-strong" to={`${basePath}/${s.id}`}>
            {s.orderReference}
          </Link>
          <span className="text-small text-muted">
            <span className="dc-mono">{s.code.name}</span>
            {s.sharedCode ? ' · shared' : ''}
            {!programId ? ` · ${s.program.brandName}` : ''}
          </span>
        </span>
      ),
    },
    {
      id: 'person',
      header: 'Participant',
      cell: (s) => (
        <span className="cluster dc-cluster-sm">
          {s.person.displayName}
          {s.isTestAccount && (
            <Badge size="sm" tone="warning">
              Test
            </Badge>
          )}
          {s.userStatus !== 'Active' && (
            <Badge size="sm" tone="danger">
              {s.userStatus}
            </Badge>
          )}
        </span>
      ),
    },
    { id: 'date', header: 'Order date', cell: (s) => <DateTime value={s.orderDate} /> },
    {
      id: 'value',
      header: 'Order value',
      align: 'right',
      cell: (s) => <Money amount={s.netAmount} currency={s.currency} />,
    },
    {
      id: 'status',
      header: 'Status',
      cell: (s) => (
        <span className="stack dc-stack-xs">
          <SaleStatusBadge status={s.status} />
          <VerificationBadge verification={s.verification} />
        </span>
      ),
    },
    {
      id: 'commission',
      header: 'Commission',
      align: 'right',
      cell: (s) =>
        s.commissionAmount !== null ? (
          <Money amount={s.commissionAmount} currency={s.program.currency} />
        ) : s.estimatedCommission !== null ? (
          <span className="text-muted" title="Estimate before caps">
            ~<Money amount={s.estimatedCommission} currency={s.program.currency} />
          </span>
        ) : (
          '—'
        ),
    },
    {
      id: 'source',
      header: 'Source',
      hideOnMobile: true,
      cell: (s) => <span className="text-small">{SOURCE_LABEL[s.source]}</span>,
    },
    {
      id: 'submitted',
      header: 'Reported',
      hideOnMobile: true,
      cell: (s) => <DateTime value={s.submittedAt} />,
    },
  ];

  const filtered = !!(search || filters.status || filters.verification || filters.source || filters.program);
  return (
    <div className="stack">
      <FilterBar
        search={search}
        onSearchChange={(value) => {
          setSearch(value);
          setPage(1);
        }}
        searchLabel="Search sales"
        searchPlaceholder="Order number, code, participant…"
        filters={[
          { id: 'status', label: 'Status', options: SALE_STATUS_OPTIONS },
          { id: 'verification', label: 'Brand report', options: VERIFICATION_OPTIONS },
          {
            id: 'source',
            label: 'Source',
            options: (Object.keys(SOURCE_LABEL) as (keyof typeof SOURCE_LABEL)[]).map((k) => ({
              value: k,
              label: SOURCE_LABEL[k],
            })),
          },
          ...(programId
            ? []
            : [
                {
                  id: 'program',
                  label: 'Program',
                  options: (programs.data?.items ?? []).map((p) => ({
                    value: p.id,
                    label: `${p.brandName} — ${p.name}`,
                  })),
                },
              ]),
        ]}
        values={filters}
        onFilterChange={(id, value) => {
          setFilters((f) => ({ ...f, [id]: value }));
          setPage(1);
        }}
        onReset={() => {
          setSearch('');
          setFilters({});
          setPage(1);
        }}
        actions={
          canExport ? (
            <Button
              variant="secondary"
              size="sm"
              leadingIcon={<Download />}
              onClick={() =>
                void api
                  .download('/admin/code-sales/export.csv', 'code-sales.csv', {
                    query: {
                      programId: params.programId,
                      status: params.status,
                      verification: params.verification,
                      search: params.search,
                    },
                  })
                  .catch((e: unknown) => toast.error('Export failed', errorMessage(e)))
              }
            >
              Export CSV
            </Button>
          ) : undefined
        }
      />
      {bulkResult && bulkResult.skipped > 0 && (
        <Alert
          tone="warning"
          title={`${bulkResult.skipped} not approved`}
          onDismiss={() => setBulkResult(null)}
        >
          <ul className="dc-perks">
            {bulkResult.items
              .filter((i) => !i.succeeded)
              .slice(0, 10)
              .map((i) => (
                <li key={i.saleId}>{i.message}</li>
              ))}
          </ul>
        </Alert>
      )}
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} retrying={query.isFetching} />
      ) : (
        <>
          <DataTable
            caption={caption}
            columns={columns}
            rows={query.data?.items ?? []}
            getRowId={(s) => s.id}
            rowLabel={(s) => `Order ${s.orderReference}`}
            loading={query.isLoading}
            selectable={canReview}
            selectedIds={selected}
            onSelectionChange={setSelected}
            bulkActions={(ids) => (
              <Button
                size="sm"
                leadingIcon={<CheckCheck />}
                loading={bulk.isPending}
                onClick={() => bulk.mutate(ids)}
              >
                Approve matched ({ids.length})
              </Button>
            )}
            emptyState={
              <EmptyState
                icon={<Inbox />}
                headingLevel={2}
                title={filtered ? 'No sales match' : 'No sales yet'}
                description={
                  filtered
                    ? 'Try another search or clear the filters.'
                    : 'Sales reported with discount codes appear here.'
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
              onPageSizeChange={(n) => {
                setPageSize(n);
                setPage(1);
              }}
            />
          )}
        </>
      )}
    </div>
  );
}
