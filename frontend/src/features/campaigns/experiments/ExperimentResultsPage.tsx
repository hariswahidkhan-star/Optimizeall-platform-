import { useQuery } from '@tanstack/react-query';
import { Trophy } from 'lucide-react';
import { useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  BarChart,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  ErrorState,
  KeyValueList,
  MeasurementTag,
  PageHeader,
  Skeleton,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { formatNumber } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import { qk } from '../api/queries';
import type { Experiment, ExperimentResults, VariantComparison, VariantResult } from '../api/types';
import { measurementTag } from '../analytics/metrics';
import { STATUS_TONES } from './ExperimentsPage';
import '../campaigns.css';

/** A 0–1 fraction as a percentage, or "—" when the API returned null (no denominator). */
function pct(value: number | null | undefined, digits = 1): string {
  if (value === null || value === undefined) return '—';
  return `${formatNumber(value * 100, { maximumFractionDigits: digits })}%`;
}

/** Absolute lift is a difference of fractions → percentage points. */
function points(value: number | null): string {
  if (value === null) return '—';
  const pp = formatNumber(value * 100, { maximumFractionDigits: 2, signDisplay: 'exceptZero' });
  return `${pp} pp`;
}

function pValue(value: number | null): string {
  if (value === null) return '—';
  return value < 0.0001 ? '< 0.0001' : formatNumber(value, { maximumFractionDigits: 4 });
}

export function ExperimentResultsView({
  experiment,
  results,
}: {
  experiment?: Experiment;
  results: ExperimentResults;
}) {
  const winner = experiment?.winningVariantId;
  const variantColumns: DataTableColumn<VariantResult>[] = [
    {
      id: 'variant',
      nowrap: true,
      header: 'Variant',
      primary: true,
      cell: (v) => (
        <span className="cluster mg-cluster-sm">
          <span className="mg-strong">
            {v.key} · {v.name}
          </span>
          {v.key === 'A' && <Badge size="sm">Control</Badge>}
          {winner === v.variantId && (
            <Badge size="sm" tone="success" icon={<Trophy />}>
              Winner
            </Badge>
          )}
        </span>
      ),
    },
    { id: 'weight', header: 'Weight', align: 'right', cell: (v) => `${v.weight}` },
    { id: 'assigned', header: 'Assigned', align: 'right', cell: (v) => formatNumber(v.assigned) },
    {
      id: 'submissions',
      header: 'Participants who submitted',
      align: 'right',
      cell: (v) => formatNumber(v.submissions),
    },
    {
      id: 'approved',
      header: 'With an approved post',
      align: 'right',
      cell: (v) => formatNumber(v.approved),
    },
    {
      id: 'totals',
      header: 'Submissions (approved)',
      align: 'right',
      hideOnMobile: true,
      cell: (v) => `${formatNumber(v.totalSubmissions)} (${formatNumber(v.totalApproved)})`,
    },
    { id: 'rate', header: 'Submission rate', align: 'right', cell: (v) => pct(v.submissionRate) },
    { id: 'approvalRate', header: 'Approval rate', align: 'right', cell: (v) => pct(v.approvalRate) },
  ];

  const comparisonColumns: DataTableColumn<VariantComparison>[] = [
    { id: 'pair', header: 'Comparison', primary: true, cell: (c) => `${c.variantKey} vs ${c.controlKey}` },
    { id: 'abs', header: 'Absolute lift', align: 'right', cell: (c) => points(c.absoluteLift) },
    { id: 'rel', header: 'Relative lift', align: 'right', cell: (c) => pct(c.relativeLift) },
    {
      id: 'z',
      header: 'z-score',
      align: 'right',
      hideOnMobile: true,
      cell: (c) => (c.zScore === null ? '—' : formatNumber(c.zScore, { maximumFractionDigits: 3 })),
    },
    { id: 'p', header: 'p-value', align: 'right', cell: (c) => pValue(c.pValue) },
    {
      id: 'sig',
      header: 'Significance',
      cell: (c) =>
        c.significant ? (
          <Badge tone="success">Significant</Badge>
        ) : (
          <Badge tone="neutral">Not significant</Badge>
        ),
    },
    { id: 'note', header: 'Note', cell: (c) => <span className="text-small">{c.note}</span> },
  ];

  const anySignificant = results.comparisons.some((c) => c.significant);
  const notes = [...new Set(results.comparisons.filter((c) => !c.significant).map((c) => c.note))];
  const metricLabel = humanize(results.metric);

  return (
    <div className="stack">
      <Card as="section" aria-labelledby="results-variants-title">
        <CardHeader
          titleId="results-variants-title"
          headingLevel={2}
          title={
            <span className="cluster mg-cluster-sm">
              <span>Variants</span>
              <MeasurementTag measurement={measurementTag(results.measurement)} />
            </span>
          }
          description={`Primary metric: ${metricLabel} (participants who submitted ÷ participants assigned).`}
        />
        <CardBody className="stack">
          <BarChart
            title={`${metricLabel} by variant`}
            description="Share of assigned participants who submitted at least one post, per variant."
            data={results.variants.map((v) => ({
              label: `${v.key} · ${v.name}`,
              value: v.submissionRate === null ? 0 : Math.round(v.submissionRate * 10000) / 100,
            }))}
            valueLabel="Submission rate (%)"
            valueFormatter={(v) => `${formatNumber(v, { maximumFractionDigits: 2 })}%`}
          />
          <DataTable
            caption="Results per variant"
            columns={variantColumns}
            rows={results.variants}
            getRowId={(v) => v.variantId}
          />
        </CardBody>
      </Card>

      <Card as="section" aria-labelledby="results-comparisons-title">
        <CardHeader
          titleId="results-comparisons-title"
          headingLevel={2}
          title="Comparison with the control"
          description={results.method}
        />
        <CardBody className="stack">
          {!anySignificant && (
            <Alert tone="info" title="No significant result" role="status">
              {notes.length > 0 ? notes.join(' ') : 'No statistically significant difference was reported.'}
            </Alert>
          )}
          <DataTable
            caption="Comparisons with the control variant"
            columns={comparisonColumns}
            rows={results.comparisons}
            getRowId={(c) => `${c.variantKey}-${c.controlKey}`}
            emptyState={<p className="text-muted">No comparisons yet.</p>}
          />
          <p className="text-small text-muted">
            Lift, p-value and significance are shown exactly as calculated by the server. A result is marked
            significant only when the server reports it.
          </p>
        </CardBody>
      </Card>
    </div>
  );
}

export function ExperimentResultsPage() {
  const { experimentId = '' } = useParams();
  const experiment = useQuery({
    queryKey: qk.experiment(experimentId),
    queryFn: () => api.get<Experiment>(`/marketing/experiments/${experimentId}`),
  });
  const results = useQuery({
    queryKey: qk.experimentResults(experimentId),
    queryFn: () => api.get<ExperimentResults>(`/marketing/experiments/${experimentId}/results`),
  });
  const e = experiment.data;
  const campaignTitle = e?.campaignTitle;

  return (
    <>
      <PageHeader
        title={e?.name ?? 'Experiment results'}
        eyebrow="Experiment"
        breadcrumbs={[{ label: 'Experiments', to: '/manage/experiments' }, { label: e?.name ?? 'Results' }]}
        meta={e ? <Badge tone={STATUS_TONES[e.status]}>{e.status}</Badge> : undefined}
      />
      <div className="stack">
        {experiment.isError ? (
          <ErrorState error={experiment.error} onRetry={() => void experiment.refetch()} />
        ) : e ? (
          <KeyValueList
            layout="inline"
            items={[
              { label: 'Campaign', value: campaignTitle ?? '—' },
              { label: 'Element', value: humanize(e.element) },
              { label: 'Started', value: e.startedAt ? <DateTime value={e.startedAt} /> : 'Not started' },
              { label: 'Ended', value: e.endedAt ? <DateTime value={e.endedAt} /> : '—' },
              ...(e.hypothesis ? [{ label: 'Hypothesis', value: e.hypothesis }] : []),
            ]}
          />
        ) : (
          <Skeleton height={48} />
        )}
        {results.isError ? (
          <ErrorState error={results.error} onRetry={() => void results.refetch()} />
        ) : results.data ? (
          <ExperimentResultsView experiment={e} results={results.data} />
        ) : (
          <Skeleton height={320} />
        )}
      </div>
    </>
  );
}
