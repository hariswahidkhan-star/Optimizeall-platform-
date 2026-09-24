import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { EyeOff, Pencil, Plus, RotateCcw, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Tabs,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { socialKeys, type AdminPreset, type AwarenessDayAdmin } from './api';
import { NetworkChip } from './shared';
import './social.css';

const READ_ONLY =
  'Changing agency-wide social settings needs the settings.manage permission (an administrator).';
const MONTHS = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

function fieldError(error: unknown, field: string): string | undefined {
  if (!isApiError(error)) return undefined;
  return Object.entries(error.errors ?? {}).find(([k]) => k.toLowerCase() === field.toLowerCase())?.[1][0];
}

/** Agency-wide network presets (limits and best posting times) and the awareness-day calendar. */
export function SocialSettingsPage() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.SettingsManage);
  return (
    <>
      <PageHeader
        title="Social settings"
        description="Network limits used to validate posts, recommended posting times and the holidays shown on every client's calendar."
        breadcrumbs={[{ label: 'Social', to: '/agency/social' }, { label: 'Settings' }]}
      />
      {!canEdit && (
        <Alert tone="info" title="Read only">
          {READ_ONLY}
        </Alert>
      )}
      <Tabs
        label="Social settings"
        tabs={[
          { id: 'presets', label: 'Network presets', content: <PresetsPanel canEdit={canEdit} /> },
          { id: 'days', label: 'Awareness days', content: <AwarenessDaysPanel canEdit={canEdit} /> },
        ]}
      />
    </>
  );
}

function PresetsPanel({ canEdit }: { canEdit: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<AdminPreset | null>(null);
  const presets = useQuery({
    queryKey: socialKeys.adminPresets(),
    queryFn: () => api.get<AdminPreset[]>('/agency/social/admin/presets'),
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: socialKeys.adminPresets() });
    void queryClient.invalidateQueries({ queryKey: socialKeys.presets() });
  };
  const reset = useMutation({
    mutationFn: (p: AdminPreset) =>
      api.post<AdminPreset>(`/agency/social/admin/presets/${p.preset.network}/reset`),
    onSuccess: (p) => {
      toast.success('Preset reset to the defaults', p.preset.label);
      refresh();
    },
    onError: (e) => toast.error('Not reset', errorMessage(e)),
  });
  if (presets.isError) return <ErrorState error={presets.error} onRetry={() => void presets.refetch()} />;
  const columns: DataTableColumn<AdminPreset>[] = [
    {
      id: 'network',
      header: 'Network',
      primary: true,
      cell: (p) => (
        <span className="cluster">
          <NetworkChip network={p.preset.network} /> {p.preset.label}
        </span>
      ),
    },
    {
      id: 'text',
      header: 'Text limit',
      align: 'right',
      cell: (p) => p.preset.maxTextLength.toLocaleString('en'),
    },
    {
      id: 'hashtags',
      header: 'Hashtags',
      align: 'right',
      hideOnMobile: true,
      cell: (p) => p.preset.maxHashtags,
    },
    { id: 'media', header: 'Media', align: 'right', hideOnMobile: true, cell: (p) => p.preset.maxMedia },
    {
      id: 'times',
      header: 'Best times',
      hideOnMobile: true,
      cell: (p) => p.preset.recommendedTimes.join(', ') || '—',
    },
    {
      id: 'custom',
      header: 'Customized',
      cell: (p) => (p.isCustomized ? <Badge tone="brand">Yes</Badge> : '—'),
    },
  ];
  return (
    <div className="stack">
      <p className="sm-muted">
        Post validation, counters and the calendar's best-time hints read these values. Update them when a
        network changes its limits.
      </p>
      <DataTable
        caption="Network presets"
        columns={columns}
        rows={presets.data ?? []}
        getRowId={(p) => p.preset.network}
        rowLabel={(p) => p.preset.label}
        loading={presets.isLoading}
        rowActions={(p) => [
          {
            id: 'edit',
            label: 'Edit preset',
            icon: <Pencil />,
            disabled: !canEdit,
            description: canEdit ? undefined : READ_ONLY,
            onSelect: () => setEditing(p),
          },
          {
            id: 'reset',
            label: 'Reset to defaults',
            icon: <RotateCcw />,
            disabled: !canEdit || !p.isCustomized,
            description: !p.isCustomized ? 'Already the default.' : undefined,
            onSelect: () => reset.mutate(p),
          },
        ]}
      />
      {editing && <PresetDialog preset={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
    </div>
  );
}

export function PresetDialog({
  preset,
  onClose,
  onSaved,
}: {
  preset: AdminPreset;
  onClose: () => void;
  onSaved: () => void;
}) {
  const p = preset.preset;
  const num = (v: number | null | undefined) => (v == null ? '' : String(v));
  const [form, setForm] = useState({
    maxTextLength: num(p.maxTextLength),
    maxTitleLength: num(p.maxTitleLength),
    maxHashtags: num(p.maxHashtags),
    recommendedHashtags: num(p.recommendedHashtags),
    maxMentions: num(p.maxMentions),
    maxMedia: num(p.maxMedia),
    maxVideos: num(p.maxVideos),
    maxAltTextLength: num(p.maxAltTextLength),
    maxVideoSeconds: num(p.maxVideoSeconds),
    maxImageMb: p.maxImageBytes ? String(Math.round((p.maxImageBytes / (1024 * 1024)) * 10) / 10) : '',
    supportsFirstComment: p.supportsFirstComment,
    recommendedTimes: p.recommendedTimes.join('\n'),
    source: p.source,
  });
  const opt = (v: string) => (v.trim() === '' ? null : Number(v));
  const save = useMutation({
    mutationFn: () =>
      api.put<AdminPreset>(`/agency/social/admin/presets/${p.network}`, {
        maxTextLength: Number(form.maxTextLength),
        maxTitleLength: opt(form.maxTitleLength),
        maxHashtags: Number(form.maxHashtags),
        recommendedHashtags: opt(form.recommendedHashtags),
        maxMentions: Number(form.maxMentions),
        maxMedia: Number(form.maxMedia),
        maxVideos: Number(form.maxVideos),
        maxAltTextLength: Number(form.maxAltTextLength),
        maxVideoSeconds: opt(form.maxVideoSeconds),
        maxImageBytes:
          form.maxImageMb.trim() === '' ? null : Math.round(Number(form.maxImageMb) * 1024 * 1024),
        supportsFirstComment: form.supportsFirstComment,
        recommendedTimes: form.recommendedTimes
          .split(/\n|,/)
          .map((t) => t.trim())
          .filter(Boolean),
        source: form.source,
        concurrencyStamp: preset.concurrencyStamp,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  const numberField = (key: keyof typeof form, label: string, optional = false) => (
    <FormField label={label} optional={optional} error={fieldError(save.error, key)}>
      <Input
        type="number"
        min={0}
        value={form[key] as string}
        onChange={(e) => setForm({ ...form, [key]: e.target.value })}
      />
    </FormField>
  );
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={`Edit ${p.label} preset`}
      description="New limits apply to validation immediately; posts already scheduled are re-validated when edited."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="sm-preset" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form id="sm-preset" className="stack" onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}>
        {save.isError && (
          <Alert tone="danger" title="Could not save">
            {errorMessage(save.error)}
          </Alert>
        )}
        <div className="sm-grid-stats">
          {numberField('maxTextLength', 'Max text length')}
          {numberField('maxTitleLength', 'Max title length', true)}
          {numberField('maxHashtags', 'Max hashtags')}
          {numberField('recommendedHashtags', 'Recommended hashtags', true)}
          {numberField('maxMentions', 'Max mentions')}
          {numberField('maxMedia', 'Max media items')}
          {numberField('maxVideos', 'Max videos')}
          {numberField('maxAltTextLength', 'Max alt text length')}
          {numberField('maxVideoSeconds', 'Max video length (s)', true)}
          {numberField('maxImageMb', 'Max image size (MB)', true)}
        </div>
        <Checkbox
          label="Supports a first comment"
          checked={form.supportsFirstComment}
          onChange={(e) => setForm({ ...form, supportsFirstComment: e.target.checked })}
        />
        <FormField
          label="Recommended posting times"
          hint="One per line, like “Mon 09:00” (client's local time)."
          error={fieldError(save.error, 'recommendedTimes')}
        >
          <Textarea
            rows={4}
            value={form.recommendedTimes}
            onChange={(e) => setForm({ ...form, recommendedTimes: e.target.value })}
          />
        </FormField>
        <FormField
          label="Source"
          required
          hint="Where the limits come from (shown next to the guidance)."
          error={fieldError(save.error, 'source')}
        >
          <Input
            value={form.source}
            maxLength={500}
            onChange={(e) => setForm({ ...form, source: e.target.value })}
          />
        </FormField>
      </form>
    </Dialog>
  );
}

function AwarenessDaysPanel({ canEdit }: { canEdit: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<AwarenessDayAdmin | 'new' | null>(null);
  const [deleting, setDeleting] = useState<AwarenessDayAdmin | null>(null);
  const days = useQuery({
    queryKey: socialKeys.awarenessDays(),
    queryFn: () => api.get<AwarenessDayAdmin[]>('/agency/social/admin/awareness-days'),
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: socialKeys.awarenessDays() });
    void queryClient.invalidateQueries({ queryKey: ['social', 'calendar'] });
  };
  const toggle = useMutation({
    mutationFn: (d: AwarenessDayAdmin) =>
      api.put<AwarenessDayAdmin>(`/agency/social/admin/awareness-days/${d.id}`, {
        ...d,
        isActive: !d.isActive,
      }),
    onSuccess: (d) => {
      toast.success(d.isActive ? 'Day shown on calendars' : 'Day hidden', d.name);
      refresh();
    },
    onError: (e) => toast.error('Not changed', errorMessage(e)),
  });
  if (days.isError) return <ErrorState error={days.error} onRetry={() => void days.refetch()} />;
  const columns: DataTableColumn<AwarenessDayAdmin>[] = [
    {
      id: 'name',
      header: 'Day',
      primary: true,
      cell: (d) => (
        <span className="stack" style={{ gap: 2 }}>
          <strong>{d.name}</strong>
          <SafeExternalLink href={d.sourceUrl}>Source</SafeExternalLink>
        </span>
      ),
    },
    {
      id: 'date',
      header: 'Date',
      cell: (d) => `${d.day} ${MONTHS[d.month - 1]}${d.year ? ` ${d.year}` : ' (every year)'}`,
    },
    {
      id: 'countries',
      header: 'Countries',
      hideOnMobile: true,
      cell: (d) => (d.countries.length ? d.countries.join(', ') : 'Global'),
    },
    {
      id: 'state',
      header: 'State',
      cell: (d) => (
        <span className="cluster">
          {d.isActive ? <Badge tone="success">Shown</Badge> : <Badge>Hidden</Badge>}
          {!d.isBuiltIn && <Badge tone="brand">Agency</Badge>}
        </span>
      ),
    },
  ];
  return (
    <div className="stack">
      <div className="cluster">
        <Button
          leadingIcon={<Plus />}
          disabled={!canEdit}
          title={canEdit ? undefined : READ_ONLY}
          onClick={() => setEditing('new')}
        >
          Add a day
        </Button>
      </div>
      <DataTable
        caption="Awareness days"
        columns={columns}
        rows={days.data ?? []}
        getRowId={(d) => d.id}
        rowLabel={(d) => d.name}
        loading={days.isLoading}
        rowActions={(d) => [
          { id: 'edit', label: 'Edit', icon: <Pencil />, disabled: !canEdit, onSelect: () => setEditing(d) },
          {
            id: 'toggle',
            label: d.isActive ? 'Hide from calendars' : 'Show on calendars',
            icon: <EyeOff />,
            disabled: !canEdit,
            onSelect: () => toggle.mutate(d),
          },
          {
            id: 'delete',
            label: d.isBuiltIn ? 'Remove (hides built-in day)' : 'Delete',
            icon: <Trash2 />,
            danger: true,
            disabled: !canEdit || (d.isBuiltIn && !d.isActive),
            onSelect: () => setDeleting(d),
          },
        ]}
        emptyState={<EmptyState compact headingLevel={3} title="No awareness days" />}
      />
      {editing && (
        <AwarenessDayDialog
          day={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={refresh}
        />
      )}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title={deleting?.isBuiltIn ? 'Hide this built-in day?' : 'Delete this day?'}
        description={
          deleting?.isBuiltIn
            ? 'Built-in days are hidden rather than deleted, so they are not added again.'
            : 'It is removed from every calendar.'
        }
        confirmLabel={deleting?.isBuiltIn ? 'Hide' : 'Delete'}
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/social/admin/awareness-days/${deleting.id}`);
          refresh();
        }}
      />
    </div>
  );
}

export function AwarenessDayDialog({
  day,
  onClose,
  onSaved,
}: {
  day: AwarenessDayAdmin | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    name: day?.name ?? '',
    month: String(day?.month ?? 1),
    day: String(day?.day ?? 1),
    year: day?.year ? String(day.year) : '',
    countries: day?.countries.join(', ') ?? '',
    sourceUrl: day?.sourceUrl ?? 'https://',
    isActive: day?.isActive ?? true,
  });
  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: form.name,
        month: Number(form.month),
        day: Number(form.day),
        year: form.year ? Number(form.year) : null,
        countries: form.countries
          .split(',')
          .map((c) => c.trim())
          .filter(Boolean),
        sourceUrl: form.sourceUrl,
        isActive: form.isActive,
        concurrencyStamp: day?.concurrencyStamp,
      };
      return day
        ? api.put(`/agency/social/admin/awareness-days/${day.id}`, body)
        : api.post('/agency/social/admin/awareness-days', body);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title={day ? 'Edit awareness day' : 'Add an awareness day'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="sm-awareness-day" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form
        id="sm-awareness-day"
        className="stack"
        onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}
      >
        {save.isError && (
          <Alert tone="danger" title="Could not save">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField label="Name" required error={fieldError(save.error, 'name')}>
          <Input
            value={form.name}
            maxLength={200}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
          />
        </FormField>
        <div className="sm-grid-stats">
          <FormField label="Month">
            <Select
              value={form.month}
              onChange={(e) => setForm({ ...form, month: e.target.value })}
              options={MONTHS.map((m, i) => ({ value: String(i + 1), label: m }))}
            />
          </FormField>
          <FormField label="Day" error={fieldError(save.error, 'day')}>
            <Input
              type="number"
              min={1}
              max={31}
              value={form.day}
              onChange={(e) => setForm({ ...form, day: e.target.value })}
            />
          </FormField>
          <FormField label="Only in year" optional hint="Empty = every year (moveable dates need a year).">
            <Input
              type="number"
              min={2000}
              max={2100}
              value={form.year}
              onChange={(e) => setForm({ ...form, year: e.target.value })}
            />
          </FormField>
        </div>
        <FormField
          label="Countries"
          optional
          hint="Two-letter codes, comma-separated. Empty = global."
          error={fieldError(save.error, 'countries')}
        >
          <Input value={form.countries} onChange={(e) => setForm({ ...form, countries: e.target.value })} />
        </FormField>
        <FormField label="Source URL" required error={fieldError(save.error, 'sourceUrl')}>
          <Input
            type="url"
            value={form.sourceUrl}
            onChange={(e) => setForm({ ...form, sourceUrl: e.target.value })}
          />
        </FormField>
        <Checkbox
          label="Show on calendars"
          checked={form.isActive}
          onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
        />
      </form>
    </Dialog>
  );
}
