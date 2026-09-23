import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { BadgeCheck } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  FilterBar,
  PageHeader,
  Pagination,
  StatusBadge,
  statusOptions,
  type DataTableColumn,
} from '@/components/ui';
import { formatNumber } from '@/lib/format/money';
import { reviewApi, reviewKeys, type SocialQueueFilters } from '../api/reviewApi';
import { SOCIAL_PLATFORMS, type ReviewSocialAccount } from '../api/types';

const DEFAULT_STATUS = 'PendingReview';

export function SocialVerificationPage() {
  const [params, setParams] = useSearchParams();
  // `status=all` shows every status; no parameter = the review queue (PendingReview).
  const statusParam = params.get('status');
  const filters: SocialQueueFilters = {
    status: statusParam === 'all' ? undefined : (statusParam ?? DEFAULT_STATUS),
    platform: params.get('platform') ?? undefined,
    search: params.get('search') ?? undefined,
    oldestFirst: params.get('order') !== 'newest',
    page: Math.max(1, Number(params.get('page')) || 1),
  };

  const list = useQuery({
    queryKey: reviewKeys.social(filters),
    queryFn: ({ signal }) => reviewApi.socialAccounts(filters, signal),
    placeholderData: keepPreviousData,
  });

  const update = (key: string, value: string | undefined) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    if (key !== 'page') next.delete('page');
    setParams(next, { replace: key !== 'page' });
  };

  const columns: DataTableColumn<ReviewSocialAccount>[] = [
    {
      id: 'profile',
      header: 'Profile',
      primary: true,
      cell: (a) => (
        <div className="rv-cell-main">
          <Link to={`/review/social-verification/${a.id}`} className="ui-link">
            @{a.handle}
          </Link>
          <span className="text-small text-muted">{a.platform}</span>
        </div>
      ),
    },
    {
      id: 'owner',
      header: 'Owner',
      cell: (a) => (
        <div className="rv-cell-main">
          <span>{a.owner.displayName}</span>
          <span className="text-small text-muted rv-break">{a.owner.email}</span>
        </div>
      ),
    },
    { id: 'age', header: 'Declared age', align: 'right', cell: (a) => `${a.accountAgeDays} d` },
    {
      id: 'followers',
      header: 'Followers',
      align: 'right',
      cell: (a) => <span className="tabular">{formatNumber(a.followerCount)}</span>,
    },
    {
      id: 'status',
      header: 'Status',
      cell: (a) => <StatusBadge kind="socialVerification" status={a.verificationStatus} size="sm" />,
    },
    {
      id: 'updated',
      header: 'Waiting since',
      cell: (a) => <DateTime value={a.updatedAt} format="relative" />,
    },
  ];

  const data = list.data;
  return (
    <>
      <PageHeader
        title="Social verification"
        description="Check that each profile exists, belongs to the participant and matches the declared age and followers."
        breadcrumbs={[{ label: 'Review', to: '/review' }, { label: 'Social verification' }]}
      />
      <div className="stack">
        <FilterBar
          search={filters.search ?? ''}
          onSearchChange={(value) => update('search', value.trim() || undefined)}
          searchPlaceholder="Handle, owner name or email"
          searchLabel="Search profiles"
          filters={[
            {
              id: 'status',
              label: 'Status',
              allLabel: 'Pending review',
              options: [
                { value: 'all', label: 'All statuses' },
                ...statusOptions('socialVerification').filter((o) => o.value !== DEFAULT_STATUS),
              ],
            },
            {
              id: 'platform',
              label: 'Platform',
              options: SOCIAL_PLATFORMS.map((p) => ({ value: p, label: p })),
            },
            {
              id: 'order',
              label: 'Order',
              allLabel: 'Oldest first',
              options: [{ value: 'newest', label: 'Newest first' }],
            },
          ]}
          values={{
            status: statusParam ?? undefined,
            platform: filters.platform,
            order: filters.oldestFirst ? undefined : 'newest',
          }}
          onFilterChange={update}
          onReset={() => setParams({}, { replace: true })}
        />
        {list.isError && !data ? (
          <ErrorState error={list.error} onRetry={() => void list.refetch()} retrying={list.isFetching} />
        ) : (
          <DataTable
            caption="Social profiles"
            columns={columns}
            rows={data?.items ?? []}
            getRowId={(a) => a.id}
            loading={list.isPending}
            emptyState={
              <EmptyState
                icon={<BadgeCheck />}
                headingLevel={2}
                title="No profiles to show"
                description={
                  filters.status === DEFAULT_STATUS
                    ? 'No profile is waiting for verification.'
                    : 'Try another filter.'
                }
              />
            }
          />
        )}
        {data && data.total > data.pageSize && (
          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            total={data.total}
            onPageChange={(p) => update('page', String(p))}
          />
        )}
      </div>
    </>
  );
}
