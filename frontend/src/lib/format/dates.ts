import { browserLocale } from './locale';

/**
 * Date formatting. The API sends UTC ISO-8601 timestamps; we render them in the user's time zone (from their
 * profile, falling back to the browser's).
 */

export type DateInput = string | number | Date;

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

/**
 * True for a calendar date without a time of day ("2026-03-01", a C# DateOnly: due dates, issue dates, periods).
 * It names the same day everywhere, so it is never shifted through a time zone (parsed as an instant it is UTC
 * midnight, which is the previous day west of Greenwich).
 */
export function isDateOnly(value: unknown): value is string {
  return typeof value === 'string' && DATE_ONLY.test(value);
}

export function toDate(value: DateInput): Date {
  return value instanceof Date ? value : new Date(value);
}

export function isValidDate(value: DateInput | null | undefined): boolean {
  if (value === null || value === undefined || value === '') return false;
  return !Number.isNaN(toDate(value).getTime());
}

export function browserTimeZone(): string {
  try {
    return new Intl.DateTimeFormat(browserLocale()).resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

/** True when the IANA zone is known to this runtime. */
export function isValidTimeZone(timeZone: string): boolean {
  try {
    new Intl.DateTimeFormat('en', { timeZone });
    return true;
  } catch {
    return false;
  }
}

export interface FormatDateOptions {
  timeZone?: string;
  locale?: string;
}

function safeZone(timeZone?: string): string | undefined {
  return timeZone && isValidTimeZone(timeZone) ? timeZone : undefined;
}

/** "Sep 23, 2026, 2:05 PM" in the given zone. */
export function formatDateTime(
  value: DateInput,
  options: FormatDateOptions & { withZone?: boolean } = {},
): string {
  if (!isValidDate(value)) return '—';
  if (isDateOnly(value)) return formatDate(value, options);
  // dateStyle/timeStyle can't be combined with timeZoneName, so the zoned variant spells out the components.
  const style: Intl.DateTimeFormatOptions = options.withZone
    ? {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
        timeZoneName: 'short',
      }
    : { dateStyle: 'medium', timeStyle: 'short' };
  return new Intl.DateTimeFormat(options.locale ?? browserLocale(), {
    ...style,
    timeZone: safeZone(options.timeZone),
  }).format(toDate(value));
}

/** "Sep 23, 2026" in the given zone. A date-only value ("2026-09-23") is that calendar day in every zone. */
export function formatDate(value: DateInput, options: FormatDateOptions = {}): string {
  if (!isValidDate(value)) return '—';
  return new Intl.DateTimeFormat(options.locale ?? browserLocale(), {
    dateStyle: 'medium',
    timeZone: isDateOnly(value) ? 'UTC' : safeZone(options.timeZone),
  }).format(toDate(value));
}

/** "2:05 PM" in the given zone. */
export function formatTime(value: DateInput, options: FormatDateOptions = {}): string {
  if (!isValidDate(value)) return '—';
  return new Intl.DateTimeFormat(options.locale ?? browserLocale(), {
    timeStyle: 'short',
    timeZone: safeZone(options.timeZone),
  }).format(toDate(value));
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
  ['second', 1],
];

/** "3 hours ago", "in 2 days", "now". */
export function formatRelative(value: DateInput, options: { now?: DateInput; locale?: string } = {}): string {
  if (!isValidDate(value)) return '—';
  const now = options.now !== undefined ? toDate(options.now) : new Date();
  const diffSeconds = (toDate(value).getTime() - now.getTime()) / 1000;
  const rtf = new Intl.RelativeTimeFormat(options.locale ?? browserLocale(), { numeric: 'auto' });
  if (Math.abs(diffSeconds) < 45) return rtf.format(0, 'second');
  for (const [unit, seconds] of RELATIVE_UNITS) {
    if (Math.abs(diffSeconds) >= seconds) return rtf.format(Math.round(diffSeconds / seconds), unit);
  }
  return rtf.format(0, 'second');
}

/** Calendar date (YYYY-MM-DD) of an instant in a zone — for grouping and date inputs. */
export function toZonedDateKey(value: DateInput, timeZone?: string): string {
  if (isDateOnly(value)) return value;
  const parts = new Intl.DateTimeFormat('en-CA', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    timeZone: safeZone(timeZone),
  }).formatToParts(toDate(value));
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
}

/** Greeting for the user's local hour in their zone. */
export function greetingFor(date: DateInput = new Date(), timeZone?: string): string {
  const hour = Number(
    new Intl.DateTimeFormat('en-US', {
      hour: 'numeric',
      hourCycle: 'h23',
      timeZone: safeZone(timeZone),
    }).format(toDate(date)),
  );
  if (hour < 5) return 'Good evening';
  if (hour < 12) return 'Good morning';
  if (hour < 18) return 'Good afternoon';
  return 'Good evening';
}
