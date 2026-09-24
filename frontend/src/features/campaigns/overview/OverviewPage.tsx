import { useQuery } from '@tanstack/react-query';
import { ArrowRight, Megaphone, Plus, Wallet } from 'lucide-react';
import { Link } from 'react-router-dom';
import {
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  DashboardCell,
  DashboardGrid,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  LineChart,
  PageHeader,
  Skeleton,
  MeterList,
  Stat,
  StatGrid,
  StatusBadge,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { metricTrend, periodDelta, previousRange } from '@/lib/format/delta';
import { formatMoney } from '@/lib/format/money';
import { qk } from '../api/queries';
import type { AdminCampaignListItem, AnalyticsOverview, Metric } from '../api/types';
import { MetricValue, measurementTag } from '../analytics/metrics';
import { defaultRange, rangeQuery } from '../analytics/AnalyticsPage';
import { SpendCell } from '../list/CampaignsListPage';
import '../campaigns.css';

const KPI_KEYS: {
  section: keyof Pick<AnalyticsOverview, 'posts' | 'spend' | 'funnel' | 'traffic'>;
  key: string;
}[] = [
  { section: 'posts', key: 'postsSubmitted' },
  { section: 'posts', key: 'postsApproved' },
  { section: 'posts', key: 'approvalRate' },
  { section: 'spend', key: 'spend' },
  { section: 'spend', key: 'costPerApprovedPost' },
  { section: 'funnel', key: 'registrations' },
  { section: 'traffic', key: 'trackedClicks' },
];

function pickKpis(
  data: AnalyticsOverview,
): { metric: Metric; section: (typeof KPI_KEYS)[number]['section'] }[] {
  return KPI_KEYS.flatMap(({ section, key }) =>
    data[section].metrics.filter((m) => m.key === key).map((metric) => ({ metric, section })),
  );
}

/** Top campaigns by spend in the period's most common currency (amounts are never converted). */
function SpendByCampaign({ data }: { data: AnalyticsOverview }) {
  const byCurrency = new Map<string, number>();
  for (const row of data.spendByCampaign)
    byCurrency.set(row.currency, (byCurrency.get(row.currency) ?? 0) + 1);
  const currency = [...byCurrency.entries()].sort((a, b) => b[1] - a[1])[0]?.[0];
  const rows = data.spendByCampaign
    .filter((r) => r.currency === currency && r.amount > 0)
    .sort((a, b) => b.amount - a.amount)
    .slice(0, 6);
  if (!currency || rows.length === 0)
    return <EmptyState compact headingLevel={3} icon={<Wallet />} title="No spend in the last 30 days" />;
  const top = rows[0]!.amount;
  const others = data.spendByCampaign.filter((r) => r.currency !== currency).length;
  return (
    <>
      <MeterList
        label={`Spend by campaign, ${currency}`}
        tone="accent"
        items={rows.map((r) => ({
          id: r.campaignId,
          label: r.title,
          to: `/manage/campaigns/${r.campaignId}`,
          percent: (r.amount / top) * 100,
          valueText: formatMoney(r.amount, r.currency),
        }))}
      />
      {others > 0 && (
        <p className="mg-note">
          {others} {others === 1 ? 'campaign pays' : 'campaigns pay'} in other currencies — see Analytics.
        </p>
      )}
    </>
  );
}

export function OverviewPage() {
  const { hasPermission } = useAuth();
  const canAnalytics = hasPermission(Permissions.AnalyticsView);
  const canCreate = hasPermission(Permissions.RewardsEdit);
  const range = defaultRange(30);
  const params = rangeQuery(range);
  const previousParams = rangeQuery(previousRange(range.from, range.to));

  const analytics = useQuery({
    queryKey: qk.analytics({ ...params, overview: true }),
    queryFn: () => api.get<AnalyticsOverview>('/analytics/overview', { query: params }),
    enabled: canAnalytics,
  });
  // The same-length window before it, for "vs previous 30 days" deltas.
  const before = useQuery({
    queryKey: qk.analytics({ ...previousParams, overview: true }),
    queryFn: () => api.get<AnalyticsOverview>('/analytics/overview', { query: previousParams }),
    enabled: canAnalytics,
  });
  const active = useQuery({
    queryKey: qk.campaigns({ status: 'Active', overview: true }),
    queryFn: () =>
      api.get<PagedResult<AdminCampaignListItem>>('/admin/campaigns', {
        query: { status: 'Active', pageSize: 10, sort: 'deadline' },
      }),
  });

  const columns: DataTableColumn<AdminCampaignListItem>[] = [
    {
      id: 'title',
      header: 'Campaign',
      primary: true,
      cell: (r) => (
        <Link className="ui-link" to={`/manage/campaigns/${r.id}`}>
          {r.title}
        </Link>
      ),
    },
    { id: 'status', header: 'Status', cell: (r) => <StatusBadge kind="campaign" status={r.status} /> },
    {
      id: 'deadline',
      header: 'Submission deadline',
      cell: (r) => <DateTime value={r.submissionDeadline} format="both" />,
    },
    {
      id: 'pending',
      header: 'Pending review',
      align: 'right',
      cell: (r) => r.submissions.pending,
    },
    { id: 'spend', header: 'Spend vs budget', cell: (r) => <SpendCell row={r} /> },
  ];

  const data = analytics.data;

  return (
    <div className="ui-dash">
      <PageHeader
        title="Campaign manager"
        description="Your campaigns at a glance for the last 30 days."
        actions={
          canCreate ? (
            <ButtonLink to="/manage/campaigns/new" leadingIcon={<Plus />}>
              New campaign
            </ButtonLink>
          ) : undefined
        }
      />
      {canAnalytics && (
        <section aria-labelledby="kpi-title">
          <div className="ui-dash-head">
            <h2 id="kpi-title" className="ui-dash-head__title">
              Last 30 days
            </h2>
            <Link to="/manage/analytics">Open analytics</Link>
          </div>
          {analytics.isError ? (
            <Card flat>
              <ErrorState
                compact
                headingLevel={3}
                error={analytics.error}
                onRetry={() => void analytics.refetch()}
              />
            </Card>
          ) : (
            <StatGrid strip min="250px">
              {analytics.isLoading || !data
                ? Array.from({ length: 4 }, (_, i) => <Stat key={i} label="Loading" value="" loading />)
                : pickKpis(data).map(({ metric: m, section }, i) => (
                    <Stat
                      key={`${m.key}-${m.currency ?? ''}-${i}`}
                      label={m.currency ? `${m.label} (${m.currency})` : m.label}
                      value={<MetricValue metric={m} />}
                      measurement={measurementTag(m.measurement)}
                      delta={periodDelta(m, before.data?.[section].metrics, 'vs previous 30 days')}
                      trend={metricTrend(m.key, m.label, data.timeseries)}
                    />
                  ))}
            </StatGrid>
          )}
        </section>
      )}

      <Card as="section" aria-labelledby="active-title">
        <CardHeader
          titleId="active-title"
          headingLevel={2}
          title="Active campaigns"
          description="Soonest submission deadline first."
          actions={
            <ButtonLink to="/manage/campaigns" variant="ghost" size="sm" trailingIcon={<ArrowRight />}>
              All campaigns
            </ButtonLink>
          }
        />
        <CardBody>
          {active.isError ? (
            <ErrorState compact headingLevel={3} error={active.error} onRetry={() => void active.refetch()} />
          ) : (
            <DataTable
              caption="Active campaigns"
              columns={columns}
              rows={active.data?.items ?? []}
              getRowId={(r) => r.id}
              loading={active.isLoading}
              emptyState={
                <EmptyState
                  compact
                  headingLevel={3}
                  icon={<Megaphone />}
                  title="No active campaigns"
                  description="Publish a draft to start collecting posts."
                />
              }
            />
          )}
        </CardBody>
      </Card>

      {canAnalytics && (
        <DashboardGrid>
          <DashboardCell span={8}>
            <Card as="section" aria-labelledby="volume-title">
              <CardHeader
                titleId="volume-title"
                headingLevel={2}
                title="Recent submissions"
                description="Posts submitted and approved per day (counted from our records)."
              />
              <CardBody>
                {analytics.isLoading ? (
                  <Skeleton height={220} />
                ) : data && data.timeseries.length > 0 ? (
                  <LineChart
                    area
                    title="Submissions and approvals per day, last 30 days"
                    description={`${data.timeseries.reduce((s, p) => s + p.submissions, 0)} submissions and ${data.timeseries.reduce((s, p) => s + p.approvals, 0)} approvals in the last 30 days.`}
                    labels={data.timeseries.map((p) => p.date)}
                    series={[
                      {
                        id: 'submissions',
                        label: 'Submissions',
                        values: data.timeseries.map((p) => p.submissions),
                      },
                      {
                        id: 'approvals',
                        label: 'Approvals',
                        values: data.timeseries.map((p) => p.approvals),
                      },
                    ]}
                  />
                ) : (
                  <EmptyState compact headingLevel={3} title="No submissions in the last 30 days" />
                )}
              </CardBody>
            </Card>
          </DashboardCell>
          <DashboardCell span={4}>
            <Card as="section" aria-labelledby="spend-title">
              <CardHeader
                titleId="spend-title"
                headingLevel={2}
                title="Spend by campaign"
                description="Top campaigns in the last 30 days."
              />
              <CardBody>
                {analytics.isLoading ? (
                  <Skeleton height={220} />
                ) : data ? (
                  <SpendByCampaign data={data} />
                ) : null}
              </CardBody>
            </Card>
          </DashboardCell>
        </DashboardGrid>
      )}
    </div>
  );
}
