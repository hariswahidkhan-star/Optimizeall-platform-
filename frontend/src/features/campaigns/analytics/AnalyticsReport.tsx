import { Link } from 'react-router-dom';
import {
  Alert,
  BarChart,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  EmptyState,
  LineChart,
  MeasurementTag,
  Money,
  StatusBadge,
  type DataTableColumn,
} from '@/components/ui';
import { formatMoney, formatNumber } from '@/lib/format/money';
import type { AnalyticsOverview, CampaignAnalyticsRow, PlatformAnalyticsRow } from '../api/types';
import { AmountList, MetricSectionCard } from './metrics';

function rate(value: number | null) {
  return value === null ? '—' : `${formatNumber(value, { maximumFractionDigits: 2 })}%`;
}

function HeaderWithTag({ label, tag }: { label: string; tag: 'Measured' | 'Estimated' | 'Count' }) {
  return (
    <span className="mg-th-tag">
      {label} <MeasurementTag measurement={tag} />
    </span>
  );
}

const campaignColumns: DataTableColumn<CampaignAnalyticsRow>[] = [
  {
    id: 'title',
    header: 'Campaign',
    primary: true,
    sortable: true,
    sortValue: (r) => r.title,
    cell: (r) => (
      <Link className="ui-link" to={`/manage/analytics/campaigns/${r.campaignId}`}>
        {r.title}
      </Link>
    ),
  },
  { id: 'status', header: 'Status', cell: (r) => <StatusBadge kind="campaign" status={r.status} /> },
  {
    id: 'submitted',
    header: <HeaderWithTag label="Submitted" tag="Count" />,
    align: 'right',
    sortable: true,
    sortValue: (r) => r.submitted,
    cell: (r) => formatNumber(r.submitted),
  },
  {
    id: 'approved',
    header: <HeaderWithTag label="Approved" tag="Count" />,
    align: 'right',
    sortable: true,
    sortValue: (r) => r.approved,
    cell: (r) => formatNumber(r.approved),
  },
  { id: 'rate', header: 'Approval rate', align: 'right', cell: (r) => rate(r.approvalRate) },
  { id: 'spend', header: 'Spend', align: 'right', cell: (r) => <AmountList amounts={r.spend} /> },
  {
    id: 'cpa',
    header: 'Cost per approved post',
    align: 'right',
    cell: (r) => <AmountList amounts={r.costPerApproved} />,
  },
  {
    id: 'clicks',
    header: <HeaderWithTag label="Clicks (unique)" tag="Measured" />,
    align: 'right',
    sortable: true,
    sortValue: (r) => r.clicks,
    cell: (r) => `${formatNumber(r.clicks)} (${formatNumber(r.uniqueClicks)})`,
  },
  {
    id: 'conversions',
    header: <HeaderWithTag label="Verified conversions" tag="Measured" />,
    align: 'right',
    sortable: true,
    sortValue: (r) => r.verifiedConversions,
    cell: (r) => formatNumber(r.verifiedConversions),
  },
  {
    id: 'reach',
    header: <HeaderWithTag label="Reach" tag="Estimated" />,
    align: 'right',
    sortable: true,
    sortValue: (r) => r.estimatedReach,
    cell: (r) => formatNumber(r.estimatedReach),
  },
];

const platformColumns: DataTableColumn<PlatformAnalyticsRow>[] = [
  { id: 'platform', header: 'Platform', primary: true, cell: (r) => r.platform },
  {
    id: 'submitted',
    header: <HeaderWithTag label="Submitted" tag="Count" />,
    align: 'right',
    cell: (r) => formatNumber(r.submitted),
  },
  {
    id: 'approved',
    header: <HeaderWithTag label="Approved" tag="Count" />,
    align: 'right',
    cell: (r) => formatNumber(r.approved),
  },
  { id: 'rate', header: 'Approval rate', align: 'right', cell: (r) => rate(r.approvalRate) },
  { id: 'spend', header: 'Spend', align: 'right', cell: (r) => <AmountList amounts={r.spend} /> },
  {
    id: 'cpa',
    header: 'Cost per approved post',
    align: 'right',
    cell: (r) => <AmountList amounts={r.costPerApproved} />,
  },
  {
    id: 'reach',
    header: <HeaderWithTag label="Reach" tag="Estimated" />,
    align: 'right',
    cell: (r) => formatNumber(r.estimatedReach),
  },
];

/**
 * The analytics payload rendered section by section, exactly as the API groups it: counted (funnel, posts,
 * spend), estimated (reach) and measured (traffic, conversions) figures are never combined in one card.
 */
export function AnalyticsReport({
  data,
  loading,
}: {
  data: AnalyticsOverview | undefined;
  loading?: boolean;
}) {
  const labels = (data?.timeseries ?? []).map((p) => p.date);
  const spendCurrencies = [...new Set((data?.spendByCampaign ?? []).map((s) => s.currency))];

  return (
    <div className="stack mg-report">
      {data && <p className="text-small text-muted">{data.timeBasis}</p>}

      <div className="mg-report__grid">
        <MetricSectionCard
          section={data?.funnel}
          title="Funnel"
          loading={loading}
          description="Participants moving from registration to a first submission."
        />
        <MetricSectionCard
          section={data?.posts}
          title="Posts"
          loading={loading}
          description="Submissions and review outcomes."
        />
      </div>

      <MetricSectionCard
        section={data?.spend}
        title="Spend"
        loading={loading}
        description="Earnings recorded for approved posts, per currency. Cost per approved post divides spend by approved posts."
      >
        {data && data.spendByCampaign.length > 0 && (
          <div className="stack">
            {spendCurrencies.map((currency) => {
              const rows = data.spendByCampaign.filter((s) => s.currency === currency);
              return (
                <BarChart
                  key={currency}
                  title={`Spend by campaign (${currency})`}
                  description={`Spend on approved posts per campaign in ${currency}.`}
                  data={rows.map((r) => ({ label: r.title, value: r.amount }))}
                  valueLabel={`Spend (${currency})`}
                  valueFormatter={(v) => formatMoney(v, currency)}
                />
              );
            })}
            <DataTable
              caption="Spend by campaign"
              columns={[
                { id: 'title', header: 'Campaign', primary: true, cell: (r) => r.title },
                {
                  id: 'amount',
                  header: 'Spend',
                  align: 'right',
                  cell: (r) => <Money amount={r.amount} currency={r.currency} />,
                },
              ]}
              rows={data.spendByCampaign}
              getRowId={(r) => `${r.campaignId}-${r.currency}`}
            />
          </div>
        )}
      </MetricSectionCard>

      <div className="mg-report__grid">
        <MetricSectionCard
          section={data?.reach}
          title="Reach (estimated)"
          loading={loading}
          description="Estimated from declared follower counts. It is not a measurement of views."
        />
        <MetricSectionCard
          section={data?.traffic}
          title="Traffic (measured)"
          loading={loading}
          description="Clicks recorded by our tracking redirect. Suspected bots are excluded."
        />
      </div>

      <MetricSectionCard
        section={data?.conversions}
        title="Conversions (measured, verified)"
        loading={loading}
        description="Conversions confirmed by a signed postback from the destination site."
      />

      {data && (
        <Card as="section" aria-labelledby="analytics-trend-title">
          <CardHeader titleId="analytics-trend-title" headingLevel={2} title="Over time" />
          <CardBody className="stack">
            {labels.length === 0 ? (
              <EmptyState compact headingLevel={3} title="No activity in this period" />
            ) : (
              <div className="mg-report__grid">
                <LineChart
                  title="Registrations, submissions and approvals per day"
                  description="Daily counts from our records."
                  labels={labels}
                  series={[
                    {
                      id: 'registrations',
                      label: 'Registrations',
                      values: data.timeseries.map((p) => p.registrations),
                    },
                    {
                      id: 'submissions',
                      label: 'Submissions',
                      values: data.timeseries.map((p) => p.submissions),
                    },
                    { id: 'approvals', label: 'Approvals', values: data.timeseries.map((p) => p.approvals) },
                  ]}
                />
                <LineChart
                  title="Tracked clicks per day (measured)"
                  description="Clicks on participant tracking links, bots excluded."
                  labels={labels}
                  series={[
                    { id: 'clicks', label: 'Tracked clicks', values: data.timeseries.map((p) => p.clicks) },
                  ]}
                />
              </div>
            )}
          </CardBody>
        </Card>
      )}

      {data?.platforms && (
        <Card as="section" aria-labelledby="analytics-platforms-title">
          <CardHeader titleId="analytics-platforms-title" headingLevel={2} title="By platform" />
          <CardBody>
            <DataTable
              caption="Performance by platform"
              columns={platformColumns}
              rows={data.platforms}
              getRowId={(r) => r.platform}
              emptyState={<p className="text-muted">No posts on any platform in this period.</p>}
            />
          </CardBody>
        </Card>
      )}

      {data && !data.campaignId && (
        <Card as="section" aria-labelledby="analytics-campaigns-title">
          <CardHeader
            titleId="analytics-campaigns-title"
            headingLevel={2}
            title="By campaign"
            description="Counted, measured and estimated columns are tagged. Select a campaign for its platform breakdown."
          />
          <CardBody>
            <DataTable
              caption="Performance by campaign"
              columns={campaignColumns}
              rows={data.campaigns}
              getRowId={(r) => r.campaignId}
              defaultSort={{ id: 'submitted', desc: true }}
              emptyState={<p className="text-muted">No campaign activity in this period.</p>}
            />
          </CardBody>
        </Card>
      )}
      {data && data.campaignId && data.campaigns.length === 0 && !data.platforms && (
        <Alert tone="info">No activity for this campaign in the selected period.</Alert>
      )}
    </div>
  );
}
