import clsx from 'clsx';
import { AlertCircle } from 'lucide-react';
import { useId, useMemo, type ReactNode } from 'react';
import { FieldContext } from './fieldContext';
import './forms.css';

export interface FormFieldProps {
  label: ReactNode;
  /** The control (Input, Select, Textarea, ...). It receives id/aria props from context. */
  children: ReactNode;
  hint?: ReactNode;
  /** One message or several (e.g. server password-policy errors). */
  error?: string | string[] | null;
  required?: boolean;
  /** Shows "Optional" next to the label. */
  optional?: boolean;
  /** Explicit control id (otherwise generated). */
  id?: string;
  /** Extra content on the label row (e.g. "Forgot password?" link). */
  labelAside?: ReactNode;
  /** Visually hide the label (it stays the accessible name). */
  hideLabel?: boolean;
  className?: string;
}

/**
 * Label + control + hint + error with correct wiring: `<label for>`, `aria-describedby` pointing at hint and error,
 * `aria-invalid` when there is an error.
 */
export function FormField({
  label,
  children,
  hint,
  error,
  required = false,
  optional,
  id: explicitId,
  labelAside,
  hideLabel,
  className,
}: FormFieldProps) {
  const generated = useId();
  const id = explicitId ?? `field-${generated}`;
  const hintId = hint ? `${id}-hint` : undefined;
  const errors = (Array.isArray(error) ? error : error ? [error] : []).filter(Boolean);
  const errorId = errors.length > 0 ? `${id}-error` : undefined;

  const context = useMemo(
    () => ({
      id,
      describedBy: [errorId, hintId].filter(Boolean).join(' ') || undefined,
      invalid: errors.length > 0,
      required,
    }),
    [id, errorId, hintId, errors.length, required],
  );

  return (
    <div className={clsx('ui-field', className)}>
      <div className={clsx('ui-field__label-row', hideLabel && 'visually-hidden')}>
        <label htmlFor={id} className="ui-field__label">
          {label}
          {optional && <span className="ui-field__optional"> (optional)</span>}
        </label>
        {labelAside}
      </div>
      <FieldContext.Provider value={context}>{children}</FieldContext.Provider>
      {errors.length > 0 && (
        <div id={errorId} className="ui-field__error">
          <AlertCircle aria-hidden="true" />
          {errors.length === 1 ? (
            <span>{errors[0]}</span>
          ) : (
            <ul>
              {errors.map((message) => (
                <li key={message}>{message}</li>
              ))}
            </ul>
          )}
        </div>
      )}
      {hint && (
        <div id={hintId} className="ui-field__hint">
          {hint}
        </div>
      )}
    </div>
  );
}
