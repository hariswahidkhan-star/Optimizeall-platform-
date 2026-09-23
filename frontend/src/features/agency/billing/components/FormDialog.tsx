import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { Alert, Button, Dialog } from '@/components/ui';
import { billingErrorMessage } from '../lib';

export interface FormDialogProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  description?: ReactNode;
  children: ReactNode;
  /** Runs the action; resolve to close (return false to stay open), throw to show the error inline. */
  onSubmit: () => Promise<boolean | void>;
  submitLabel: string;
  tone?: 'primary' | 'danger';
  canSubmit?: boolean;
  size?: 'sm' | 'md' | 'lg';
  renderError?: (error: unknown) => ReactNode;
}

/**
 * Dialog around a form with a ref-based in-flight guard: double clicks or repeated Enter never send the request twice,
 * and the dialog can't be dismissed while the request runs.
 */
export function FormDialog({
  open,
  onClose,
  title,
  description,
  children,
  onSubmit,
  submitLabel,
  tone = 'primary',
  canSubmit = true,
  size = 'md',
  renderError,
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
      tone={tone === 'danger' ? 'danger' : 'brand'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant={tone === 'danger' ? 'danger' : 'primary'} loading={submitting} disabled={!canSubmit}>
            {submitLabel}
          </Button>
        </>
      }
    >
      <form id={formId} className="stack" onSubmit={handleSubmit} noValidate>
        {children}
        {error !== null &&
          (renderError?.(error) ?? (
            <Alert tone="danger" role="alert">
              {billingErrorMessage(error)}
            </Alert>
          ))}
      </form>
    </Dialog>
  );
}
