import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Inbox, RefreshCw, UserPlus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Badge,
  Button,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  PageHeader,
  Pagination,
  Select,
  StatusBadge,
  useToast,
  type DataTableColumn,
  type FilterDefinition,
  type SortState,
} from '@/components/ui';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { pluralize } from '@/lib/format/text';
import { describeReviewError } from '../api/errors';
import { reviewApi, reviewKeys } from '../api/reviewApi';
import { SOCIAL_PLATFORMS, type ReviewQueueItem } from '../api/types';
import { ActionError } from '../components/common';
import { FlagChips, RiskBadge } from '../components/risk';
import { useClaim, workspacePath, type WorkspaceState } from '../hooks/useReviewActions';
import { parseQueueParams, withQueueParam } from '../queueParams';

export const QUEUE_REFRESH_MS = 30_000;

function ClaimCell({ item, myId }: { item: ReviewQueueItem; myId?: string }) {
  const mine = item.claimedBy?.id === myId;
  const assigned = item.assignedReviewer;
  return (
    <span className="rv-claim-cell">
      {item.claimedBy ? (
        <>
          <Badge size="sm" tone={mine ? 'brand' : 'info'}>
            {mine ? 'You' : item.claimedBy.displayName}
          </Badge>
          {item.claimExpiresAt && (
            <span className="text-small text-muted">
              expires <DateTime value={item.claimExpiresAt} format="relative" />
            </span>
          )}
        </>
      ) : (
        <span className="text-muted text-small">Unclaimed</span>
      )}
      {assigned && (
        <span className="text-small">Assigned: {assigned.id === myId ? 'you' : assigned.displayName}</span>
      )}
    </span>
  );
}

function AssignDialog({
  open,
  onClose,
  ids,
  onDone,
}: {
  open: boolean;
  onClose: () => void;
  ids: string[];
  onDone: () => void;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [reviewerId, setReviewerId] = useState('');
  const [touched, setTouched] = useState(false);
  const reviewers = useQuery({
    queryKey: reviewKeys.reviewers(),
    queryFn: ({ signal }) => reviewApi.reviewers(signal),
    enabled: open,
  });
  const assign = useMutation({
    mutationFn: () => reviewApi.assign(ids, reviewerId),
    onSuccess: (result) => {
      const name = reviewers.data?.find((r) => r.id === reviewerId)?.displayName ?? 'the reviewer';
      toast.success(
        `Assigned ${pluralize(result.updated, 'submission')} to ${name}`,
        result.skippedIds.length > 0
          ? `${result.skippedIds.length} skipped because they are no longer open.`
          : undefined,
      );
      void queryClient.invalidateQueries({ queryKey: reviewKeys.queueRoot() });
      void queryClient.invalidateQueries({ queryKey: reviewKeys.reviewers() });
      onDone();
      onClose();
    },
    onError: (error) => toast.error(describeReviewError(error).title, describeReviewError(error).description),
  });

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!assign.isPending}
      title="Assign to a reviewer"
      description={`${pluralize(ids.length, 'selected submission')}. Only open submissions are assigned.`}
      icon={<UserPlus />}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={assign.isPending}>
            Cancel
          </Button>
          <Button
            loading={assign.isPending}
            onClick={() => {
              setTouched(true);
              if (reviewerId) assign.mutate();
            }}
          >
            Assign
          </Button>
        </>
      }
    >
      {reviewers.isError ? (
        <ErrorState compact error={reviewers.error} onRetry={() => void reviewers.refetch()} />
      ) : (
        <div className="stack">
          <FormField label="Reviewer" required error={touched && !reviewerId ? 'Choose a reviewer.' : null}>
            <Select
              value={reviewerId}
              onChange={(e) => setReviewerId(e.target.value)}
              disabled={reviewers.isPending}
              placeholder={reviewers.isPending ? 'Loading reviewers…' : 'Choose a reviewer'}
              options={(reviewers.data ?? []).map((r) => ({
                value: r.id,
                label: `${r.displayName} — ${r.assignedOpen} open, ${r.decisionsToday} today`,
              }))}
            />
          </FormField>
          {assign.isError && <ActionError error={assign.error} />}
        </div>
      )}
    </Dialog>
  );
}

export function QueuePage() {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => parseQueueParams(params), [params]);
  const navigate = useNavigate();
  const toast = useToast();
  const { user, hasPermission } = useAuth();
  const canAssign = hasPermission(Permissions.ReviewAssign);
  const [selected, setSelected] = useState<string[]>([]);
  const [assignOpen, setAssignOpen] = useState(false);
  const [claimError, setClaimError] = useState<unknown>(null);

  const queue = useQuery({
    queryKey: reviewKeys.queue(filters),
    queryFn: ({ signal }) => reviewApi.queue(filters, signal),
    placeholderData: keepPreviousData,
    refetchInterval: QUEUE_REFRESH_MS,
    refetchIntervalInBackground: false,
  });

  // Reviewers can't list campaigns (campaigns.manage), so campaign filter options come from the open queue itself.
  const campaigns = useQuery({
    queryKey: reviewKeys.queueCampaigns(),
    queryFn: ({ signal }) => reviewApi.queue({ pageSize: 200 }, signal),
    staleTime: 5 * 60_000,
    select: (page) => {
      const seen = new Map<string, string>();
      for (const item of page.items) seen.set(item.campaign.id, item.campaign.title);
      return [...seen]
        .map(([value, label]) => ({ value, label }))
        .sort((a, b) => a.label.localeCompare(b.label));
    },
  });

  const claim = useClaim();
  const queueSearch = params.toString();

  const review = (item: ReviewQueueItem) => {
    setClaimError(null);
    claim.mutate(item.id, {
      onSuccess: () => navigate(workspacePath(item.id), { state: { queueSearch } satisfies WorkspaceState }),
      onError: (error) => {
        setClaimError(error);
        const info = describeReviewError(error);
        toast.error(info.title, info.description);
      },
    });
  };

  const setParam = (key: string, value: string | undefined) => {
    setSelected([]);
    setParams(withQueueParam(params, key, value), { replace: key !== 'page' });
  };

  const campaignOptions = useMemo(() => {
    const options = campaigns.data ?? [];
    if (filters.campaignId && !options.some((o) => o.value === filters.campaignId))
      return [...options, { value: filters.campaignId, label: 'Selected campaign' }];
    return options;
  }, [campaigns.data, filters.campaignId]);

  const filterDefs: FilterDefinition[] = [
    {
      id: 'status',
      label: 'Status',
      allLabel: 'Open',
      options: [
        { value: 'Pending', label: 'Pending' },
        { value: 'UnderReview', label: 'Under review' },
      ],
    },
    { id: 'campaignId', label: 'Campaign', options: campaignOptions },
    { id: 'platform', label: 'Platform', options: SOCIAL_PLATFORMS.map((p) => ({ value: p, label: p })) },
    {
      id: 'minRisk',
      label: 'Min risk',
      allLabel: 'Any',
      options: [
        { value: '15', label: '15+ (medium)' },
        { value: '40', label: '40+ (high)' },
        { value: '60', label: '60+' },
        { value: '80', label: '80+' },
      ],
    },
    {
      id: 'flagged',
      label: 'Flags',
      allLabel: 'Any',
      options: [
        { value: 'true', label: 'Flagged only' },
        { value: 'false', label: 'Unflagged only' },
      ],
    },
    { id: 'mine', label: 'Assigned', allLabel: 'Anyone', options: [{ value: '1', label: 'Assigned to me' }] },
  ];
  const filterValues: Record<string, string | undefined> = {
    status: filters.status,
    campaignId: filters.campaignId,
    platform: filters.platform,
    minRisk: filters.minRisk,
    flagged: filters.flagged,
    mine: filters.assignedToMe ? '1' : undefined,
  };

  const sort: SortState =
    filters.sort === 'risk' ? { id: 'risk', desc: true } : { id: 'submitted', desc: false };

  const columns: DataTableColumn<ReviewQueueItem>[] = [
    {
      id: 'submission',
      header: 'Submission',
      primary: true,
      cell: (item) => (
        <div className="rv-cell-main">
          <span className="rv-inline">
            <Link
              to={workspacePath(item.id)}
              state={{ queueSearch } satisfies WorkspaceState}
              className="ui-link"
            >
              {item.campaign.title}
            </Link>
            <StatusBadge kind="submission" status={item.status} size="sm" />
          </span>
          <span className="text-small text-muted">
            {item.platform} · @{item.handle} · {item.participant.displayName} ({item.participant.countryCode})
          </span>
        </div>
      ),
    },
    {
      id: 'risk',
      header: 'Risk',
      sortable: true,
      nowrap: true,
      cell: (item) => <RiskBadge score={item.riskScore} size="sm" />,
    },
    { id: 'flags', header: 'Flags', cell: (item) => <FlagChips flags={item.flagTypes} max={3} /> },
    {
      id: 'claim',
      header: 'Claim',
      cell: (item) => <ClaimCell item={item} myId={user?.id} />,
    },
    {
      id: 'corrections',
      header: 'Corrections',
      align: 'right',
      width: '7rem',
      cell: (item) =>
        item.correctionCount > 0 ? (
          <Badge size="sm" tone="warning">
            {item.correctionCount}
          </Badge>
        ) : (
          <span className="tabular">0</span>
        ),
    },
    {
      id: 'submitted',
      header: 'Submitted',
      sortable: true,
      cell: (item) => <DateTime value={item.submittedAt} format="relative" />,
    },
    {
      id: 'action',
      header: <span className="visually-hidden">Action</span>,
      align: 'right',
      cell: (item) => {
        const heldByOther = !!item.claimedBy && item.claimedBy.id !== user?.id;
        const busy = claim.isPending && claim.variables === item.id;
        const label = item.claimedBy?.id === user?.id ? 'Continue' : 'Review';
        return (
          <Button
            size="sm"
            variant={heldByOther ? 'secondary' : 'primary'}
            loading={busy}
            disabled={claim.isPending && !busy}
            onClick={() => review(item)}
            aria-label={`${label} ${item.campaign.title} by ${item.participant.displayName}`}
          >
            {label}
          </Button>
        );
      },
    },
  ];

  const data = queue.data;
  const hasFilters = Object.values(filterValues).some(Boolean);

  return (
    <>
      <PageHeader
        title="Review queue"
        description="Claim a submission to review it. Claims expire, so release anything you can’t finish."
        breadcrumbs={[{ label: 'Review', to: '/review' }, { label: 'Queue' }]}
        actions={
          <Button
            variant="secondary"
            leadingIcon={<RefreshCw />}
            loading={queue.isFetching && !queue.isPending}
            onClick={() => void queue.refetch()}
          >
            Refresh
          </Button>
        }
      />
      <div className="stack">
        <FilterBar
          filters={filterDefs}
          values={filterValues}
          onFilterChange={setParam}
          onReset={() => {
            setSelected([]);
            setParams(filters.sort === 'risk' ? { sort: 'risk' } : {}, { replace: true });
          }}
          actions={
            <div className="rv-sort">
              <label htmlFor="rv-queue-sort" className="visually-hidden">
                Sort
              </label>
              <Select
                id="rv-queue-sort"
                value={filters.sort}
                onChange={(e) => setParam('sort', e.target.value)}
                options={[
                  { value: 'oldest', label: 'Sort: Oldest first' },
                  { value: 'risk', label: 'Sort: Highest risk' },
                ]}
              />
            </div>
          }
        />

        {claimError !== null && (
          <ActionError
            error={claimError}
            onDismiss={() => setClaimError(null)}
            onRefresh={() => {
              setClaimError(null);
              void queue.refetch();
            }}
            refreshing={queue.isFetching}
          />
        )}

        <p className="text-small text-muted" aria-live="polite">
          {data ? pluralize(data.total, 'submission') : ''}
          {queue.dataUpdatedAt > 0 && (
            <>
              {' · updated '}
              <DateTime value={queue.dataUpdatedAt} format="relative" />
              {' · refreshes every 30 s'}
            </>
          )}
        </p>

        {queue.isError && !data ? (
          <ErrorState error={queue.error} onRetry={() => void queue.refetch()} retrying={queue.isFetching} />
        ) : (
          <DataTable
            caption="Submissions waiting for review"
            columns={columns}
            rows={data?.items ?? []}
            getRowId={(item) => item.id}
            rowLabel={(item) => `${item.campaign.title} by ${item.participant.displayName}`}
            loading={queue.isPending}
            sort={sort}
            onSortChange={(next) => setParam('sort', next.id === 'risk' ? 'risk' : 'oldest')}
            selectable={canAssign}
            selectedIds={selected}
            onSelectionChange={setSelected}
            bulkActions={() => (
              <Button size="sm" leadingIcon={<UserPlus />} onClick={() => setAssignOpen(true)}>
                Assign…
              </Button>
            )}
            emptyState={
              <EmptyState
                icon={<Inbox />}
                headingLevel={2}
                title={hasFilters ? 'No submissions match these filters' : 'The queue is clear'}
                description={
                  hasFilters ? 'Try removing a filter.' : 'New submissions appear here automatically.'
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
            onPageChange={(page) => setParam('page', String(page))}
          />
        )}
      </div>

      {canAssign && (
        <AssignDialog
          open={assignOpen}
          onClose={() => setAssignOpen(false)}
          ids={selected}
          onDone={() => setSelected([])}
        />
      )}
    </>
  );
}
