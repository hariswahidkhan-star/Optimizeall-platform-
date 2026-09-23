import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Download } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Alert,
  BarChart,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Stat,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatNumber } from '@/lib/format/money';
import { qk, useCampaignOptions } from '../api/queries';
import type { AnalyticsOverview, RetentionSummary, SocialPlatform, TrackingSummary } from '../api/types';
import { platformOptions, retentionKindLabel } from '../shared/labels';
import { AmountList } from './metrics';
import { AnalyticsReport } from './AnalyticsReport';
import '../campaigns.css';

export interface AnalyticsFilters {
  from: string;
  to: string;
  campaignId: string;
  platform: string;
}

function dateKey(d: Date) {
  return d.toISOString().slice(0, 10);
}

export function defaultRange(days = 30): { from: string; to: string } {
  const to = new Date();
  const from = new Date(to.getTime() - (days - 1) * 24 * 3600_000);
  return { from: dateKey(from), to: dateKey(to) };
}

/** Date inputs are whole UTC days (the API reports in UTC). */
export function rangeQuery(filters: Pick<AnalyticsFilters, 'from' | 'to'>) {
  return {
    from: filters.from ? `${filters.from}T00:00:00Z` : undefined,
    to: filters.to ? `${filters.to}T23:59:59Z` : undefined,
  };
}

function FilterControls({
  filters,
  onChange,
  showCampaign = true,
}: {
  filters: AnalyticsFilters;
  onChange: (next: AnalyticsFilters) => void;
  showCampaign?: boolean;
}) {
  const campaigns = useCampaignOptions();
  const invalid = filters.from && filters.to && filters.from > filters.to;
  return (
    <Card flat>
      <CardBody className="stack mg-stack-sm">
        <div className="mg-grid mg-grid--4">
          <FormField label="From (UTC)" error={invalid ? 'Start after end.' : undefined}>
            <Input
              type="date"
              value={filters.from}
              max={filters.to || undefined}
              onChange={(e) => onChange({ ...filters, from: e.target.value })}
            />
          </FormField>
          <FormField label="To (UTC)">
            <Input
              type="date"
              value={filters.to}
              min={filters.from || undefined}
              onChange={(e) => onChange({ ...filters, to: e.target.value })}
            />
          </FormField>
          {showCampaign && (
            <FormField label="Campaign">
              <Select
                value={filters.campaignId}
                options={[
                  { value: '', label: 'All campaigns' },
                  ...(campaigns.data?.items ?? []).map((c) => ({ value: c.id, label: c.title })),
                ]}
                onChange={(e) => onChange({ ...filters, campaignId: e.target.value })}
              />
            </FormField>
          )}
          <FormField label="Platform">
            <Select
              value={filters.platform}
              options={[{ value: '', label: 'All platforms' }, ...platformOptions]}
              onChange={(e) => onChange({ ...filters, platform: e.target.value })}
            />
          </FormField>
        </div>
        <p className="text-small text-muted">Ranges are limited to 366 days.</p>
      </CardBody>
    </Card>
  );
}

export function AnalyticsPage() {
  const { hasPermission } = useAuth();
  const canMarketing = hasPermission(Permissions.MarketingManage);
  const toast = useToast();
  const [filters, setFilters] = useState<AnalyticsFilters>({
    ...defaultRange(),
    campaignId: '',
    platform: '',
  });
  const [exporting, setExporting] = useState(false);
  const params = {
    ...rangeQuery(filters),
    campaignId: filters.campaignId || undefined,
    platform: (filters.platform || undefined) as SocialPlatform | undefined,
  };
  const valid = !(filters.from && filters.to && filters.from > filters.to);

  const overview = useQuery({
    queryKey: qk.analytics(params),
    queryFn: () => api.get<AnalyticsOverview>('/analytics/overview', { query: params }),
    placeholderData: keepPreviousData,
    enabled: valid,
  });

  const exportCsv = async () => {
    setExporting(true);
    try {
      await api.download('/analytics/overview/export.csv', `analytics-${filters.from}-${filters.to}.csv`, {
        query: params,
      });
    } catch (err) {
      toast.error('Export failed', errorMessage(err));
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Analytics"
        description="Participation, spend, reach and traffic. Counted, estimated and measured figures are kept apart."
        actions={
          <Button
            variant="secondary"
            leadingIcon={<Download />}
            loading={exporting}
            onClick={() => void exportCsv()}
            disabled={!valid}
          >
            Export CSV
          </Button>
        }
      />
      <div className="stack">
        <FilterControls filters={filters} onChange={setFilters} />
        {overview.isError ? (
          <ErrorState
            error={overview.error}
            onRetry={() => void overview.refetch()}
            retrying={overview.isFetching}
          />
        ) : (
          <AnalyticsReport data={overview.data} loading={overview.isLoading} />
        )}
        {canMarketing && (
          <div className="mg-report__grid">
            <TrackingCard
              params={{ ...rangeQuery(filters), campaignId: filters.campaignId || undefined }}
              enabled={valid}
            />
            <RetentionCard params={rangeQuery(filters)} enabled={valid} />
          </div>
        )}
      </div>
    </>
  );
}

function TrackingCard({ params, enabled }: { params: Record<string, string | undefined>; enabled: boolean }) {
  const query = useQuery({
    queryKey: qk.tracking(params),
    queryFn: () => api.get<TrackingSummary>('/marketing/tracking/summary', { query: params }),
    enabled,
  });
  const data = query.data;
  return (
    <Card as="section" aria-labelledby="tracking-summary-title">
      <CardHeader
        titleId="tracking-summary-title"
        headingLevel={2}
        title="Top participants by tracked traffic"
        description={data?.note}
      />
      <CardBody className="stack">
        {query.isLoading ? (
          <Skeleton height={120} />
        ) : query.isError ? (
          <ErrorState compact headingLevel={3} error={query.error} onRetry={() => void query.refetch()} />
        ) : data ? (
          <>
            <div className="mg-stats">
              <Stat label="Unique clicks" value={formatNumber(data.uniqueClicks)} measurement="Measured" />
              <Stat
                label="Verified conversions"
                value={formatNumber(data.verifiedConversions)}
                measurement="Measured"
              />
              <Stat
                label="Conversion value"
                value={<AmountList amounts={data.conversionValue} empty="—" />}
                measurement="Measured"
              />
            </div>
            <DataTable
              caption="Top participants by tracked clicks"
              columns={[
                { id: 'name', header: 'Participant', primary: true, cell: (r) => r.displayName ?? 'Unknown' },
                { id: 'clicks', header: 'Clicks', align: 'right', cell: (r) => formatNumber(r.clicks) },
                { id: 'unique', header: 'Unique', align: 'right', cell: (r) => formatNumber(r.uniqueClicks) },
                {
                  id: 'conv',
                  header: 'Conversions',
                  align: 'right',
                  cell: (r) => formatNumber(r.verifiedConversions),
                },
              ]}
              rows={data.topParticipants}
              getRowId={(r) => r.userId ?? r.displayName ?? 'unknown'}
              emptyState={<p className="text-muted">No tracked clicks in this period.</p>}
            />
          </>
        ) : null}
      </CardBody>
    </Card>
  );
}

function RetentionCard({
  params,
  enabled,
}: {
  params: Record<string, string | undefined>;
  enabled: boolean;
}) {
  const query = useQuery({
    queryKey: qk.retention(params),
    queryFn: () => api.get<RetentionSummary>('/marketing/retention/summary', { query: params }),
    enabled,
  });
  const data = query.data;
  return (
    <Card as="section" aria-labelledby="retention-title">
      <CardHeader
        titleId="retention-title"
        headingLevel={2}
        title="Retention messages sent"
        description="Automated onboarding, campaign alert and reactivation messages, by send time."
      />
      <CardBody className="stack">
        {query.isLoading ? (
          <Skeleton height={120} />
        ) : query.isError ? (
          <ErrorState compact headingLevel={3} error={query.error} onRetry={() => void query.refetch()} />
        ) : !data || data.total === 0 ? (
          <EmptyState compact headingLevel={3} title="No retention messages in this period" />
        ) : (
          <>
            <Stat label="Messages sent" value={formatNumber(data.total)} measurement="Count" />
            <BarChart
              title="Retention messages by kind"
              description={`${data.total} messages sent in the period.`}
              data={data.items.map((i) => ({ label: retentionKindLabel(i.kind), value: i.sent }))}
              valueLabel="Sent"
            />
          </>
        )}
      </CardBody>
    </Card>
  );
}

export function CampaignAnalyticsPage() {
  const { campaignId = '' } = useParams();
  const toast = useToast();
  const [filters, setFilters] = useState<AnalyticsFilters>({ ...defaultRange(), campaignId, platform: '' });
  const params = {
    ...rangeQuery(filters),
    platform: (filters.platform || undefined) as SocialPlatform | undefined,
  };
  const valid = !(filters.from && filters.to && filters.from > filters.to);
  const query = useQuery({
    queryKey: qk.campaignAnalytics(campaignId, params),
    queryFn: () => api.get<AnalyticsOverview>(`/analytics/campaigns/${campaignId}`, { query: params }),
    placeholderData: keepPreviousData,
    enabled: valid && !!campaignId,
  });
  const campaigns = useCampaignOptions();
  const title = campaigns.data?.items.find((c) => c.id === campaignId)?.title ?? 'Campaign analytics';
  const [exporting, setExporting] = useState(false);

  return (
    <>
      <PageHeader
        title={title}
        eyebrow="Campaign analytics"
        breadcrumbs={[{ label: 'Analytics', to: '/manage/analytics' }, { label: title }]}
        actions={
          <Button
            variant="secondary"
            leadingIcon={<Download />}
            loading={exporting}
            disabled={!valid}
            onClick={async () => {
              setExporting(true);
              try {
                await api.download(
                  '/analytics/overview/export.csv',
                  `analytics-campaign-${filters.from}-${filters.to}.csv`,
                  {
                    query: { ...params, campaignId },
                  },
                );
              } catch (err) {
                toast.error('Export failed', errorMessage(err));
              } finally {
                setExporting(false);
              }
            }}
          >
            Export CSV
          </Button>
        }
      />
      <div className="stack">
        <FilterControls filters={filters} onChange={setFilters} showCampaign={false} />
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} retrying={query.isFetching} />
        ) : (
          <AnalyticsReport data={query.data} loading={query.isLoading} />
        )}
        {!valid && <Alert tone="warning">The start date must be on or before the end date.</Alert>}
      </div>
    </>
  );
}
