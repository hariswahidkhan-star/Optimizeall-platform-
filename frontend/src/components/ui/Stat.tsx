import clsx from 'clsx';
import { ArrowDownRight, ArrowRight, ArrowUpRight } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { browserLocale } from '@/lib/format/locale';
import { Sparkline } from './Charts';
import { Skeleton } from './Skeleton';
import './display.css';

/** How a number was obtained — shown on every KPI so estimates are never mistaken for measurements. */
export type Measurement = 'Measured' | 'Estimated' | 'Count';

const MEASUREMENT_HELP: Record<Measurement, string> = {
  Measured: 'Measured from tracked events',
  Estimated: 'Estimated — not directly measured',
  Count: 'Count of records',
};

export interface StatProps {
  label: ReactNode;
  value: ReactNode;
  measurement?: Measurement;
  /**
   * Change vs. previous period. `value` is a signed ratio (0.12 = +12%) unless `display` is given. The direction is
   * conveyed by an arrow, a tint and (for screen readers) the words "Up", "Down" or "No change" — never colour alone.
   * `neutral` keeps the tint grey when a rise is neither good nor bad (e.g. spend).
   */
  delta?: { value: number; display?: string; label?: string; positiveIsGood?: boolean; neutral?: boolean };
  /** Small trend line next to the value (decorative detail; give it an accessible summary in `label`). */
  trend?: { values: number[]; label: string };
  icon?: ReactNode;
  hint?: ReactNode;
  loading?: boolean;
  className?: string;
}

export function MeasurementTag({ measurement }: { measurement: Measurement }) {
  return (
    <span
      className={clsx('ui-measure', measurement === 'Estimated' && 'ui-measure--estimated')}
      title={MEASUREMENT_HELP[measurement]}
    >
      {measurement}
      <span className="visually-hidden">: {MEASUREMENT_HELP[measurement]}</span>
    </span>
  );
}

/**
 * KPI tile. Rendered as a `group` named by its label, so assistive tech (and tests) find e.g. the "Pending" tile and
 * read its value in context.
 */
export function Stat({ label, value, measurement, delta, trend, icon, hint, loading, className }: StatProps) {
  const labelId = useId();
  const direction = !delta ? 'flat' : delta.value > 0 ? 'up' : delta.value < 0 ? 'down' : 'flat';
  const good = delta?.positiveIsGood ?? true;
  const deltaTone =
    direction === 'flat' || delta?.neutral ? 'flat' : (direction === 'up') === good ? 'up' : 'down';
  const directionWord = direction === 'up' ? 'Up' : direction === 'down' ? 'Down' : 'No change';
  const deltaText =
    delta?.display ??
    (delta
      ? new Intl.NumberFormat(browserLocale(), {
          style: 'percent',
          maximumFractionDigits: 1,
          signDisplay: 'exceptZero',
        }).format(delta.value)
      : '');

  return (
    <div
      role="group"
      aria-labelledby={labelId}
      className={clsx('ui-stat', className)}
      aria-busy={loading || undefined}
    >
      <div className="ui-stat__top">
        <span id={labelId} className="ui-stat__label">
          {label}
        </span>
        {icon && (
          <span className="ui-stat__icon" aria-hidden="true">
            {icon}
          </span>
        )}
      </div>
      <div className="ui-stat__body">
        <div className="ui-stat__value">{loading ? <Skeleton width="7ch" height={30} /> : value}</div>
        {trend && !loading && (
          <Sparkline
            className="ui-stat__spark"
            values={trend.values}
            label={trend.label}
            width={88}
            height={28}
          />
        )}
      </div>
      {(delta || measurement || hint) && (
        <div className="ui-stat__footer">
          {delta && !loading && (
            <span className={clsx('ui-stat__delta', `ui-stat__delta--${deltaTone}`)}>
              <span className="visually-hidden">{directionWord} </span>
              {direction === 'up' ? (
                <ArrowUpRight aria-hidden="true" />
              ) : direction === 'down' ? (
                <ArrowDownRight aria-hidden="true" />
              ) : (
                <ArrowRight aria-hidden="true" />
              )}
              {deltaText}
              {delta.label && <span className="visually-hidden"> {delta.label}</span>}
            </span>
          )}
          {delta?.label && !loading && <span aria-hidden="true">{delta.label}</span>}
          {measurement && <MeasurementTag measurement={measurement} />}
          {hint && <span>{hint}</span>}
        </div>
      )}
    </div>
  );
}
