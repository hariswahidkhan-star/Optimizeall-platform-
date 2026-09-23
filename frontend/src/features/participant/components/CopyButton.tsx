import { Check, Copy } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/Button';

async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // fall through
  }
  const area = document.createElement('textarea');
  area.value = text;
  area.setAttribute('readonly', '');
  area.style.position = 'fixed';
  area.style.opacity = '0';
  document.body.appendChild(area);
  area.select();
  try {
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    area.remove();
  }
}

/** Copies multi-line text (captions, disclosures) and announces the result politely. */
export function CopyButton({ text, label }: { text: string; label: string }) {
  const [status, setStatus] = useState<'idle' | 'copied' | 'failed'>('idle');
  useEffect(() => {
    if (status === 'idle') return;
    const timer = setTimeout(() => setStatus('idle'), 2500);
    return () => clearTimeout(timer);
  }, [status]);

  return (
    <>
      <Button
        size="sm"
        variant="secondary"
        leadingIcon={status === 'copied' ? <Check /> : <Copy />}
        onClick={async () => setStatus((await copyText(text)) ? 'copied' : 'failed')}
        aria-label={`Copy ${label}`}
      >
        {status === 'copied' ? 'Copied' : 'Copy'}
      </Button>
      <span className="visually-hidden" aria-live="polite">
        {status === 'copied'
          ? `${label} copied to clipboard`
          : status === 'failed'
            ? 'Copy failed — select the text and copy it manually'
            : ''}
      </span>
    </>
  );
}
