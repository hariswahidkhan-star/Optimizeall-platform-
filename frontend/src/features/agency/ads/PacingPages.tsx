import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BellRing, Gauge, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  KeyValueList,
  PageHeader,
  ProgressBar,
  Select,
  Skeleton,
  useToast,
  type DataTableColumn,
  type Tone,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useClientParam } from '../social/shared';
import { AD_PLATFORMS, PLATFORM_LABELS, adsKeys, useAdsClients, type AdAlert, type AdPlatform, type Budget } from './api';
import { AdsClientPicker, money, times } from './shared';
import './ads.css';

const STATE_TONES: Record<Budget['pacing']['state'], Tone> = {
  Over: 'danger',
  Under: 'warning',
  OnTrack: 'success',
  NotStarted: 'neutral',
  NoBudget: 'neutral',
};

const STATE_LABELS: Record<Budget['pacing']['state'], string> = {
  Over: 'Over pacing',
  Under: 'Under pacing',
  OnTrack: 'On track',
  NotStarted: 'Not started',
  NoBudget: 'No budget',
};

function monthValue(d = new Date()) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

/** Budget pacing board: actual vs expected-to-date and month-end projection for every budget. */
export function PacingPage() {
  const [clientId, setClientId] = useClientParam();
  const [month, setMonth] = useState(monthValue());
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<Budget | null>(null);
  const [deleting, setDeleting] = useState<Budget | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const params = { month: `${month}-01`, clientId };
  const query = useQuery({
    queryKey: adsKeys.pacing(params),
    queryFn: () => api.get<Budget[]>('/agency/ads/pacing', { query: params }),
  });
  return (
    <>
      <PageHeader
        title="Budget pacing"
        description="Expected-to-date = budget × days elapsed ÷ days in month (data through yesterday). The projection adds the last 7 days' run-rate for the rest of the month."
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setAdding(true)}>
            Add budget
          </Button>
        }
      />
      <div className="ad-toolbar">
        <AdsClientPicker value={clientId} onChange={setClientId} allowAll />
        <FormField label="Month">
          <Input type="month" value={month} onChange={(e) => setMonth(e.target.value || monthValue())} />
        </FormField>
      </div>
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : query.isLoading ? (
        <Skeleton height="16rem" />
      ) : (query.data ?? []).length === 0 ? (
        <EmptyState icon={<Gauge />} title="No budgets for this month" description="Add a monthly budget per client, platform or campaign." />
      ) : (
        <ul className="ad-pacing-board" aria-label="Budgets">
          {query.data!.map((b) => (
            <li key={b.id}>
              <PacingCard budget={b} onEdit={() => setEditing(b)} onDelete={() => setDeleting(b)} />
            </li>
          ))}
        </ul>
      )}
      {adding && <BudgetDialog defaultClient={clientId} month={month} onClose={() => setAdding(false)} />}
      {editing && <BudgetDialog budget={editing} month={editing.month.slice(0, 7)} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this budget?"
        description={deleting ? `${deleting.clientName} · ${deleting.scopeLabel}: pacing and alerts for it stop.` : undefined}
        confirmLabel="Delete budget"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/ads/budgets/${deleting.id}`);
          toast.success('Budget deleted');
          void queryClient.invalidateQueries({ queryKey: adsKeys.all });
        }}
      />
    </>
  );
}

export function PacingCard({ budget: b, onEdit, onDelete }: { budget: Budget; onEdit?: () => void; onDelete?: () => void }) {
  const p = b.pacing;
  const spentShare = b.amount > 0 ? Math.min(p.actualToDate / b.amount, 1.5) : 0;
  return (
    <Card as="article" aria-labelledby={`pace-${b.id}`} className="ad-pacing-card">
      <CardBody className="stack">
        <div className="cluster" style={{ justifyContent: 'space-between' }}>
          <h2 id={`pace-${b.id}`} className="ad-h2">
            {b.clientName}
          </h2>
          <Badge tone={STATE_TONES[p.state]} dot>
            {STATE_LABELS[p.state]}
          </Badge>
        </div>
        <p className="ad-muted">
          {b.scopeLabel} · {new Date(`${b.month}T12:00:00Z`).toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}
        </p>
        <ProgressBar
          value={Math.round(spentShare * 100)}
          max={150}
          label={`${b.clientName} spend against budget`}
          valueText={`${money(p.actualToDate, b.currency)} of ${money(b.amount, b.currency)}`}
        />
        <KeyValueList
          layout="inline"
          items={[
            { label: 'Spent to date', value: money(p.actualToDate, b.currency) },
            { label: 'Expected to date', value: money(p.expectedToDate, b.currency) },
            { label: 'Pacing', value: p.pacingRatio == null ? '—' : `${Math.round(p.pacingRatio * 100)}%` },
            { label: 'Projected month-end', value: money(p.projectedMonthEnd, b.currency) },
            { label: 'Budget', value: money(b.amount, b.currency) },
            { label: 'Days elapsed', value: `${p.daysElapsed} of ${p.daysInMonth}` },
            ...(b.targetCpa != null ? [{ label: 'CPA (target)', value: `${money(b.actualCpa, b.currency)} (${money(b.targetCpa, b.currency)})` }] : []),
            ...(b.targetRoas != null ? [{ label: 'ROAS (target)', value: `${times(b.actualRoas)} (${times(b.targetRoas)})` }] : []),
          ]}
        />
        {b.fxMissing.length > 0 && <p className="ad-warning">Missing exchange rate: {b.fxMissing.join(', ')}</p>}
        {(onEdit || onDelete) && (
          <div className="cluster">
            {onEdit && (
              <Button size="sm" variant="secondary" leadingIcon={<Pencil />} onClick={onEdit} aria-label={`Edit budget of ${b.clientName} (${b.scopeLabel})`}>
                Edit
              </Button>
            )}
            {onDelete && (
              <Button size="sm" variant="ghost" leadingIcon={<Trash2 />} onClick={onDelete} aria-label={`Delete budget of ${b.clientName} (${b.scopeLabel})`}>
                Delete
              </Button>
            )}
          </div>
        )}
      </CardBody>
    </Card>
  );
}

export function BudgetDialog({ defaultClient, budget, month, onClose }: { defaultClient?: string; budget?: Budget; month: string; onClose: () => void }) {
  const clients = useAdsClients();
  const queryClient = useQueryClient();
  const [clientId, setClientId] = useState(budget?.clientAccountId ?? defaultClient ?? '');
  const [budgetMonth, setBudgetMonth] = useState(month);
  const [platform, setPlatform] = useState<string>(budget?.platform ?? '');
  const [amount, setAmount] = useState(budget ? String(budget.amount) : '');
  const [currency, setCurrency] = useState(budget?.currency ?? clients.data?.find((c) => c.id === defaultClient)?.currency ?? 'USD');
  const [over, setOver] = useState(budget ? String(Math.round(budget.overPacingThreshold * 100)) : '115');
  const [under, setUnder] = useState(budget ? String(Math.round(budget.underPacingThreshold * 100)) : '85');
  const [targetCpa, setTargetCpa] = useState(budget?.targetCpa?.toString() ?? '');
  const [targetRoas, setTargetRoas] = useState(budget?.targetRoas?.toString() ?? '');
  const [notes, setNotes] = useState(budget?.notes ?? '');
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => {
      const body = {
        clientAccountId: clientId,
        month: `${budgetMonth}-01`,
        platform: platform || null,
        campaignId: budget?.campaignId ?? null,
        amount: Number(amount),
        currency,
        overPacingThreshold: Number(over) / 100,
        underPacingThreshold: Number(under) / 100,
        targetCpa: targetCpa ? Number(targetCpa) : null,
        targetRoas: targetRoas ? Number(targetRoas) : null,
        notes: notes || null,
        concurrencyStamp: budget?.concurrencyStamp,
      };
      return budget ? api.put<Budget>(`/agency/ads/budgets/${budget.id}`, body) : api.post<Budget>('/agency/ads/budgets', body);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adsKeys.all });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title={budget ? 'Edit budget' : 'Add monthly budget'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={!clientId || !amount} loading={save.isPending} onClick={() => save.mutate()}>
            {budget ? 'Save' : 'Add budget'}
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Client" required hint={budget ? 'A budget cannot move to another client.' : undefined}>
          <Select
            value={clientId}
            disabled={!!budget}
            placeholder="Choose a client"
            onChange={(e) => {
              setClientId(e.target.value);
              const c = clients.data?.find((x) => x.id === e.target.value);
              if (c) setCurrency(c.currency);
            }}
            options={(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
          />
        </FormField>
        <FormField label="Platform" optional hint="Leave empty for all platforms">
          <Select value={platform} placeholder="All platforms" onChange={(e) => setPlatform(e.target.value)} options={AD_PLATFORMS.map((p: AdPlatform) => ({ value: p, label: PLATFORM_LABELS[p] }))} />
        </FormField>
        <div className="ad-stats">
          <FormField label="Amount" required>
            <Input type="number" min={0} step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} />
          </FormField>
          <FormField label="Currency">
            <Input value={currency} maxLength={3} onChange={(e) => setCurrency(e.target.value.toUpperCase())} />
          </FormField>
          <FormField label="Alert when over (%)">
            <Input type="number" value={over} onChange={(e) => setOver(e.target.value)} />
          </FormField>
          <FormField label="Alert when under (%)">
            <Input type="number" value={under} onChange={(e) => setUnder(e.target.value)} />
          </FormField>
          <FormField label="Target CPA" optional>
            <Input type="number" value={targetCpa} onChange={(e) => setTargetCpa(e.target.value)} />
          </FormField>
          <FormField label="Target ROAS" optional>
            <Input type="number" step="0.1" value={targetRoas} onChange={(e) => setTargetRoas(e.target.value)} />
          </FormField>
          <FormField label="Month">
            <Input type="month" value={budgetMonth} onChange={(e) => setBudgetMonth(e.target.value || month)} />
          </FormField>
        </div>
        <FormField label="Notes" optional>
          <Input value={notes} maxLength={1000} onChange={(e) => setNotes(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

const SEVERITY_TONES: Record<AdAlert['severity'], Tone> = { Critical: 'danger', Warning: 'warning', Info: 'info' };

/** Alerts raised by the daily evaluation (deduplicated per scope and month). */
export function AlertsPage() {
  const [clientId, setClientId] = useClientParam();
  const [status, setStatus] = useState<string>('Open');
  const toast = useToast();
  const queryClient = useQueryClient();
  const params = { clientId, status: status || undefined };
  const query = useQuery({
    queryKey: adsKeys.alerts(params),
    queryFn: () => api.get<AdAlert[]>('/agency/ads/alerts', { query: params }),
  });
  const act = useMutation({
    mutationFn: ({ id, kind }: { id: string; kind: 'acknowledge' | 'resolve' | 'reopen' }) => api.post<AdAlert>(`/agency/ads/alerts/${id}/${kind}`),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['ads', 'alerts'] }),
    onError: (e) => toast.error('Not updated', errorMessage(e)),
  });
  const columns: DataTableColumn<AdAlert>[] = [
    {
      id: 'alert',
      header: 'Alert',
      primary: true,
      cell: (a) => (
        <span className="stack" style={{ gap: 2 }}>
          <strong>{a.title}</strong>
          <span className="ad-muted">{a.message}</span>
        </span>
      ),
    },
    { id: 'client', header: 'Client', cell: (a) => a.clientName },
    { id: 'severity', header: 'Severity', cell: (a) => <Badge tone={SEVERITY_TONES[a.severity]}>{a.severity}</Badge> },
    { id: 'raised', header: 'Raised', hideOnMobile: true, cell: (a) => <DateTime value={a.createdAt} format="relative" /> },
    { id: 'status', header: 'Status', cell: (a) => a.status },
  ];
  return (
    <>
      <PageHeader title="Ads alerts" description="Over/under pacing, CPA above target, ROAS below target and spend without conversions. One alert per condition, scope and month." />
      <div className="ad-toolbar">
        <AdsClientPicker value={clientId} onChange={setClientId} allowAll />
        <FormField label="Status">
          <Select value={status} placeholder="All" onChange={(e) => setStatus(e.target.value)} options={['Open', 'Acknowledged', 'Resolved'].map((s) => ({ value: s, label: s }))} />
        </FormField>
      </div>
      {query.isError ? (
        <ErrorState error={query.error} />
      ) : (
        <DataTable
          caption="Alerts"
          columns={columns}
          rows={query.data ?? []}
          getRowId={(a) => a.id}
          rowLabel={(a) => a.title}
          loading={query.isLoading}
          rowActions={(a) => [
            ...(a.status === 'Open' ? [{ id: 'ack', label: 'Acknowledge', onSelect: () => act.mutate({ id: a.id, kind: 'acknowledge' }) }] : []),
            ...(a.status !== 'Resolved' ? [{ id: 'resolve', label: 'Resolve (done)', onSelect: () => act.mutate({ id: a.id, kind: 'resolve' }) }] : []),
            ...(a.status !== 'Open' ? [{ id: 'reopen', label: 'Reopen', onSelect: () => act.mutate({ id: a.id, kind: 'reopen' }) }] : []),
          ]}
          emptyState={<EmptyState icon={<BellRing />} headingLevel={2} title="No alerts" />}
        />
      )}
    </>
  );
}
