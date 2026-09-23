import { AlertTriangle, ShieldCheck } from 'lucide-react';
import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { Alert, Button, Dialog } from '@/components/ui';
import { financeErrorMessage } from '../api/errors';

export interface FormDialogProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  description?: ReactNode;
  children: ReactNode;
  /**
   * Runs the action. Resolve to close (return `false` to keep the dialog open, e.g. to show results); throw to show
   * the error inline. Concurrent submits are ignored until it settles (double-submit prevention).
   */
  onSubmit: () => Promise<boolean | void>;
  submitLabel: string;
  cancelLabel?: string;
  tone?: 'primary' | 'danger';
  /** Disables the submit button (e.g. until a typed confirmation matches). */
  canSubmit?: boolean;
  size?: 'sm' | 'md' | 'lg';
  /** Replaces the default inline error rendering (e.g. to add a Refresh action for a known code). */
  renderError?: (error: unknown) => ReactNode;
  /** Extra footer content shown before the buttons. */
  footerExtra?: ReactNode;
  /** Hide the submit button (e.g. after a result is shown). */
  hideSubmit?: boolean;
  sensitive?: boolean;
}

/**
 * Dialog wrapping a form: busy state, inline human error, a ref-based guard so double clicks / Enter repeats never
 * send the request twice, and no dismissal while the request is running.
 */
export function FormDialog({
  open,
  onClose,
  title,
  description,
  children,
  onSubmit,
  submitLabel,
  cancelLabel = 'Cancel',
  tone = 'primary',
  canSubmit = true,
  size = 'md',
  renderError,
  footerExtra,
  hideSubmit,
  sensitive,
}: FormDialogProps) {
  const formId = useId();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const inFlight = useRef(false);

  useEffect(() => {
    if (open) {
      setError(null);
      setSubmitting(false);
      inFlight.current = false;
    }
  }, [open]);

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (inFlight.current || !canSubmit) return;
    inFlight.current = true;
    setSubmitting(true);
    setError(null);
    try {
      const keepOpen = (await onSubmit()) === false;
      if (!keepOpen) onClose();
    } catch (err) {
      setError(err ?? new Error('Something went wrong.'));
    } finally {
      inFlight.current = false;
      setSubmitting(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={title}
      description={description}
      size={size}
      dismissible={!submitting}
      role={sensitive ? 'alertdialog' : 'dialog'}
      icon={sensitive ? tone === 'danger' ? <AlertTriangle /> : <ShieldCheck /> : undefined}
      tone={tone === 'danger' ? 'danger' : 'brand'}
      footer={
        <>
          {footerExtra}
          <Button variant="secondary" onClick={onClose} disabled={submitting}>
            {hideSubmit ? 'Close' : cancelLabel}
          </Button>
          {!hideSubmit && (
            <Button
              type="submit"
              form={formId}
              variant={tone === 'danger' ? 'danger' : 'primary'}
              loading={submitting}
              disabled={!canSubmit}
            >
              {submitLabel}
            </Button>
          )}
        </>
      }
    >
      <form id={formId} className="stack" onSubmit={handleSubmit} noValidate>
        {children}
        {error !== null &&
          (renderError?.(error) ?? (
            <Alert tone="danger" role="alert">
              {financeErrorMessage(error)}
            </Alert>
          ))}
      </form>
    </Dialog>
  );
}
