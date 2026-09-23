import clsx from 'clsx';
import { AlertTriangle, CheckCircle2, Info, Megaphone, X, XCircle } from 'lucide-react';
import type { ReactNode } from 'react';
import { IconButton } from './IconButton';
import type { Tone } from './tones';
import './feedback.css';

export interface AlertProps {
  tone?: Tone;
  title?: ReactNode;
  children?: ReactNode;
  /** Buttons/links shown under the message. */
  actions?: ReactNode;
  icon?: ReactNode | false;
  onDismiss?: () => void;
  /**
   * `alert` interrupts screen readers — use for errors that appear after a user action. `status` is polite.
   * Omit for static callouts that are part of the page.
   */
  role?: 'alert' | 'status';
  className?: string;
  id?: string;
}

const ICONS: Record<Tone, ReactNode> = {
  neutral: <Info />,
  brand: <Megaphone />,
  info: <Info />,
  success: <CheckCircle2 />,
  warning: <AlertTriangle />,
  danger: <XCircle />,
};

/** Inline message / callout. */
export function Alert({
  tone = 'info',
  title,
  children,
  actions,
  icon,
  onDismiss,
  role,
  className,
  id,
}: AlertProps) {
  return (
    <div id={id} role={role} className={clsx('ui-alert', `tone-${tone}`, className)}>
      {icon !== false && (
        <span className="ui-alert__icon" aria-hidden="true">
          {icon ?? ICONS[tone]}
        </span>
      )}
      <div className="ui-alert__body">
        {title && <p className="ui-alert__title">{title}</p>}
        {children && <div className="ui-alert__content">{children}</div>}
        {actions && <div className="ui-alert__actions cluster">{actions}</div>}
      </div>
      {onDismiss && (
        <IconButton
          className="ui-alert__dismiss"
          size="sm"
          label="Dismiss"
          icon={<X />}
          onClick={onDismiss}
        />
      )}
    </div>
  );
}
