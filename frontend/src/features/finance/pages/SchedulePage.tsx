import { Pencil } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  DataTable,
  DateTime,
  FormField,
  Input,
  KeyValueList,
  Money,
  PageHeader,
  Select,
  Skeleton,
  Switch,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { useSchedule, useUpdateSchedule } from '../api/hooks';
import type { PayoutFrequency, PayoutPeriod, PayoutSchedule, PayoutScheduleResponse } from '../api/types';
import { QueryError } from '../components/common';
import { FormDialog } from '../components/FormDialog';
import { currencyOptions, dateOnlyToDisplay, localInputToIso, toLocalInputValue } from '../lib/format';
import { useCan } from '../lib/useCan';

const FREQUENCIES: { value: PayoutFrequency; label: string }[] = [
  { value: 'Weekly', label: 'Weekly' },
  { value: 'Biweekly', label: 'Every two weeks' },
  { value: 'Monthly', label: 'Monthly' },
];

function timeZones(): string[] {
  try {
    const zones = (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.(
      'timeZone',
    );
    if (zones && zones.length > 0) return zones.includes('UTC') ? zones : ['UTC', ...zones];
  } catch {
    // Older engines: free text only.
  }
  return ['UTC'];
}

function frequencyLabel(f: string) {
  return FREQUENCIES.find((x) => x.value === f)?.label ?? f;
}

export function scheduleItems(s: PayoutSchedule) {
  return [
    { label: 'Frequency', value: frequencyLabel(s.frequency) },
    { label: 'Anchor cutoff date', value: dateOnlyToDisplay(s.anchorCutoffDate) },
    { label: 'Cutoff time', value: `${s.cutoffLocalTime} (${s.timeZone})` },
    {
      label: 'Payment delay',
      value: `${s.paymentDelayDays} ${s.paymentDelayDays === 1 ? 'day' : 'days'} after cutoff`,
    },
    {
      label: 'Minimum payout',
      value: <Money amount={s.minimumPayoutAmount} currency={s.settlementCurrency} />,
    },
    { label: 'Settlement currency', value: s.settlementCurrency },
    { label: 'Earning hold', value: `${s.earningHoldDays} ${s.earningHoldDays === 1 ? 'day' : 'days'}` },
    { label: 'Auto-prepare batches', value: s.autoPrepareBatches ? 'On' : 'Off' },
  ];
}

function PeriodsTable({
  periods,
  caption,
  timeZone,
}: {
  periods: PayoutPeriod[];
  caption: string;
  timeZone: string;
}) {
  const columns: DataTableColumn<PayoutPeriod>[] = [
    { id: 'key', header: 'Period', primary: true, cell: (p) => <strong>{p.periodKey}</strong> },
    {
      id: 'start',
      header: 'Starts after',
      cell: (p) => <DateTime value={p.periodStart} withZone />,
      hideOnMobile: true,
    },
    {
      id: 'cutoff',
      header: `Cutoff (${timeZone})`,
      cell: (p) => <DateTime value={p.cutoffAt} timeZone={timeZone} withZone />,
    },
    { id: 'mine', header: 'Cutoff (your time)', cell: (p) => <DateTime value={p.cutoffAt} withZone /> },
    { id: 'pay', header: 'Payment date', cell: (p) => dateOnlyToDisplay(p.paymentDate) },
  ];
  return <DataTable caption={caption} columns={columns} rows={periods} getRowId={(p) => p.periodKey} />;
}

function EditScheduleDialog({
  open,
  onClose,
  current,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  current: PayoutSchedule;
  onSaved: (data: PayoutScheduleResponse) => void;
}) {
  const update = useUpdateSchedule();
  const toast = useToast();
  const zones = useMemo(timeZones, []);
  const zoneListId = useId();
  const [form, setForm] = useState({
    frequency: current.frequency as PayoutFrequency,
    anchorCutoffDate: current.anchorCutoffDate,
    cutoffLocalTime: current.cutoffLocalTime,
    timeZone: current.timeZone,
    paymentDelayDays: String(current.paymentDelayDays),
    minimumPayoutAmount: String(current.minimumPayoutAmount),
    settlementCurrency: current.settlementCurrency,
    earningHoldDays: String(current.earningHoldDays),
    autoPrepareBatches: current.autoPrepareBatches,
    effectiveFrom: '',
    reason: '',
  });
  const [confirmed, setConfirmed] = useState(false);
  const [touched, setTouched] = useState(false);
  const currentRef = useRef(current);
  currentRef.current = current;

  // Prefill from the active schedule each time the dialog opens (not on background refetches).
  useEffect(() => {
    if (open) {
      const current = currentRef.current;
      setForm({
        frequency: current.frequency,
        anchorCutoffDate: current.anchorCutoffDate,
        cutoffLocalTime: current.cutoffLocalTime,
        timeZone: current.timeZone,
        paymentDelayDays: String(current.paymentDelayDays),
        minimumPayoutAmount: String(current.minimumPayoutAmount),
        settlementCurrency: current.settlementCurrency,
        earningHoldDays: String(current.earningHoldDays),
        autoPrepareBatches: current.autoPrepareBatches,
        effectiveFrom: toLocalInputValue(new Date(Date.now() + 60_000)),
        reason: '',
      });
      setConfirmed(false);
      setTouched(false);
    }
  }, [open]);

  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }));
  const int = (v: string, min: number, max: number) => {
    const n = Number(v);
    return v.trim() !== '' && Number.isInteger(n) && n >= min && n <= max;
  };
  const effectiveIso = localInputToIso(form.effectiveFrom);
  const errors = {
    anchor: /^\d{4}-\d{2}-\d{2}$/.test(form.anchorCutoffDate) ? null : 'Pick the anchor cutoff date.',
    time: /^([01]\d|2[0-3]):[0-5]\d(:[0-5]\d)?$/.test(form.cutoffLocalTime) ? null : 'Use HH:mm or HH:mm:ss.',
    zone: form.timeZone.trim() ? null : 'Enter an IANA time zone.',
    delay: int(form.paymentDelayDays, 0, 30) ? null : 'Whole days from 0 to 30.',
    minimum:
      form.minimumPayoutAmount.trim() !== '' &&
      Number(form.minimumPayoutAmount) >= 0 &&
      Number(form.minimumPayoutAmount) <= 100_000
        ? null
        : 'An amount from 0 to 100,000.',
    hold: int(form.earningHoldDays, 0, 60) ? null : 'Whole days from 0 to 60.',
    effective: !effectiveIso
      ? 'Enter when the change takes effect.'
      : new Date(effectiveIso).getTime() < Date.now() - 5 * 60_000
        ? 'The change can’t take effect in the past.'
        : null,
    reason:
      form.reason.trim().length < 10 || form.reason.trim().length > 500
        ? 'Give a reason of 10–500 characters.'
        : null,
    confirm: confirmed ? null : 'Please confirm.',
  };
  const valid = Object.values(errors).every((e) => !e);
  const err = (e: string | null) => (touched ? e : null);

  return (
    <FormDialog
      open={open}
      onClose={onClose}
      size="lg"
      sensitive
      title="Change payout schedule"
      description="Saves a new schedule version from the effective time. Past periods and existing batches are not changed."
      submitLabel="Save schedule"
      onSubmit={async () => {
        setTouched(true);
        if (!valid) return false;
        const data = await update.mutateAsync({
          frequency: form.frequency,
          anchorCutoffDate: form.anchorCutoffDate,
          cutoffLocalTime: form.cutoffLocalTime,
          timeZone: form.timeZone.trim(),
          paymentDelayDays: Number(form.paymentDelayDays),
          minimumPayoutAmount: Number(form.minimumPayoutAmount),
          settlementCurrency: form.settlementCurrency,
          earningHoldDays: Number(form.earningHoldDays),
          autoPrepareBatches: form.autoPrepareBatches,
          effectiveFrom: effectiveIso!,
          reason: form.reason.trim(),
          confirm: true,
        });
        onSaved(data);
        toast.success('Payout schedule saved', 'The server’s period preview is shown on the page.');
      }}
    >
      <div className="fin-form-grid">
        <FormField label="Frequency" required>
          <Select
            value={form.frequency}
            onChange={(e) => set('frequency', e.target.value as PayoutFrequency)}
            options={FREQUENCIES}
          />
        </FormField>
        <FormField
          label="Anchor cutoff date"
          hint="Any past or future cutoff date of the cycle."
          required
          error={err(errors.anchor)}
        >
          <Input
            type="date"
            value={form.anchorCutoffDate}
            onChange={(e) => set('anchorCutoffDate', e.target.value)}
          />
        </FormField>
        <FormField label="Cutoff time" hint="Local to the time zone below." required error={err(errors.time)}>
          <Input
            type="time"
            step={1}
            value={form.cutoffLocalTime}
            onChange={(e) => set('cutoffLocalTime', e.target.value)}
          />
        </FormField>
        <FormField label="Time zone" hint="IANA name, e.g. Asia/Karachi." required error={err(errors.zone)}>
          <Input
            list={zoneListId}
            value={form.timeZone}
            autoComplete="off"
            onChange={(e) => set('timeZone', e.target.value)}
          />
        </FormField>
        <datalist id={zoneListId}>
          {zones.map((z) => (
            <option key={z} value={z} />
          ))}
        </datalist>
        <FormField label="Payment delay (days)" required error={err(errors.delay)}>
          <Input
            type="number"
            min={0}
            max={30}
            value={form.paymentDelayDays}
            onChange={(e) => set('paymentDelayDays', e.target.value)}
          />
        </FormField>
        <FormField label="Settlement currency" required>
          <Select
            value={form.settlementCurrency}
            onChange={(e) => set('settlementCurrency', e.target.value)}
            options={currencyOptions}
          />
        </FormField>
        <FormField label={`Minimum payout (${form.settlementCurrency})`} required error={err(errors.minimum)}>
          <Input
            type="number"
            min={0}
            step="any"
            inputMode="decimal"
            value={form.minimumPayoutAmount}
            onChange={(e) => set('minimumPayoutAmount', e.target.value)}
          />
        </FormField>
        <FormField
          label="Earning hold (days)"
          hint="Reversal buffer before an earning is payable."
          required
          error={err(errors.hold)}
        >
          <Input
            type="number"
            min={0}
            max={60}
            value={form.earningHoldDays}
            onChange={(e) => set('earningHoldDays', e.target.value)}
          />
        </FormField>
        <FormField
          label="Effective from"
          hint="Your local time; now or later."
          required
          error={err(errors.effective)}
        >
          <Input
            type="datetime-local"
            value={form.effectiveFrom}
            onChange={(e) => set('effectiveFrom', e.target.value)}
          />
        </FormField>
      </div>
      <Switch
        checked={form.autoPrepareBatches}
        onCheckedChange={(v) => set('autoPrepareBatches', v)}
        label="Prepare batches automatically after each cutoff"
        description="The background job creates the draft batch; finance still reviews and finalizes it."
      />
      {form.settlementCurrency !== current.settlementCurrency && (
        <Alert tone="warning">
          Changing the settlement currency is refused while unpaid earnings are settled in{' '}
          {current.settlementCurrency}.
        </Alert>
      )}
      <FormField
        label="Reason"
        hint="10–500 characters. Recorded in the audit log."
        required
        error={err(errors.reason)}
      >
        <Textarea
          rows={2}
          maxLength={500}
          value={form.reason}
          onChange={(e) => set('reason', e.target.value)}
        />
      </FormField>
      <Checkbox
        label="I confirm this schedule change."
        checked={confirmed}
        invalid={touched && !confirmed}
        onChange={(e) => setConfirmed(e.target.checked)}
      />
    </FormDialog>
  );
}

export function SchedulePage() {
  const can = useCan();
  const query = useSchedule();
  const [editing, setEditing] = useState(false);
  const [savedAt, setSavedAt] = useState<string | null>(null);

  const historyColumns: DataTableColumn<PayoutSchedule>[] = [
    {
      id: 'from',
      header: 'Effective from',
      primary: true,
      cell: (s) => <DateTime value={s.effectiveFrom} withZone />,
    },
    { id: 'freq', header: 'Frequency', cell: (s) => frequencyLabel(s.frequency) },
    { id: 'cutoff', header: 'Cutoff', cell: (s) => `${s.cutoffLocalTime} ${s.timeZone}` },
    {
      id: 'min',
      header: 'Minimum',
      align: 'right',
      cell: (s) => <Money amount={s.minimumPayoutAmount} currency={s.settlementCurrency} />,
    },
    { id: 'hold', header: 'Hold', cell: (s) => `${s.earningHoldDays} d`, hideOnMobile: true },
    { id: 'delay', header: 'Payment delay', cell: (s) => `${s.paymentDelayDays} d`, hideOnMobile: true },
    { id: 'reason', header: 'Reason', cell: (s) => s.changeReason ?? '—' },
  ];

  if (query.isPending) {
    return (
      <>
        <PageHeader title="Payout schedule" />
        <Skeleton height={320} />
      </>
    );
  }
  if (query.isError) {
    return (
      <>
        <PageHeader title="Payout schedule" />
        <QueryError error={query.error} onRetry={() => query.refetch()} compact={false} />
      </>
    );
  }
  const data = query.data;
  const s = data.current;

  return (
    <>
      <PageHeader
        title="Payout schedule"
        description="When periods close, when participants are paid, and the rules for including earnings."
        actions={
          can.settings && (
            <Button leadingIcon={<Pencil />} onClick={() => setEditing(true)}>
              Change schedule
            </Button>
          )
        }
      />
      <div className="stack fin-page">
        {savedAt && (
          <Alert tone="success" role="status" title="Schedule saved" onDismiss={() => setSavedAt(null)}>
            The periods below are the server’s calculation after your change. A version effective later
            appears under “Scheduled changes” and takes over from its effective time.
          </Alert>
        )}
        <div className="fin-two-col">
          <Card as="section" aria-labelledby="schedule-current">
            <CardHeader
              titleId="schedule-current"
              title="Current schedule"
              actions={s.isDefault ? <Badge tone="neutral">Built-in default</Badge> : undefined}
              description={
                s.effectiveFrom ? (
                  <>
                    In force since <DateTime value={s.effectiveFrom} withZone />
                  </>
                ) : undefined
              }
            />
            <CardBody>
              <KeyValueList items={scheduleItems(s)} />
            </CardBody>
          </Card>
          <Card as="section" aria-labelledby="schedule-period">
            <CardHeader
              titleId="schedule-period"
              title="Current period"
              description={data.currentPeriod.periodKey}
            />
            <CardBody>
              <KeyValueList
                items={[
                  {
                    label: 'Started after',
                    value: <DateTime value={data.currentPeriod.periodStart} withZone />,
                  },
                  {
                    label: `Cutoff (${s.timeZone})`,
                    value: <DateTime value={data.currentPeriod.cutoffAt} timeZone={s.timeZone} withZone />,
                  },
                  {
                    label: 'Cutoff (your time)',
                    value: <DateTime value={data.currentPeriod.cutoffAt} withZone />,
                  },
                  { label: 'Payment date', value: dateOnlyToDisplay(data.currentPeriod.paymentDate) },
                  { label: 'Last completed period', value: data.lastCompletedPeriod.periodKey },
                ]}
              />
            </CardBody>
          </Card>
        </div>
        <Card as="section" aria-labelledby="schedule-upcoming">
          <CardHeader
            titleId="schedule-upcoming"
            title="Upcoming periods"
            description="Calculated by the server from the active schedule (current period first)."
          />
          <CardBody>
            <PeriodsTable periods={data.upcoming} caption="Upcoming payout periods" timeZone={s.timeZone} />
          </CardBody>
        </Card>
        {data.scheduledChanges.length > 0 && (
          <Card as="section" aria-labelledby="schedule-changes">
            <CardHeader
              titleId="schedule-changes"
              title="Scheduled changes"
              description="Versions that take effect in the future."
            />
            <CardBody>
              <DataTable
                caption="Scheduled schedule changes"
                columns={historyColumns}
                rows={data.scheduledChanges}
                getRowId={(x) => x.id ?? `${x.effectiveFrom}`}
              />
            </CardBody>
          </Card>
        )}
        <Card as="section" aria-labelledby="schedule-history">
          <CardHeader titleId="schedule-history" title="History" description="Every version, newest first." />
          <CardBody>
            <DataTable
              caption="Payout schedule history"
              columns={historyColumns}
              rows={data.history}
              getRowId={(x) => x.id ?? `${x.effectiveFrom}`}
              emptyState={
                <p className="text-muted">No saved versions yet — the built-in default is in use.</p>
              }
            />
          </CardBody>
        </Card>
      </div>
      <EditScheduleDialog
        open={editing}
        onClose={() => setEditing(false)}
        current={s}
        onSaved={() => setSavedAt(new Date().toISOString())}
      />
    </>
  );
}
