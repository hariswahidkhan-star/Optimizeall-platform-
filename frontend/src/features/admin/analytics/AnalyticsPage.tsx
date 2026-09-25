import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { CalendarRange } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { StatGrid } from '@/components/ui/Dashboard';
import { LineChart } from '@/components/ui/Charts';
import { DataTable } from '@/components/ui/DataTable';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { SkeletonText } from '@/components/ui/Skeleton';
import { MeasurementTag } from '@/components/ui/Stat';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import type { AnalyticsOverview, MetricSection } from '../api/types';
import { dateInputToIso, ExportCsvButton, QueryError } from '../shared/common';
import { mapFieldErrors } from '../shared/errors';
import { MetricStat, measurementTag } from './metrics';

function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function daysAgo(days: number): string {
  return isoDate(new Date(Date.now() - days * 86_400_000));
}

const PRESETS = [
  { days: 7, label: 'Last 7 days' },
  { days: 30, label: 'Last 30 days' },
  { days: 90, label: 'Last 90 days' },
  { days: 365, label: 'Last 12 months' },
];

export function analyticsQuery(from: string, to: string) {
  return { from: dateInputToIso(from), to: dateInputToIso(to, true) };
}

export function useAnalyticsOverview(from: string, to: string, enabled = true) {
  const query = analyticsQuery(from, to);
  return useQuery({
    queryKey: ['analytics', 'overview', query],
    queryFn: ({ signal }) => api.get<AnalyticsOverview>('/analytics/overview', { query, signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

function SectionCard({ section }: { section: MetricSection | undefined }) {
  if (!section) return null;
  const id = `metrics-${section.key}`;
  return (
    <Card as="section" aria-labelledby={id}>
      <CardHeader
        titleId={id}
        headingLevel={3}
        title={section.title}
        actions={<MeasurementTag measurement={measurementTag(section.measurement)} />}
      />
      <CardBody>
        <StatGrid strip min="10.5rem">
          {section.metrics.map((m, i) => (
            <MetricStat key={`${m.key}-${m.currency ?? i}`} metric={m} />
          ))}
        </StatGrid>
      </CardBody>
    </Card>
  );
}

function Group({
  id,
  title,
  description,
  children,
}: {
  id: string;
  title: string;
  description: string;
  children: ReactNode;
}) {
  return (
    <section aria-labelledby={id} className="stack admin-analytics-group">
      <div>
        <h2 id={id} className="admin-section-title">
          {title}
        </h2>
        <p className="text-small text-muted">{description}</p>
      </div>
      {children}
    </section>
  );
}

export function AnalyticsPage() {
  const [params, setParams] = useSearchParams();
  const from = params.get('from') ?? daysAgo(30);
  const to = params.get('to') ?? isoDate(new Date());
  const [draftFrom, setDraftFrom] = useState(from);
  const [draftTo, setDraftTo] = useState(to);
  const [rangeError, setRangeError] = useState<string | null>(null);
  const overview = useAnalyticsOverview(from, to);
  const server = mapFieldErrors(overview.error, ['from', 'to']);

  const applyRange = (nextFrom: string, nextTo: string) => {
    setDraftFrom(nextFrom);
    setDraftTo(nextTo);
    if (!nextFrom || !nextTo) return setRangeError('Choose both dates.');
    if (nextFrom > nextTo) return setRangeError('The start date must be on or before the end date.');
    if ((new Date(nextTo).getTime() - new Date(nextFrom).getTime()) / 86_400_000 > 366)
      return setRangeError('Choose a range of at most 366 days.');
    setRangeError(null);
    setParams({ from: nextFrom, to: nextTo }, { replace: true });
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    applyRange(draftFrom, draftTo);
  };

  const data = overview.data;
  const rangeProblem = isApiError(overview.error) && overview.error.code.startsWith('range.');

  return (
    <>
      <PageHeader
        title="Analytics"
        description="Platform-wide growth, quality and spend. Counted, measured and estimated figures are kept apart so estimates are never read as measurements."
        actions={
          <ExportCsvButton
            path="/analytics/overview/export.csv"
            query={analyticsQuery(from, to)}
            fileName="analytics-overview.csv"
          />
        }
      />
      <Card>
        <CardBody>
          <form className="admin-range" onSubmit={submit} noValidate aria-label="Date range">
            <FormField label="From" error={rangeError ?? server.fields.from}>
              <Input
                type="date"
                value={draftFrom}
                max={draftTo}
                onChange={(e) => setDraftFrom(e.target.value)}
              />
            </FormField>
            <FormField label="To" error={server.fields.to}>
              <Input
                type="date"
                value={draftTo}
                min={draftFrom}
                onChange={(e) => setDraftTo(e.target.value)}
              />
            </FormField>
            <Button type="submit" leadingIcon={<CalendarRange />}>
              Apply
            </Button>
            <div className="cluster admin-presets" role="group" aria-label="Quick ranges">
              {PRESETS.map((p) => (
                <Button
                  key={p.days}
                  size="sm"
                  variant="ghost"
                  onClick={() => applyRange(daysAgo(p.days), isoDate(new Date()))}
                >
                  {p.label}
                </Button>
              ))}
            </div>
          </form>
          <p className="text-small text-muted">
            Dates are whole days in UTC; a range can cover up to 366 days.
          </p>
        </CardBody>
      </Card>

      {overview.isPending ? (
        <Card>
          <CardBody>
            <SkeletonText lines={8} />
          </CardBody>
        </Card>
      ) : overview.isError ? (
        rangeProblem ? (
          <Alert tone="danger" role="alert" title="That date range can’t be used">
            {server.form?.title ?? 'Choose a valid range.'}
          </Alert>
        ) : (
          <QueryError error={overview.error} onRetry={() => void overview.refetch()} />
        )
      ) : (
        data && (
          <div className="stack" aria-busy={overview.isFetching || undefined}>
            <Group
              id="group-counted"
              title="Counted"
              description="Exact counts of platform records (registrations, submissions, spend) in the period."
            >
              <SectionCard section={data.funnel} />
              <SectionCard section={data.posts} />
              <SectionCard section={data.spend} />
            </Group>

            <Group
              id="group-measured"
              title="Measured"
              description="From tracked events: link clicks (bots excluded) and verified conversions."
            >
              <SectionCard section={data.traffic} />
              <SectionCard section={data.conversions} />
            </Group>

            <Group
              id="group-estimated"
              title="Estimated"
              description="Not directly measured. Reach is based on the follower counts participants declare, not actual views."
            >
              <SectionCard section={data.reach} />
            </Group>

            {data.timeseries.length > 0 && (
              <Card as="section" aria-labelledby="trend">
                <CardHeader
                  titleId="trend"
                  headingLevel={2}
                  title="Daily trend"
                  description="Counted per UTC day."
                />
                <CardBody>
                  <LineChart
                    title="Registrations, submissions and approvals per day"
                    description={`Daily counts from ${data.timeseries[0]?.date} to ${data.timeseries[data.timeseries.length - 1]?.date}.`}
                    labels={data.timeseries.map((p) => p.date)}
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
                      {
                        id: 'approvals',
                        label: 'Approvals',
                        values: data.timeseries.map((p) => p.approvals),
                      },
                    ]}
                  />
                </CardBody>
              </Card>
            )}

            <Card as="section" aria-labelledby="by-campaign">
              <CardHeader
                titleId="by-campaign"
                headingLevel={2}
                title="By campaign"
                description="Submitted/approved and spend are counted; clicks and conversions are measured; reach is estimated."
              />
              <CardBody>
                <DataTable
                  caption="Campaign performance"
                  rows={data.campaigns}
                  getRowId={(c) => c.campaignId}
                  defaultSort={{ id: 'submitted', desc: true }}
                  columns={[
                    {
                      id: 'title',
                      header: 'Campaign',
                      primary: true,
                      sortable: true,
                      sortValue: (c) => c.title,
                      cell: (c) => <strong>{c.title}</strong>,
                    },
                    { id: 'status', header: 'Status', cell: (c) => humanize(c.status) },
                    {
                      id: 'submitted',
                      header: 'Submitted',
                      align: 'right',
                      sortable: true,
                      sortValue: (c) => c.submitted,
                      cell: (c) => formatNumber(c.submitted),
                    },
                    {
                      id: 'approved',
                      header: 'Approved',
                      align: 'right',
                      sortable: true,
                      sortValue: (c) => c.approved,
                      cell: (c) => formatNumber(c.approved),
                    },
                    {
                      id: 'rate',
                      header: 'Approval rate',
                      align: 'right',
                      cell: (c) =>
                        c.approvalRate === null
                          ? '—'
                          : `${formatNumber(c.approvalRate, { maximumFractionDigits: 2 })}%`,
                    },
                    {
                      id: 'spend',
                      header: 'Spend',
                      align: 'right',
                      cell: (c) =>
                        c.spend.length === 0
                          ? '—'
                          : c.spend.map((s) => (
                              <div key={s.currency}>
                                <Money amount={s.amount} currency={s.currency} />
                              </div>
                            )),
                    },
                    {
                      id: 'clicks',
                      header: 'Clicks (measured)',
                      align: 'right',
                      hideOnMobile: true,
                      cell: (c) => formatNumber(c.clicks),
                    },
                    {
                      id: 'conversions',
                      header: 'Conversions (measured)',
                      align: 'right',
                      hideOnMobile: true,
                      cell: (c) => formatNumber(c.verifiedConversions),
                    },
                    {
                      id: 'reach',
                      header: 'Reach (estimated)',
                      align: 'right',
                      hideOnMobile: true,
                      cell: (c) => formatNumber(c.estimatedReach),
                    },
                  ]}
                />
              </CardBody>
            </Card>
            <p className="text-small text-muted">{data.timeBasis}</p>
          </div>
        )
      )}
    </>
  );
}
