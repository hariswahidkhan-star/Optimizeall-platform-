import clsx from 'clsx';
import { ArrowDownRight, ArrowRight, ArrowUpRight } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { browserLocale } from '@/lib/format/locale';
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
  /** Change vs. previous period. `value` is a signed ratio (0.12 = +12%) unless `display` is given. */
  delta?: { value: number; display?: string; label?: string; positiveIsGood?: boolean };
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
export function Stat({ label, value, measurement, delta, icon, hint, loading, className }: StatProps) {
  const labelId = useId();
  const direction = !delta ? 'flat' : delta.value > 0 ? 'up' : delta.value < 0 ? 'down' : 'flat';
  const good = delta?.positiveIsGood ?? true;
  const deltaTone = direction === 'flat' ? 'flat' : (direction === 'up') === good ? 'up' : 'down';
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
      <div className="ui-stat__value">{loading ? <Skeleton width="60%" height={30} /> : value}</div>
      {(delta || measurement || hint) && (
        <div className="ui-stat__footer">
          {delta && !loading && (
            <span className={clsx('ui-stat__delta', `ui-stat__delta--${deltaTone}`)}>
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
