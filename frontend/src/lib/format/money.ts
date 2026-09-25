import { browserLocale } from './locale';

/**
 * Money formatting. The UI never computes money — it only formats amounts the API returns, always with their ISO
 * currency. Minor units follow the API (JPY 0, USD 2, KWD 3, ...), not Intl: the browser's CLDR data gives some
 * currencies fewer digits than ISO 4217 (PKR, IDR, HUF, COP → 0, IQD → 0), which would show "PKR 1,235" for an
 * invoice the API totals as 1,234.56.
 */

/**
 * Currencies whose minor unit is not 2. Mirrors `Money.MinorUnits` in backend/src/OptimizeAll.Domain/Common/Money.cs:
 * the backend unit test `MinorUnitsSyncTests` parses this literal and fails when the two differ, so keep it a plain
 * `CODE: digits` list.
 */
const MINOR_UNITS: Readonly<Record<string, number>> = {
  BHD: 3, JOD: 3, KWD: 3, OMR: 3, TND: 3, IQD: 3, LYD: 3,
  JPY: 0, KRW: 0, VND: 0, CLP: 0, ISK: 0, UGX: 0, XAF: 0, XOF: 0,
  PYG: 0, RWF: 0, KMF: 0, GNF: 0, DJF: 0, VUV: 0, XPF: 0,
};

export interface FormatMoneyOptions {
  locale?: string;
  /** Show +/− explicitly: 'auto' (default), 'always', 'exceptZero', 'never'. */
  signDisplay?: Intl.NumberFormatOptions['signDisplay'];
  /** Compact notation for dashboards (e.g. $12.4K). */
  compact?: boolean;
  /** 'symbol' (default), 'code' (USD 10.00), 'narrowSymbol' ($10.00). */
  currencyDisplay?: Intl.NumberFormatOptions['currencyDisplay'];
}

const formatterCache = new Map<string, Intl.NumberFormat>();

function getFormatter(currency: string, options: FormatMoneyOptions): Intl.NumberFormat {
  const key = JSON.stringify([currency, options]);
  let formatter = formatterCache.get(key);
  if (!formatter) {
    const digits = currencyMinorUnits(currency);
    formatter = new Intl.NumberFormat(options.locale ?? browserLocale(), {
      style: 'currency',
      currency,
      currencyDisplay: options.currencyDisplay ?? 'symbol',
      signDisplay: options.signDisplay ?? 'auto',
      ...(options.compact
        ? { notation: 'compact', maximumFractionDigits: 1 }
        : { minimumFractionDigits: digits, maximumFractionDigits: digits }),
    });
    formatterCache.set(key, formatter);
  }
  return formatter;
}

function toNumber(amount: number | string): number {
  return typeof amount === 'number' ? amount : Number(amount);
}

/** Number of minor-unit digits for a currency, as the API rounds it (JPY → 0, USD → 2, KWD → 3). */
export function currencyMinorUnits(currency: string): number {
  return MINOR_UNITS[currency.toUpperCase()] ?? 2;
}

/** Formats an amount in its currency, e.g. formatMoney(1234.5, 'USD') → "$1,234.50". */
export function formatMoney(
  amount: number | string,
  currency: string,
  options: FormatMoneyOptions = {},
): string {
  const value = toNumber(amount);
  const code = currency.toUpperCase();
  if (!Number.isFinite(value)) return '—';
  try {
    return getFormatter(code, options).format(value);
  } catch {
    // Unknown currency code: fall back to a plain number + code rather than crashing a page.
    return `${new Intl.NumberFormat(options.locale ?? browserLocale(), { maximumFractionDigits: 4 }).format(value)} ${code}`;
  }
}

/** Formats a plain count with grouping, e.g. 12345 → "12,345". */
export function formatNumber(
  value: number,
  options: Intl.NumberFormatOptions & { locale?: string } = {},
): string {
  const { locale, ...rest } = options;
  return new Intl.NumberFormat(locale ?? browserLocale(), rest).format(value);
}

/** Formats a ratio (0.125) as a percentage ("12.5%"). */
export function formatPercent(
  ratio: number,
  options: { locale?: string; maximumFractionDigits?: number } = {},
): string {
  return new Intl.NumberFormat(options.locale ?? browserLocale(), {
    style: 'percent',
    maximumFractionDigits: options.maximumFractionDigits ?? 1,
  }).format(ratio);
}
