import { Info } from 'lucide-react';
import type { ReactNode } from 'react';
import { Badge, Card, CardBody, CardHeader, Money, Stat, type Measurement } from '@/components/ui';
import { formatNumber } from '@/lib/format/money';
import type { CurrencyAmount, Metric, MetricMeasurement, MetricSection } from '../api/types';

/** API measurement → the design system's Stat tag. `counted` = records we hold (Count). */
export function measurementTag(measurement: MetricMeasurement | string): Measurement {
  if (measurement === 'estimated') return 'Estimated';
  if (measurement === 'measured') return 'Measured';
  return 'Count';
}

const SECTION_BADGE: Record<string, { label: string; tone: 'info' | 'warning' | 'success' | 'neutral' }> = {
  counted: { label: 'Counted from our records', tone: 'neutral' },
  measured: { label: 'Measured', tone: 'success' },
  estimated: { label: 'Estimated — not measured', tone: 'warning' },
};

export function sectionBadge(measurement: string) {
  return SECTION_BADGE[measurement] ?? { label: measurement, tone: 'neutral' as const };
}

/** Formats a metric exactly as returned: counts grouped, percents are 0–100, money in its currency, null → "—". */
export function MetricValue({ metric }: { metric: Pick<Metric, 'value' | 'unit' | 'currency'> }) {
  if (metric.value === null || metric.value === undefined) return <span>—</span>;
  if (metric.unit === 'money' && metric.currency)
    return <Money amount={metric.value} currency={metric.currency} />;
  if (metric.unit === 'percent')
    return <span>{formatNumber(metric.value, { maximumFractionDigits: 2 })}%</span>;
  return <span>{formatNumber(metric.value)}</span>;
}

export function AmountList({ amounts, empty = '—' }: { amounts: CurrencyAmount[]; empty?: ReactNode }) {
  if (amounts.length === 0) return <>{empty}</>;
  return (
    <span className="stack mg-stack-xs">
      {amounts.map((a) => (
        <Money key={a.currency} amount={a.amount} currency={a.currency} />
      ))}
    </span>
  );
}

/** One API section rendered as its own card: never mixes metrics of different measurement kinds. */
export function MetricSectionCard({
  section,
  title,
  description,
  children,
  loading,
}: {
  section: MetricSection | undefined;
  title: string;
  description?: ReactNode;
  children?: ReactNode;
  loading?: boolean;
}) {
  const badge = section ? sectionBadge(section.measurement) : null;
  const headingId = `analytics-section-${section?.key ?? title.toLowerCase().replace(/\W+/g, '-')}`;
  return (
    <Card
      as="section"
      aria-labelledby={headingId}
      className="mg-section"
      data-measurement={section?.measurement}
    >
      <CardHeader
        titleId={headingId}
        headingLevel={2}
        title={
          <span className="cluster mg-cluster-sm">
            <span>{title}</span>
            {badge && (
              <Badge tone={badge.tone} size="sm">
                {badge.label}
              </Badge>
            )}
          </span>
        }
        description={description}
      />
      <CardBody className="stack">
        <div className="mg-stats">
          {(section?.metrics ?? (loading ? [null, null, null] : [])).map((metric, i) =>
            metric ? (
              <Stat
                key={`${metric.key}-${metric.currency ?? ''}-${i}`}
                label={metric.currency ? `${metric.label} (${metric.currency})` : metric.label}
                value={<MetricValue metric={metric} />}
                measurement={measurementTag(metric.measurement)}
                hint={
                  metric.note ? (
                    <span className="mg-note">
                      <Info aria-hidden="true" />
                      {metric.note}
                    </span>
                  ) : undefined
                }
              />
            ) : (
              <Stat key={i} label="Loading" value="" loading />
            ),
          )}
        </div>
        {children}
      </CardBody>
    </Card>
  );
}
