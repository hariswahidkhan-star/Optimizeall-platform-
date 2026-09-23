import clsx from 'clsx';
import type { ReactNode } from 'react';
import './display.css';

export interface KeyValueItem {
  label: ReactNode;
  value: ReactNode;
  id?: string;
}

export interface KeyValueListProps {
  items: KeyValueItem[];
  /** `stacked` (label above value, responsive grid) or `inline` (label left, value right, divided rows). */
  layout?: 'stacked' | 'inline';
  className?: string;
}

/** Description list for record details. */
export function KeyValueList({ items, layout = 'stacked', className }: KeyValueListProps) {
  return (
    <dl className={clsx('ui-kv', layout === 'inline' ? 'ui-kv--inline' : 'ui-kv--2', className)}>
      {items.map((item, index) => (
        <div key={item.id ?? index} className="ui-kv__row">
          <dt className="ui-kv__key">{item.label}</dt>
          <dd className="ui-kv__value">{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}
