import clsx from 'clsx';
import { X } from 'lucide-react';
import { useId, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { IconButton } from './IconButton';
import { useModalBehavior } from './useModalBehavior';
import './overlay.css';

export interface DrawerProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  /** Hide the visible title (still labels the drawer). */
  hideTitle?: boolean;
  /** Content shown in the header in place of the visible title (e.g. a logo). */
  headerContent?: ReactNode;
  side?: 'left' | 'right';
  children: ReactNode;
  className?: string;
}

/** Off-canvas panel (mobile navigation, filters). Modal: focus trapped, Escape/backdrop close it. */
export function Drawer({
  open,
  onClose,
  title,
  hideTitle,
  headerContent,
  side = 'left',
  children,
  className,
}: DrawerProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  useModalBehavior(open, panelRef, { onClose });

  if (!open) return null;

  return createPortal(
    <>
      <div className="ui-drawer-backdrop" aria-hidden="true" onMouseDown={onClose} />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className={clsx('ui-drawer', `ui-drawer--${side}`, className)}
      >
        <div className="ui-drawer__header">
          {headerContent}
          <h2
            id={titleId}
            className={clsx('ui-drawer__title', (hideTitle || headerContent) && 'visually-hidden')}
          >
            {title}
          </h2>
          <IconButton label="Close menu" icon={<X />} onClick={onClose} />
        </div>
        <div className="ui-drawer__body">{children}</div>
      </div>
    </>,
    document.body,
  );
}
