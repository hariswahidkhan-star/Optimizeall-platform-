import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Inbox, Plus, TicketPercent } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Alert,
  Button,
  ButtonLink,
  Checkbox,
  DataTable,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  Money,
  PageHeader,
  Pagination,
  type DataTableColumn,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { ApiError, errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatDate } from '@/lib/format/dates';
import { formatNumber } from '@/lib/format/money';
import { invalidateCodes, useCodePrograms } from '../api/queries';
import type { CodeProgram, CodeProgramListItem } from '../api/types';
import { ProgramStatusBadge } from '../labels';
import {
  emptyPayout,
  emptyProgramDetails,
  PayoutFields,
  payoutToInput,
  ProgramDetailsFields,
  programDetailsToInput,
} from './ProgramForms';
import '../codes.css';

/** Campaign manager: discount-code programs per brand. */
export function CodeProgramsPage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.CodesManage);
  const canReviewSales = hasPermission(Permissions.CodesView);
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [creating, setCreating] = useState(false);
  const query = useCodePrograms({ search, status: filters.status, page, pageSize });

  const columns: DataTableColumn<CodeProgramListItem>[] = [
    {
      id: 'name',
      header: 'Program',
      primary: true,
      cell: (p) => (
        <span className="stack dc-stack-xs">
          <Link className="ui-link dc-strong" to={`/manage/codes/${p.id}`}>
            {p.brandName} — {p.name}
          </Link>
          <span className="text-small text-muted">{p.payoutSummary}</span>
        </span>
      ),
    },
    { id: 'status', header: 'Status', cell: (p) => <ProgramStatusBadge status={p.status} /> },
    {
      id: 'window',
      header: 'Runs',
      cell: (p) => `${formatDate(p.startsAt)} – ${p.endsAt ? formatDate(p.endsAt) : 'open-ended'}`,
    },
    {
      id: 'codes',
      header: 'Codes',
      align: 'right',
      cell: (p) => `${formatNumber(p.assignedCodes)} / ${formatNumber(p.codes)} assigned`,
    },
    { id: 'pending', header: 'To review', align: 'right', cell: (p) => formatNumber(p.pendingSales) },
    { id: 'approved', header: 'Approved sales', align: 'right', cell: (p) => formatNumber(p.approvedSales) },
    {
      id: 'commission',
      header: 'Commission',
      align: 'right',
      cell: (p) => <Money amount={p.commissionApproved} currency={p.currency} />,
    },
  ];

  const filtered = !!(search || filters.status);
  return (
    <>
      <PageHeader
        title="Discount codes"
        description="Brands give us discount codes; assign them to people or rate groups, collect the sales made with them and pay a commission per approved sale."
        actions={
          <span className="cluster dc-cluster-sm">
            {canReviewSales && (
              <ButtonLink to="/manage/codes/sales" variant="secondary" leadingIcon={<Inbox />}>
                All code sales
              </ButtonLink>
            )}
            {canManage && (
              <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
                New program
              </Button>
            )}
          </span>
        }
      />
      <div className="stack">
        <FilterBar
          search={search}
          onSearchChange={(s) => {
            setSearch(s);
            setPage(1);
          }}
          searchLabel="Search programs"
          searchPlaceholder="Brand or program name…"
          filters={[
            {
              id: 'status',
              label: 'Status',
              options: ['Draft', 'Active', 'Paused', 'Archived'].map((s) => ({ value: s, label: s })),
              allLabel: 'Not archived',
            },
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
        />
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} retrying={query.isFetching} />
        ) : (
          <>
            <DataTable
              caption="Discount-code programs"
              columns={columns}
              rows={query.data?.items ?? []}
              getRowId={(p) => p.id}
              rowLabel={(p) => p.name}
              loading={query.isLoading}
              emptyState={
                <EmptyState
                  icon={<TicketPercent />}
                  headingLevel={2}
                  title={filtered ? 'No programs match' : 'No discount-code programs yet'}
                  description={
                    filtered
                      ? 'Try another search or clear the filters.'
                      : 'Create a program for a brand, import the codes it gave you and assign them to your creators.'
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
      {creating && <CreateProgramDialog onClose={() => setCreating(false)} />}
    </>
  );
}

function CreateProgramDialog({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const client = useQueryClient();
  const [details, setDetails] = useState(emptyProgramDetails());
  const [payout, setPayout] = useState(emptyPayout());
  const [activate, setActivate] = useState(true);
  const save = useMutation({
    mutationFn: () =>
      api.post<CodeProgram>('/admin/code-programs', {
        ...programDetailsToInput(details),
        payout: payoutToInput(payout),
        activate,
      }),
    onSuccess: async (p) => {
      toast.success('Program created', `${p.brandName} — ${p.name}`);
      await invalidateCodes(client);
      onClose();
      navigate(`/manage/codes/${p.id}`);
    },
  });
  const errors = save.error instanceof ApiError ? save.error.errors : undefined;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="New discount-code program"
      description="The brand, where its codes can be used and how participants are paid."
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button
            onClick={() => save.mutate()}
            loading={save.isPending}
            disabled={details.name.trim().length < 2 || details.brandName.trim().length < 2}
          >
            Create program
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="The program was not created">
            {errorMessage(save.error)}
          </Alert>
        )}
        <ProgramDetailsFields value={details} onChange={setDetails} errors={errors} />
        <h3 className="dc-strong" style={{ margin: 0 }}>
          Payout rules
        </h3>
        <PayoutFields value={payout} onChange={setPayout} currency={details.currency} errors={errors} />
        <Checkbox
          label="Open to participants now"
          description="Otherwise the program starts as a draft: you can add and assign codes before anyone sees it."
          checked={activate}
          onChange={(e) => setActivate(e.target.checked)}
        />
      </div>
    </Dialog>
  );
}
