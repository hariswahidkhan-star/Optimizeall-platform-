import clsx from 'clsx';
import { formatMoney, type FormatMoneyOptions } from '@/lib/format/money';
import './display.css';

export interface MoneyProps extends Pick<FormatMoneyOptions, 'signDisplay' | 'compact' | 'currencyDisplay'> {
  amount: number | string | null | undefined;
  /** ISO 4217 code — every amount travels with its currency. */
  currency: string;
  /** Color positive green and negative red (ledger views). */
  colored?: boolean;
  className?: string;
}

/** Formats an API amount in its currency with tabular numerals. Never computes money. */
export function Money({ amount, currency, colored, className, ...options }: MoneyProps) {
  if (amount === null || amount === undefined || amount === '') {
    return <span className={clsx('ui-money', className)}>—</span>;
  }
  const numeric = Number(amount);
  return (
    <span
      className={clsx(
        'ui-money',
        colored && numeric > 0 && 'ui-money--positive',
        colored && numeric < 0 && 'ui-money--negative',
        className,
      )}
    >
      {formatMoney(numeric, currency, options)}
    </span>
  );
}
