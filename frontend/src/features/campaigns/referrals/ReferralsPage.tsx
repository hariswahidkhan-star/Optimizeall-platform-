import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { ShieldAlert, Users, XCircle } from 'lucide-react';
import { useState } from 'react';
import {
  Badge,
  ConfirmDialog,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  FilterBar,
  Money,
  PageHeader,
  Pagination,
  StatusBadge,
  useToast,
  type DataTableColumn,
  type Tone,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { humanize } from '@/lib/format/text';
import { qk } from '../api/queries';
import { REFERRAL_STATUSES, type ReferralAdmin, type ReferralStatus } from '../api/types';
import { fraudSignalLabel } from '../shared/labels';
import '../campaigns.css';

const STATUS_TONES: Record<ReferralStatus, Tone> = {
  Registered: 'info',
  Qualified: 'success',
  Rejected: 'danger',
  Expired: 'neutral',
};

const REWARD_ACTIONS: Record<string, string> = {
  none: 'No reward was affected.',
  declined: 'The pending reward was declined.',
  reversed: 'The approved reward was reversed.',
  unchanged_paid: 'The reward was already paid and was left unchanged.',
  unchanged_scheduled: 'The reward is already scheduled for payout and was left unchanged.',
};

export function ReferralsPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [rejecting, setRejecting] = useState<ReferralAdmin | null>(null);

  const params = { search, status: filters.status, flagged: filters.flagged, page, pageSize: 25 };
  const query = useQuery({
    queryKey: qk.referrals(params),
    queryFn: () => api.get<PagedResult<ReferralAdmin>>('/marketing/referrals', { query: params }),
    placeholderData: keepPreviousData,
  });

  const columns: DataTableColumn<ReferralAdmin>[] = [
    {
      id: 'referred',
      header: 'Referred participant',
      primary: true,
      cell: (r) => (
        <span className="stack mg-stack-xs">
          <span className="mg-strong">{r.referred.displayName}</span>
          <span className="text-small text-muted mg-break">{r.referred.email}</span>
        </span>
      ),
    },
    {
      id: 'referrer',
      header: 'Referrer',
      cell: (r) => (
        <span className="stack mg-stack-xs">
          <span>{r.referrer.displayName}</span>
          <span className="text-small text-muted">
            Code <code>{r.codeUsed}</code>
          </span>
        </span>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      cell: (r) => (
        <span className="stack mg-stack-xs">
          <Badge tone={STATUS_TONES[r.status]}>{r.status}</Badge>
          {r.rejectionReason && <span className="text-small text-muted">{r.rejectionReason}</span>}
        </span>
      ),
    },
    {
      id: 'signals',
      header: 'Fraud signals',
      cell: (r) =>
        r.fraudSignals.length === 0 ? (
          <span className="text-muted">None</span>
        ) : (
          <ul className="mg-chips" aria-label="Fraud signals">
            {r.fraudSignals.map((s) => (
              <li key={s}>
                <Badge tone="warning" size="sm" icon={<ShieldAlert />}>
                  {fraudSignalLabel(s)}
                </Badge>
              </li>
            ))}
          </ul>
        ),
    },
    {
      id: 'reward',
      header: 'Reward',
      cell: (r) =>
        r.rewardAmount !== null && r.rewardCurrency ? (
          <span className="stack mg-stack-xs">
            <Money amount={r.rewardAmount} currency={r.rewardCurrency} />
            {r.rewardStatus && <StatusBadge kind="earning" status={r.rewardStatus} />}
          </span>
        ) : (
          '—'
        ),
    },
    {
      id: 'dates',
      header: 'Registered',
      hideOnMobile: true,
      cell: (r) => (
        <span className="stack mg-stack-xs text-small">
          <DateTime value={r.registeredAt} format="date" />
          <span className="text-muted">
            {r.qualifiedAt ? (
              <>
                Qualified <DateTime value={r.qualifiedAt} format="date" />
              </>
            ) : (
              <>
                Qualify by <DateTime value={r.qualifyBy} format="date" /> ({humanize(r.qualifyingAction)})
              </>
            )}
          </span>
        </span>
      ),
    },
  ];

  return (
    <>
      <PageHeader title="Referrals" description="Referred participants, their rewards and fraud signals." />
      <div className="stack">
        <FilterBar
          search={search}
          onSearchChange={(s) => {
            setSearch(s);
            setPage(1);
          }}
          searchLabel="Search referrals"
          searchPlaceholder="Name, email or code…"
          filters={[
            {
              id: 'status',
              label: 'Status',
              options: REFERRAL_STATUSES.map((s) => ({ value: s, label: s })),
            },
            {
              id: 'flagged',
              label: 'Fraud signals',
              options: [
                { value: 'true', label: 'Flagged' },
                { value: 'false', label: 'Not flagged' },
              ],
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
          }}
        />
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Referrals"
              columns={columns}
              rows={query.data?.items ?? []}
              getRowId={(r) => r.id}
              rowLabel={(r) => `referral of ${r.referred.displayName}`}
              loading={query.isLoading}
              rowActions={(r) => [
                {
                  id: 'reject',
                  label: 'Reject…',
                  icon: <XCircle />,
                  danger: true,
                  disabled: r.status === 'Rejected',
                  onSelect: () => setRejecting(r),
                },
              ]}
              emptyState={<EmptyState icon={<Users />} headingLevel={2} title="No referrals match" />}
            />
            {query.data && query.data.total > 0 && (
              <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />
            )}
          </>
        )}
      </div>
      <ConfirmDialog
        open={!!rejecting}
        onClose={() => setRejecting(null)}
        tone="danger"
        title={rejecting ? `Reject the referral of ${rejecting.referred.displayName}?` : ''}
        description="A pending reward is declined and an approved (unpaid) reward is reversed. Scheduled or paid rewards are left unchanged."
        confirmLabel="Reject referral"
        requireReason
        reasonMinLength={3}
        onConfirm={async ({ reason }) => {
          if (!rejecting) return;
          const result = await api.post<{ id: string; status: string; rewardAction: string }>(
            `/marketing/referrals/${rejecting.id}/reject`,
            { reason },
          );
          toast.success('Referral rejected', REWARD_ACTIONS[result.rewardAction] ?? undefined);
          await queryClient.invalidateQueries({ queryKey: ['manage', 'referrals'] });
        }}
      />
    </>
  );
}
