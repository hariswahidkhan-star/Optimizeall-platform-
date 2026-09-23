import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BarChart3, CheckCircle2, FlaskConical, Pause, Pencil, Play, Plus, Trash2 } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Pagination,
  RadioGroup,
  Select,
  Textarea,
  useToast,
  type DataTableColumn,
  type MenuEntry,
  type Tone,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { humanize } from '@/lib/format/text';
import { useCampaignOptions } from '@/lib/api/campaignOptions';
import { qk, useCampaign } from '../api/queries';
import {
  EXPERIMENT_ELEMENTS,
  EXPERIMENT_STATUSES,
  type Experiment,
  type ExperimentElement,
  type ExperimentInput,
  type ExperimentStatus,
  type VariantInput,
} from '../api/types';
import { fieldError, fieldErrorsFrom, type FieldErrorMap } from '../shared/formErrors';
import '../campaigns.css';

export const STATUS_TONES: Record<ExperimentStatus, Tone> = {
  Draft: 'neutral',
  Running: 'success',
  Paused: 'warning',
  Completed: 'info',
};

const ELEMENT_HELP: Record<ExperimentElement, string> = {
  Title: 'Different campaign titles shown to participants.',
  CreativeAsset: 'A different campaign image per variant.',
  Instructions: 'Different posting instructions.',
  LandingPage: 'Different public landing headline and body.',
};

const KEYS = ['A', 'B', 'C', 'D'];

type Lifecycle = 'start' | 'pause' | 'resume' | 'complete' | 'delete';

export function ExperimentsPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const campaigns = useCampaignOptions();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Experiment | 'new' | null>(null);
  const [action, setAction] = useState<{ kind: Lifecycle; experiment: Experiment } | null>(null);
  const [winner, setWinner] = useState('');

  const params = { search, campaignId: filters.campaign, status: filters.status, page, pageSize: 25 };
  const query = useQuery({
    queryKey: qk.experiments(params),
    queryFn: () => api.get<PagedResult<Experiment>>('/marketing/experiments', { query: params }),
    placeholderData: keepPreviousData,
  });

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['manage'] });

  const runAction = async () => {
    if (!action) return;
    const { kind, experiment } = action;
    if (kind === 'delete') {
      await api.delete(`/marketing/experiments/${experiment.id}`);
      toast.success('Experiment deleted');
    } else {
      await api.post<Experiment>(
        `/marketing/experiments/${experiment.id}/${kind}`,
        kind === 'complete' ? { winningVariantId: winner || null } : undefined,
      );
      toast.success(
        `Experiment ${kind === 'start' ? 'started' : kind === 'complete' ? 'completed' : `${kind}d`}`,
      );
    }
    await refresh();
  };

  const menu = (e: Experiment): MenuEntry[] => [
    { id: 'results', label: 'View results', icon: <BarChart3 />, to: `/manage/experiments/${e.id}` },
    ...(e.status === 'Draft'
      ? [
          { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditing(e) } satisfies MenuEntry,
          {
            id: 'start',
            label: 'Start…',
            icon: <Play />,
            onSelect: () => setAction({ kind: 'start', experiment: e }),
          } satisfies MenuEntry,
          {
            id: 'delete',
            label: 'Delete…',
            icon: <Trash2 />,
            danger: true,
            onSelect: () => setAction({ kind: 'delete', experiment: e }),
          } satisfies MenuEntry,
        ]
      : []),
    ...(e.status === 'Running'
      ? [
          {
            id: 'pause',
            label: 'Pause…',
            icon: <Pause />,
            onSelect: () => setAction({ kind: 'pause', experiment: e }),
          } satisfies MenuEntry,
        ]
      : []),
    ...(e.status === 'Paused'
      ? [
          {
            id: 'resume',
            label: 'Resume…',
            icon: <Play />,
            onSelect: () => setAction({ kind: 'resume', experiment: e }),
          } satisfies MenuEntry,
        ]
      : []),
    ...(e.status === 'Running' || e.status === 'Paused'
      ? [
          {
            id: 'complete',
            label: 'Complete and choose winner…',
            icon: <CheckCircle2 />,
            onSelect: () => {
              setWinner('');
              setAction({ kind: 'complete', experiment: e });
            },
          } satisfies MenuEntry,
        ]
      : []),
  ];

  const columns: DataTableColumn<Experiment>[] = [
    {
      id: 'name',
      header: 'Experiment',
      primary: true,
      cell: (e) => (
        <span className="stack mg-stack-xs">
          <Link className="ui-link mg-strong" to={`/manage/experiments/${e.id}`}>
            {e.name}
          </Link>
          <span className="text-small text-muted">{e.campaignTitle}</span>
        </span>
      ),
    },
    { id: 'element', header: 'Element', cell: (e) => humanize(e.element) },
    { id: 'status', header: 'Status', cell: (e) => <Badge tone={STATUS_TONES[e.status]}>{e.status}</Badge> },
    {
      id: 'variants',
      header: 'Variants',
      cell: (e) => e.variants.map((v) => `${v.key} ${v.weight}%`).join(' · '),
    },
    {
      id: 'started',
      header: 'Started',
      hideOnMobile: true,
      cell: (e) => (e.startedAt ? <DateTime value={e.startedAt} format="date" /> : '—'),
    },
  ];

  const spec: Record<
    Lifecycle,
    { title: string; description: string; label: string; tone: 'primary' | 'danger' }
  > = {
    start: {
      title: 'Start this experiment?',
      description:
        'Participants are assigned a sticky variant from now on. Variants can no longer be edited.',
      label: 'Start experiment',
      tone: 'primary',
    },
    pause: {
      title: 'Pause this experiment?',
      description: 'New participants see the base content; existing assignments are kept.',
      label: 'Pause',
      tone: 'primary',
    },
    resume: {
      title: 'Resume this experiment?',
      description: 'Assignment continues.',
      label: 'Resume',
      tone: 'primary',
    },
    complete: {
      title: 'Complete this experiment?',
      description:
        'The experiment stops for good. Choosing a winner is optional and only recorded — it does not change the campaign.',
      label: 'Complete experiment',
      tone: 'primary',
    },
    delete: {
      title: 'Delete this draft experiment?',
      description: 'This cannot be undone.',
      label: 'Delete',
      tone: 'danger',
    },
  };
  const current = action ? spec[action.kind] : null;

  return (
    <>
      <PageHeader
        title="Experiments"
        description="A/B tests on campaign titles, creative, instructions and landing pages."
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setEditing('new')}>
            New experiment
          </Button>
        }
      />
      <div className="stack">
        <FilterBar
          search={search}
          onSearchChange={(s) => {
            setSearch(s);
            setPage(1);
          }}
          searchLabel="Search experiments"
          filters={[
            {
              id: 'campaign',
              label: 'Campaign',
              options: (campaigns.data ?? []).map((c) => ({ value: c.id, label: c.title })),
            },
            {
              id: 'status',
              label: 'Status',
              options: EXPERIMENT_STATUSES.map((s) => ({ value: s, label: s })),
            },
          ]}
          values={filters}
          onFilterChange={(id, value) => {
            setFilters((f) => ({ ...f, [id]: value }));
            setPage(1);
          }}
          onReset={() => {
            setSearch('');
            setFilters({});
          }}
        />
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Experiments"
              columns={columns}
              rows={query.data?.items ?? []}
              getRowId={(e) => e.id}
              rowLabel={(e) => e.name}
              loading={query.isLoading}
              rowActions={menu}
              emptyState={
                <EmptyState
                  icon={<FlaskConical />}
                  headingLevel={2}
                  title="No experiments"
                  description="Test two to four variants of a campaign element and compare submission rates."
                />
              }
            />
            {query.data && query.data.total > 0 && (
              <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />
            )}
          </>
        )}
      </div>
      {editing && (
        <ExperimentDialog
          experiment={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void refresh();
          }}
        />
      )}
      <ConfirmDialog
        open={!!action}
        onClose={() => setAction(null)}
        onConfirm={runAction}
        title={current?.title ?? ''}
        description={current?.description}
        confirmLabel={current?.label}
        tone={current?.tone}
      >
        {action?.kind === 'complete' && (
          <RadioGroup
            legend="Winning variant (optional)"
            value={winner}
            onChange={setWinner}
            options={[
              { value: '', label: 'No winner' },
              ...action.experiment.variants.map((v) => ({ value: v.id, label: `${v.key} · ${v.name}` })),
            ]}
          />
        )}
      </ConfirmDialog>
    </>
  );
}

interface VariantForm extends VariantInput {
  weightText: string;
}

function blankVariant(key: string, weight: number): VariantForm {
  return {
    key,
    name: key === 'A' ? 'Control' : `Variant ${key}`,
    weight,
    weightText: String(weight),
    title: '',
    instructions: '',
    assetId: '',
    landingHeadline: '',
    landingBody: '',
  };
}

export function ExperimentDialog({
  experiment,
  onClose,
  onSaved,
}: {
  experiment: Experiment | null;
  onClose: () => void;
  onSaved: (e: Experiment) => void;
}) {
  const toast = useToast();
  const campaigns = useCampaignOptions();
  const [campaignId, setCampaignId] = useState(experiment?.campaignId ?? '');
  const campaignChoices = useMemo(() => {
    const options = (campaigns.data ?? []).map((c) => ({ value: c.id, label: c.title }));
    // An existing experiment's campaign stays selectable even beyond the newest 500.
    if (experiment && !options.some((o) => o.value === experiment.campaignId))
      options.unshift({ value: experiment.campaignId, label: experiment.campaignTitle });
    return options;
  }, [campaigns.data, experiment]);
  const [name, setName] = useState(experiment?.name ?? '');
  const [hypothesis, setHypothesis] = useState(experiment?.hypothesis ?? '');
  const [element, setElement] = useState<ExperimentElement>(experiment?.element ?? 'Title');
  const [variants, setVariants] = useState<VariantForm[]>(
    experiment
      ? experiment.variants.map((v) => ({
          ...v,
          weightText: String(v.weight),
          title: v.title ?? '',
          instructions: v.instructions ?? '',
          assetId: v.assetId ?? '',
          landingHeadline: v.landingHeadline ?? '',
          landingBody: v.landingBody ?? '',
        }))
      : [blankVariant('A', 50), blankVariant('B', 50)],
  );
  const [errors, setErrors] = useState<FieldErrorMap>({});
  const [formError, setFormError] = useState<string | null>(null);
  const campaign = useCampaign(campaignId || undefined);
  const assetOptions = (campaign.data?.assets ?? [])
    .filter((a) => a.type === 'Image')
    .map((a) => ({ value: a.id, label: a.title }));

  const setVariant = (index: number, patch: Partial<VariantForm>) =>
    setVariants((list) => list.map((v, i) => (i === index ? { ...v, ...patch } : v)));

  const save = useMutation({
    mutationFn: (body: ExperimentInput) =>
      experiment
        ? api.put<Experiment>(`/marketing/experiments/${experiment.id}`, body)
        : api.post<Experiment>('/marketing/experiments', body),
    onSuccess: (saved) => {
      toast.success(experiment ? 'Experiment saved' : 'Draft experiment created', saved.name);
      onSaved(saved);
    },
    onError: (err) => {
      const mapped = fieldErrorsFrom(err, { 'experiment.campaign_not_found': 'campaignId' });
      setErrors(mapped);
      setFormError(errorMessage(err));
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const local: FieldErrorMap = {};
    if (!campaignId) local.campaignid = ['Choose a campaign.'];
    if (name.trim().length < 2) local.name = ['Enter a name (at least 2 characters).'];
    variants.forEach((v, i) => {
      const w = Number(v.weightText);
      if (!Number.isInteger(w) || w < 1 || w > 100)
        local[`variants[${i}].weight`] = ['Weight must be 1–100.'];
    });
    setErrors(local);
    setFormError(null);
    if (Object.keys(local).length) return;
    const orNull = (s: string | null | undefined) => (s && s.trim() ? s.trim() : null);
    save.mutate({
      campaignId,
      name: name.trim(),
      hypothesis: orNull(hypothesis),
      element,
      concurrencyStamp: experiment?.concurrencyStamp,
      variants: variants.map((v) => ({
        key: v.key,
        name: v.name.trim(),
        weight: Number(v.weightText),
        title: element === 'Title' ? orNull(v.title) : null,
        instructions: element === 'Instructions' ? orNull(v.instructions) : null,
        assetId: element === 'CreativeAsset' ? orNull(v.assetId) : null,
        landingHeadline: element === 'LandingPage' ? orNull(v.landingHeadline) : null,
        landingBody: element === 'LandingPage' ? orNull(v.landingBody) : null,
      })),
    });
  };

  const totalWeight = variants.reduce((s, v) => s + (Number(v.weightText) || 0), 0);

  return (
    <Dialog
      open
      onClose={onClose}
      title={experiment ? 'Edit experiment' : 'New experiment'}
      description="Experiments start as drafts. Variant A is the control."
      size="lg"
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="experiment-form" loading={save.isPending}>
            {experiment ? 'Save experiment' : 'Create draft'}
          </Button>
        </>
      }
    >
      <form id="experiment-form" className="stack" onSubmit={submit} noValidate>
        {formError && (
          <Alert tone="danger" role="alert">
            {formError}
          </Alert>
        )}
        <div className="mg-grid mg-grid--2">
          <FormField label="Campaign" required error={fieldError(errors, 'campaignId')}>
            <Select
              value={campaignId}
              disabled={!!experiment}
              placeholder="Choose a campaign"
              options={campaignChoices}
              onChange={(e) => setCampaignId(e.target.value)}
            />
          </FormField>
          <FormField label="Name" required error={fieldError(errors, 'name')}>
            <Input value={name} maxLength={150} onChange={(e) => setName(e.target.value)} />
          </FormField>
        </div>
        <FormField label="Hypothesis" optional error={fieldError(errors, 'hypothesis')}>
          <Textarea
            value={hypothesis}
            rows={2}
            maxLength={1000}
            onChange={(e) => setHypothesis(e.target.value)}
          />
        </FormField>
        <RadioGroup
          legend="Element under test"
          value={element}
          onChange={(v) => setElement(v as ExperimentElement)}
          variant="cards"
          orientation="horizontal"
          options={EXPERIMENT_ELEMENTS.map((el) => ({
            value: el,
            label: humanize(el),
            description: ELEMENT_HELP[el],
          }))}
        />
        {fieldError(errors, 'variants') && (
          <Alert tone="danger" role="alert">
            {fieldError(errors, 'variants')!.join(' ')}
          </Alert>
        )}
        <ol className="stack mg-variants" aria-label="Variants">
          {variants.map((v, i) => (
            <li key={v.key} className="mg-variant">
              <div className="cluster mg-space-between">
                <h3 className="mg-h3">
                  Variant {v.key}
                  {v.key === 'A' && ' (control)'}
                </h3>
                {variants.length > 2 && i === variants.length - 1 && (
                  <IconButton
                    size="sm"
                    variant="ghost"
                    label={`Remove variant ${v.key}`}
                    icon={<Trash2 />}
                    onClick={() => setVariants((list) => list.slice(0, -1))}
                  />
                )}
              </div>
              <div className="mg-grid mg-grid--2">
                <FormField label="Name" required error={fieldError(errors, `variants[${i}].name`)}>
                  <Input
                    value={v.name}
                    maxLength={100}
                    onChange={(e) => setVariant(i, { name: e.target.value })}
                  />
                </FormField>
                <FormField
                  label="Weight"
                  hint="1–100, relative share of traffic"
                  error={fieldError(errors, `variants[${i}].weight`)}
                >
                  <Input
                    type="number"
                    min={1}
                    max={100}
                    step={1}
                    value={v.weightText}
                    onChange={(e) => setVariant(i, { weightText: e.target.value })}
                  />
                </FormField>
              </div>
              {element === 'Title' && (
                <FormField label="Title" required error={fieldError(errors, `variants[${i}].title`)}>
                  <Input
                    value={v.title ?? ''}
                    maxLength={200}
                    onChange={(e) => setVariant(i, { title: e.target.value })}
                  />
                </FormField>
              )}
              {element === 'Instructions' && (
                <FormField
                  label="Instructions"
                  required
                  error={fieldError(errors, `variants[${i}].instructions`)}
                >
                  <Textarea
                    value={v.instructions ?? ''}
                    rows={3}
                    onChange={(e) => setVariant(i, { instructions: e.target.value })}
                  />
                </FormField>
              )}
              {element === 'CreativeAsset' && (
                <FormField
                  label="Image asset"
                  required
                  hint={
                    campaignId && assetOptions.length === 0
                      ? 'This campaign has no image assets yet.'
                      : undefined
                  }
                  error={fieldError(errors, `variants[${i}].assetId`)}
                >
                  <Select
                    value={v.assetId ?? ''}
                    placeholder="Choose an asset"
                    options={assetOptions}
                    disabled={!campaignId}
                    onChange={(e) => setVariant(i, { assetId: e.target.value })}
                  />
                </FormField>
              )}
              {element === 'LandingPage' && (
                <>
                  <FormField
                    label="Landing headline"
                    error={fieldError(errors, `variants[${i}].landingHeadline`)}
                  >
                    <Input
                      value={v.landingHeadline ?? ''}
                      maxLength={200}
                      onChange={(e) => setVariant(i, { landingHeadline: e.target.value })}
                    />
                  </FormField>
                  <FormField label="Landing body" error={fieldError(errors, `variants[${i}].landingBody`)}>
                    <Textarea
                      value={v.landingBody ?? ''}
                      rows={3}
                      onChange={(e) => setVariant(i, { landingBody: e.target.value })}
                    />
                  </FormField>
                </>
              )}
            </li>
          ))}
        </ol>
        <div className="cluster mg-space-between">
          <p className="text-small text-muted">
            Total weight {totalWeight}. Traffic is split in proportion to the weights.
          </p>
          <Button
            size="sm"
            variant="secondary"
            leadingIcon={<Plus />}
            disabled={variants.length >= 4}
            onClick={() => setVariants((list) => [...list, blankVariant(KEYS[list.length]!, 50)])}
          >
            Add variant
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
