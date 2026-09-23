import { Check, Copy } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { Button } from './Button';
import './display.css';

export interface CopyFieldProps {
  label: string;
  value: string;
  /** Hide the visible label (still the accessible name). */
  hideLabel?: boolean;
  onCopied?: () => void;
  className?: string;
}

async function writeClipboard(text: string, fallbackInput: HTMLInputElement | null): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Fall through to the selection-based fallback (e.g. insecure context).
  }
  if (!fallbackInput) return false;
  fallbackInput.select();
  try {
    return document.execCommand('copy');
  } catch {
    return false;
  }
}

/** Read-only value with a copy button (referral codes, tracking links). Announces the result politely. */
export function CopyField({ label, value, hideLabel, onCopied, className }: CopyFieldProps) {
  const id = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [status, setStatus] = useState<'idle' | 'copied' | 'failed'>('idle');

  useEffect(() => {
    if (status === 'idle') return;
    const timer = setTimeout(() => setStatus('idle'), 2500);
    return () => clearTimeout(timer);
  }, [status]);

  const copy = async () => {
    const ok = await writeClipboard(value, inputRef.current);
    setStatus(ok ? 'copied' : 'failed');
    if (ok) onCopied?.();
  };

  return (
    <div className={className}>
      <label htmlFor={id} className={hideLabel ? 'visually-hidden' : 'ui-copy__label'}>
        {label}
      </label>
      <div className="ui-copy">
        <input
          ref={inputRef}
          id={id}
          className="ui-copy__value"
          value={value}
          readOnly
          onFocus={(e) => e.currentTarget.select()}
        />
        <Button variant="secondary" leadingIcon={status === 'copied' ? <Check /> : <Copy />} onClick={copy}>
          {status === 'copied' ? 'Copied' : 'Copy'}
        </Button>
      </div>
      <span className="visually-hidden" aria-live="polite">
        {status === 'copied'
          ? `${label} copied to clipboard`
          : status === 'failed'
            ? 'Copy failed — select the text and copy it manually'
            : ''}
      </span>
    </div>
  );
}
