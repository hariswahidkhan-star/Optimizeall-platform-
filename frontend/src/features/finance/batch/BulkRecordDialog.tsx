import { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Badge,
  FormField,
  Input,
  Money,
  RadioGroup,
  Skeleton,
  Textarea,
  useToast,
  type Tone,
} from '@/components/ui';
import { useBatch, useRecordPayments } from '../api/hooks';
import type { BulkPaymentLine, BulkPaymentResult, BulkPaymentStatus, PayoutItem } from '../api/types';
import { QueryError } from '../components/common';
import { FormDialog } from '../components/FormDialog';
import { GUID_RE, localInputToIso, toLocalInputValue } from '../lib/format';

export interface BulkRecordDialogProps {
  open: boolean;
  onClose: () => void;
  batchId: string;
}

export interface ParsedCsv {
  lines: BulkPaymentLine[];
  errors: string[];
}

/**
 * Parses pasted CSV/TSV: `itemId,paymentReference[,paidAt]` per line. A header row is skipped. Lines without their
 * own paidAt use `defaultPaidAt`.
 */
export function parsePaymentCsv(text: string, defaultPaidAt: string | null): ParsedCsv {
  const lines: BulkPaymentLine[] = [];
  const errors: string[] = [];
  text
    .split(/\r?\n/)
    .map((l) => l.trim())
    .forEach((raw, index) => {
      if (!raw) return;
      const cells = raw.split(/[,;\t]/).map((c) => c.trim().replace(/^"|"$/g, ''));
      const [itemId = '', reference = '', paidAtCell = ''] = cells;
      if (index === 0 && !GUID_RE.test(itemId) && /item/i.test(itemId)) return; // header
      const lineNo = index + 1;
      if (!GUID_RE.test(itemId)) {
        errors.push(`Line ${lineNo}: “${itemId || '(empty)'}” is not an item id.`);
        return;
      }
      if (reference.length < 3 || reference.length > 120) {
        errors.push(`Line ${lineNo}: the payment reference must be 3–120 characters.`);
        return;
      }
      let paidAt = defaultPaidAt;
      if (paidAtCell) {
        const date = new Date(paidAtCell);
        if (Number.isNaN(date.getTime())) {
          errors.push(`Line ${lineNo}: “${paidAtCell}” is not a date.`);
          return;
        }
        paidAt = date.toISOString();
      }
      if (!paidAt) {
        errors.push(`Line ${lineNo}: no payment date.`);
        return;
      }
      lines.push({ itemId, paymentReference: reference, paidAt });
    });
  return { lines, errors };
}

const RESULT_META: Record<BulkPaymentStatus, { tone: Tone; label: string }> = {
  recorded: { tone: 'success', label: 'Recorded' },
  already_recorded: { tone: 'warning', label: 'Already recorded' },
  invalid: { tone: 'danger', label: 'Not recorded' },
};

export function BulkResults({
  results,
  itemsById,
}: {
  results: BulkPaymentResult[];
  itemsById: Map<string, PayoutItem>;
}) {
  const count = (s: BulkPaymentStatus) => results.filter((r) => r.status === s).length;
  const recorded = count('recorded');
  const already = count('already_recorded');
  const invalid = count('invalid');
  return (
    <section aria-label="Bulk record results" className="stack">
      <Alert
        role="status"
        tone={invalid > 0 ? 'warning' : 'success'}
        title={`${recorded} recorded · ${already} already recorded · ${invalid} not recorded`}
      >
        Each line is processed on its own. Lines that were not recorded can be corrected and sent again.
      </Alert>
      <ul className="fin-results">
        {results.map((r) => {
          const item = itemsById.get(r.itemId);
          const meta = RESULT_META[r.status] ?? { tone: 'neutral' as Tone, label: r.status };
          return (
            <li key={r.itemId} className="fin-results__row" data-status={r.status}>
              <span className="fin-results__who">
                <strong>{item ? item.user.displayName : r.itemId}</strong>
                {item && <Money amount={item.amount} currency={item.currency} />}
              </span>
              <Badge tone={meta.tone} dot>
                {meta.label}
              </Badge>
              <span className="fin-results__message text-small">{r.message}</span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

/** Bulk record payments: edit a table of items awaiting payment, or paste CSV. Shows per-item results. */
export function BulkRecordDialog({ open, onClose, batchId }: BulkRecordDialogProps) {
  const awaiting = useBatch(batchId, { itemStatus: 'AwaitingPayment', page: 1, pageSize: 200 }, open);
  const record = useRecordPayments(batchId);
  const toast = useToast();
  const [mode, setMode] = useState<'table' | 'csv'>('table');
  const [refs, setRefs] = useState<Record<string, string>>({});
  const [csv, setCsv] = useState('');
  const [paidAt, setPaidAt] = useState('');
  const [results, setResults] = useState<BulkPaymentResult[] | null>(null);
  const [itemsSnapshot, setItemsSnapshot] = useState<Map<string, PayoutItem>>(new Map());

  useEffect(() => {
    if (open) {
      setMode('table');
      setRefs({});
      setCsv('');
      setPaidAt(toLocalInputValue());
      setResults(null);
    }
  }, [open]);

  const items = useMemo(() => awaiting.data?.items.items ?? [], [awaiting.data]);
  const paidIso = localInputToIso(paidAt);

  const tableLines: BulkPaymentLine[] = paidIso
    ? items
        .filter((i) => (refs[i.itemId] ?? '').trim())
        .map((i) => ({ itemId: i.itemId, paymentReference: refs[i.itemId]!.trim(), paidAt: paidIso }))
    : [];
  const parsed = mode === 'csv' ? parsePaymentCsv(csv, paidIso) : { lines: tableLines, errors: [] };
  const lines = parsed.lines;
  const canSubmit = !results && lines.length > 0 && parsed.errors.length === 0 && lines.length <= 1000;

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      size="lg"
      title="Record payments in bulk"
      description="Record references for payments you have already made. Each line succeeds or fails on its own."
      submitLabel={`Record ${lines.length} ${lines.length === 1 ? 'payment' : 'payments'}`}
      canSubmit={canSubmit}
      hideSubmit={!!results}
      onSubmit={async () => {
        const map = new Map(items.map((i) => [i.itemId, i]));
        const response = await record.mutateAsync(lines);
        setItemsSnapshot(map);
        setResults(response);
        const ok = response.filter((r) => r.status === 'recorded').length;
        toast.info('Bulk record finished', `${ok} of ${response.length} payments recorded.`);
        return false;
      }}
    >
      {results ? (
        <BulkResults results={results} itemsById={itemsSnapshot} />
      ) : (
        <>
          <RadioGroup
            legend="How do you want to enter references?"
            orientation="horizontal"
            value={mode}
            onChange={(v) => setMode(v as 'table' | 'csv')}
            options={[
              { value: 'table', label: 'Edit a table' },
              { value: 'csv', label: 'Paste CSV' },
            ]}
          />
          <FormField
            label="Paid at"
            hint="Your local time. Used for every line without its own date."
            required
            error={!paidIso ? 'Enter when the payments were made.' : null}
          >
            <Input
              type="datetime-local"
              value={paidAt}
              max={toLocalInputValue()}
              onChange={(e) => setPaidAt(e.target.value)}
            />
          </FormField>
          {mode === 'table' ? (
            awaiting.isPending ? (
              <Skeleton height={120} />
            ) : awaiting.isError ? (
              <QueryError error={awaiting.error} onRetry={() => awaiting.refetch()} />
            ) : items.length === 0 ? (
              <Alert tone="info">No items are awaiting payment in this batch.</Alert>
            ) : (
              <fieldset className="fin-fieldset">
                <legend className="ui-field__label">Payment references</legend>
                <p className="text-muted text-small">Leave a row empty to skip it.</p>
                <ul className="fin-bulk-table">
                  {items.map((i) => (
                    <li key={i.itemId} className="fin-bulk-table__row">
                      <label htmlFor={`bulk-${i.itemId}`} className="fin-bulk-table__who">
                        <strong>{i.user.displayName}</strong>
                        <span className="text-muted text-small">
                          <Money amount={i.amount} currency={i.currency} /> ·{' '}
                          {i.destinationHint ?? 'no destination'}
                        </span>
                      </label>
                      <Input
                        id={`bulk-${i.itemId}`}
                        size="sm"
                        value={refs[i.itemId] ?? ''}
                        maxLength={120}
                        autoComplete="off"
                        spellCheck={false}
                        placeholder="Payment reference"
                        onChange={(e) => setRefs((r) => ({ ...r, [i.itemId]: e.target.value }))}
                      />
                    </li>
                  ))}
                </ul>
              </fieldset>
            )
          ) : (
            <FormField
              label="CSV lines"
              hint="One payment per line: itemId,paymentReference[,paidAt]. A header row is ignored."
              required
              error={parsed.errors.length > 0 ? parsed.errors : null}
            >
              <Textarea
                rows={6}
                value={csv}
                spellCheck={false}
                className="fin-mono"
                placeholder={'itemId,paymentReference,paidAt\n3f2c…,TRX-10021,2026-10-02T10:00:00Z'}
                onChange={(e) => setCsv(e.target.value)}
              />
            </FormField>
          )}
        </>
      )}
    </FormDialog>
  );
}
