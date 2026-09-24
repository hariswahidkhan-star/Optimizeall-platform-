import clsx from 'clsx';
import { useId, useState, type ReactNode } from 'react';
import { browserLocale } from '@/lib/format/locale';
import './Charts.css';

/**
 * Lightweight SVG charts. Each chart is an <svg role="img"> with <title>/<desc>, plus a visually hidden data table
 * so screen-reader users get the exact values. Colors come from the validated --chart-* tokens (fixed order).
 */

export const SERIES_COLORS = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)'] as const;

export interface ChartDatum {
  label: string;
  value: number;
}

export interface ChartSeries {
  id: string;
  label: string;
  values: number[];
}

type Formatter = (value: number) => string;
const defaultFormat: Formatter = (value) => new Intl.NumberFormat(browserLocale()).format(value);

/** Rounds a maximum up to a "nice" axis value (1, 2, 2.5, 5 × 10^n). */
export function niceMax(value: number): number {
  if (value <= 0) return 1;
  const exponent = Math.floor(Math.log10(value));
  const base = 10 ** exponent;
  const fraction = value / base;
  const nice = [1, 2, 2.5, 5, 10].find((n) => fraction <= n) ?? 10;
  return nice * base;
}

function ChartTable({
  caption,
  labels,
  series,
  format,
}: {
  caption: string;
  labels: string[];
  series: ChartSeries[];
  format: Formatter;
}) {
  // The wrapper is what gets hidden: a table ignores width constraints and would widen the page on phones.
  return (
    <div className="visually-hidden">
      <table>
        <caption>{caption}</caption>
        <thead>
          <tr>
            <th scope="col">Label</th>
            {series.map((s) => (
              <th key={s.id} scope="col">
                {s.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {labels.map((label, i) => (
            <tr key={label}>
              <th scope="row">{label}</th>
              {series.map((s) => (
                <td key={s.id}>{s.values[i] !== undefined ? format(s.values[i]) : '—'}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Legend({ series }: { series: ChartSeries[] }) {
  if (series.length < 2) return null;
  return (
    <ul className="ui-chart__legend" aria-hidden="true">
      {series.map((s, i) => (
        <li key={s.id}>
          <span
            className="ui-chart__swatch"
            style={{ background: SERIES_COLORS[i % SERIES_COLORS.length] }}
          />
          {s.label}
        </li>
      ))}
    </ul>
  );
}

interface BaseChartProps {
  /** Short accessible title (also the table caption). */
  title: string;
  /** One-sentence summary of what the chart shows (the takeaway). */
  description: string;
  height?: number;
  valueFormatter?: Formatter;
  className?: string;
}

// ---------- Bar chart ----------

export interface BarChartProps extends BaseChartProps {
  data: ChartDatum[];
  /** Label for the value column in the data table. */
  valueLabel?: string;
}

const PAD = { top: 20, right: 8, bottom: 28, left: 44 };
const WIDTH = 640;

export function BarChart({
  data,
  title,
  description,
  height = 240,
  valueFormatter = defaultFormat,
  valueLabel = 'Value',
  className,
}: BarChartProps) {
  const titleId = useId();
  const descId = useId();
  const [active, setActive] = useState<number | null>(null);
  const max = niceMax(Math.max(0, ...data.map((d) => d.value)));
  const innerW = WIDTH - PAD.left - PAD.right;
  const innerH = height - PAD.top - PAD.bottom;
  const slot = data.length > 0 ? innerW / data.length : innerW;
  const barW = Math.max(4, Math.min(48, slot * 0.6));
  const ticks = [0, 0.25, 0.5, 0.75, 1].map((t) => t * max);
  const y = (v: number) => PAD.top + innerH - (Math.max(0, v) / max) * innerH;
  const showAllLabels = data.length <= 8;
  const activeDatum = active !== null ? data[active] : undefined;

  return (
    <figure className={clsx('ui-chart', className)} style={{ margin: 0 }}>
      <svg viewBox={`0 0 ${WIDTH} ${height}`} role="img" aria-labelledby={titleId} aria-describedby={descId}>
        <title id={titleId}>{title}</title>
        <desc id={descId}>{description}</desc>
        {ticks.map((tick) => (
          <g key={tick}>
            <line className="ui-chart__grid" x1={PAD.left} x2={WIDTH - PAD.right} y1={y(tick)} y2={y(tick)} />
            <text className="ui-chart__axis-label" x={PAD.left - 8} y={y(tick)} dy="0.32em" textAnchor="end">
              {valueFormatter(tick)}
            </text>
          </g>
        ))}
        {data.map((d, i) => {
          const cx = PAD.left + slot * i + slot / 2;
          const top = y(d.value);
          const h = PAD.top + innerH - top;
          const r = Math.min(4, barW / 2, h);
          // Rounded data-end, square baseline.
          const path = `M${cx - barW / 2},${PAD.top + innerH} V${top + r} Q${cx - barW / 2},${top} ${cx - barW / 2 + r},${top} H${cx + barW / 2 - r} Q${cx + barW / 2},${top} ${cx + barW / 2},${top + r} V${PAD.top + innerH} Z`;
          return (
            <g key={d.label}>
              <path
                className={clsx('ui-chart__bar', active === i && 'is-active')}
                d={path}
                fill={SERIES_COLORS[0]}
              />
              {(showAllLabels || active === i) && h > 0 && (
                <text className="ui-chart__value-label" x={cx} y={top - 6} textAnchor="middle">
                  {valueFormatter(d.value)}
                </text>
              )}
              <text className="ui-chart__axis-label" x={cx} y={height - 8} textAnchor="middle">
                {d.label}
              </text>
              <rect
                className="ui-chart__hit"
                x={PAD.left + slot * i}
                y={PAD.top}
                width={slot}
                height={innerH}
                onMouseEnter={() => setActive(i)}
                onMouseLeave={() => setActive(null)}
              />
            </g>
          );
        })}
      </svg>
      {activeDatum && active !== null && (
        <div
          className="ui-chart__tooltip"
          aria-hidden="true"
          style={{
            left: `${((PAD.left + slot * active + slot / 2) / WIDTH) * 100}%`,
            top: `${(y(activeDatum.value) / height) * 100}%`,
          }}
        >
          <div className="ui-chart__tooltip-title">{activeDatum.label}</div>
          <div className="ui-chart__tooltip-row">{valueFormatter(activeDatum.value)}</div>
        </div>
      )}
      <ChartTable
        caption={title}
        labels={data.map((d) => d.label)}
        series={[{ id: 'value', label: valueLabel, values: data.map((d) => d.value) }]}
        format={valueFormatter}
      />
    </figure>
  );
}

// ---------- Line chart ----------

export interface LineChartProps extends BaseChartProps {
  /** X-axis labels (e.g. dates), one per value. */
  labels: string[];
  /** Up to three series (fixed color order). */
  series: ChartSeries[];
  /** Fill a soft gradient under the first series (an area chart). */
  area?: boolean;
}

export function LineChart({
  labels,
  series,
  title,
  description,
  height = 240,
  valueFormatter = defaultFormat,
  area = false,
  className,
}: LineChartProps) {
  const titleId = useId();
  const descId = useId();
  const gradientId = `ui-area-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`;
  const [active, setActive] = useState<number | null>(null);
  const shown = series.slice(0, SERIES_COLORS.length);
  const max = niceMax(Math.max(0, ...shown.flatMap((s) => s.values)));
  const innerW = WIDTH - PAD.left - PAD.right;
  const innerH = height - PAD.top - PAD.bottom;
  const step = labels.length > 1 ? innerW / (labels.length - 1) : 0;
  const x = (i: number) => PAD.left + (labels.length > 1 ? step * i : innerW / 2);
  const y = (v: number) => PAD.top + innerH - (Math.max(0, v) / max) * innerH;
  const ticks = [0, 0.25, 0.5, 0.75, 1].map((t) => t * max);
  const labelEvery = Math.max(1, Math.ceil(labels.length / 6));

  return (
    <figure className={clsx('ui-chart', className)} style={{ margin: 0 }}>
      <Legend series={shown} />
      <svg
        viewBox={`0 0 ${WIDTH} ${height}`}
        role="img"
        aria-labelledby={titleId}
        aria-describedby={descId}
        onMouseLeave={() => setActive(null)}
      >
        <title id={titleId}>{title}</title>
        <desc id={descId}>{description}</desc>
        {ticks.map((tick) => (
          <g key={tick}>
            <line className="ui-chart__grid" x1={PAD.left} x2={WIDTH - PAD.right} y1={y(tick)} y2={y(tick)} />
            <text className="ui-chart__axis-label" x={PAD.left - 8} y={y(tick)} dy="0.32em" textAnchor="end">
              {valueFormatter(tick)}
            </text>
          </g>
        ))}
        {labels.map((label, i) =>
          i % labelEvery === 0 || i === labels.length - 1 ? (
            <text key={label} className="ui-chart__axis-label" x={x(i)} y={height - 8} textAnchor="middle">
              {label}
            </text>
          ) : null,
        )}
        {active !== null && (
          <line
            className="ui-chart__crosshair"
            x1={x(active)}
            x2={x(active)}
            y1={PAD.top}
            y2={PAD.top + innerH}
          />
        )}
        {area && shown[0] && shown[0].values.length > 1 && (
          <>
            <defs>
              <linearGradient id={gradientId} x1="0" x2="0" y1="0" y2="1">
                <stop offset="0%" stopColor={SERIES_COLORS[0]} stopOpacity={0.18} />
                <stop offset="100%" stopColor={SERIES_COLORS[0]} stopOpacity={0} />
              </linearGradient>
            </defs>
            <path
              className="ui-chart__area"
              fill={`url(#${gradientId})`}
              d={`${shown[0].values.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i)},${y(v)}`).join(' ')} L${x(shown[0].values.length - 1)},${PAD.top + innerH} L${x(0)},${PAD.top + innerH} Z`}
            />
          </>
        )}
        {shown.map((s, si) => (
          <g key={s.id}>
            <path
              className="ui-chart__line"
              stroke={SERIES_COLORS[si]}
              d={s.values.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i)},${y(v)}`).join(' ')}
            />
            {active !== null && s.values[active] !== undefined && (
              <circle
                className="ui-chart__dot"
                cx={x(active)}
                cy={y(s.values[active]!)}
                r={5}
                fill={SERIES_COLORS[si]}
              />
            )}
          </g>
        ))}
        {labels.map((label, i) => (
          <rect
            key={label}
            className="ui-chart__hit"
            x={x(i) - (step || innerW) / 2}
            y={PAD.top}
            width={step || innerW}
            height={innerH}
            onMouseEnter={() => setActive(i)}
          />
        ))}
      </svg>
      {active !== null && (
        <div
          className="ui-chart__tooltip"
          aria-hidden="true"
          style={{
            left: `${(x(active) / WIDTH) * 100}%`,
            top: `${(y(Math.max(...shown.map((s) => s.values[active] ?? 0))) / height) * 100}%`,
          }}
        >
          <div className="ui-chart__tooltip-title">{labels[active]}</div>
          {shown.map((s, si) => (
            <div key={s.id} className="ui-chart__tooltip-row">
              <span className="ui-chart__swatch" style={{ background: SERIES_COLORS[si] }} />
              {shown.length > 1 && <span style={{ fontWeight: 400 }}>{s.label}</span>}
              {s.values[active] !== undefined ? valueFormatter(s.values[active]!) : '—'}
            </div>
          ))}
        </div>
      )}
      <ChartTable caption={title} labels={labels} series={shown} format={valueFormatter} />
    </figure>
  );
}

// ---------- Sparkline ----------

export interface SparklineProps {
  values: number[];
  /** Accessible summary, e.g. "Approved posts, last 14 days: trending up". */
  label: string;
  width?: number;
  height?: number;
  area?: boolean;
  className?: string;
}

export function Sparkline({
  values,
  label,
  width = 96,
  height = 28,
  area = true,
  className,
}: SparklineProps): ReactNode {
  if (values.length < 2) return null;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = max - min || 1;
  const pad = 2;
  const pts = values.map((v, i) => [
    pad + (i / (values.length - 1)) * (width - pad * 2),
    pad + (1 - (v - min) / range) * (height - pad * 2),
  ]);
  const line = pts.map(([px, py], i) => `${i === 0 ? 'M' : 'L'}${px},${py}`).join(' ');
  const fill = `${line} L${pts[pts.length - 1]![0]},${height} L${pts[0]![0]},${height} Z`;
  return (
    <svg
      className={clsx('ui-sparkline', className)}
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      role="img"
      aria-label={label}
    >
      {area && <path className="ui-sparkline__area" d={fill} />}
      <path d={line} />
    </svg>
  );
}
