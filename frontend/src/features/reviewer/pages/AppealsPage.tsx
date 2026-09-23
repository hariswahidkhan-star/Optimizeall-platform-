import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Scale } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  Badge,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  FilterBar,
  PageHeader,
  Pagination,
  StatusBadge,
  type DataTableColumn,
  type Tone,
} from '@/components/ui';
import { useAuth } from '@/lib/auth/useAuth';
import { truncate } from '@/lib/format/text';
import { reviewApi, reviewKeys } from '../api/reviewApi';
import type { AppealListItem, AppealStatus } from '../api/types';

export const APPEAL_TONE: Record<AppealStatus, Tone> = {
  Open: 'info',
  Upheld: 'neutral',
  Overturned: 'success',
  Withdrawn: 'neutral',
};

const STATUSES: AppealStatus[] = ['Open', 'Upheld', 'Overturned', 'Withdrawn'];

export function AppealsPage() {
  const [params, setParams] = useSearchParams();
  const raw = params.get('status');
  const status: AppealStatus = STATUSES.includes(raw as AppealStatus) ? (raw as AppealStatus) : 'Open';
  const page = Math.max(1, Number(params.get('page')) || 1);
  const { user } = useAuth();

  const list = useQuery({
    queryKey: reviewKeys.appeals(status, page),
    queryFn: ({ signal }) => reviewApi.appeals(status, page, signal),
    placeholderData: keepPreviousData,
  });

  const columns: DataTableColumn<AppealListItem>[] = [
    {
      id: 'appeal',
      header: 'Appeal',
      primary: true,
      cell: (a) => (
        <div className="rv-cell-main">
          <Link to={`/review/appeals/${a.id}`} className="ui-link">
            {a.campaign.title}
          </Link>
          <span className="text-small text-muted">{a.participant.displayName}</span>
        </div>
      ),
    },
    { id: 'status', header: 'Status', cell: (a) => <Badge tone={APPEAL_TONE[a.status]}>{a.status}</Badge> },
    {
      id: 'against',
      header: 'Against',
      cell: (a) => <StatusBadge kind="submission" status={a.decisionAppealed} size="sm" />,
    },
    {
      id: 'decider',
      header: 'Original decision by',
      cell: (a) =>
        a.originalDecidedBy ? (
          <span className="rv-inline">
            {a.originalDecidedBy.displayName}
            {a.originalDecidedBy.id === user?.id && (
              <Badge size="sm" tone="warning">
                You
              </Badge>
            )}
          </span>
        ) : (
          <span className="text-muted">—</span>
        ),
    },
    { id: 'reason', header: 'Reason', hideOnMobile: true, cell: (a) => truncate(a.reason, 90) },
    { id: 'filed', header: 'Filed', cell: (a) => <DateTime value={a.createdAt} format="relative" /> },
  ];

  const data = list.data;
  return (
    <>
      <PageHeader
        title="Appeals"
        description="Participants can appeal rejections and reversals. An appeal must be resolved by someone other than the reviewer who made the original decision."
        breadcrumbs={[{ label: 'Review', to: '/review' }, { label: 'Appeals' }]}
      />
      <div className="stack">
        <FilterBar
          filters={[
            {
              id: 'status',
              label: 'Status',
              allLabel: 'Open',
              options: STATUSES.filter((s) => s !== 'Open').map((s) => ({ value: s, label: s })),
            },
          ]}
          values={{ status: status === 'Open' ? undefined : status }}
          onFilterChange={(_id, value) => setParams(value ? { status: value } : {}, { replace: true })}
        />
        {list.isError && !data ? (
          <ErrorState error={list.error} onRetry={() => void list.refetch()} retrying={list.isFetching} />
        ) : (
          <DataTable
            caption={`${status} appeals, oldest first`}
            columns={columns}
            rows={data?.items ?? []}
            getRowId={(a) => a.id}
            loading={list.isPending}
            emptyState={
              <EmptyState
                icon={<Scale />}
                headingLevel={2}
                title={status === 'Open' ? 'No open appeals' : `No ${status.toLowerCase()} appeals`}
              />
            }
          />
        )}
        {data && data.total > data.pageSize && (
          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            total={data.total}
            onPageChange={(p) => {
              const next = new URLSearchParams(params);
              next.set('page', String(p));
              setParams(next);
            }}
          />
        )}
      </div>
    </>
  );
}
