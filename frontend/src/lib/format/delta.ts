import { formatNumber } from './money';

/** The subset of an analytics metric needed to compare two periods. */
export interface ComparableMetric {
  key: string;
  value: number | null;
  unit: string;
  currency?: string | null;
}

export interface PeriodDelta {
  value: number;
  display?: string;
  label: string;
  positiveIsGood?: boolean;
  neutral?: boolean;
}

/** Metrics where a rise is neither good nor bad (money going out) — the delta stays neutral grey. */
const NEUTRAL_KEYS = new Set(['spend']);
/** Metrics where a fall is the good direction. */
const LOWER_IS_BETTER = new Set(['costPerApprovedPost', 'costPerClick', 'rejectionRate']);

/**
 * Change of `current` against the same metric (same key and currency) in the previous period. Counts and money are
 * compared as a relative change; percentages as a difference in percentage points ("+2.4 pts"). Returns undefined when
 * either side has no value or the previous value is zero (no meaningful ratio).
 */
export function periodDelta(
  current: ComparableMetric,
  previous: readonly ComparableMetric[] | undefined,
  label: string,
): PeriodDelta | undefined {
  if (!previous || current.value === null) return undefined;
  const before = previous.find(
    (m) => m.key === current.key && (m.currency ?? null) === (current.currency ?? null),
  );
  if (!before || before.value === null) return undefined;
  const common = {
    label,
    positiveIsGood: !LOWER_IS_BETTER.has(current.key),
    neutral: NEUTRAL_KEYS.has(current.key),
  };
  if (current.unit === 'percent') {
    const points = current.value - before.value;
    const rounded = Math.round(points * 10) / 10;
    const sign = rounded > 0 ? '+' : rounded < 0 ? '−' : '';
    return {
      ...common,
      value: rounded,
      display: `${sign}${formatNumber(Math.abs(rounded), { maximumFractionDigits: 1 })} pts`,
    };
  }
  if (before.value === 0) return undefined;
  return { ...common, value: (current.value - before.value) / Math.abs(before.value) };
}

type DailyPoint = {
  date: string;
  registrations: number;
  submissions: number;
  approvals: number;
  clicks: number;
};

const TREND_FIELD: Record<string, keyof Omit<DailyPoint, 'date'>> = {
  registrations: 'registrations',
  postsSubmitted: 'submissions',
  postsApproved: 'approvals',
  trackedClicks: 'clicks',
};

/**
 * Daily values behind a count metric (from the analytics overview's timeseries) for a KPI sparkline, with a spoken
 * summary; undefined for metrics that have no daily series.
 */
export function metricTrend(
  key: string,
  label: string,
  timeseries: readonly DailyPoint[] | undefined,
): { values: number[]; label: string } | undefined {
  const field = TREND_FIELD[key];
  if (!field || !timeseries || timeseries.length < 2) return undefined;
  const values = timeseries.map((p) => p[field]);
  const peak = Math.max(...values);
  return {
    values,
    label: `${label} per day over the period, from ${formatNumber(values[0]!)} to ${formatNumber(values[values.length - 1]!)}; peak ${formatNumber(peak)}`,
  };
}

/** Previous window of the same length that ends the day before `from` (both ISO dates, inclusive). */
export function previousRange(from: string, to: string): { from: string; to: string } {
  const day = 86_400_000;
  const start = Date.parse(`${from}T00:00:00Z`);
  const end = Date.parse(`${to}T00:00:00Z`);
  const length = Math.round((end - start) / day) + 1;
  const prevEnd = start - day;
  const prevStart = prevEnd - (length - 1) * day;
  const iso = (t: number) => new Date(t).toISOString().slice(0, 10);
  return { from: iso(prevStart), to: iso(prevEnd) };
}
