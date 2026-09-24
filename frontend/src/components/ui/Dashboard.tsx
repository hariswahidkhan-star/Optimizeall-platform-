import clsx from 'clsx';
import type { CSSProperties, HTMLAttributes, ReactNode } from 'react';
import { Link } from 'react-router-dom';
import './display.css';
import './dashboard.css';

/**
 * Dashboard building blocks shared by every portal home:
 *
 * - `StatGrid` lays out KPI `Stat` tiles; `strip` joins them into one hairline-divided surface.
 * - `DashboardGrid` is a 12-column grid (one column below lg); children take `span` classes via `DashboardCell`.
 * - `MeterList` is a labelled list of horizontal bars whose values are always printed as text (bars are decorative).
 */
export function StatGrid({
  strip,
  min,
  className,
  style,
  ...rest
}: HTMLAttributes<HTMLDivElement> & { strip?: boolean; min?: string }) {
  return (
    <div
      className={clsx('ui-stat-grid', strip && 'ui-stat-grid--strip', className)}
      style={min ? ({ '--stat-min': min, ...style } as CSSProperties) : style}
      {...rest}
    />
  );
}

export function DashboardGrid({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={clsx('ui-dash-grid', className)} {...rest} />;
}

export function DashboardCell({
  span = 12,
  className,
  ...rest
}: HTMLAttributes<HTMLDivElement> & { span?: 3 | 4 | 5 | 6 | 7 | 8 | 12 }) {
  return <div className={clsx('ui-dash-cell', `ui-dash-cell--${span}`, className)} {...rest} />;
}

export interface MeterItem {
  id: string;
  label: ReactNode;
  /** Link for the label (internal route). */
  to?: string;
  /** 0–100; values above 100 are drawn full and flagged. */
  percent: number;
  /** The figure printed next to the bar (e.g. "65% utilized"). */
  valueText: ReactNode;
  meta?: ReactNode;
}

export function MeterList({
  items,
  label,
  className,
  tone = 'primary',
}: {
  items: MeterItem[];
  /** Accessible name of the list. */
  label: string;
  className?: string;
  tone?: 'primary' | 'accent';
}) {
  return (
    <ul className={clsx('ui-meter-list', `ui-meter-list--${tone}`, className)} aria-label={label}>
      {items.map((item) => {
        const pct = Math.max(0, Math.min(100, item.percent));
        return (
          <li key={item.id} className="ui-meter-list__item">
            <div className="ui-meter-list__row">
              <span className="ui-meter-list__label">
                {item.to ? (
                  <Link className="ui-meter-list__link" to={item.to}>
                    {item.label}
                  </Link>
                ) : (
                  item.label
                )}
              </span>
              <span className="ui-meter-list__value">{item.valueText}</span>
            </div>
            <span className="ui-meter-list__track" aria-hidden="true">
              <span className="ui-meter-list__fill" style={{ width: `${pct}%` }} />
            </span>
            {item.meta && <span className="ui-meter-list__meta">{item.meta}</span>}
          </li>
        );
      })}
    </ul>
  );
}
