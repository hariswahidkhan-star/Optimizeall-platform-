import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, CheckCircle2, ClipboardList, CopyPlus, FlaskConical, Link2, Megaphone, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  CopyField,
  DataTable,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Select,
  Switch,
  Textarea,
  useToast,
  type DataTableColumn,
  type MenuEntry,
  type Tone,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useClientParam } from '../social/shared';
import {
  AD_PLATFORMS,
  PLATFORM_LABELS,
  adsKeys,
  useAdsClients,
  type AdPlatform,
  type AdsSettings,
  type CopyLimit,
  type Creative,
  type Experiment,
  type MediaPlan,
  type MediaPlanLine,
  type PlanActuals,
  type UtmLink,
} from './api';
import { AdsClientPicker, SourceBadge, count, money, ratio } from './shared';
import './ads.css';

const platformOptions = AD_PLATFORMS.map((p) => ({ value: p, label: PLATFORM_LABELS[p] }));

// ---------------------------------------------------------------- media plans

export function MediaPlansPage() {
  const [clientId, setClientId] = useClientParam();
  const [creating, setCreating] = useState(false);
  const [editingPlan, setEditingPlan] = useState<MediaPlan | null>(null);
  const [deletingPlan, setDeletingPlan] = useState<MediaPlan | null>(null);
  const [viewing, setViewing] = useState<MediaPlan | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const refreshPlans = () => void queryClient.invalidateQueries({ queryKey: ['ads', 'plans'] });
  const setPlanStatus = useMutation({
    mutationFn: ({ plan, status }: { plan: MediaPlan; status: MediaPlan['status'] }) =>
      api.put<MediaPlan>(`/agency/ads/media-plans/${plan.id}`, { ...planBody(plan), status }),
    onSuccess: (p) => {
      toast.success(p.status === 'Approved' ? 'Plan approved' : p.status === 'Archived' ? 'Plan archived' : 'Plan back to draft', p.name);
      refreshPlans();
    },
    onError: (e) => toast.error('Not changed', errorMessage(e)),
  });
  const duplicatePlan = useMutation({
    mutationFn: (plan: MediaPlan) => {
      const [y, m] = plan.month.split('-').map(Number);
      const next = new Date(Date.UTC(y!, m!, 1)).toISOString().slice(0, 10);
      return api.post<MediaPlan>(`/agency/ads/media-plans/${plan.id}/duplicate`, undefined, { query: { month: next } });
    },
    onSuccess: (p) => {
      toast.success('Plan copied to the next month', p.name);
      refreshPlans();
    },
    onError: (e) => toast.error('Not copied', errorMessage(e)),
  });
  const plans = useQuery({
    queryKey: adsKeys.plans(clientId),
    queryFn: () => api.get<MediaPlan[]>('/agency/ads/media-plans', { query: { clientId } }),
  });
  const columns: DataTableColumn<MediaPlan>[] = [
    {
      id: 'name',
      header: 'Plan',
      primary: true,
      cell: (p) => (
        <Button variant="link" onClick={() => setViewing(p)}>
          {p.name}
        </Button>
      ),
    },
    { id: 'client', header: 'Client', cell: (p) => p.clientName },
    { id: 'month', header: 'Month', cell: (p) => p.month.slice(0, 7) },
    { id: 'total', header: 'Planned', align: 'right', cell: (p) => money(p.plannedTotal, p.currency) },
    { id: 'lines', header: 'Channels', cell: (p) => p.lines.map((l) => PLATFORM_LABELS[l.platform]).join(', ') },
    { id: 'status', header: 'Status', cell: (p) => <Badge tone={p.status === 'Approved' ? 'success' : 'neutral'}>{p.status}</Badge> },
  ];
  return (
    <>
      <PageHeader
        title="Media plans"
        description="Planned channels, budgets, flights and KPI targets per client and month — compared with actual spend and results."
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
            New media plan
          </Button>
        }
      />
      <div className="ad-toolbar">
        <AdsClientPicker value={clientId} onChange={setClientId} allowAll />
      </div>
      {plans.isError ? (
        <ErrorState error={plans.error} />
      ) : (
        <DataTable
          caption="Media plans"
          columns={columns}
          rows={plans.data ?? []}
          getRowId={(p) => p.id}
          rowLabel={(p) => p.name}
          loading={plans.isLoading}
          rowActions={(p) => [
            { id: 'view', label: 'Actual vs plan', onSelect: () => setViewing(p) },
            { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditingPlan(p) },
            ...(p.status === 'Draft'
              ? [{ id: 'approve', label: 'Approve plan', icon: <CheckCircle2 />, onSelect: () => setPlanStatus.mutate({ plan: p, status: 'Approved' }) }]
              : [{ id: 'draft', label: 'Back to draft', onSelect: () => setPlanStatus.mutate({ plan: p, status: 'Draft' }) }]),
            ...(p.status !== 'Archived' ? [{ id: 'archive', label: 'Archive', icon: <Archive />, onSelect: () => setPlanStatus.mutate({ plan: p, status: 'Archived' }) }] : []),
            { id: 'duplicate', label: 'Copy to next month', icon: <CopyPlus />, onSelect: () => duplicatePlan.mutate(p) },
            {
              id: 'delete',
              label: 'Delete',
              icon: <Trash2 />,
              danger: true,
              disabled: p.status === 'Approved',
              description: p.status === 'Approved' ? 'Approved plans are the agreed budget — archive first.' : undefined,
              onSelect: () => setDeletingPlan(p),
            },
          ]}
          emptyState={<EmptyState icon={<ClipboardList />} headingLevel={2} title="No media plans" />}
        />
      )}
      {viewing && <PlanActualsDialog plan={viewing} onClose={() => setViewing(null)} />}
      {creating && <PlanDialog defaultClient={clientId} onClose={() => setCreating(false)} />}
      {editingPlan && <PlanDialog plan={editingPlan} onClose={() => setEditingPlan(null)} />}
      <ConfirmDialog
        open={deletingPlan !== null}
        onClose={() => setDeletingPlan(null)}
        tone="danger"
        title="Delete this media plan?"
        description={deletingPlan ? `“${deletingPlan.name}” and its channel lines are removed.` : undefined}
        confirmLabel="Delete plan"
        onConfirm={async () => {
          if (!deletingPlan) return;
          await api.delete(`/agency/ads/media-plans/${deletingPlan.id}`);
          toast.success('Plan deleted');
          refreshPlans();
        }}
      />
    </>
  );
}

function PlanActualsDialog({ plan, onClose }: { plan: MediaPlan; onClose: () => void }) {
  const query = useQuery({ queryKey: adsKeys.planActuals(plan.id), queryFn: () => api.get<PlanActuals>(`/agency/ads/media-plans/${plan.id}/actuals`) });
  const d = query.data;
  return (
    <Dialog open onClose={onClose} size="lg" title={`${plan.name}: actual vs plan`} description={d?.note}>
      {query.isError && <ErrorState error={query.error} />}
      {d && (
        <div className="stack">
          <div className="cluster">
            <span>
              Planned {money(d.plannedTotal, plan.currency)} · Actual {money(d.actualTotal, plan.currency)}
            </span>
            <SourceBadge label={d.sourceLabel} />
          </div>
          {d.fxMissing.length > 0 && <Alert tone="warning">Missing exchange rates: {d.fxMissing.join(', ')}</Alert>}
          <DataTable
            caption="Plan lines"
            columns={[
              { id: 'channel', header: 'Channel', primary: true, cell: (l: PlanActuals['lines'][number]) => `${PLATFORM_LABELS[l.line.platform]} · ${l.line.channel}` },
              { id: 'planned', header: 'Planned', align: 'right', cell: (l: PlanActuals['lines'][number]) => money(l.line.plannedBudget, plan.currency) },
              { id: 'actual', header: 'Actual', align: 'right', cell: (l: PlanActuals['lines'][number]) => money(l.actualSpend, plan.currency) },
              { id: 'pct', header: 'Of plan', align: 'right', cell: (l: PlanActuals['lines'][number]) => ratio(l.spendVsPlan, 0) },
              {
                id: 'kpi',
                header: 'KPI',
                cell: (l: PlanActuals['lines'][number]) => (
                  <span>
                    {l.line.kpiName}: {l.kpiActual == null ? '—' : l.kpiActual.toLocaleString(undefined, { maximumFractionDigits: 4 })}
                    {l.line.kpiTarget != null && ` (target ${l.line.kpiTarget})`}{' '}
                    {l.kpiMet != null && <Badge tone={l.kpiMet ? 'success' : 'danger'}>{l.kpiMet ? 'Met' : 'Missed'}</Badge>}
                  </span>
                ),
              },
            ]}
            rows={d.lines}
            getRowId={(l) => l.line.id ?? l.line.channel}
          />
        </div>
      )}
    </Dialog>
  );
}

/** Full PUT body of a plan (the API replaces the plan and its lines). */
function planBody(plan: MediaPlan) {
  return {
    clientAccountId: plan.clientAccountId,
    name: plan.name,
    month: plan.month,
    currency: plan.currency,
    status: plan.status,
    notes: plan.notes,
    lines: plan.lines.map(({ id: _id, ...l }) => l),
    concurrencyStamp: plan.concurrencyStamp,
  };
}

function PlanDialog({ defaultClient, plan, onClose }: { defaultClient?: string; plan?: MediaPlan; onClose: () => void }) {
  const clients = useAdsClients();
  const queryClient = useQueryClient();
  const now = new Date();
  const month = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
  const [clientId, setClientId] = useState(plan?.clientAccountId ?? defaultClient ?? '');
  const [name, setName] = useState(plan?.name ?? '');
  const [planMonth, setPlanMonth] = useState(plan ? plan.month.slice(0, 7) : month);
  const [currency, setCurrency] = useState(plan?.currency ?? clients.data?.find((c) => c.id === defaultClient)?.currency ?? 'USD');
  const [notes, setNotes] = useState(plan?.notes ?? '');
  const monthEnd = (m: string) => {
    const [y, mo] = m.split('-').map(Number);
    return new Date(Date.UTC(y!, mo!, 0)).toISOString().slice(0, 10);
  };
  const [lines, setLines] = useState<MediaPlanLine[]>(
    plan?.lines.map(({ id: _id, ...l }) => l) ?? [
      { platform: 'GoogleAds', channel: 'Search', objective: 'Conversions', plannedBudget: 0, flightStart: `${month}-01`, flightEnd: monthEnd(month), kpiName: 'CPA', kpiTarget: null },
    ],
  );
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => {
      const body = { clientAccountId: clientId, name, month: `${planMonth}-01`, currency, lines, notes: notes || null, status: plan?.status ?? 'Draft', concurrencyStamp: plan?.concurrencyStamp };
      return plan ? api.put<MediaPlan>(`/agency/ads/media-plans/${plan.id}`, body) : api.post<MediaPlan>('/agency/ads/media-plans', body);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['ads', 'plans'] });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  const setLine = (i: number, patch: Partial<MediaPlanLine>) => setLines((all) => all.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title={plan ? 'Edit media plan' : 'New media plan'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={!clientId || !name} loading={save.isPending} onClick={() => save.mutate()}>
            Save plan
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <div className="ad-stats">
          <FormField label="Client" required>
            <Select
              value={clientId}
              disabled={!!plan}
              placeholder="Choose a client"
              onChange={(e) => {
                setClientId(e.target.value);
                const c = clients.data?.find((x) => x.id === e.target.value);
                if (c) setCurrency(c.currency);
              }}
              options={(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
            />
          </FormField>
          <FormField label="Name" required>
            <Input value={name} onChange={(e) => setName(e.target.value)} />
          </FormField>
          <FormField label="Month">
            <Input type="month" value={planMonth} onChange={(e) => setPlanMonth(e.target.value)} />
          </FormField>
          <FormField label="Currency">
            <Input value={currency} maxLength={3} onChange={(e) => setCurrency(e.target.value.toUpperCase())} />
          </FormField>
        </div>
        <FormField label="Notes" optional>
          <Textarea rows={2} maxLength={4000} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </FormField>
        {lines.map((l, i) => (
          <fieldset key={i} className="ad-mapping" style={{ border: '1px solid var(--color-border)', borderRadius: 8, padding: 12 }}>
            <legend>Channel {i + 1}</legend>
            <FormField label="Platform">
              <Select value={l.platform} onChange={(e) => setLine(i, { platform: e.target.value as AdPlatform })} options={platformOptions} />
            </FormField>
            <FormField label="Channel">
              <Input value={l.channel} onChange={(e) => setLine(i, { channel: e.target.value })} />
            </FormField>
            <FormField label="Budget">
              <Input type="number" value={l.plannedBudget} onChange={(e) => setLine(i, { plannedBudget: Number(e.target.value) })} />
            </FormField>
            <FormField label="Flight start">
              <Input type="date" value={l.flightStart} onChange={(e) => setLine(i, { flightStart: e.target.value })} />
            </FormField>
            <FormField label="Flight end">
              <Input type="date" value={l.flightEnd} onChange={(e) => setLine(i, { flightEnd: e.target.value })} />
            </FormField>
            <FormField label="KPI">
              <Select
                value={l.kpiName}
                onChange={(e) => setLine(i, { kpiName: e.target.value })}
                options={['CPA', 'ROAS', 'CPC', 'CPM', 'CTR', 'Conversions', 'Clicks', 'Impressions'].map((k) => ({ value: k, label: k }))}
              />
            </FormField>
            <FormField label="KPI target" optional>
              <Input type="number" value={l.kpiTarget ?? ''} onChange={(e) => setLine(i, { kpiTarget: e.target.value ? Number(e.target.value) : null })} />
            </FormField>
            <IconButton label={`Remove channel ${i + 1}`} icon={<Trash2 />} onClick={() => setLines((all) => all.filter((_, j) => j !== i))} />
          </fieldset>
        ))}
        <div>
          <Button
            variant="secondary"
            size="sm"
            leadingIcon={<Plus />}
            onClick={() =>
              setLines((all) => [
                ...all,
                { platform: 'MetaAds', channel: 'Feed', objective: null, plannedBudget: 0, flightStart: `${planMonth}-01`, flightEnd: monthEnd(planMonth), kpiName: 'ROAS', kpiTarget: null },
              ])
            }
          >
            Add channel
          </Button>
        </div>
      </div>
    </Dialog>
  );
}

// ---------------------------------------------------------------- creatives

const CREATIVE_TONES: Record<Creative['status'], Tone> = { Draft: 'neutral', InternalReview: 'info', ClientApproval: 'warning', Approved: 'success' };

export function CreativesPage() {
  const [clientId, setClientId] = useClientParam();
  const [editing, setEditing] = useState<Creative | 'new' | null>(null);
  const [acting, setActing] = useState<{ creative: Creative; step: string } | null>(null);
  const [deletingCreative, setDeletingCreative] = useState<Creative | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const duplicateCreative = useMutation({
    mutationFn: (c: Creative) => api.post<Creative>(`/agency/ads/creatives/${c.id}/duplicate`),
    onSuccess: (c) => {
      toast.success('Creative duplicated', c.name);
      void queryClient.invalidateQueries({ queryKey: ['ads', 'creatives'] });
    },
    onError: (e) => toast.error('Not duplicated', errorMessage(e)),
  });
  const creatives = useQuery({
    queryKey: adsKeys.creatives(clientId),
    queryFn: () => api.get<Creative[]>('/agency/ads/creatives', { query: { clientId } }),
  });
  const columns: DataTableColumn<Creative>[] = [
    {
      id: 'name',
      header: 'Creative',
      primary: true,
      cell: (c) => (
        <span className="stack" style={{ gap: 2 }}>
          <strong>{c.name}</strong>
          <span className="ad-muted">
            {PLATFORM_LABELS[c.platform]} · {c.format} · {c.headlines[0] ?? 'no headline'}
          </span>
        </span>
      ),
    },
    { id: 'status', header: 'Approval', cell: (c) => <Badge tone={CREATIVE_TONES[c.status]}>{c.status}</Badge> },
    {
      id: 'issues',
      header: 'Copy checks',
      cell: (c) => {
        const errors = c.issues.filter((i) => i.severity === 'Error').length;
        const warnings = c.issues.length - errors;
        return errors > 0 ? <span className="ad-error">{errors} error(s)</span> : warnings > 0 ? <span className="ad-warning">{warnings} warning(s)</span> : 'OK';
      },
    },
  ];
  const menu = (c: Creative): MenuEntry[] => [
    { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditing(c) },
    ...c.allowedActions.map((a) => ({ id: a, label: a.replace(/-/g, ' ').replace(/^./, (x) => x.toUpperCase()), onSelect: () => setActing({ creative: c, step: a }) })),
    { id: 'duplicate', label: 'Duplicate', icon: <CopyPlus />, onSelect: () => duplicateCreative.mutate(c) },
    { id: 'delete', label: 'Delete', icon: <Trash2 />, danger: true, onSelect: () => setDeletingCreative(c) },
  ];
  return (
    <>
      <PageHeader
        title="Creative & copy library"
        description="Headlines, descriptions and linked creatives, checked against each platform's limits and approved like social posts."
        actions={
          clientId && (
            <Button leadingIcon={<Plus />} onClick={() => setEditing('new')}>
              New creative
            </Button>
          )
        }
      />
      <div className="ad-toolbar">
        <AdsClientPicker value={clientId} onChange={setClientId} allowAll />
      </div>
      {creatives.isError ? (
        <ErrorState error={creatives.error} />
      ) : (
        <DataTable
          caption="Creatives"
          columns={columns}
          rows={creatives.data ?? []}
          getRowId={(c) => c.id}
          rowLabel={(c) => c.name}
          rowActions={menu}
          loading={creatives.isLoading}
          emptyState={<EmptyState icon={<Megaphone />} headingLevel={2} title="No creatives" />}
        />
      )}
      {editing && (clientId || editing !== 'new') && (
        <CreativeDialog creative={editing === 'new' ? null : editing} clientId={editing === 'new' ? clientId! : editing.clientAccountId} onClose={() => setEditing(null)} />
      )}
      {acting && <CreativeActionDialog creative={acting.creative} step={acting.step} onClose={() => setActing(null)} />}
      <ConfirmDialog
        open={deletingCreative !== null}
        onClose={() => setDeletingCreative(null)}
        tone="danger"
        title="Delete this creative?"
        description="Creatives used by ads cannot be deleted — unlink them from those ads first."
        confirmLabel="Delete creative"
        onConfirm={async () => {
          if (!deletingCreative) return;
          await api.delete(`/agency/ads/creatives/${deletingCreative.id}`);
          toast.success('Creative deleted');
          void queryClient.invalidateQueries({ queryKey: ['ads', 'creatives'] });
        }}
      />
    </>
  );
}

function CreativeActionDialog({ creative, step, onClose }: { creative: Creative; step: string; onClose: () => void }) {
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const needsNote = step === 'request-changes' || step === 'record-client-approval';
  const act = useMutation({
    mutationFn: () => api.post<Creative>(`/agency/ads/creatives/${creative.id}/${step}`, { note: note || null, concurrencyStamp: creative.concurrencyStamp }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['ads', 'creatives'] });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title={step === 'record-client-approval' ? 'Record client approval' : step.replace(/-/g, ' ').replace(/^./, (x) => x.toUpperCase())}
      description={step === 'record-client-approval' ? 'Note how and when the client approved (e.g. “Approved by email, 12 Sep”). This is audited.' : undefined}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={needsNote && !note.trim()} loading={act.isPending} onClick={() => act.mutate()}>
            Confirm
          </Button>
        </>
      }
    >
      {error && <Alert tone="danger">{error}</Alert>}
      <FormField label="Note" optional={!needsNote} required={needsNote}>
        <Textarea rows={3} value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
    </Dialog>
  );
}

function limitFor(limits: CopyLimit[] | undefined, field: string) {
  return limits?.find((l) => l.field === field);
}

function CreativeDialog({ creative, clientId, onClose }: { creative: Creative | null; clientId: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const limitsQuery = useQuery({ queryKey: ['ads', 'copy-limits'], queryFn: () => api.get<Record<AdPlatform, CopyLimit[]>>('/agency/ads/copy-limits') });
  const [name, setName] = useState(creative?.name ?? '');
  const [platform, setPlatform] = useState<AdPlatform>(creative?.platform ?? 'GoogleAds');
  const [format, setFormat] = useState(creative?.format ?? 'ResponsiveSearch');
  const [headlines, setHeadlines] = useState<string[]>(creative?.headlines ?? ['', '', '']);
  const [descriptions, setDescriptions] = useState<string[]>(creative?.descriptions ?? ['', '']);
  const [primaryText, setPrimaryText] = useState(creative?.primaryText ?? '');
  const [finalUrl, setFinalUrl] = useState(creative?.finalUrl ?? '');
  const [error, setError] = useState<string | null>(null);
  const limits = limitsQuery.data?.[platform];
  const save = useMutation({
    mutationFn: () => {
      const body = {
        clientAccountId: clientId,
        name,
        platform,
        format,
        headlines: headlines.filter((h) => h.trim()),
        descriptions: descriptions.filter((d) => d.trim()),
        primaryText: primaryText || null,
        finalUrl: finalUrl || null,
        concurrencyStamp: creative?.concurrencyStamp,
      };
      return creative ? api.put<Creative>(`/agency/ads/creatives/${creative.id}`, body) : api.post<Creative>('/agency/ads/creatives', body);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['ads', 'creatives'] });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  const listEditor = (label: string, field: string, values: string[], set: (v: string[]) => void) => {
    const limit = limitFor(limits, field);
    return (
      <fieldset className="stack" style={{ border: 0, padding: 0 }}>
        <legend className="ad-h2">
          {label}
          {limit ? ` (${limit.hard ? 'max' : 'recommended'} ${limit.max} characters, ${limit.minCount}–${limit.maxCount})` : ''}
        </legend>
        {values.map((v, i) => {
          const over = limit ? v.length > limit.max : false;
          return (
            <FormField
              key={i}
              label={`${label.replace(/s$/, '')} ${i + 1}`}
              labelAside={
                limit && (
                  <span className={over ? (limit.hard ? 'ad-error' : 'ad-warning') : 'ad-muted'}>
                    {v.length}/{limit.max}
                  </span>
                )
              }
            >
              <Input value={v} invalid={over && limit?.hard} onChange={(e) => set(values.map((x, j) => (j === i ? e.target.value : x)))} />
            </FormField>
          );
        })}
        <div>
          <Button size="sm" variant="secondary" onClick={() => set([...values, ''])}>
            Add {label.toLowerCase().replace(/s$/, '')}
          </Button>
        </div>
      </fieldset>
    );
  };
  const primaryLimit = limitFor(limits, 'primaryText');
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title={creative ? 'Edit creative' : 'New creative'}
      description={creative && creative.status !== 'Draft' ? 'Saving changes sends it back to Draft for approval.' : undefined}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={!name} loading={save.isPending} onClick={() => save.mutate()}>
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <div className="ad-stats">
          <FormField label="Name" required>
            <Input value={name} onChange={(e) => setName(e.target.value)} />
          </FormField>
          <FormField label="Platform">
            <Select value={platform} onChange={(e) => setPlatform(e.target.value as AdPlatform)} options={platformOptions} />
          </FormField>
          <FormField label="Format">
            <Select
              value={format}
              onChange={(e) => setFormat(e.target.value)}
              options={['ResponsiveSearch', 'SingleImage', 'Video', 'Carousel', 'Text'].map((f) => ({ value: f, label: f }))}
            />
          </FormField>
        </div>
        {listEditor('Headlines', 'headline', headlines, setHeadlines)}
        {listEditor('Descriptions', 'description', descriptions, setDescriptions)}
        {primaryLimit && (
          <FormField label="Primary text" labelAside={<span className={primaryText.length > primaryLimit.max ? 'ad-warning' : 'ad-muted'}>{primaryText.length}/{primaryLimit.max}</span>}>
            <Textarea rows={3} value={primaryText} onChange={(e) => setPrimaryText(e.target.value)} />
          </FormField>
        )}
        <FormField label="Final URL" optional>
          <Input type="url" value={finalUrl} onChange={(e) => setFinalUrl(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

// ---------------------------------------------------------------- experiments

export function AdExperimentsPage() {
  const [clientId, setClientId] = useClientParam();
  const [creating, setCreating] = useState(false);
  const [editingExperiment, setEditingExperiment] = useState<Experiment | null>(null);
  const [deletingExperiment, setDeletingExperiment] = useState<Experiment | null>(null);
  const queryClient = useQueryClient();
  const toast = useToast();
  const experiments = useQuery({
    queryKey: adsKeys.experiments(clientId),
    queryFn: () => api.get<Experiment[]>('/agency/ads/experiments', { query: { clientId } }),
  });
  return (
    <>
      <PageHeader
        title="Experiments log"
        description="A/B tests run on the ad platforms: hypothesis, variants, result and significance (computed with a two-proportion z-test, or entered from the platform's report)."
        actions={
          clientId && (
            <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
              Log experiment
            </Button>
          )
        }
      />
      <div className="ad-toolbar">
        <AdsClientPicker value={clientId} onChange={setClientId} allowAll />
      </div>
      {experiments.isError ? (
        <ErrorState error={experiments.error} />
      ) : (experiments.data ?? []).length === 0 ? (
        <EmptyState icon={<FlaskConical />} title="No experiments logged" />
      ) : (
        <div className="stack">
          {experiments.data!.map((e) => (
            <Card key={e.id} as="article" aria-labelledby={`exp-${e.id}`}>
              <CardHeader
                title={e.name}
                titleId={`exp-${e.id}`}
                headingLevel={2}
                description={`${PLATFORM_LABELS[e.platform]} · ${e.status}${e.winnerVariant ? ` · winner: ${e.winnerVariant}` : ''}`}
                actions={
                  <div className="cluster">
                    <Button size="sm" variant="secondary" leadingIcon={<Pencil />} onClick={() => setEditingExperiment(e)} aria-label={`Edit ${e.name}`}>
                      {e.status === 'Running' ? 'Update / conclude' : 'Edit'}
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      leadingIcon={<Trash2 />}
                      disabled={e.status === 'Running'}
                      title={e.status === 'Running' ? 'Conclude the running experiment before deleting it.' : undefined}
                      onClick={() => setDeletingExperiment(e)}
                      aria-label={`Delete ${e.name}`}
                    >
                      Delete
                    </Button>
                  </div>
                }
              />
              <CardBody className="stack">
                <p>
                  <strong>Hypothesis:</strong> {e.hypothesis}
                </p>
                <DataTable
                  caption={`${e.name} variants`}
                  columns={[
                    { id: 'name', header: 'Variant', primary: true, cell: (v: Experiment['variants'][number]) => `${v.name}${v.isControl ? ' (control)' : ''}` },
                    { id: 'trials', header: e.metric === 'Ctr' ? 'Impressions' : 'Clicks', align: 'right', cell: (v: Experiment['variants'][number]) => count(e.metric === 'Ctr' ? v.impressions : v.clicks) },
                    { id: 'succ', header: e.metric === 'Ctr' ? 'Clicks' : 'Conversions', align: 'right', cell: (v: Experiment['variants'][number]) => count(e.metric === 'Ctr' ? v.clicks : v.conversions) },
                    { id: 'rate', header: e.metric === 'Ctr' ? 'CTR' : 'Conv. rate', align: 'right', cell: (v: Experiment['variants'][number]) => ratio(v.rate) },
                    { id: 'p', header: 'p-value', align: 'right', cell: (v: Experiment['variants'][number]) => (v.pValue == null ? '—' : v.pValue.toFixed(4)) },
                    {
                      id: 'sig',
                      header: 'Significant',
                      cell: (v: Experiment['variants'][number]) =>
                        v.significant == null ? '—' : <Badge tone={v.significant ? 'success' : 'neutral'}>{v.significant ? 'Yes' : 'Not yet'}</Badge>,
                    },
                  ]}
                  rows={e.variants}
                  getRowId={(v) => v.id}
                />
                {e.enteredPValue != null && <p className="ad-muted">Platform-reported p-value: {e.enteredPValue}</p>}
                {e.result && <p>{e.result}</p>}
                <p className="ad-muted">
                  {e.significanceSource}. {e.method}
                </p>
              </CardBody>
            </Card>
          ))}
        </div>
      )}
      {creating && clientId && <ExperimentDialog clientId={clientId} onClose={() => setCreating(false)} />}
      {editingExperiment && <ExperimentDialog clientId={editingExperiment.clientAccountId} experiment={editingExperiment} onClose={() => setEditingExperiment(null)} />}
      <ConfirmDialog
        open={deletingExperiment !== null}
        onClose={() => setDeletingExperiment(null)}
        tone="danger"
        title="Delete this experiment?"
        description={deletingExperiment ? `“${deletingExperiment.name}” and its variant results are removed from the log.` : undefined}
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deletingExperiment) return;
          await api.delete(`/agency/ads/experiments/${deletingExperiment.id}`);
          toast.success('Experiment deleted');
          void queryClient.invalidateQueries({ queryKey: ['ads', 'experiments'] });
        }}
      />
    </>
  );
}

function ExperimentDialog({ clientId, experiment, onClose }: { clientId: string; experiment?: Experiment; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(experiment?.name ?? '');
  const [hypothesis, setHypothesis] = useState(experiment?.hypothesis ?? '');
  const [platform, setPlatform] = useState<AdPlatform>(experiment?.platform ?? 'GoogleAds');
  const [metric, setMetric] = useState<string>(experiment?.metric ?? 'ConversionRate');
  const [status, setStatus] = useState<Experiment['status']>(experiment?.status ?? 'Running');
  const [result, setResult] = useState(experiment?.result ?? '');
  const [winner, setWinner] = useState(experiment?.winnerVariant ?? '');
  const [variants, setVariants] = useState(
    experiment?.variants.map((v) => ({ name: v.name, isControl: v.isControl, impressions: v.impressions, clicks: v.clicks, conversions: v.conversions, spend: v.spend })) ?? [
      { name: 'A — control', isControl: true, impressions: 0, clicks: 0, conversions: 0, spend: 0 },
      { name: 'B', isControl: false, impressions: 0, clicks: 0, conversions: 0, spend: 0 },
    ],
  );
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => {
      const body = {
        clientAccountId: clientId, name, hypothesis, platform, metric, variants, status, result: result || null, winnerVariant: winner || null,
        adAccountId: experiment?.adAccountId ?? null, campaignId: experiment?.campaignId ?? null, startDate: experiment?.startDate ?? null,
        endDate: experiment?.endDate ?? null, enteredPValue: experiment?.enteredPValue ?? null, concurrencyStamp: experiment?.concurrencyStamp,
      };
      return experiment ? api.put<Experiment>(`/agency/ads/experiments/${experiment.id}`, body) : api.post<Experiment>('/agency/ads/experiments', body);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['ads', 'experiments'] });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title={experiment ? 'Update experiment' : 'Log an experiment'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={!name || !hypothesis} loading={save.isPending} onClick={() => save.mutate()}>
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Name" required>
          <Input value={name} onChange={(e) => setName(e.target.value)} />
        </FormField>
        <FormField label="Hypothesis" required>
          <Textarea rows={2} value={hypothesis} onChange={(e) => setHypothesis(e.target.value)} />
        </FormField>
        <div className="ad-stats">
          <FormField label="Platform">
            <Select value={platform} onChange={(e) => setPlatform(e.target.value as AdPlatform)} options={platformOptions} />
          </FormField>
          <FormField label="Metric">
            <Select value={metric} onChange={(e) => setMetric(e.target.value)} options={[{ value: 'ConversionRate', label: 'Conversion rate' }, { value: 'Ctr', label: 'Click-through rate' }]} />
          </FormField>
          <FormField label="Status" hint="Mark Concluded when the test is done.">
            <Select
              value={status}
              onChange={(e) => setStatus(e.target.value as Experiment['status'])}
              options={(['Planned', 'Running', 'Concluded'] as const).map((s) => ({ value: s, label: s }))}
            />
          </FormField>
          {status === 'Concluded' && (
            <FormField label="Winner" optional>
              <Select value={winner} placeholder="No clear winner" onChange={(e) => setWinner(e.target.value)} options={variants.map((v) => ({ value: v.name, label: v.name }))} />
            </FormField>
          )}
        </div>
        {status === 'Concluded' && (
          <FormField label="Result / learnings" optional>
            <Textarea rows={2} maxLength={2000} value={result} onChange={(e) => setResult(e.target.value)} />
          </FormField>
        )}
        {variants.map((v, i) => (
          <fieldset key={i} className="ad-mapping" style={{ border: 0, padding: 0 }}>
            <legend>{v.name}</legend>
            {(['impressions', 'clicks', 'conversions'] as const).map((f) => (
              <FormField key={f} label={`${v.name} ${f}`}>
                <Input type="number" min={0} value={v[f]} onChange={(e) => setVariants((all) => all.map((x, j) => (j === i ? { ...x, [f]: Number(e.target.value) } : x)))} />
              </FormField>
            ))}
          </fieldset>
        ))}
      </div>
    </Dialog>
  );
}

// ---------------------------------------------------------------- UTM builder & naming

export function UtmPage() {
  const [clientId, setClientId] = useClientParam();
  const queryClient = useQueryClient();
  const toast = useToast();
  const settings = useQuery({
    queryKey: adsKeys.settings(clientId ?? ''),
    queryFn: () => api.get<AdsSettings>(`/agency/ads/clients/${clientId}/settings`),
    enabled: !!clientId,
  });
  const history = useQuery({
    queryKey: adsKeys.utm(clientId ?? ''),
    queryFn: () => api.get<UtmLink[]>(`/agency/ads/clients/${clientId}/utm`),
    enabled: !!clientId,
  });
  const [url, setUrl] = useState('');
  const [platform, setPlatform] = useState<AdPlatform>('GoogleAds');
  const [campaign, setCampaign] = useState('');
  const [source, setSource] = useState('');
  const [medium, setMedium] = useState('');
  const [content, setContent] = useState('');
  const [term, setTerm] = useState('');
  const [built, setBuilt] = useState<UtmLink | null>(null);
  const [buildError, setBuildError] = useState<string | null>(null);
  const build = useMutation({
    mutationFn: () =>
      api.post<UtmLink>(`/agency/ads/clients/${clientId}/utm`, {
        url,
        platform,
        campaign,
        source: source || null,
        medium: medium || null,
        content: content || null,
        term: term || null,
      }),
    onSuccess: (l) => {
      setBuilt(l);
      setBuildError(null);
      void queryClient.invalidateQueries({ queryKey: adsKeys.utm(clientId ?? '') });
    },
    onError: (e) => setBuildError(errorMessage(e)),
  });

  const [template, setTemplate] = useState<string | null>(null);
  const [lower, setLower] = useState<boolean | null>(null);
  const [defaultSource, setDefaultSource] = useState<string | null>(null);
  const [defaultMedium, setDefaultMedium] = useState<string | null>(null);
  const [deletingLink, setDeletingLink] = useState<UtmLink | null>(null);
  const saveSettings = useMutation({
    mutationFn: () =>
      api.put<AdsSettings>(`/agency/ads/clients/${clientId}/settings`, {
        campaignNamingTemplate: template ?? settings.data?.campaignNamingTemplate ?? null,
        defaultUtmSource: defaultSource ?? settings.data?.defaultUtmSource ?? '{platform}',
        defaultUtmMedium: defaultMedium ?? settings.data?.defaultUtmMedium ?? 'cpc',
        lowercaseUtm: lower ?? settings.data?.lowercaseUtm ?? true,
        concurrencyStamp: settings.data?.concurrencyStamp,
      }),
    onSuccess: () => {
      toast.success('Naming & UTM defaults saved');
      void queryClient.invalidateQueries({ queryKey: adsKeys.settings(clientId ?? '') });
    },
    onError: (e) => toast.error('Not saved', errorMessage(e)),
  });
  const [checkName, setCheckName] = useState('');
  const [checkResult, setCheckResult] = useState<{ compliant: boolean; suggested: string | null; problems: string[]; templateConfigured: boolean } | null>(null);
  const check = useMutation({
    mutationFn: () => api.post<typeof checkResult>(`/agency/ads/clients/${clientId}/naming/check`, { platform, name: checkName }),
    onSuccess: setCheckResult,
  });

  return (
    <>
      <PageHeader title="UTM builder & naming" description="Tag landing-page URLs consistently and keep campaign names on the client's template." />
      <div className="ad-toolbar">
        <AdsClientPicker value={clientId} onChange={setClientId} />
      </div>
      {!clientId ? (
        <EmptyState icon={<Link2 />} title="Choose a client" />
      ) : (
        <div className="stack">
          <Card as="section" aria-labelledby="ad-utm">
            <CardHeader title="UTM builder" titleId="ad-utm" description="Existing utm_* parameters are replaced; other parameters and the #fragment are kept." />
            <CardBody className="stack">
              {buildError && <Alert tone="danger">{buildError}</Alert>}
              <FormField label="Landing page URL" required>
                <Input type="url" value={url} onChange={(e) => setUrl(e.target.value)} />
              </FormField>
              <div className="ad-stats">
                <FormField label="Platform">
                  <Select value={platform} onChange={(e) => setPlatform(e.target.value as AdPlatform)} options={platformOptions} />
                </FormField>
                <FormField label="utm_campaign" required>
                  <Input value={campaign} onChange={(e) => setCampaign(e.target.value)} />
                </FormField>
                <FormField label="utm_source" optional hint={`Default: ${settings.data?.defaultUtmSource ?? '{platform}'}`}>
                  <Input value={source} onChange={(e) => setSource(e.target.value)} />
                </FormField>
                <FormField label="utm_medium" optional hint={`Default: ${settings.data?.defaultUtmMedium ?? 'cpc'}`}>
                  <Input value={medium} onChange={(e) => setMedium(e.target.value)} />
                </FormField>
                <FormField label="utm_content" optional>
                  <Input value={content} onChange={(e) => setContent(e.target.value)} />
                </FormField>
                <FormField label="utm_term" optional>
                  <Input value={term} onChange={(e) => setTerm(e.target.value)} />
                </FormField>
              </div>
              <div>
                <Button disabled={!url || !campaign} loading={build.isPending} onClick={() => build.mutate()}>
                  Build tagged URL
                </Button>
              </div>
              {built && <CopyField label="Tagged URL" value={built.taggedUrl} />}
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="ad-naming">
            <CardHeader title="Campaign naming convention" titleId="ad-naming" description={`Tokens: ${(settings.data?.tokens ?? []).map((t) => `{${t}}`).join(' ')}`} />
            <CardBody className="stack">
              <FormField label="Template" hint="Example: {client}_{platform}_{objective}_{yyyymm}_{name}. Planned campaigns must match it.">
                <Input value={template ?? settings.data?.campaignNamingTemplate ?? ''} onChange={(e) => setTemplate(e.target.value)} />
              </FormField>
              <div className="ad-stats">
                <FormField label="Default utm_source" hint="{platform} becomes google, meta, tiktok…">
                  <Input value={defaultSource ?? settings.data?.defaultUtmSource ?? ''} maxLength={100} onChange={(e) => setDefaultSource(e.target.value)} />
                </FormField>
                <FormField label="Default utm_medium">
                  <Input value={defaultMedium ?? settings.data?.defaultUtmMedium ?? ''} maxLength={100} onChange={(e) => setDefaultMedium(e.target.value)} />
                </FormField>
              </div>
              <Switch
                checked={lower ?? settings.data?.lowercaseUtm ?? true}
                onCheckedChange={setLower}
                label="Lower-case and hyphenate UTM values"
              />
              <div>
                <Button variant="secondary" loading={saveSettings.isPending} onClick={() => saveSettings.mutate()}>
                  Save naming & UTM defaults
                </Button>
              </div>
              <div className="cluster">
                <FormField label="Check a campaign name">
                  <Input value={checkName} onChange={(e) => setCheckName(e.target.value)} />
                </FormField>
                <Button variant="secondary" disabled={!checkName} onClick={() => check.mutate()}>
                  Check
                </Button>
              </div>
              {checkResult && (
                <Alert tone={checkResult.compliant ? 'success' : 'warning'} title={checkResult.templateConfigured ? (checkResult.compliant ? 'Follows the template' : 'Does not follow the template') : 'No template set'}>
                  {checkResult.problems.join(' ')}
                  {checkResult.suggested && (
                    <>
                      {' '}
                      Example: <code>{checkResult.suggested}</code>
                    </>
                  )}
                </Alert>
              )}
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="ad-utm-history">
            <CardHeader title="Recent tagged links" titleId="ad-utm-history" />
            <CardBody>
              <DataTable
                caption="Recent tagged links"
                columns={[
                  { id: 'campaign', header: 'Campaign', primary: true, cell: (l: UtmLink) => l.campaign },
                  { id: 'source', header: 'Source / medium', cell: (l: UtmLink) => `${l.source} / ${l.medium}` },
                  { id: 'url', header: 'Tagged URL', cell: (l: UtmLink) => <p className="ad-code">{l.taggedUrl}</p> },
                ]}
                rows={history.data ?? []}
                getRowId={(l) => l.id ?? l.taggedUrl}
                rowLabel={(l) => l.campaign}
                rowActions={(l) => (l.id ? [{ id: 'delete', label: 'Delete from history', icon: <Trash2 />, danger: true, onSelect: () => setDeletingLink(l) }] : [])}
                emptyState={<EmptyState compact icon={<Link2 />} headingLevel={3} title="No links yet" />}
              />
            </CardBody>
          </Card>
        </div>
      )}
      <ConfirmDialog
        open={deletingLink !== null}
        onClose={() => setDeletingLink(null)}
        tone="danger"
        title="Delete this tagged link from the history?"
        description="Links already in use keep working; only the saved record is removed."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deletingLink?.id) return;
          await api.delete(`/agency/ads/utm/${deletingLink.id}`);
          void queryClient.invalidateQueries({ queryKey: adsKeys.utm(clientId ?? '') });
        }}
      />
    </>
  );
}
