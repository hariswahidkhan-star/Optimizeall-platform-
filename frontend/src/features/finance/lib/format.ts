import type { PayoutExclusionReason } from '../api/types';

/**
 * Currencies the backend accepts (mirror of `Domain/Common/Money.SupportedCurrencies`). There is no endpoint that
 * lists them, so the form offers these and the server remains the judge (`ledger.currency_unsupported`).
 */
export const SUPPORTED_CURRENCIES = [
  'USD',
  'EUR',
  'GBP',
  'AED',
  'SAR',
  'PKR',
  'INR',
  'CAD',
  'AUD',
  'JPY',
  'KWD',
  'BHD',
  'OMR',
  'QAR',
  'EGP',
  'TRY',
  'NGN',
  'ZAR',
  'BRL',
  'MXN',
];

export const currencyOptions = SUPPORTED_CURRENCIES.map((c) => ({ value: c, label: c }));

const pad = (n: number) => String(n).padStart(2, '0');

/** Value for an `<input type="datetime-local">` in the browser's zone (e.g. "2026-09-23T14:05"). */
export function toLocalInputValue(date: Date = new Date()): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(
    date.getMinutes(),
  )}`;
}

/** Converts a datetime-local value (browser zone) to a UTC ISO string; null when empty/invalid. */
export function localInputToIso(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** Whole days from now until an instant (0 when it is today or past). Calendar arithmetic only — never money. */
export function daysUntil(iso: string, now: Date = new Date()): number {
  const ms = new Date(iso).getTime() - now.getTime();
  return Math.max(0, Math.ceil(ms / 86_400_000));
}

/** `yyyy-MM-dd` → the following day, for turning an inclusive "to" date into the API's exclusive bound. */
export function nextDay(dateOnly: string): string {
  const [y, m, d] = dateOnly.split('-').map(Number);
  const date = new Date(Date.UTC(y!, m! - 1, d! + 1));
  return date.toISOString().slice(0, 10);
}

/** DateOnly ("2026-10-02") rendered as a calendar date without shifting it through a time zone. */
export function dateOnlyToDisplay(value: string | null | undefined): string {
  if (!value) return '—';
  const [y, m, d] = value.split('-').map(Number);
  if (!y || !m || !d) return value;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeZone: 'UTC' }).format(
    new Date(Date.UTC(y, m - 1, d)),
  );
}

export const EXCLUSION_LABELS: Record<PayoutExclusionReason, { label: string; help: string }> = {
  PayoutHold: { label: 'Payout hold', help: 'The participant has an active payout hold.' },
  AccountInactive: { label: 'Account suspended', help: 'The account is suspended or deactivated.' },
  NonPositiveBalance: {
    label: 'No positive balance',
    help: 'Debits or clawbacks exceed credits; the balance carries over.',
  },
  BelowMinimum: {
    label: 'Below minimum',
    help: 'Below the minimum payout amount; carried over to a later batch.',
  },
};

export function exclusionLabel(reason: string): string {
  return EXCLUSION_LABELS[reason as PayoutExclusionReason]?.label ?? reason;
}

export const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Generates a request id for idempotent POSTs. */
export function newRequestId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  // Fallback for very old browsers: RFC 4122 v4 from getRandomValues.
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  const hex = [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
