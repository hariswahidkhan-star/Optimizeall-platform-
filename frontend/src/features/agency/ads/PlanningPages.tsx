import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ClipboardList, FlaskConical, Link2, Megaphone, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
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
  const [viewing, setViewing] = useState<MediaPlan | null>(null);
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
          loading={plans.isLoading}
          emptyState={<EmptyState icon={<ClipboardList />} headingLevel={2} title="No media plans" />}
        />
      )}
      {viewing && <PlanActualsDialog plan={viewing} onClose={() => setViewing(null)} />}
      {creating && <PlanDialog defaultClient={clientId} onClose={() => setCreating(false)} />}
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

function PlanDialog({ defaultClient, onClose }: { defaultClient?: string; onClose: () => void }) {
  const clients = useAdsClients();
  const queryClient = useQueryClient();
  const now = new Date();
  const month = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
  const [clientId, setClientId] = useState(defaultClient ?? '');
  const [name, setName] = useState('');
  const [planMonth, setPlanMonth] = useState(month);
  const [currency, setCurrency] = useState(clients.data?.find((c) => c.id === defaultClient)?.currency ?? 'USD');
  const monthEnd = (m: string) => {
    const [y, mo] = m.split('-').map(Number);
    return new Date(Date.UTC(y!, mo!, 0)).toISOString().slice(0, 10);
  };
  const [lines, setLines] = useState<MediaPlanLine[]>([
    { platform: 'GoogleAds', channel: 'Search', objective: 'Conversions', plannedBudget: 0, flightStart: `${month}-01`, flightEnd: monthEnd(month), kpiName: 'CPA', kpiTarget: null },
  ]);
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => api.post<MediaPlan>('/agency/ads/media-plans', { clientAccountId: clientId, name, month: `${planMonth}-01`, currency, lines }),
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
      title="New media plan"
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
    { id: 'edit', label: 'Edit', onSelect: () => setEditing(c) },
    ...c.allowedActions.map((a) => ({ id: a, label: a.replace(/-/g, ' ').replace(/^./, (x) => x.toUpperCase()), onSelect: () => setActing({ creative: c, step: a }) })),
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
    </>
  );
}

function ExperimentDialog({ clientId, onClose }: { clientId: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [hypothesis, setHypothesis] = useState('');
  const [platform, setPlatform] = useState<AdPlatform>('GoogleAds');
  const [metric, setMetric] = useState('ConversionRate');
  const [variants, setVariants] = useState([
    { name: 'A — control', isControl: true, impressions: 0, clicks: 0, conversions: 0 },
    { name: 'B', isControl: false, impressions: 0, clicks: 0, conversions: 0 },
  ]);
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => api.post<Experiment>('/agency/ads/experiments', { clientAccountId: clientId, name, hypothesis, platform, metric, variants, status: 'Running' }),
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
      title="Log an experiment"
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
        </div>
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
  const saveSettings = useMutation({
    mutationFn: () =>
      api.put<AdsSettings>(`/agency/ads/clients/${clientId}/settings`, {
        campaignNamingTemplate: template ?? settings.data?.campaignNamingTemplate ?? null,
        defaultUtmSource: settings.data?.defaultUtmSource ?? '{platform}',
        defaultUtmMedium: settings.data?.defaultUtmMedium ?? 'cpc',
        lowercaseUtm: lower ?? settings.data?.lowercaseUtm ?? true,
        concurrencyStamp: settings.data?.concurrencyStamp,
      }),
    onSuccess: () => {
      toast.success('Naming settings saved');
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
              <Switch
                checked={lower ?? settings.data?.lowercaseUtm ?? true}
                onCheckedChange={setLower}
                label="Lower-case and hyphenate UTM values"
              />
              <div>
                <Button variant="secondary" loading={saveSettings.isPending} onClick={() => saveSettings.mutate()}>
                  Save naming settings
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
                emptyState={<EmptyState compact icon={<Link2 />} headingLevel={3} title="No links yet" />}
              />
            </CardBody>
          </Card>
        </div>
      )}
    </>
  );
}
