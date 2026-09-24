import clsx from 'clsx';
import { Inbox } from 'lucide-react';
import type { ReactNode } from 'react';
import './feedback.css';

export interface EmptyStateProps {
  icon?: ReactNode;
  title: ReactNode;
  description?: ReactNode;
  /** Primary call to action (Button / ButtonLink). */
  action?: ReactNode;
  compact?: boolean;
  /** Heading level for the title (defaults to h2; use h3 inside cards, h1 when it is the whole page). */
  headingLevel?: 1 | 2 | 3 | 4;
  className?: string;
}

export function EmptyState({
  icon,
  title,
  description,
  action,
  compact,
  headingLevel = 2,
  className,
}: EmptyStateProps) {
  const Heading = `h${headingLevel}` as const;
  return (
    <div className={clsx('ui-state', compact && 'ui-state--compact', className)}>
      <span className="ui-state__art" aria-hidden="true">
        {/* Soft concentric rings (an echo of the logo's rings) behind the icon tile. */}
        <svg className="ui-state__rings" viewBox="0 0 160 160" width="160" height="160">
          <circle cx="80" cy="80" r="36" />
          <circle cx="80" cy="80" r="54" />
          <circle cx="80" cy="80" r="72" />
        </svg>
        <span className="ui-state__icon">{icon ?? <Inbox />}</span>
      </span>
      <Heading className="ui-state__title">{title}</Heading>
      {description && <p className="ui-state__description">{description}</p>}
      {action && <div className="ui-state__actions">{action}</div>}
    </div>
  );
}
