import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Megaphone, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Badge,
  ButtonLink,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  FilterBar,
  Money,
  PageHeader,
  Pagination,
  ProgressBar,
  StatusBadge,
  type DataTableColumn,
  type SortState,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { qk, useCategories } from '../api/queries';
import { CAMPAIGN_STATUSES, type AdminCampaignListItem } from '../api/types';
import { useCampaignActions } from './campaignActions';
import '../campaigns.css';

const SORT_KEYS: Record<string, string> = {
  title: 'title',
  startsAt: 'startsAt',
  deadline: 'deadline',
  created: 'newest',
};

export function SpendCell({
  row,
}: {
  row: Pick<AdminCampaignListItem, 'spent' | 'budget' | 'currency' | 'title'>;
}) {
  if (row.budget === null || row.budget <= 0) {
    return (
      <span className="stack mg-stack-xs">
        <Money amount={row.spent} currency={row.currency} />
        <span className="text-small text-muted">No budget</span>
      </span>
    );
  }
  return (
    <span className="mg-spend">
      <ProgressBar
        label={`Spend for ${row.title}`}
        hideLabel
        value={Math.min(row.spent, row.budget)}
        max={row.budget}
        tone={row.spent >= row.budget ? 'accent' : 'primary'}
        valueText={`${row.spent} of ${row.budget} ${row.currency}`}
      />
      <span className="text-small">
        <Money amount={row.spent} currency={row.currency} /> of{' '}
        <Money amount={row.budget} currency={row.currency} />
      </span>
    </span>
  );
}

export function CampaignsListPage() {
  const { hasPermission } = useAuth();
  const canCreate = hasPermission(Permissions.CampaignsManage) && hasPermission(Permissions.RewardsEdit);
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [sort, setSort] = useState<SortState>({ id: 'created', desc: true });
  const categories = useCategories();
  const actions = useCampaignActions();

  const params = {
    search,
    status: filters.status,
    categoryId: filters.category,
    page,
    pageSize,
    sort: SORT_KEYS[sort.id] ?? 'newest',
    desc: sort.id === 'created' ? undefined : sort.desc,
  };
  const query = useQuery({
    queryKey: qk.campaigns(params),
    queryFn: () => api.get<PagedResult<AdminCampaignListItem>>('/admin/campaigns', { query: params }),
    placeholderData: keepPreviousData,
  });

  const columns: DataTableColumn<AdminCampaignListItem>[] = [
    {
      id: 'title',
      header: 'Campaign',
      sortable: true,
      primary: true,
      cell: (row) => (
        <span className="stack mg-stack-xs mg-campaign-cell">
          <Link className="ui-link mg-strong" to={`/manage/campaigns/${row.id}`}>
            {row.title}
          </Link>
          <span className="cluster mg-cluster-sm text-small text-muted">
            {row.category && <span>{row.category.name}</span>}
            {row.visibility === 'InviteOnly' && <Badge size="sm">Invite only</Badge>}
            <span>{row.platforms.join(', ')}</span>
          </span>
        </span>
      ),
    },
    { id: 'status', header: 'Status', cell: (row) => <StatusBadge kind="campaign" status={row.status} /> },
    {
      id: 'startsAt',
      header: 'Runs',
      sortable: true,
      nowrap: true,
      cell: (row) => (
        <span className="stack mg-stack-xs text-small">
          <DateTime value={row.startsAt} format="date" />
          <span className="text-muted">
            to <DateTime value={row.endsAt} format="date" />
          </span>
        </span>
      ),
    },
    {
      id: 'deadline',
      header: 'Deadline',
      sortable: true,
      nowrap: true,
      hideOnMobile: true,
      cell: (row) => <DateTime value={row.submissionDeadline} format="date" />,
    },
    {
      id: 'submissions',
      header: 'Submissions',
      cell: (row) => (
        <span className="mg-counts">
          <span className="mg-strong">{row.submissions.total} submitted</span>
          <Badge size="sm" tone="neutral">
            {row.submissions.pending} pending
          </Badge>
          <Badge size="sm" tone="success">
            {row.submissions.approved} approved
          </Badge>
          <Badge size="sm" tone="danger">
            {row.submissions.rejected} rejected
          </Badge>
        </span>
      ),
    },
    { id: 'spend', header: 'Spend vs budget', cell: (row) => <SpendCell row={row} /> },
    {
      id: 'created',
      header: 'Created',
      sortable: true,
      nowrap: true,
      hideOnMobile: true,
      cell: (row) => <DateTime value={row.createdAt} format="relative" />,
    },
  ];

  const data = query.data;
  const statusOptions = CAMPAIGN_STATUSES.map((s) => ({ value: s, label: s }));
  const categoryOptions = (categories.data ?? []).map((c) => ({ value: c.id, label: c.name }));

  return (
    <>
      <PageHeader
        title="Campaigns"
        description="Create, schedule and run sharing campaigns."
        actions={
          canCreate ? (
            <ButtonLink to="/manage/campaigns/new" leadingIcon={<Plus />}>
              New campaign
            </ButtonLink>
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
          searchLabel="Search campaigns"
          searchPlaceholder="Search by title or slug…"
          filters={[
            { id: 'status', label: 'Status', options: statusOptions },
            { id: 'category', label: 'Category', options: categoryOptions },
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
              caption="Campaigns"
              columns={columns}
              rows={data?.items ?? []}
              getRowId={(r) => r.id}
              rowLabel={(r) => r.title}
              loading={query.isLoading}
              sort={sort}
              onSortChange={(s) => {
                setSort(s);
                setPage(1);
              }}
              rowActions={(row) => actions.menuItems(row, { edit: `/manage/campaigns/${row.id}` })}
              emptyState={
                <EmptyState
                  icon={<Megaphone />}
                  headingLevel={2}
                  title={
                    search || filters.status || filters.category ? 'No campaigns match' : 'No campaigns yet'
                  }
                  description={
                    search || filters.status || filters.category
                      ? 'Try another search or clear the filters.'
                      : 'Create your first campaign to start inviting participants.'
                  }
                  action={
                    canCreate ? (
                      <ButtonLink to="/manage/campaigns/new" leadingIcon={<Plus />}>
                        New campaign
                      </ButtonLink>
                    ) : undefined
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
      {actions.dialog}
    </>
  );
}
