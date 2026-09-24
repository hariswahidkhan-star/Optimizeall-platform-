import clsx from 'clsx';
import { AlertTriangle, CheckCircle2, Info, X, XCircle } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { IconButton } from './IconButton';
import { ToastContext, type ToastApi, type ToastInput, type ToastTone } from './toastContext';
import './feedback.css';
import './overlay.css';

interface ToastItem extends Required<Pick<ToastInput, 'tone' | 'duration'>> {
  id: string;
  title: ReactNode;
  description?: ReactNode;
}

const ICONS: Record<ToastTone, ReactNode> = {
  success: <CheckCircle2 />,
  danger: <XCircle />,
  warning: <AlertTriangle />,
  info: <Info />,
};

const MAX_VISIBLE = 4;
let counter = 0;

function ToastView({ toast, onDismiss }: { toast: ToastItem; onDismiss: (id: string) => void }) {
  const [paused, setPaused] = useState(false);
  useEffect(() => {
    if (toast.duration <= 0 || paused) return;
    const timer = setTimeout(() => onDismiss(toast.id), toast.duration);
    return () => clearTimeout(timer);
  }, [toast.id, toast.duration, paused, onDismiss]);

  return (
    <li
      className={clsx('ui-toast', `tone-${toast.tone}`)}
      onMouseEnter={() => setPaused(true)}
      onMouseLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)}
      onBlur={() => setPaused(false)}
    >
      <span className="ui-toast__icon" aria-hidden="true">
        {ICONS[toast.tone]}
      </span>
      <div className="ui-toast__body">
        <p className="ui-toast__title">{toast.title}</p>
        {toast.description && <p className="ui-toast__description">{toast.description}</p>}
      </div>
      <IconButton size="sm" label="Dismiss notification" icon={<X />} onClick={() => onDismiss(toast.id)} />
    </li>
  );
}

/**
 * Toast notifications in a polite live region (announced without stealing focus). Hovering or focusing a toast
 * pauses its auto-dismiss timer; error toasts do not auto-dismiss.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [portalReady, setPortalReady] = useState(false);

  // Render the live region after mount so it exists (empty) before the first toast is announced.
  useEffect(() => setPortalReady(true), []);

  const dismiss = useCallback((id: string) => {
    setToasts((current) => current.filter((t) => t.id !== id));
  }, []);

  const show = useCallback((input: ToastInput) => {
    counter += 1;
    const id = `toast-${counter}`;
    const item: ToastItem = {
      id,
      title: input.title,
      description: input.description,
      tone: input.tone ?? 'info',
      // Errors stay until dismissed (WCAG 2.2.1): they explain why something failed and may need more time to read.
      duration: input.duration ?? (input.tone === 'danger' ? 0 : 5000),
    };
    setToasts((current) => [...current, item].slice(-MAX_VISIBLE));
    return id;
  }, []);

  const api = useMemo<ToastApi>(
    () => ({
      show,
      dismiss,
      success: (title, description) => show({ title, description, tone: 'success' }),
      error: (title, description) => show({ title, description, tone: 'danger' }),
      info: (title, description) => show({ title, description, tone: 'info' }),
    }),
    [show, dismiss],
  );

  return (
    <ToastContext.Provider value={api}>
      {children}
      {portalReady &&
        createPortal(
          <section aria-label="Notifications">
            <ol className="ui-toast-region" aria-live="polite" aria-relevant="additions text">
              {toasts.map((toast) => (
                <ToastView key={toast.id} toast={toast} onDismiss={dismiss} />
              ))}
            </ol>
          </section>,
          document.body,
        )}
    </ToastContext.Provider>
  );
}
