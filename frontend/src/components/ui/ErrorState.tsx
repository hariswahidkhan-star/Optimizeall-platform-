import clsx from 'clsx';
import { AlertOctagon, RefreshCw, WifiOff } from 'lucide-react';
import type { ReactNode } from 'react';
import { isApiError } from '@/lib/api/errors';
import { Button } from './Button';
import './feedback.css';

export interface ErrorStateProps {
  error: unknown;
  /** Overrides the heading (defaults to a friendly heading chosen from the error). */
  title?: ReactNode;
  onRetry?: () => void;
  retrying?: boolean;
  compact?: boolean;
  headingLevel?: 1 | 2 | 3 | 4;
  action?: ReactNode;
  className?: string;
}

/** Shows an ApiError's message plus its trace id (so support can find the request in logs). */
export function ErrorState({
  error,
  title,
  onRetry,
  retrying,
  compact,
  headingLevel = 2,
  action,
  className,
}: ErrorStateProps) {
  const apiError = isApiError(error) ? error : null;
  const offline = apiError?.isNetworkError ?? false;
  const heading =
    title ??
    (offline
      ? 'You appear to be offline'
      : apiError?.status === 404
        ? 'Not available yet'
        : 'Something went wrong');
  const message =
    apiError?.title ?? (error instanceof Error ? error.message : 'An unexpected error occurred.');
  const Heading = `h${headingLevel}` as const;

  return (
    <div
      role="alert"
      className={clsx('ui-state', 'ui-state--error', compact && 'ui-state--compact', className)}
    >
      <span className="ui-state__icon" aria-hidden="true">
        {offline ? <WifiOff /> : <AlertOctagon />}
      </span>
      <Heading className="ui-state__title">{heading}</Heading>
      <p className="ui-state__description">{message}</p>
      {apiError?.traceId && (
        <p className="ui-state__trace">
          Reference: <code>{apiError.traceId}</code>
        </p>
      )}
      {(onRetry || action) && (
        <div className="ui-state__actions">
          {onRetry && (
            <Button variant="secondary" leadingIcon={<RefreshCw />} onClick={onRetry} loading={retrying}>
              Try again
            </Button>
          )}
          {action}
        </div>
      )}
    </div>
  );
}
