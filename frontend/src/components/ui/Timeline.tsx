import clsx from 'clsx';
import type { ReactNode } from 'react';
import { DateTime } from './DateTime';
import type { Tone } from './tones';
import './display.css';
import './feedback.css';

export interface TimelineItem {
  id: string;
  title: ReactNode;
  description?: ReactNode;
  /** ISO timestamp. */
  timestamp: string;
  /** Who made the change (e.g. reviewer name). */
  actor?: ReactNode;
  tone?: Tone;
}

export interface TimelineProps {
  items: TimelineItem[];
  /** Accessible name for the list. */
  label: string;
  className?: string;
}

/** Status history, oldest first (pass items in the order to display). */
export function Timeline({ items, label, className }: TimelineProps) {
  return (
    <ol className={clsx('ui-timeline', className)} aria-label={label}>
      {items.map((item) => (
        <li key={item.id} className={clsx('ui-timeline__item', `tone-${item.tone ?? 'neutral'}`)}>
          <span className="ui-timeline__dot" aria-hidden="true" />
          <div>
            <div className="ui-timeline__head">
              <p className="ui-timeline__title">{item.title}</p>
              <DateTime value={item.timestamp} className="ui-timeline__time" />
            </div>
            {item.description && <div className="ui-timeline__body">{item.description}</div>}
            {item.actor && <p className="ui-timeline__actor">{item.actor}</p>}
          </div>
        </li>
      ))}
    </ol>
  );
}
