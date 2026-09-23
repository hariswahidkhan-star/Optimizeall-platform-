import { useQuery } from '@tanstack/react-query';
import { ArrowRight, Megaphone, Plus } from 'lucide-react';
import { Link } from 'react-router-dom';
import {
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  LineChart,
  PageHeader,
  Skeleton,
  Stat,
  StatusBadge,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
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

function pickKpis(data: AnalyticsOverview): Metric[] {
  return KPI_KEYS.flatMap(({ section, key }) => data[section].metrics.filter((m) => m.key === key));
}

export function OverviewPage() {
  const { hasPermission } = useAuth();
  const canAnalytics = hasPermission(Permissions.AnalyticsView);
  const canCreate = hasPermission(Permissions.RewardsEdit);
  const range = defaultRange(30);
  const params = rangeQuery(range);

  const analytics = useQuery({
    queryKey: qk.analytics({ ...params, overview: true }),
    queryFn: () => api.get<AnalyticsOverview>('/analytics/overview', { query: params }),
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
    <>
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
      <div className="stack">
        {canAnalytics && (
          <section aria-labelledby="kpi-title" className="stack mg-stack-sm">
            <h2 id="kpi-title" className="mg-h2">
              Last 30 days
            </h2>
            {analytics.isError ? (
              <ErrorState
                compact
                headingLevel={3}
                error={analytics.error}
                onRetry={() => void analytics.refetch()}
              />
            ) : (
              <div className="mg-stats">
                {analytics.isLoading || !data
                  ? Array.from({ length: 4 }, (_, i) => <Stat key={i} label="Loading" value="" loading />)
                  : pickKpis(data).map((m, i) => (
                      <Stat
                        key={`${m.key}-${m.currency ?? ''}-${i}`}
                        label={m.currency ? `${m.label} (${m.currency})` : m.label}
                        value={<MetricValue metric={m} />}
                        measurement={measurementTag(m.measurement)}
                      />
                    ))}
              </div>
            )}
          </section>
        )}

        <Card as="section" aria-labelledby="active-title">
          <CardHeader
            titleId="active-title"
            headingLevel={2}
            title="Active campaigns"
            actions={
              <ButtonLink to="/manage/campaigns" variant="ghost" size="sm" trailingIcon={<ArrowRight />}>
                All campaigns
              </ButtonLink>
            }
          />
          <CardBody>
            {active.isError ? (
              <ErrorState
                compact
                headingLevel={3}
                error={active.error}
                onRetry={() => void active.refetch()}
              />
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
                  title="Submissions and approvals per day, last 30 days"
                  description={`${data.timeseries.reduce((s, p) => s + p.submissions, 0)} submissions and ${data.timeseries.reduce((s, p) => s + p.approvals, 0)} approvals in the last 30 days.`}
                  labels={data.timeseries.map((p) => p.date)}
                  series={[
                    {
                      id: 'submissions',
                      label: 'Submissions',
                      values: data.timeseries.map((p) => p.submissions),
                    },
                    { id: 'approvals', label: 'Approvals', values: data.timeseries.map((p) => p.approvals) },
                  ]}
                />
              ) : (
                <EmptyState compact headingLevel={3} title="No submissions in the last 30 days" />
              )}
            </CardBody>
          </Card>
        )}
      </div>
    </>
  );
}
