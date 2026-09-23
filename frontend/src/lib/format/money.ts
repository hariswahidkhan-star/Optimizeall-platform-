import { browserLocale } from './locale';

/**
 * Money formatting. The UI never computes money — it only formats amounts the API returns, always with their ISO
 * currency. Minor units come from Intl (JPY 0, USD 2, KWD 3, ...).
 */

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
    formatter = new Intl.NumberFormat(options.locale ?? browserLocale(), {
      style: 'currency',
      currency,
      currencyDisplay: options.currencyDisplay ?? 'symbol',
      signDisplay: options.signDisplay ?? 'auto',
      ...(options.compact ? { notation: 'compact', maximumFractionDigits: 1 } : {}),
    });
    formatterCache.set(key, formatter);
  }
  return formatter;
}

function toNumber(amount: number | string): number {
  return typeof amount === 'number' ? amount : Number(amount);
}

/** Number of minor-unit digits for a currency (JPY → 0, USD → 2, KWD → 3). */
export function currencyMinorUnits(currency: string): number {
  return (
    new Intl.NumberFormat('en', { style: 'currency', currency: currency.toUpperCase() }).resolvedOptions()
      .maximumFractionDigits ?? 2
  );
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
