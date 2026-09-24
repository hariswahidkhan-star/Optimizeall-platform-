import { Money } from '@/components/ui/Money';
import { Stat, type Measurement, type StatProps } from '@/components/ui/Stat';
import { formatNumber } from '@/lib/format/money';
import type { Metric, MetricMeasurement } from '../api/types';

/** API measurement (`counted | measured | estimated`) → the design system's Stat tag. */
export function measurementTag(measurement: MetricMeasurement): Measurement {
  if (measurement === 'estimated') return 'Estimated';
  if (measurement === 'measured') return 'Measured';
  return 'Count';
}

export function MetricValue({ metric }: { metric: Metric }) {
  if (metric.value === null || metric.value === undefined)
    return <span title="No data for this period">—</span>;
  if (metric.unit === 'money' && metric.currency)
    return <Money amount={metric.value} currency={metric.currency} />;
  if (metric.unit === 'percent') return <>{formatNumber(metric.value, { maximumFractionDigits: 2 })}%</>;
  return <>{formatNumber(metric.value)}</>;
}

/** A KPI tile for one metric, labelled with how it was obtained exactly as the API reports it. */
export function MetricStat({
  metric,
  label,
  delta,
  trend,
}: {
  metric: Metric;
  label?: string;
  delta?: StatProps['delta'];
  trend?: StatProps['trend'];
}) {
  const currencySuffix = metric.unit === 'money' && metric.currency ? ` (${metric.currency})` : '';
  return (
    <Stat
      label={`${label ?? metric.label}${currencySuffix}`}
      value={<MetricValue metric={metric} />}
      measurement={measurementTag(metric.measurement)}
      hint={metric.note ?? undefined}
      delta={delta}
      trend={trend}
    />
  );
}

/** Metrics with the given key (money metrics repeat once per currency). */
export function metricsByKey(metrics: Metric[] | undefined, key: string): Metric[] {
  return (metrics ?? []).filter((m) => m.key === key);
}
