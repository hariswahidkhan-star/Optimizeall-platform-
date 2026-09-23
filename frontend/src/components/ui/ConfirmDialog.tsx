import { AlertTriangle, ShieldCheck } from 'lucide-react';
import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { errorMessage } from '@/lib/api/errors';
import { Alert } from './Alert';
import { Button } from './Button';
import { Dialog } from './Dialog';
import { FormField } from './FormField';
import { Input } from './Input';
import { Textarea } from './Textarea';

export interface ConfirmResult {
  /** Trimmed reason text ('' when no reason was requested). */
  reason: string;
}

export interface ConfirmDialogProps {
  open: boolean;
  onClose: () => void;
  /** Runs the action. May return a promise: the dialog shows a busy state and surfaces errors inline. */
  onConfirm: (result: ConfirmResult) => void | Promise<void>;
  title: ReactNode;
  description?: ReactNode;
  /** Extra content (e.g. a summary of what will change). */
  children?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: 'danger' | 'primary';
  /** Require a written reason (sensitive, audited actions). */
  requireReason?: boolean;
  reasonLabel?: string;
  reasonHint?: ReactNode;
  /** Minimum reason length when required. */
  reasonMinLength?: number;
  /** When set, the user must type exactly this text to enable the confirm button. */
  confirmText?: string;
}

/**
 * Confirmation for sensitive actions. Optionally requires the user to type a phrase and to give a reason, which is
 * passed to `onConfirm` for the audit log.
 */
export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  title,
  description,
  children,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  tone = 'primary',
  requireReason = false,
  reasonLabel = 'Reason',
  reasonHint = 'Recorded in the audit log.',
  reasonMinLength = 3,
  confirmText,
}: ConfirmDialogProps) {
  const [reason, setReason] = useState('');
  const [typed, setTyped] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showReasonError, setShowReasonError] = useState(false);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const formId = useId();

  useEffect(() => {
    if (open) {
      setReason('');
      setTyped('');
      setError(null);
      setShowReasonError(false);
      setSubmitting(false);
    }
  }, [open]);

  const reasonValid = !requireReason || reason.trim().length >= reasonMinLength;
  const typedValid = !confirmText || typed.trim() === confirmText;

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (!reasonValid) {
      setShowReasonError(true);
      return;
    }
    if (!typedValid || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm({ reason: requireReason ? reason.trim() : '' });
      onClose();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={title}
      description={description}
      role="alertdialog"
      size="sm"
      icon={tone === 'danger' ? <AlertTriangle /> : <ShieldCheck />}
      tone={tone === 'danger' ? 'danger' : 'brand'}
      dismissible={!submitting}
      initialFocusRef={requireReason || confirmText ? undefined : cancelRef}
      footer={
        <>
          <Button ref={cancelRef} variant="secondary" onClick={onClose} disabled={submitting}>
            {cancelLabel}
          </Button>
          <Button
            type="submit"
            form={formId}
            variant={tone === 'danger' ? 'danger' : 'primary'}
            loading={submitting}
            disabled={!typedValid}
          >
            {confirmLabel}
          </Button>
        </>
      }
    >
      <form id={formId} className="stack" onSubmit={handleSubmit} noValidate>
        {children}
        {error && (
          <Alert tone="danger" role="alert">
            {error}
          </Alert>
        )}
        {requireReason && (
          <FormField
            label={reasonLabel}
            hint={reasonHint}
            required
            error={
              showReasonError && !reasonValid
                ? `Enter a reason (at least ${reasonMinLength} characters).`
                : null
            }
          >
            <Textarea
              value={reason}
              rows={3}
              maxLength={500}
              onChange={(e) => setReason(e.target.value)}
              onBlur={() => setShowReasonError(true)}
            />
          </FormField>
        )}
        {confirmText && (
          <FormField
            label={
              <>
                Type <strong>{confirmText}</strong> to confirm
              </>
            }
          >
            <Input
              value={typed}
              autoComplete="off"
              spellCheck={false}
              onChange={(e) => setTyped(e.target.value)}
            />
          </FormField>
        )}
      </form>
    </Dialog>
  );
}
