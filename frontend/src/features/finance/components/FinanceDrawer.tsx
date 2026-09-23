import { X } from 'lucide-react';
import { useId, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { IconButton } from '@/components/ui';
import { useModalBehavior } from '@/components/ui/useModalBehavior';
import '../finance.css';

export interface FinanceDrawerProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  description?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}

/**
 * Right-hand detail panel (row details, participant breakdown). A local variant of the design-system Drawer: wider,
 * with a description line, a sticky footer for actions and a "Close panel" label. Full screen on phones.
 */
export function FinanceDrawer({ open, onClose, title, description, children, footer }: FinanceDrawerProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const descriptionId = useId();
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
        aria-describedby={description ? descriptionId : undefined}
        tabIndex={-1}
        className="ui-drawer ui-drawer--right fin-drawer"
      >
        <div className="fin-drawer__header">
          <div className="fin-drawer__titles">
            <h2 id={titleId} className="fin-drawer__title">
              {title}
            </h2>
            {description && (
              <p id={descriptionId} className="fin-drawer__description">
                {description}
              </p>
            )}
          </div>
          <IconButton label="Close panel" icon={<X />} onClick={onClose} />
        </div>
        <div className="fin-drawer__body">{children}</div>
        {footer && <div className="fin-drawer__footer">{footer}</div>}
      </div>
    </>,
    document.body,
  );
}
