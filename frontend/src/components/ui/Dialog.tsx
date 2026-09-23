import clsx from 'clsx';
import { X } from 'lucide-react';
import { useId, useRef, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { IconButton } from './IconButton';
import { useModalBehavior } from './useModalBehavior';
import './overlay.css';

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  /** Action buttons, right-aligned (stacked full width on phones). */
  footer?: ReactNode;
  size?: 'sm' | 'md' | 'lg';
  /** Element to focus when the dialog opens (defaults to the first focusable element). */
  initialFocusRef?: RefObject<HTMLElement | null>;
  /** When false, Escape/backdrop/close button do nothing (e.g. while a request is running). */
  dismissible?: boolean;
  /** Decorative icon next to the title. */
  icon?: ReactNode;
  /** Tone for the icon chip. */
  tone?: 'brand' | 'danger' | 'warning' | 'success' | 'info';
  /** role=alertdialog for confirmations that interrupt the user. */
  role?: 'dialog' | 'alertdialog';
  className?: string;
}

/**
 * Modal dialog rendered in a portal: aria-modal, labelled by its title, focus trapped while open, Escape and backdrop
 * close it, and focus returns to the trigger afterwards. On phones it docks to the bottom as a sheet.
 */
export function Dialog({
  open,
  onClose,
  title,
  description,
  children,
  footer,
  size = 'md',
  initialFocusRef,
  dismissible = true,
  icon,
  tone = 'brand',
  role = 'dialog',
  className,
}: DialogProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const descriptionId = useId();
  const close = () => {
    if (dismissible) onClose();
  };
  useModalBehavior(open, panelRef, { onClose: close, initialFocusRef });

  if (!open) return null;

  return createPortal(
    <div
      role="presentation"
      className="ui-backdrop ui-backdrop--sheet"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) close();
      }}
    >
      <div
        ref={panelRef}
        role={role}
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        tabIndex={-1}
        className={clsx('ui-dialog', size !== 'md' && `ui-dialog--${size}`, className)}
      >
        <div className="ui-dialog__header">
          {icon && (
            <span className={clsx('ui-dialog__icon', `tone-${tone}`)} aria-hidden="true">
              {icon}
            </span>
          )}
          <div className="ui-dialog__titles">
            <h2 id={titleId} className="ui-dialog__title">
              {title}
            </h2>
            {description && (
              <p id={descriptionId} className="ui-dialog__description">
                {description}
              </p>
            )}
          </div>
          {dismissible && (
            <IconButton className="ui-dialog__close" label="Close dialog" icon={<X />} onClick={onClose} />
          )}
        </div>
        {children && <div className="ui-dialog__body">{children}</div>}
        {footer && <div className="ui-dialog__footer">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}
