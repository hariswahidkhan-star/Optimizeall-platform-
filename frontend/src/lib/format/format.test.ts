import { describe, expect, it } from 'vitest';
import { formatDate, formatDateTime, formatRelative, greetingFor, toZonedDateKey } from './dates';
import { currencyMinorUnits, formatMoney } from './money';
import { formatBytes, humanize, initials } from './text';

const nbsp = /[\u00a0\u202f]/g;
const clean = (s: string) => s.replace(nbsp, ' ');

describe('money', () => {
  it('uses each currency’s minor units', () => {
    expect(currencyMinorUnits('USD')).toBe(2);
    expect(currencyMinorUnits('JPY')).toBe(0);
    expect(currencyMinorUnits('KWD')).toBe(3);
  });

  it('formats amounts in their currency', () => {
    expect(formatMoney(1234.5, 'USD', { locale: 'en-US' })).toBe('$1,234.50');
    expect(formatMoney(1500, 'JPY', { locale: 'en-US' })).toBe('¥1,500');
    expect(clean(formatMoney(4.25, 'KWD', { locale: 'en-US' }))).toBe('KWD 4.250');
    expect(clean(formatMoney(9, 'EUR', { locale: 'de-DE' }))).toBe('9,00 €');
  });

  it('accepts decimal strings and signs', () => {
    expect(formatMoney('12.3456', 'USD', { locale: 'en-US' })).toBe('$12.35');
    expect(formatMoney(-5, 'USD', { locale: 'en-US' })).toBe('-$5.00');
    expect(formatMoney(5, 'USD', { locale: 'en-US', signDisplay: 'always' })).toBe('+$5.00');
  });

  it('degrades gracefully for invalid input', () => {
    expect(formatMoney(Number.NaN, 'USD')).toBe('—');
    expect(formatMoney(10, 'NOTACODE', { locale: 'en-US' })).toBe('10 NOTACODE');
  });
});

describe('dates', () => {
  const instant = '2026-01-15T23:30:00Z';

  it('renders absolute times in the requested time zone', () => {
    expect(formatDateTime(instant, { timeZone: 'UTC', locale: 'en-US' })).toBe('Jan 15, 2026, 11:30 PM');
    expect(formatDateTime(instant, { timeZone: 'Asia/Tokyo', locale: 'en-US' })).toBe(
      'Jan 16, 2026, 8:30 AM',
    );
    expect(formatDate(instant, { timeZone: 'America/Los_Angeles', locale: 'en-US' })).toBe('Jan 15, 2026');
  });

  it('includes the zone name when asked', () => {
    expect(
      clean(formatDateTime(instant, { timeZone: 'Asia/Tokyo', locale: 'en-US', withZone: true })),
    ).toMatch(/Jan 16, 2026, 8:30 AM GMT\+9/);
  });

  it('computes the calendar date in a zone', () => {
    expect(toZonedDateKey(instant, 'UTC')).toBe('2026-01-15');
    expect(toZonedDateKey(instant, 'Pacific/Auckland')).toBe('2026-01-16');
  });

  it('ignores unknown time zones instead of throwing', () => {
    expect(() => formatDateTime(instant, { timeZone: 'Mars/Olympus' })).not.toThrow();
  });

  it('formats relative times', () => {
    const now = '2026-01-15T12:00:00Z';
    expect(formatRelative('2026-01-15T09:00:00Z', { now, locale: 'en' })).toBe('3 hours ago');
    expect(formatRelative('2026-01-17T12:00:00Z', { now, locale: 'en' })).toBe('in 2 days');
    expect(formatRelative('2026-01-15T11:59:50Z', { now, locale: 'en' })).toBe('now');
    expect(formatRelative('not a date')).toBe('—');
  });

  it('greets by local hour', () => {
    expect(greetingFor('2026-01-15T08:00:00Z', 'UTC')).toBe('Good morning');
    expect(greetingFor('2026-01-15T08:00:00Z', 'America/New_York')).toBe('Good evening');
  });
});

describe('text', () => {
  it('humanizes enum values', () => {
    expect(humanize('UnderReview')).toBe('Under review');
    expect(humanize('PENDING_APPROVAL')).toBe('Pending approval');
    expect(humanize('social-accounts')).toBe('Social accounts');
  });

  it('builds initials', () => {
    expect(initials('Ada Lovelace')).toBe('AL');
    expect(initials('leo.martins@example.com')).toBe('LM');
    expect(initials('')).toBe('?');
  });

  it('formats bytes', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(8 * 1024 * 1024)).toBe('8.0 MB');
  });
});
