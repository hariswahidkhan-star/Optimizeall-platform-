import clsx from 'clsx';
import type { ReactNode } from 'react';
import type { Tone } from './tones';
import './Badge.css';
import './feedback.css';

export interface BadgeProps {
  tone?: Tone;
  size?: 'sm' | 'md';
  /** Leading status dot. */
  dot?: boolean;
  icon?: ReactNode;
  children: ReactNode;
  className?: string;
  title?: string;
}

export function Badge({ tone = 'neutral', size = 'md', dot, icon, children, className, title }: BadgeProps) {
  return (
    <span
      className={clsx('ui-badge', `tone-${tone}`, size === 'sm' && 'ui-badge--sm', className)}
      title={title}
    >
      {dot && <span className="ui-badge__dot" aria-hidden="true" />}
      {icon && <span aria-hidden="true">{icon}</span>}
      {children}
    </span>
  );
}
