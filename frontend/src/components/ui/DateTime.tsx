import clsx from 'clsx';
import { useOptionalAuth } from '@/lib/auth/useAuth';
import {
  browserTimeZone,
  formatDate,
  formatDateTime,
  formatRelative,
  isValidDate,
  toDate,
  type DateInput,
} from '@/lib/format/dates';

export interface DateTimeProps {
  value: DateInput | null | undefined;
  /**
   * `datetime` (default): absolute date + time · `date`: date only · `relative`: "3 hours ago" with the absolute
   * time as a tooltip · `both`: absolute followed by relative.
   */
  format?: 'datetime' | 'date' | 'relative' | 'both';
  /** Override the time zone (defaults to the signed-in user's zone, then the browser's). */
  timeZone?: string;
  /** Include the zone abbreviation (e.g. "GMT+1"). */
  withZone?: boolean;
  className?: string;
}

/** Renders a timestamp in the user's time zone inside a semantic <time dateTime>. */
export function DateTime({ value, format = 'datetime', timeZone, withZone, className }: DateTimeProps) {
  const auth = useOptionalAuth();
  if (!isValidDate(value ?? null)) return <span className={className}>—</span>;
  const date = toDate(value as DateInput);
  const zone = timeZone ?? auth?.user?.timeZone ?? browserTimeZone();
  const absolute =
    format === 'date'
      ? formatDate(date, { timeZone: zone })
      : formatDateTime(date, { timeZone: zone, withZone });
  const relative = formatRelative(date);

  let text: string;
  switch (format) {
    case 'relative':
      text = relative;
      break;
    case 'both':
      text = `${absolute} · ${relative}`;
      break;
    default:
      text = absolute;
  }

  return (
    <time
      dateTime={date.toISOString()}
      title={format === 'relative' ? formatDateTime(date, { timeZone: zone, withZone: true }) : undefined}
      className={clsx('tabular', className)}
    >
      {text}
    </time>
  );
}
