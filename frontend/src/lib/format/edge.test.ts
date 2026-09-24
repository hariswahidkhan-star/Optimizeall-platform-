import { describe, expect, it } from 'vitest';
import { formatDate, formatDateTime, isDateOnly, toZonedDateKey } from './dates';
import { currencyMinorUnits, formatMoney } from './money';

const clean = (s: string) => s.replace(/[\u00a0\u202f]/g, ' ');

// Extreme and fractional offsets: UTC-12, UTC+14, +05:30, +05:45, and a DST zone.
const ZONES = [
  'Etc/GMT+12',
  'Pacific/Kiritimati',
  'Asia/Kolkata',
  'Asia/Kathmandu',
  'America/Los_Angeles',
  'Pacific/Chatham',
];

describe('date-only values (calendar dates from the API)', () => {
  it('recognises yyyy-MM-dd strings only', () => {
    expect(isDateOnly('2026-03-01')).toBe(true);
    expect(isDateOnly('2026-03-01T00:00:00Z')).toBe(false);
    expect(isDateOnly(new Date())).toBe(false);
    expect(isDateOnly(1_700_000_000_000)).toBe(false);
  });

  it('show the same calendar day in every time zone', () => {
    for (const timeZone of ZONES) {
      expect(formatDate('2026-03-01', { timeZone, locale: 'en-US' })).toBe('Mar 1, 2026');
      expect(formatDateTime('2028-02-29', { timeZone, locale: 'en-US' })).toBe('Feb 29, 2028');
      expect(toZonedDateKey('2026-12-31', timeZone)).toBe('2026-12-31');
    }
  });

  it('still render instants in the requested zone', () => {
    expect(formatDate('2026-03-01T00:00:00Z', { timeZone: 'America/Los_Angeles', locale: 'en-US' })).toBe(
      'Feb 28, 2026',
    );
    expect(formatDate('2026-03-01T11:00:00Z', { timeZone: 'Pacific/Kiritimati', locale: 'en-US' })).toBe(
      'Mar 2, 2026',
    );
    expect(toZonedDateKey('2026-03-01T20:00:00Z', 'Asia/Kathmandu')).toBe('2026-03-02');
    expect(toZonedDateKey('2026-03-01T18:14:00Z', 'Asia/Kathmandu')).toBe('2026-03-01');
    expect(toZonedDateKey('2026-03-01T18:15:00Z', 'Asia/Kathmandu')).toBe('2026-03-02');
  });

  it('render wall-clock times across a DST change', () => {
    // US spring-forward 2026-03-08 02:00 → 03:00 PDT; fall-back 2026-11-01 02:00 → 01:00 PST.
    expect(formatDateTime('2026-03-08T09:59:00Z', { timeZone: 'America/Los_Angeles', locale: 'en-US' })).toBe(
      'Mar 8, 2026, 1:59 AM',
    );
    expect(formatDateTime('2026-03-08T10:00:00Z', { timeZone: 'America/Los_Angeles', locale: 'en-US' })).toBe(
      'Mar 8, 2026, 3:00 AM',
    );
    expect(formatDateTime('2026-11-01T08:30:00Z', { timeZone: 'America/Los_Angeles', locale: 'en-US' })).toBe(
      'Nov 1, 2026, 1:30 AM',
    );
    expect(formatDateTime('2026-11-01T09:30:00Z', { timeZone: 'America/Los_Angeles', locale: 'en-US' })).toBe(
      'Nov 1, 2026, 1:30 AM',
    );
  });
});

describe('money follows the API’s minor units', () => {
  it('matches the API for currencies where CLDR differs from ISO 4217', () => {
    // The API rounds and totals PKR to 2 decimals and IQD to 3 (Money.MinorUnits); CLDR would show neither.
    expect(currencyMinorUnits('PKR')).toBe(2);
    expect(currencyMinorUnits('pkr')).toBe(2);
    expect(currencyMinorUnits('IQD')).toBe(3);
    expect(clean(formatMoney(1234.56, 'PKR', { locale: 'en-US' }))).toBe('PKR 1,234.56');
    expect(clean(formatMoney(0.5, 'PKR', { locale: 'en-US' }))).toBe('PKR 0.50');
    expect(clean(formatMoney(12.345, 'IQD', { locale: 'en-US' }))).toBe('IQD 12.345');
    expect(clean(formatMoney(1234.56, 'IDR', { locale: 'en-US' }))).toBe('IDR 1,234.56');
  });

  it('formats zero-, two- and three-decimal currencies, large and negative amounts', () => {
    expect(formatMoney(0, 'JPY', { locale: 'en-US' })).toBe('¥0');
    expect(formatMoney(-1500, 'JPY', { locale: 'en-US' })).toBe('-¥1,500');
    expect(clean(formatMoney(-0.125, 'KWD', { locale: 'en-US' }))).toBe('-KWD 0.125');
    expect(clean(formatMoney('1.1', 'BHD', { locale: 'en-US' }))).toBe('BHD 1.100');
    expect(formatMoney(123456789.99, 'USD', { locale: 'en-US' })).toBe('$123,456,789.99');
    expect(formatMoney(1234.5, 'USD', { locale: 'en-US', compact: true })).toBe('$1.2K');
  });
});
