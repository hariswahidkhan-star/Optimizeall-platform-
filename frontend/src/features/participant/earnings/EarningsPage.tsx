import { CircleHelp, Wallet } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DataTable, type DataTableColumn } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Select } from '@/components/ui/Select';
import { Stat } from '@/components/ui/Stat';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { statusOptions } from '@/components/ui/statusMap';
import { Tooltip } from '@/components/ui/Tooltip';
import { useAuth } from '@/lib/auth/useAuth';
import { useEarnings, useEarningsSummary, useSubmissions } from '../api/queries';
import type { Earning, EarningsSummary } from '../api/types';
import { NextPayoutCard } from '../home/HomeSections';
import { earningTypeLabel, earningTypeOptions } from '../lib/labels';
import { addDays, zonedLocalToUtcIso } from '../lib/zonedTime';
import '../participant.css';

/** Plain-language definitions of each balance bucket (PAYOUTS.md §1 "Balance buckets"). */
export const BUCKETS = [
  {
    key: 'pending',
    label: 'Pending',
    help: 'Estimated rewards for posts still being reviewed, plus earnings waiting for a live check or bonus approval. Not guaranteed yet.',
  },
  {
    key: 'approved',
    label: 'Approved',
    help: 'Confirmed earnings that haven’t been added to a payout yet. New approvals are on hold for a few days before they can be paid.',
  },
  {
    key: 'scheduled',
    label: 'Scheduled for payment',
    help: 'Earnings included in a payout batch that finance is preparing or paying.',
  },
  { key: 'paid', label: 'Paid', help: 'Earnings paid out to you (after any recovered reversals).' },
  {
    key: 'reversed',
    label: 'Reversed',
    help: 'Earnings cancelled after approval — for example when a post was removed before the required live time.',
  },
  {
    key: 'lifetimeEarned',
    label: 'Lifetime earned',
    help: 'Everything approved, scheduled and paid to date, net of reversals.',
  },
] as const;

function BucketHelp({ label, help }: { label: string; help: string }) {
  return (
    <Tooltip content={help}>
      <button type="button" className="pp-help" aria-label={`What is “${label}”?`}>
        <CircleHelp aria-hidden="true" />
      </button>
    </Tooltip>
  );
}

type Bucket = (typeof BUCKETS)[number];
const TOP_BUCKETS = BUCKETS.filter((b) => b.key === 'approved' || b.key === 'lifetimeEarned');
const REST_BUCKETS = BUCKETS.filter((b) => b.key !== 'approved' && b.key !== 'lifetimeEarned');

function BucketStat({
  bucket: b,
  summary,
  feature,
}: {
  bucket: Bucket;
  summary: EarningsSummary;
  feature?: boolean;
}) {
  const c = summary.currency;
  return (
    <Stat
      className={feature ? 'pp-balance__feature' : undefined}
      label={
        <span>
          {b.label} <BucketHelp label={b.label} help={b.help} />
        </span>
      }
      icon={feature ? <Wallet /> : undefined}
      measurement={b.key === 'pending' ? 'Estimated' : undefined}
      value={<Money amount={summary[b.key]} currency={c} />}
      hint={
        b.key === 'approved' && summary.onHold > 0 ? (
          <>
            <Money amount={summary.onHold} currency={c} /> on hold until its hold period ends
          </>
        ) : b.key === 'approved' ? (
          <>
            <Money amount={summary.availableForNextPayout} currency={c} /> available for the next payout
          </>
        ) : undefined
      }
    />
  );
}

export function BalanceBuckets({ summary }: { summary: EarningsSummary }) {
  const c = summary.currency;
  const otherPending = summary.pendingByCurrency.filter((p) => p.currency !== c || !p.converted);
  return (
    <section aria-labelledby="buckets-title" className="pp-section">
      <div className="pp-balance">
        <div className="pp-balance__head">
          <div>
            <h2 id="buckets-title" className="pp-balance__title">
              Your balance
            </h2>
            <p className="pp-balance__sub">All amounts in {c}, your settlement currency.</p>
          </div>
        </div>
        <div className="pp-balance__top">
          {TOP_BUCKETS.map((b) => (
            <BucketStat key={b.key} bucket={b} summary={summary} feature />
          ))}
        </div>
        <div className="pp-balance__grid pp-balance__grid--four">
          {REST_BUCKETS.map((b) => (
            <BucketStat key={b.key} bucket={b} summary={summary} />
          ))}
        </div>
      </div>

      {otherPending.length > 0 && (
        <Alert tone="neutral" title="Pending in other currencies">
          <ul>
            {otherPending.map((p) => (
              <li key={p.currency}>
                <Money amount={p.amount} currency={p.currency} />{' '}
                {p.converted ? '(converted and included above)' : '(not converted yet — not included above)'}
              </li>
            ))}
          </ul>
        </Alert>
      )}

      {(summary.byCurrency.length > 1 || summary.byCurrency.some((b) => b.currency !== c)) && (
        <Card as="section" aria-labelledby="by-currency-title" flat>
          <CardHeader
            titleId="by-currency-title"
            headingLevel={3}
            title="By original currency"
            description="Totals of your ledger entries in the currency they were earned in."
          />
          <CardBody>
            <DataTable
              caption="Earnings by currency"
              rows={summary.byCurrency}
              getRowId={(r) => r.currency}
              columns={[
                { id: 'currency', header: 'Currency', primary: true, cell: (r) => r.currency },
                {
                  id: 'pending',
                  header: 'Pending approval',
                  align: 'right',
                  cell: (r) => <Money amount={r.pendingApproval} currency={r.currency} />,
                },
                {
                  id: 'approved',
                  header: 'Approved',
                  align: 'right',
                  cell: (r) => <Money amount={r.approved} currency={r.currency} />,
                },
                {
                  id: 'scheduled',
                  header: 'Scheduled',
                  align: 'right',
                  cell: (r) => <Money amount={r.scheduled} currency={r.currency} />,
                },
                {
                  id: 'paid',
                  header: 'Paid',
                  align: 'right',
                  cell: (r) => <Money amount={r.paid} currency={r.currency} />,
                },
                {
                  id: 'reversed',
                  header: 'Reversed',
                  align: 'right',
                  cell: (r) => <Money amount={r.reversed} currency={r.currency} />,
                },
              ]}
            />
          </CardBody>
        </Card>
      )}

      <details className="ui-card ui-card--flat pp-pad">
        <summary className="ui-link" style={{ cursor: 'pointer' }}>
          What do these balances mean?
        </summary>
        <dl className="pp-legend" style={{ marginTop: 'var(--space-4)' }}>
          {BUCKETS.map((b) => (
            <div key={b.key}>
              <dt>{b.label}</dt>
              <dd>{b.help}</dd>
            </div>
          ))}
        </dl>
      </details>
    </section>
  );
}

const PAGE_SIZE = 25;

const columns: DataTableColumn<Earning>[] = [
  {
    id: 'description',
    header: 'Earning',
    primary: true,
    cell: (e) => (
      <span className="stack" style={{ ['--stack-gap' as string]: '2px' }}>
        <span style={{ fontWeight: 600 }}>{e.description || earningTypeLabel(e.type)}</span>
        {e.campaign && <span className="text-small pp-muted">{e.campaign.title}</span>}
        {e.reason && <span className="text-small">Reason: {e.reason}</span>}
        {e.submissionId && (
          <Link to={`/app/submissions/${e.submissionId}`} className="ui-link text-small">
            View submission
          </Link>
        )}
      </span>
    ),
  },
  { id: 'date', header: 'Date', nowrap: true, cell: (e) => <DateTime value={e.createdAt} format="date" /> },
  { id: 'type', header: 'Type', cell: (e) => earningTypeLabel(e.type) },
  { id: 'status', header: 'Status', cell: (e) => <StatusBadge kind="earning" status={e.status} /> },
  {
    id: 'original',
    header: 'Original amount',
    align: 'right',
    cell: (e) => <Money amount={e.originalAmount} currency={e.originalCurrency} />,
  },
  {
    id: 'rate',
    header: 'Exchange rate',
    align: 'right',
    cell: (e) =>
      e.originalCurrency === e.settlementCurrency ? (
        <span className="pp-muted">—</span>
      ) : (
        <span
          className="tabular"
          title={`1 ${e.originalCurrency} = ${e.exchangeRate} ${e.settlementCurrency}`}
        >
          {e.exchangeRate}
        </span>
      ),
  },
  {
    id: 'settlement',
    header: 'Settlement amount',
    align: 'right',
    cell: (e) => <Money amount={e.settlementAmount} currency={e.settlementCurrency} colored />,
  },
  {
    id: 'rule',
    header: 'Rule version',
    align: 'center',
    cell: (e) => (e.ruleSetVersion ? `v${e.ruleSetVersion}` : '—'),
  },
  {
    id: 'available',
    header: 'Available / paid',
    nowrap: true,
    cell: (e) =>
      e.paidAt ? (
        <span>
          Paid <DateTime value={e.paidAt} format="date" />
        </span>
      ) : e.availableAt ? (
        <span>
          From <DateTime value={e.availableAt} format="date" />
        </span>
      ) : (
        '—'
      ),
  },
];

interface LedgerFilters {
  type?: string;
  status?: string;
  campaignId?: string;
  from?: string;
  to?: string;
}

function LedgerSection() {
  const { user } = useAuth();
  const [filters, setFilters] = useState<LedgerFilters>({});
  const [page, setPage] = useState(1);
  const campaigns = useSubmissions({ page: 1, pageSize: 200 });
  const campaignOptions = useMemo(() => {
    const seen = new Map<string, string>();
    for (const s of campaigns.data?.items ?? []) seen.set(s.campaign.id, s.campaign.title);
    return [...seen]
      .map(([value, label]) => ({ value, label }))
      .sort((a, b) => a.label.localeCompare(b.label));
  }, [campaigns.data]);

  const zone = user?.timeZone;
  const list = useEarnings({
    type: filters.type,
    status: filters.status,
    campaignId: filters.campaignId,
    from: filters.from ? (zonedLocalToUtcIso(filters.from, zone) ?? undefined) : undefined,
    // `to` is exclusive: the day after the chosen end date, at local midnight.
    to: filters.to ? (zonedLocalToUtcIso(addDays(filters.to, 1), zone) ?? undefined) : undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  const set = (key: keyof LedgerFilters, value: string) => {
    setFilters((f) => ({ ...f, [key]: value || undefined }));
    setPage(1);
  };
  const active = Object.values(filters).some(Boolean);

  return (
    <section aria-labelledby="ledger-title" className="pp-section">
      <h2 id="ledger-title" className="pp-section__title">
        Earnings history
      </h2>
      <Card flat>
        <div className="pp-filters" role="group" aria-label="Filter earnings">
          <FormField label="Type" id="ledger-type">
            <Select
              value={filters.type ?? ''}
              placeholder="All types"
              options={earningTypeOptions}
              onChange={(e) => set('type', e.target.value)}
            />
          </FormField>
          <FormField label="Status" id="ledger-status">
            <Select
              value={filters.status ?? ''}
              placeholder="All statuses"
              options={statusOptions('earning')}
              onChange={(e) => set('status', e.target.value)}
            />
          </FormField>
          <FormField label="Campaign" id="ledger-campaign">
            <Select
              value={filters.campaignId ?? ''}
              placeholder="All campaigns"
              options={campaignOptions}
              onChange={(e) => set('campaignId', e.target.value)}
            />
          </FormField>
          <FormField label="From" id="ledger-from">
            <Input
              type="date"
              value={filters.from ?? ''}
              max={filters.to}
              onChange={(e) => set('from', e.target.value)}
            />
          </FormField>
          <FormField label="To" id="ledger-to">
            <Input
              type="date"
              value={filters.to ?? ''}
              min={filters.from}
              onChange={(e) => set('to', e.target.value)}
            />
          </FormField>
          {active && (
            <div className="pp-filters__footer">
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  setFilters({});
                  setPage(1);
                }}
              >
                Clear filters
              </Button>
            </div>
          )}
        </div>
      </Card>
      {list.isError ? (
        <Card flat>
          <ErrorState
            error={list.error}
            title="Your earnings couldn’t be loaded"
            onRetry={() => void list.refetch()}
          />
        </Card>
      ) : (
        <>
          <DataTable
            caption="Earnings history"
            columns={columns}
            rows={list.data?.items ?? []}
            getRowId={(e) => e.id}
            loading={list.isPending}
            emptyState={
              <EmptyState
                headingLevel={3}
                icon={<Wallet />}
                title={active ? 'No earnings match these filters' : 'No earnings yet'}
                description={
                  active
                    ? 'Try a wider date range or another filter.'
                    : 'Earnings appear here when your posts are approved.'
                }
              />
            }
          />
          {list.data && list.data.total > PAGE_SIZE && (
            <Pagination
              page={page}
              pageSize={PAGE_SIZE}
              total={list.data.total}
              onPageChange={setPage}
              label="Earnings pages"
            />
          )}
        </>
      )}
    </section>
  );
}

export function EarningsPage() {
  const summary = useEarningsSummary();
  return (
    <div className="pp-page">
      <PageHeader
        title="Earnings"
        description="What you’ve earned, what’s still being checked, and what’s been paid."
        actions={
          <Link to="/app/payouts" className="ui-link">
            Payout history
          </Link>
        }
      />
      {summary.isPending && (
        <div className="pp-stats" aria-busy="true">
          {BUCKETS.map((b) => (
            <Stat key={b.key} label={b.label} value={null} loading />
          ))}
        </div>
      )}
      {summary.isError && (
        <Card flat>
          <ErrorState
            error={summary.error}
            title="Your balance couldn’t be loaded"
            onRetry={() => void summary.refetch()}
          />
        </Card>
      )}
      {summary.isSuccess && (
        <>
          <BalanceBuckets summary={summary.data} />
          <NextPayoutCard summary={summary.data} />
        </>
      )}
      <LedgerSection />
    </div>
  );
}
