import clsx from 'clsx';
import { AlertTriangle, CheckCircle2, Info, Megaphone, X, XCircle } from 'lucide-react';
import { useId, type ReactNode } from 'react';
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
   * Defaults: an alert with a `title` is `alert` for the danger tone and `status` otherwise (named by its title via
   * aria-labelledby); an untitled callout has no role.
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
  const titleId = useId();
  const resolvedRole = role ?? (title ? (tone === 'danger' ? 'alert' : 'status') : undefined);
  return (
    <div
      id={id}
      role={resolvedRole}
      aria-labelledby={title ? titleId : undefined}
      className={clsx('ui-alert', `tone-${tone}`, className)}
    >
      {icon !== false && (
        <span className="ui-alert__icon" aria-hidden="true">
          {icon ?? ICONS[tone]}
        </span>
      )}
      <div className="ui-alert__body">
        {title && (
          <p id={titleId} className="ui-alert__title">
            {title}
          </p>
        )}
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
