import { Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  CardBody,
  Checkbox,
  DataTable,
  DateTime,
  EmptyState,
  FilterBar,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { useCreateExchangeRate, useExchangeRates } from '../api/hooks';
import type { ExchangeRate } from '../api/types';
import { QueryError } from '../components/common';
import { FormDialog } from '../components/FormDialog';
import { currencyOptions, localInputToIso, toLocalInputValue } from '../lib/format';
import { useCan } from '../lib/useCan';

const columns: DataTableColumn<ExchangeRate>[] = [
  {
    id: 'pair',
    header: 'Pair',
    primary: true,
    cell: (r) => (
      <strong>
        {r.baseCurrency} → {r.quoteCurrency}
      </strong>
    ),
  },
  {
    id: 'rate',
    header: 'Rate',
    align: 'right',
    cell: (r) => (
      <span className="tabular">
        1 {r.baseCurrency} = {r.rate} {r.quoteCurrency}
      </span>
    ),
  },
  { id: 'effective', header: 'Effective from', cell: (r) => <DateTime value={r.effectiveAt} withZone /> },
  { id: 'source', header: 'Source', cell: (r) => r.source },
  { id: 'created', header: 'Added', cell: (r) => <DateTime value={r.createdAt} />, hideOnMobile: true },
];

function AddRateDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const create = useCreateExchangeRate();
  const toast = useToast();
  const [base, setBase] = useState('EUR');
  const [quote, setQuote] = useState('USD');
  const [rate, setRate] = useState('');
  const [effectiveAt, setEffectiveAt] = useState('');
  const [source, setSource] = useState('manual');
  const [reason, setReason] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [touched, setTouched] = useState(false);

  useEffect(() => {
    if (open) {
      setBase('EUR');
      setQuote('USD');
      setRate('');
      setEffectiveAt(toLocalInputValue());
      setSource('manual');
      setReason('');
      setConfirmed(false);
      setTouched(false);
    }
  }, [open]);

  const numeric = Number(rate);
  const errors = {
    pair: base === quote ? 'Base and quote currency must be different.' : null,
    rate:
      !rate.trim() || !Number.isFinite(numeric) || numeric <= 0 || numeric > 1_000_000
        ? 'Enter a rate greater than 0 (at most 1,000,000).'
        : /\.\d{9,}$/.test(rate.trim())
          ? 'Use at most 8 decimals.'
          : null,
    effective: !localInputToIso(effectiveAt) ? 'Enter when the rate takes effect.' : null,
    source: source.trim().length < 2 || source.trim().length > 50 ? 'Source must be 2–50 characters.' : null,
    reason: reason.trim().length < 10 ? 'Give a reason of at least 10 characters.' : null,
    confirm: !confirmed ? 'Please confirm.' : null,
  };
  const valid = Object.values(errors).every((e) => !e);

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      sensitive
      title="Add exchange rate"
      description="Rates are immutable. A new rate applies to earnings created after it takes effect."
      submitLabel="Add rate"
      onSubmit={async () => {
        setTouched(true);
        if (!valid) return false;
        await create.mutateAsync({
          baseCurrency: base,
          quoteCurrency: quote,
          rate: numeric,
          effectiveAt: localInputToIso(effectiveAt)!,
          source: source.trim(),
          reason: reason.trim(),
        });
        toast.success('Exchange rate added', `1 ${base} = ${rate} ${quote}`);
      }}
    >
      <Alert tone="info">
        Existing earnings keep the rate stored when they were created — adding a rate never changes past
        amounts.
      </Alert>
      <div className="fin-form-row">
        <FormField label="Base currency" required error={touched ? errors.pair : null}>
          <Select value={base} onChange={(e) => setBase(e.target.value)} options={currencyOptions} />
        </FormField>
        <FormField label="Quote currency" required>
          <Select value={quote} onChange={(e) => setQuote(e.target.value)} options={currencyOptions} />
        </FormField>
      </div>
      <FormField
        label="Rate"
        hint={`How many ${quote} one ${base} buys. Up to 8 decimals.`}
        required
        error={touched ? errors.rate : null}
      >
        <Input
          type="number"
          inputMode="decimal"
          step="any"
          min="0"
          value={rate}
          onChange={(e) => setRate(e.target.value)}
        />
      </FormField>
      <div className="fin-form-row">
        <FormField
          label="Effective from"
          hint="Your local time; at most one day in the past."
          required
          error={touched ? errors.effective : null}
        >
          <Input type="datetime-local" value={effectiveAt} onChange={(e) => setEffectiveAt(e.target.value)} />
        </FormField>
        <FormField label="Source" required error={touched ? errors.source : null}>
          <Input value={source} maxLength={50} onChange={(e) => setSource(e.target.value)} />
        </FormField>
      </div>
      <FormField
        label="Reason"
        hint="At least 10 characters. Recorded in the audit log."
        required
        error={touched ? errors.reason : null}
      >
        <Textarea rows={2} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
      <Checkbox
        label="I confirm this rate is correct."
        checked={confirmed}
        invalid={touched && !confirmed}
        onChange={(e) => setConfirmed(e.target.checked)}
      />
    </FormDialog>
  );
}

export function ExchangeRatesPage() {
  const can = useCan();
  const [base, setBase] = useState<string | undefined>();
  const [quote, setQuote] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [adding, setAdding] = useState(false);
  const query = useExchangeRates({ base, quote, page, pageSize });

  return (
    <>
      <PageHeader
        title="Exchange rates"
        description="Rates used to convert earnings into the settlement currency. When only the reverse pair exists, its inverse is used."
        actions={
          can.settings && (
            <Button leadingIcon={<Plus />} onClick={() => setAdding(true)}>
              Add rate
            </Button>
          )
        }
      />
      <div className="stack fin-page">
        <Alert tone="info" title="Stored rates never change">
          Every earning stores the rate that was in force when it was created. Adding a new rate only affects
          earnings created after its effective time.
        </Alert>
        <Card>
          <CardBody className="stack">
            <FilterBar
              filters={[
                { id: 'base', label: 'Base', options: currencyOptions },
                { id: 'quote', label: 'Quote', options: currencyOptions },
              ]}
              values={{ base, quote }}
              onFilterChange={(id, v) => {
                if (id === 'base') setBase(v);
                else setQuote(v);
                setPage(1);
              }}
              onReset={() => {
                setBase(undefined);
                setQuote(undefined);
                setPage(1);
              }}
            />
            {query.isError ? (
              <QueryError error={query.error} onRetry={() => query.refetch()} />
            ) : (
              <>
                <DataTable
                  caption="Exchange rates, newest effective first"
                  columns={columns}
                  rows={query.data?.items ?? []}
                  getRowId={(r) => r.id}
                  loading={query.isPending}
                  emptyState={<EmptyState compact headingLevel={3} title="No exchange rates" />}
                />
                {query.data && query.data.total > 0 && (
                  <Pagination
                    page={page}
                    pageSize={pageSize}
                    total={query.data.total}
                    onPageChange={setPage}
                    onPageSizeChange={(s) => {
                      setPageSize(s);
                      setPage(1);
                    }}
                    label="Exchange rate pages"
                  />
                )}
              </>
            )}
          </CardBody>
        </Card>
      </div>
      <AddRateDialog open={adding} onClose={() => setAdding(false)} />
    </>
  );
}
