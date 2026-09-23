import { Check, Clock, X } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  ConfirmDialog,
  DataTable,
  DateTime,
  EmptyState,
  FilterBar,
  Money,
  PageHeader,
  Pagination,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { humanize } from '@/lib/format/text';
import { humanError } from '../api/errors';
import { useDecidePending, usePendingEarnings } from '../api/hooks';
import type { PendingEarning } from '../api/types';
import { Muted, PersonCell, QueryError } from '../components/common';
import { useCan } from '../lib/useCan';

const TYPE_FILTERS = [
  { value: 'QualityBonus', label: 'Quality bonuses' },
  { value: 'ReferralReward', label: 'Referral rewards' },
  { value: 'FirstPostBonus', label: 'First post bonuses' },
  { value: 'TimeLimitedBonus', label: 'Time-limited bonuses' },
  { value: 'PostReward', label: 'Post rewards' },
  { value: 'Adjustment', label: 'Adjustments' },
];

function RiskBadge({ score }: { score: number | null }) {
  if (score === null || score === undefined) return <Muted>—</Muted>;
  const tone = score >= 50 ? 'danger' : score >= 25 ? 'warning' : 'success';
  const label = score >= 50 ? 'High' : score >= 25 ? 'Medium' : 'Low';
  return (
    <Badge tone={tone} title={`Submission risk score ${score}`}>
      {label} · {score}
    </Badge>
  );
}

/** Bonuses and other earnings that need a separate (four-eyes) approval. */
export function ApprovalsPage() {
  const can = useCan();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [type, setType] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [approving, setApproving] = useState<PendingEarning | null>(null);
  const [declining, setDeclining] = useState<PendingEarning | null>(null);
  const query = usePendingEarnings({ search, type, page, pageSize }, can.approveBonus);
  const decide = useDecidePending();

  const blockReason = (e: PendingEarning): string | null => {
    if (e.createdByUserId && e.createdByUserId === can.userId)
      return 'You created this earning, so a different finance user must decide it.';
    return null;
  };

  const columns: DataTableColumn<PendingEarning>[] = [
    {
      id: 'user',
      header: 'Participant',
      primary: true,
      cell: (e) => <PersonCell user={e.user} link={can.viewLedger} />,
    },
    {
      id: 'what',
      header: 'Earning',
      cell: (e) => (
        <span className="fin-stack">
          <span>{humanize(e.type)}</span>
          <Muted>
            {e.description}
            {e.campaign ? ` · ${e.campaign.title}` : ''}
          </Muted>
        </span>
      ),
    },
    {
      id: 'amount',
      header: 'Amount',
      align: 'right',
      cell: (e) => (
        <span className="fin-stack-right">
          <Money amount={e.settlementAmount} currency={e.settlementCurrency} />
          {e.originalCurrency !== e.settlementCurrency && (
            <Muted>
              <Money amount={e.originalAmount} currency={e.originalCurrency} /> original
            </Muted>
          )}
        </span>
      ),
    },
    { id: 'risk', header: 'Risk', cell: (e) => <RiskBadge score={e.submissionRiskScore} /> },
    {
      id: 'live',
      header: 'Live check',
      cell: (e) =>
        e.awaitingLiveCheck ? (
          <Badge tone="info" icon={<Clock />}>
            Waiting{e.liveCheckDueAt ? ' · due ' : ''}
            {e.liveCheckDueAt && <DateTime value={e.liveCheckDueAt} format="relative" />}
          </Badge>
        ) : (
          <Muted>Not required / passed</Muted>
        ),
      hideOnMobile: true,
    },
    {
      id: 'created',
      header: 'Created',
      cell: (e) => <DateTime value={e.createdAt} format="relative" />,
      hideOnMobile: true,
    },
    {
      id: 'actions',
      header: 'Decision',
      cell: (e) => {
        const blocked = blockReason(e);
        const approveBlocked =
          blocked ?? (e.awaitingLiveCheck ? 'Waiting for the post’s live check to pass.' : null);
        const approve = (
          <Button
            size="sm"
            leadingIcon={<Check />}
            disabled={!!approveBlocked}
            aria-describedby={approveBlocked ? `why-${e.id}` : undefined}
            onClick={() => setApproving(e)}
          >
            Approve
            <span className="visually-hidden">
              {' '}
              {e.user.displayName}’s {humanize(e.type)}
            </span>
          </Button>
        );
        return (
          <span className="fin-decision">
            <span className="cluster">
              {approve}
              <Button
                size="sm"
                variant="secondary"
                leadingIcon={<X />}
                disabled={!!blocked}
                onClick={() => setDeclining(e)}
              >
                Decline
                <span className="visually-hidden">
                  {' '}
                  {e.user.displayName}’s {humanize(e.type)}
                </span>
              </Button>
            </span>
            {approveBlocked && (
              <span id={`why-${e.id}`} className="text-muted text-small">
                {approveBlocked}
              </span>
            )}
          </span>
        );
      },
    },
  ];

  if (!can.approveBonus) {
    return (
      <>
        <PageHeader title="Pending approvals" />
        <Alert tone="warning" title="No access">
          Approving bonuses needs the “approve bonus” permission. Ask an administrator if you need it.
        </Alert>
      </>
    );
  }

  return (
    <>
      <PageHeader
        title="Pending approvals"
        description="Quality bonuses, referral rewards and other earnings that need a separate approval. The person who created an earning cannot decide it."
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={search}
            onSearchChange={(v) => {
              setSearch(v);
              setPage(1);
            }}
            searchPlaceholder="Participant or description"
            searchLabel="Search pending earnings"
            filters={[{ id: 'type', label: 'Type', options: TYPE_FILTERS }]}
            values={{ type }}
            onFilterChange={(_, v) => {
              setType(v);
              setPage(1);
            }}
            onReset={() => {
              setSearch('');
              setType(undefined);
              setPage(1);
            }}
          />
          {query.isError ? (
            <QueryError error={query.error} onRetry={() => query.refetch()} />
          ) : (
            <>
              <DataTable
                caption="Pending earnings (oldest first)"
                columns={columns}
                rows={query.data?.items ?? []}
                getRowId={(e) => e.id}
                loading={query.isPending}
                emptyState={
                  <EmptyState
                    compact
                    headingLevel={3}
                    title="Nothing waiting for approval"
                    description="New bonuses and referral rewards will appear here."
                  />
                }
              />
              {query.data && query.data.total > 0 && (
                <Pagination
                  page={page}
                  pageSize={pageSize}
                  total={query.data.total}
                  onPageChange={setPage}
                  onPageSizeChange={(s) => {
                    setPageSize(s);
                    setPage(1);
                  }}
                  label="Pending approvals pages"
                />
              )}
            </>
          )}
        </CardBody>
      </Card>

      <ConfirmDialog
        open={!!approving}
        onClose={() => setApproving(null)}
        title="Approve this earning?"
        description="It becomes payable after the hold period and the participant is notified."
        confirmLabel="Approve"
        onConfirm={async () => {
          if (!approving) return;
          try {
            await decide.mutateAsync({
              id: approving.id,
              decision: 'approve',
              concurrencyStamp: approving.concurrencyStamp,
            });
            toast.success('Earning approved');
          } catch (e) {
            throw humanError(e);
          }
        }}
      >
        {approving && (
          <p>
            {humanize(approving.type)} for {approving.user.displayName}:{' '}
            <Money amount={approving.settlementAmount} currency={approving.settlementCurrency} />
          </p>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={!!declining}
        onClose={() => setDeclining(null)}
        tone="danger"
        title="Decline this earning?"
        description="A declined earning is never payable. The participant is notified."
        requireReason
        reasonMinLength={5}
        confirmLabel="Decline"
        onConfirm={async ({ reason }) => {
          if (!declining) return;
          try {
            await decide.mutateAsync({
              id: declining.id,
              decision: 'decline',
              concurrencyStamp: declining.concurrencyStamp,
              reason,
            });
            toast.success('Earning declined');
          } catch (e) {
            throw humanError(e);
          }
        }}
      >
        {declining && (
          <p>
            {humanize(declining.type)} for {declining.user.displayName}:{' '}
            <Money amount={declining.settlementAmount} currency={declining.settlementCurrency} />
          </p>
        )}
      </ConfirmDialog>
    </>
  );
}
