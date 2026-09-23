import clsx from 'clsx';
import './feedback.css';

export interface SpinnerProps {
  size?: 'sm' | 'md' | 'lg';
  /** Announced to assistive tech (role=status). Ignored when decorative. */
  label?: string;
  /** Render without a status role (e.g. inside a busy button). */
  decorative?: boolean;
  className?: string;
}

export function Spinner({ size = 'md', label = 'Loading', decorative = false, className }: SpinnerProps) {
  const spinner = (
    <span className={clsx('ui-spinner', `ui-spinner--${size}`, className)} aria-hidden="true" />
  );
  if (decorative) return spinner;
  return (
    <span role="status" style={{ display: 'inline-flex' }}>
      {spinner}
      <span className="visually-hidden">{label}</span>
    </span>
  );
}
