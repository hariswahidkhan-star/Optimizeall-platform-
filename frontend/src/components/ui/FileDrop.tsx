import clsx from 'clsx';
import { AlertCircle, ImageUp, RefreshCw, Trash2 } from 'lucide-react';
import { useEffect, useId, useRef, useState, type DragEvent, type ReactNode } from 'react';
import { formatBytes } from '@/lib/format/text';
import { Button } from './Button';
import { DEFAULT_MAX_BYTES, IMAGE_TYPES, describeTypes, validateFile } from './fileValidation';
import './forms.css';
import './FileDrop.css';

export interface FileDropProps {
  label: ReactNode;
  value: File | null;
  onChange: (file: File | null) => void;
  hint?: ReactNode;
  /** Accepted MIME types (default PNG, JPEG, WebP). */
  accept?: readonly string[];
  maxSizeBytes?: number;
  /** External (e.g. server) error. */
  error?: string | null;
  disabled?: boolean;
  required?: boolean;
  className?: string;
}

/**
 * Image picker with drag & drop, click/keyboard selection (a real file input), client-side type/size validation and
 * a preview. Validation errors are announced and linked to the input.
 */
export function FileDrop({
  label,
  value,
  onChange,
  hint,
  accept = IMAGE_TYPES,
  maxSizeBytes = DEFAULT_MAX_BYTES,
  error,
  disabled,
  required,
  className,
}: FileDropProps) {
  const id = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [preview, setPreview] = useState<string | null>(null);
  const shownError = localError ?? error ?? null;
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;

  useEffect(() => {
    if (!value) {
      setPreview(null);
      return;
    }
    const url = URL.createObjectURL(value);
    setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [value]);

  const handleFile = (file: File | undefined) => {
    if (!file) return;
    const problem = validateFile(file, accept, maxSizeBytes);
    setLocalError(problem);
    if (!problem) onChange(file);
  };

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragging(false);
    if (disabled) return;
    handleFile(event.dataTransfer.files?.[0]);
  };

  const defaultHint = `${describeTypes(accept)} up to ${formatBytes(maxSizeBytes)}.`;

  return (
    <div className={clsx('ui-filedrop', className)}>
      <p className="ui-filedrop__label" id={`${id}-label`}>
        {label}
        {required && <span className="visually-hidden"> (required)</span>}
      </p>
      <div
        className={clsx(
          'ui-filedrop__zone',
          dragging && 'ui-filedrop__zone--dragging',
          shownError && 'ui-filedrop__zone--invalid',
          value && 'ui-filedrop__zone--filled',
          disabled && 'ui-filedrop__zone--disabled',
        )}
        onDragEnter={(e) => {
          e.preventDefault();
          if (!disabled) setDragging(true);
        }}
        onDragOver={(e) => e.preventDefault()}
        onDragLeave={(e) => {
          if (!e.currentTarget.contains(e.relatedTarget as Node)) setDragging(false);
        }}
        onDrop={onDrop}
      >
        <input
          ref={inputRef}
          id={id}
          type="file"
          className="ui-filedrop__input"
          accept={accept.join(',')}
          disabled={disabled}
          required={required && !value}
          aria-labelledby={`${id}-label`}
          aria-describedby={[shownError ? errorId : null, hintId].filter(Boolean).join(' ')}
          aria-invalid={shownError ? true : undefined}
          onChange={(e) => {
            handleFile(e.target.files?.[0]);
            e.target.value = '';
          }}
        />
        {value && preview ? (
          <div className="ui-filedrop__preview">
            <img src={preview} alt={`Preview of ${value.name}`} />
            <div className="ui-filedrop__meta">
              <p className="ui-filedrop__name">{value.name}</p>
              <p className="ui-filedrop__size">{formatBytes(value.size)}</p>
              <div className="cluster">
                <Button
                  size="sm"
                  variant="secondary"
                  leadingIcon={<RefreshCw />}
                  onClick={() => inputRef.current?.click()}
                  disabled={disabled}
                >
                  Replace
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  leadingIcon={<Trash2 />}
                  onClick={() => {
                    setLocalError(null);
                    onChange(null);
                  }}
                  disabled={disabled}
                >
                  Remove
                </Button>
              </div>
            </div>
          </div>
        ) : (
          <label htmlFor={id} className="ui-filedrop__prompt">
            <span className="ui-filedrop__icon" aria-hidden="true">
              <ImageUp />
            </span>
            <span className="ui-filedrop__cta">
              <span className="ui-filedrop__cta-strong">Choose a file</span> or drag it here
            </span>
          </label>
        )}
      </div>
      {shownError && (
        <p id={errorId} className="ui-field__error" role="alert">
          <AlertCircle aria-hidden="true" />
          <span>{shownError}</span>
        </p>
      )}
      <p id={hintId} className="ui-field__hint">
        {hint ?? defaultHint}
      </p>
    </div>
  );
}
