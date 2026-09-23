import { Plus, Trash2 } from 'lucide-react';
import { Alert, Button, FormField, IconButton, Input, Money, Select, Skeleton } from '@/components/ui';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { usePricePreview, useTaxRates } from '../api/hooks';
import type { PreviewResponse, PriceLineInput, Recurrence } from '../api/types';
import { billingErrorMessage } from '../lib';

export function emptyLine(recurrence: Recurrence = 'OneTime'): PriceLineInput {
  return { description: '', quantity: 1, unitPrice: 0, discountType: 'None', discountValue: 0, taxRateId: null, serviceSlug: null, recurrence };
}

const RECURRENCE_OPTIONS = [
  { value: 'OneTime', label: 'One-time' },
  { value: 'Monthly', label: 'Monthly' },
  { value: 'Quarterly', label: 'Quarterly' },
  { value: 'Annually', label: 'Annually' },
];

const DISCOUNT_OPTIONS = [
  { value: 'None', label: 'No discount' },
  { value: 'Percent', label: 'Percent (%)' },
  { value: 'Amount', label: 'Amount' },
];

function toNumber(value: string): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : 0;
}

export interface LineItemsEditorProps {
  lines: PriceLineInput[];
  onChange: (lines: PriceLineInput[]) => void;
  currency: string;
  /** Proposals price one-time vs recurring lines; invoices and contracts don't. */
  allowRecurrence?: boolean;
  disabled?: boolean;
  /** Field errors from the last save attempt, keyed like the API (`lines[0]`). */
  errors?: Record<string, string[]>;
}

/**
 * Line items with live totals. Every number shown in the totals comes from the preview API (debounced); the browser
 * only collects inputs.
 */
export function LineItemsEditor({ lines, onChange, currency, allowRecurrence, disabled, errors }: LineItemsEditorProps) {
  const taxRates = useTaxRates();
  const update = (index: number, patch: Partial<PriceLineInput>) =>
    onChange(lines.map((line, i) => (i === index ? { ...line, ...patch } : line)));
  const rateOptions = (taxRates.data ?? []).map((r) => ({
    value: r.id,
    label: `${r.name}${r.inclusive ? ' (incl.)' : ''}${r.needsReview ? ' — review' : ''}`,
  }));

  return (
    <div className="stack">
      <ol className="bill-lines" aria-label="Line items">
        {lines.map((line, index) => {
          const lineError = errors?.[`lines[${index}]`];
          return (
            <li key={index} className="bill-line">
              <FormField label={`Line ${index + 1} description`} required error={lineError?.join(' ')} className="bill-line__wide">
                <Input
                  value={line.description}
                  maxLength={500}
                  disabled={disabled}
                  onChange={(e) => update(index, { description: e.target.value })}
                />
              </FormField>
              <FormField label="Quantity" required>
                <Input
                  type="number"
                  inputMode="decimal"
                  min={0}
                  step="any"
                  value={String(line.quantity)}
                  disabled={disabled}
                  onChange={(e) => update(index, { quantity: toNumber(e.target.value) })}
                />
              </FormField>
              <FormField label={`Unit price (${currency})`} required>
                <Input
                  type="number"
                  inputMode="decimal"
                  min={0}
                  step="any"
                  value={String(line.unitPrice)}
                  disabled={disabled}
                  onChange={(e) => update(index, { unitPrice: toNumber(e.target.value) })}
                />
              </FormField>
              <FormField label="Service" optional hint="Service slug, e.g. seo">
                <Input
                  value={line.serviceSlug ?? ''}
                  maxLength={100}
                  disabled={disabled}
                  onChange={(e) => update(index, { serviceSlug: e.target.value.trim().toLowerCase() || null })}
                />
              </FormField>
              <FormField label="Discount">
                <Select
                  value={line.discountType}
                  options={DISCOUNT_OPTIONS}
                  disabled={disabled}
                  onChange={(e) =>
                    update(index, {
                      discountType: e.target.value as PriceLineInput['discountType'],
                      discountValue: e.target.value === 'None' ? 0 : line.discountValue,
                    })
                  }
                />
              </FormField>
              {line.discountType !== 'None' && (
                <FormField label={line.discountType === 'Percent' ? 'Discount (%)' : `Discount (${currency})`}>
                  <Input
                    type="number"
                    inputMode="decimal"
                    min={0}
                    step="any"
                    value={String(line.discountValue)}
                    disabled={disabled}
                    onChange={(e) => update(index, { discountValue: toNumber(e.target.value) })}
                  />
                </FormField>
              )}
              <FormField label="Tax rate">
                <Select
                  value={line.taxRateId ?? ''}
                  options={[{ value: '', label: 'No tax' }, ...rateOptions]}
                  disabled={disabled}
                  onChange={(e) => update(index, { taxRateId: e.target.value || null })}
                />
              </FormField>
              {allowRecurrence && (
                <FormField label="Billing">
                  <Select
                    value={line.recurrence ?? 'OneTime'}
                    options={RECURRENCE_OPTIONS}
                    disabled={disabled}
                    onChange={(e) => update(index, { recurrence: e.target.value as Recurrence })}
                  />
                </FormField>
              )}
              <div className="bill-line__footer">
                <span className="bill-muted">Line {index + 1}</span>
                <IconButton
                  label={`Remove line ${index + 1}`}
                  icon={<Trash2 />}
                  variant="ghost"
                  size="sm"
                  disabled={disabled || lines.length === 1}
                  onClick={() => onChange(lines.filter((_, i) => i !== index))}
                />
              </div>
            </li>
          );
        })}
      </ol>
      <div>
        <Button
          variant="secondary"
          size="sm"
          leadingIcon={<Plus />}
          disabled={disabled || lines.length >= 200}
          onClick={() => onChange([...lines, emptyLine(allowRecurrence ? 'Monthly' : 'OneTime')])}
        >
          Add line
        </Button>
      </div>
    </div>
  );
}

/** Totals from the preview API for the current (debounced) lines. */
export function LivePreviewTotals({
  lines,
  currency,
  previewPath,
  showRecurring,
}: {
  lines: PriceLineInput[];
  currency: string;
  previewPath: string;
  showRecurring?: boolean;
}) {
  const debounced = useDebouncedValue(lines, 350);
  const ready = debounced.every((l) => l.description.trim().length > 0);
  const preview = usePricePreview(previewPath, currency, debounced, ready);
  if (!ready) return <p className="bill-muted">Describe every line to see totals.</p>;
  if (preview.isError) {
    return (
      <Alert tone="warning" title="Totals can’t be calculated yet">
        {billingErrorMessage(preview.error)}
      </Alert>
    );
  }
  if (!preview.data) return <Skeleton height="8rem" />;
  return <TotalsList preview={preview.data} currency={currency} showRecurring={showRecurring} busy={preview.isFetching} />;
}

export function TotalsList({
  preview,
  currency,
  showRecurring,
  busy,
}: {
  preview: Pick<PreviewResponse, 'totals'> & Partial<Pick<PreviewResponse, 'recurring'>>;
  currency: string;
  showRecurring?: boolean;
  busy?: boolean;
}) {
  const { totals, recurring } = preview;
  return (
    <dl className="bill-totals" aria-label="Totals" aria-busy={busy || undefined}>
      <div>
        <dt>Subtotal</dt>
        <dd>
          <Money amount={totals.grossTotal} currency={currency} />
        </dd>
      </div>
      {totals.discountTotal > 0 && (
        <div>
          <dt>Discounts (deducted)</dt>
          <dd>
            <Money amount={totals.discountTotal} currency={currency} />
          </dd>
        </div>
      )}
      {totals.taxes.map((t) => (
        <div key={`${t.name}-${t.ratePercent}-${t.inclusive}`}>
          <dt>
            {t.name}
            {t.inclusive ? ' (included)' : ''}
          </dt>
          <dd>
            <Money amount={t.taxAmount} currency={currency} />
          </dd>
        </div>
      ))}
      <div className="bill-totals__grand">
        <dt>Total</dt>
        <dd>
          <Money amount={totals.total} currency={currency} />
        </dd>
      </div>
      {showRecurring && recurring && (
        <>
          <div>
            <dt>One-time</dt>
            <dd>
              <Money amount={recurring.oneTimeTotal} currency={currency} />
            </dd>
          </div>
          <div>
            <dt>Monthly recurring (MRR)</dt>
            <dd>
              <Money amount={recurring.monthlyRecurringValue} currency={currency} />
            </dd>
          </div>
          <div>
            <dt>First invoice</dt>
            <dd>
              <Money amount={recurring.firstInvoiceTotal} currency={currency} />
            </dd>
          </div>
          <div>
            <dt>First-year value</dt>
            <dd>
              <Money amount={recurring.firstYearValue} currency={currency} />
            </dd>
          </div>
        </>
      )}
    </dl>
  );
}
