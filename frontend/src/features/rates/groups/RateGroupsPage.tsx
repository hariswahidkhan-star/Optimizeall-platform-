import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus, UsersRound } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  DataTable,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  PageHeader,
  Pagination,
  type DataTableColumn,
} from '@/components/ui';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { ApiError, errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatNumber } from '@/lib/format/money';
import { rk, useRateGroups } from '../api/queries';
import type { RateGroup, RateGroupListItem } from '../api/types';
import { emptyGroupForm, GroupFields, groupToInput, type GroupForm } from './GroupFields';
import '../rates.css';

export function RateGroupsPage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.RatesManage);
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [creating, setCreating] = useState(false);
  const query = useRateGroups({
    search,
    mode: filters.mode,
    includeArchived: filters.archived === 'true',
    page,
    pageSize,
  });

  const columns: DataTableColumn<RateGroupListItem>[] = [
    {
      id: 'name',
      header: 'Group',
      primary: true,
      cell: (g) => (
        <span className="stack rt-stack-xs">
          <Link className="ui-link rt-strong" to={`/manage/rate-groups/${g.id}`}>
            {g.name}
          </Link>
          <span className="text-small text-muted">
            {g.autoRule ? `Automatic: ${g.autoRule}` : g.description}
          </span>
        </span>
      ),
    },
    {
      id: 'mode',
      header: 'Membership',
      cell: (g) => (
        <span className="cluster rt-cluster-sm">
          <Badge size="sm" tone={g.membershipMode === 'Automatic' ? 'warning' : 'neutral'}>
            {g.membershipMode}
          </Badge>
          {g.archivedAt && <Badge size="sm">Archived</Badge>}
        </span>
      ),
    },
    { id: 'priority', header: 'Priority', align: 'right', cell: (g) => g.priority },
    {
      id: 'members',
      header: 'People',
      align: 'right',
      cell: (g) => (g.memberCount === null ? '—' : formatNumber(g.memberCount)),
    },
    {
      id: 'cards',
      header: 'Rate cards',
      cell: (g) =>
        g.cards.length === 0 ? (
          <span className="text-muted">None assigned</span>
        ) : (
          <span className="cluster rt-cluster-sm">
            {g.cards.map((c) => (
              <Link key={c.id} className="ui-link text-small" to={`/manage/rate-cards/${c.id}`}>
                {c.name}
              </Link>
            ))}
          </span>
        ),
    },
  ];

  const data = query.data;
  const filtered = !!(search || filters.mode || filters.archived);
  return (
    <>
      <PageHeader
        title="Rate groups"
        description="Named groups of people who share a rate (e.g. Macro, Micro, Nano influencers). When someone is in several groups, the more specific rate and then the higher priority wins."
        actions={
          canManage ? (
            <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
              New rate group
            </Button>
          ) : undefined
        }
      />
      <div className="stack">
        <FilterBar
          search={search}
          onSearchChange={(s) => {
            setSearch(s);
            setPage(1);
          }}
          searchLabel="Search rate groups"
          searchPlaceholder="Search by name…"
          filters={[
            {
              id: 'mode',
              label: 'Membership',
              options: [
                { value: 'Manual', label: 'Manual' },
                { value: 'Automatic', label: 'Automatic' },
              ],
            },
            { id: 'archived', label: 'Archived', options: [{ value: 'true', label: 'Include archived' }] },
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
              caption="Rate groups"
              columns={columns}
              rows={data?.items ?? []}
              getRowId={(g) => g.id}
              rowLabel={(g) => g.name}
              loading={query.isLoading}
              emptyState={
                <EmptyState
                  icon={<UsersRound />}
                  headingLevel={2}
                  title={filtered ? 'No rate groups match' : 'No rate groups yet'}
                  description={
                    filtered
                      ? 'Try another search or clear the filters.'
                      : 'Create groups such as “Macro influencers” and assign a rate card to everyone in them at once.'
                  }
                />
              }
            />
            {data && data.total > 0 && (
              <Pagination
                page={page}
                pageSize={pageSize}
                total={data.total}
                onPageChange={setPage}
                onPageSizeChange={(size) => {
                  setPageSize(size);
                  setPage(1);
                }}
              />
            )}
          </>
        )}
      </div>
      {creating && <CreateGroupDialog onClose={() => setCreating(false)} />}
    </>
  );
}

function CreateGroupDialog({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<GroupForm>(emptyGroupForm());
  const save = useMutation({
    mutationFn: () => api.post<RateGroup>('/admin/rate-groups', groupToInput(form)),
    onSuccess: async (g) => {
      toast.success('Rate group created', g.name);
      await queryClient.invalidateQueries({ queryKey: rk.all });
      onClose();
      navigate(`/manage/rate-groups/${g.id}`);
    },
  });
  const err = save.error instanceof ApiError ? save.error : null;
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title="New rate group"
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button
            onClick={() => save.mutate()}
            loading={save.isPending}
            disabled={form.name.trim().length < 2}
          >
            Create group
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && (
          <Alert tone="danger" title="The group was not created">
            {errorMessage(save.error)}
          </Alert>
        )}
        <GroupFields value={form} onChange={setForm} errors={err?.errors} />
      </div>
    </Dialog>
  );
}
