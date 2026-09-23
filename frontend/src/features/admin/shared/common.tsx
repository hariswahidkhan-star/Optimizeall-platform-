import { useMutation } from '@tanstack/react-query';
import { Download, Lock } from 'lucide-react';
import type { ReactNode } from 'react';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { useToast } from '@/components/ui/toastContext';
import { api, type QueryParams } from '@/lib/api/client';
import { useAuth } from '@/lib/auth/useAuth';
import { humanize } from '@/lib/format/text';
import { adminErrorMessage, isForbidden } from './errors';

/** Whether the signed-in user holds every given permission. */
export function useCan(...permissions: string[]): boolean {
  const { hasPermission } = useAuth();
  return permissions.every((p) => hasPermission(p));
}

/** Options for a Select / FilterBar from enum values. */
export function enumOptions(values: readonly string[], label: (v: string) => string = humanize) {
  return values.map((value) => ({ value, label: label(value) }));
}

/**
 * Error display for a failed query: a 403 from the server (permission revoked since sign-in, or scoped endpoint)
 * gets an explanatory "no access" state instead of a generic failure.
 */
export function QueryError({
  error,
  onRetry,
  headingLevel = 2,
  compact,
}: {
  error: unknown;
  onRetry?: () => void;
  headingLevel?: 2 | 3 | 4;
  compact?: boolean;
}) {
  if (isForbidden(error)) {
    return (
      <EmptyState
        icon={<Lock />}
        headingLevel={headingLevel}
        compact={compact}
        title="You don’t have access to this"
        description="The server refused this request for your account. If your roles changed recently, sign out and sign in again, or ask an administrator."
      />
    );
  }
  return <ErrorState error={error} onRetry={onRetry} headingLevel={headingLevel} compact={compact} />;
}

/** "Export CSV" button: downloads with the current session, pending state + error toast. */
export function ExportCsvButton({
  path,
  query,
  fileName,
  label = 'Export CSV',
}: {
  path: string;
  query?: QueryParams;
  fileName: string;
  label?: ReactNode;
}) {
  const toast = useToast();
  const exportCsv = useMutation({
    mutationFn: () => api.download(path, fileName, { query }),
    onSuccess: () => toast.success('Export ready', 'Your CSV download has started.'),
    onError: (error) => toast.error('Export failed', adminErrorMessage(error)),
  });
  return (
    <Button
      variant="secondary"
      leadingIcon={<Download />}
      loading={exportCsv.isPending}
      onClick={() => exportCsv.mutate()}
    >
      {label}
    </Button>
  );
}

/** Converts a `<input type="datetime-local">` value (local time) to ISO UTC, or null. */
export function localInputToIso(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** Converts an ISO timestamp to a `<input type="datetime-local">` value in local time. */
export function isoToLocalInput(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(
    date.getMinutes(),
  )}`;
}

/** Converts a `<input type="date">` value to the start (or end) of that day in UTC. */
export function dateInputToIso(value: string, endOfDay = false): string | undefined {
  if (!value) return undefined;
  return `${value}T${endOfDay ? '23:59:59' : '00:00:00'}Z`;
}

/** Trims a string and turns '' into null (optional API fields). */
export function orNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}
