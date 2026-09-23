import { ExternalLink as ExternalIcon, RefreshCw } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { Alert, Button } from '@/components/ui';
import { describeReviewError } from '../api/errors';
import { SafeExternalLink } from '@/components/SafeExternalLink';

/** Inline error for a failed reviewer action, with a "Refresh" action when the data on screen is stale. */
export function ActionError({
  error,
  onRefresh,
  refreshing,
  onDismiss,
}: {
  error: unknown;
  onRefresh?: () => void;
  refreshing?: boolean;
  onDismiss?: () => void;
}) {
  const info = describeReviewError(error);
  return (
    <Alert
      tone={info.code === 'review.claimed_by_other' ? 'warning' : 'danger'}
      role="alert"
      title={info.title}
      onDismiss={onDismiss}
      actions={
        info.refresh && onRefresh ? (
          <Button
            size="sm"
            variant="secondary"
            leadingIcon={<RefreshCw />}
            onClick={onRefresh}
            loading={refreshing}
          >
            Refresh
          </Button>
        ) : undefined
      }
    >
      {info.description}
    </Alert>
  );
}

/** Link to a participant's post / profile on the social network: new tab, no opener, no referrer. */
export function ExternalLink({
  href,
  children,
  className,
  onOpen,
}: {
  href: string;
  children: ReactNode;
  className?: string;
  onOpen?: () => void;
}) {
  return (
    <SafeExternalLink
      href={href}
      nofollow
      className={className ?? 'ui-link rv-external'}
      fallback={<span className={className}>{children}</span>}
      onClick={onOpen}
      onAuxClick={onOpen}
    >
      {children}
      <ExternalIcon aria-hidden="true" className="rv-external__icon" />
      <span className="visually-hidden"> (opens in a new tab)</span>
    </SafeExternalLink>
  );
}

/** Current time, re-rendered every `intervalMs`. */
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}

/** "12:05" (mm:ss) or "1:02:05" for a positive number of milliseconds. */
export function formatCountdown(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n: number) => String(n).padStart(2, '0');
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

/** Hours → "45 min", "5.3 h", "2.1 days". */
export function formatAgeHours(hours: number | null | undefined): string {
  if (hours === null || hours === undefined) return '—';
  if (hours < 1) return `${Math.max(1, Math.round(hours * 60))} min`;
  if (hours < 48) return `${hours.toFixed(1)} h`;
  return `${(hours / 24).toFixed(1)} days`;
}
