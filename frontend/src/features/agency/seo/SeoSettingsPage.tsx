import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { EyeOff, Pencil, Plus, RotateCcw, Trash2 } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
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
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { seoKeys, severityTone, type AuditRule, type CitationSource, type SeoSeverity } from './api';
import { fieldErrors, firstError } from './common';
import './seo.css';

const READ_ONLY =
  'Changing agency-wide SEO settings needs the settings.manage permission (an administrator).';

/** Agency-wide SEO settings: audit rules (wording, severity, on/off) and the local-SEO directory list. */
export function SeoSettingsPage() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.SettingsManage);
  return (
    <>
      <PageHeader
        title="SEO settings"
        description="Audit rules and local directories used for every client. Changes apply to new audits; finished audits keep their results."
        breadcrumbs={[{ label: 'SEO', to: '/agency/seo' }, { label: 'Settings' }]}
      />
      {!canEdit && (
        <Alert tone="info" title="Read only">
          {READ_ONLY}
        </Alert>
      )}
      <Tabs
        label="SEO settings"
        tabs={[
          { id: 'rules', label: 'Audit rules', content: <RulesPanel canEdit={canEdit} /> },
          { id: 'directories', label: 'Local directories', content: <DirectoriesPanel canEdit={canEdit} /> },
        ]}
      />
    </>
  );
}

function RulesPanel({ canEdit }: { canEdit: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<AuditRule | null>(null);
  const rules = useQuery({
    queryKey: seoKeys.rules,
    queryFn: () => api.get<AuditRule[]>('/agency/seo/rules'),
  });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: seoKeys.rules });
  const reset = useMutation({
    mutationFn: (r: AuditRule) => api.post<AuditRule>(`/agency/seo/rules/${encodeURIComponent(r.key)}/reset`),
    onSuccess: (r) => {
      toast.success('Rule reset to the default', r.title);
      refresh();
    },
    onError: (err) => toast.error('Could not reset the rule', errorMessage(err)),
  });
  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (rules.data ?? []).filter(
      (r) =>
        !q || r.title.toLowerCase().includes(q) || r.category.toLowerCase().includes(q) || r.key.includes(q),
    );
  }, [rules.data, search]);
  if (rules.isError) return <ErrorState error={rules.error} onRetry={() => void rules.refetch()} />;
  const columns: DataTableColumn<AuditRule>[] = [
    {
      id: 'title',
      header: 'Rule',
      primary: true,
      sortable: true,
      sortValue: (r) => r.title,
      cell: (r) => (
        <span className="stack seo-stack-xs">
          <span className="seo-strong">{r.title}</span>
          <span className="text-small text-muted">{r.category}</span>
        </span>
      ),
    },
    {
      id: 'severity',
      header: 'Severity',
      sortable: true,
      sortValue: (r) => r.severity,
      cell: (r) => (
        <span className="seo-toolbar">
          <Badge tone={severityTone[r.severity]}>{r.severity}</Badge>
          {r.severity !== r.defaultSeverity && (
            <span className="text-small text-muted">default {r.defaultSeverity}</span>
          )}
        </span>
      ),
    },
    {
      id: 'enabled',
      header: 'Checked',
      cell: (r) => (r.isEnabled ? <Badge tone="success">On</Badge> : <Badge tone="neutral">Off</Badge>),
    },
    { id: 'custom', header: 'Customized', hideOnMobile: true, cell: (r) => (r.isCustomized ? 'Yes' : '—') },
  ];
  return (
    <div className="stack">
      <FormField label="Search rules" className="seo-inline-filter">
        <Input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Title, category or key"
        />
      </FormField>
      <DataTable
        caption="Audit rules"
        columns={columns}
        rows={rows}
        getRowId={(r) => r.key}
        rowLabel={(r) => r.title}
        loading={rules.isLoading}
        rowActions={(r) => [
          {
            id: 'edit',
            label: 'Edit rule',
            icon: <Pencil />,
            disabled: !canEdit,
            description: canEdit ? undefined : READ_ONLY,
            onSelect: () => setEditing(r),
          },
          {
            id: 'reset',
            label: 'Reset to default',
            icon: <RotateCcw />,
            disabled: !canEdit || !r.isCustomized,
            description: !r.isCustomized ? 'Already the default.' : undefined,
            onSelect: () => reset.mutate(r),
          },
        ]}
        emptyState={<EmptyState compact headingLevel={3} title="No rule matches" />}
      />
      {editing && <RuleDialog rule={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
    </div>
  );
}

export function RuleDialog({
  rule,
  onClose,
  onSaved,
}: {
  rule: AuditRule;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    title: rule.title,
    severity: rule.severity,
    whyItMatters: rule.whyItMatters,
    howToFix: rule.howToFix,
    isEnabled: rule.isEnabled,
  });
  const save = useMutation({
    mutationFn: () =>
      api.put<AuditRule>(`/agency/seo/rules/${encodeURIComponent(rule.key)}`, {
        ...form,
        concurrencyStamp: rule.concurrencyStamp,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  return (
    <Dialog
      open
      onClose={onClose}
      title="Edit audit rule"
      description={`Rule key: ${rule.key}. New audits use these settings; the health score counts errors and warnings.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-rule" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form id="seo-rule" className="stack" onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}>
        {save.isError && (
          <Alert tone="danger" title="Could not save the rule">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField label="Title" required error={firstError(errors, 'title')}>
          <Input
            value={form.title}
            maxLength={150}
            onChange={(e) => setForm({ ...form, title: e.target.value })}
          />
        </FormField>
        <FormField label="Severity" hint={`Default: ${rule.defaultSeverity}`}>
          <Select
            value={form.severity}
            onChange={(e) => setForm({ ...form, severity: e.target.value as SeoSeverity })}
            options={(['Error', 'Warning', 'Notice'] as const).map((s) => ({ value: s, label: s }))}
          />
        </FormField>
        <FormField label="Why it matters" required error={firstError(errors, 'whyItMatters')}>
          <Textarea
            rows={3}
            maxLength={2000}
            value={form.whyItMatters}
            onChange={(e) => setForm({ ...form, whyItMatters: e.target.value })}
          />
        </FormField>
        <FormField label="How to fix" required error={firstError(errors, 'howToFix')}>
          <Textarea
            rows={3}
            maxLength={2000}
            value={form.howToFix}
            onChange={(e) => setForm({ ...form, howToFix: e.target.value })}
          />
        </FormField>
        <Checkbox
          label="Check this rule in audits"
          description="Turned-off rules are skipped by new audits."
          checked={form.isEnabled}
          onChange={(e) => setForm({ ...form, isEnabled: e.target.checked })}
        />
      </form>
    </Dialog>
  );
}

function DirectoriesPanel({ canEdit }: { canEdit: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<CitationSource | 'new' | null>(null);
  const [deleting, setDeleting] = useState<CitationSource | null>(null);
  const sources = useQuery({
    queryKey: seoKeys.sources,
    queryFn: () => api.get<CitationSource[]>('/agency/seo/citation-sources'),
  });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: seoKeys.sources });
  const toggle = useMutation({
    mutationFn: (s: CitationSource) =>
      api.put<CitationSource>(`/agency/seo/citation-sources/${s.id}`, {
        name: s.name,
        url: s.url,
        category: s.category,
        countries: s.countries,
        sortOrder: s.sortOrder,
        isActive: !s.isActive,
        concurrencyStamp: s.concurrencyStamp,
      }),
    onSuccess: (s) => {
      toast.success(s.isActive ? 'Directory shown' : 'Directory hidden', s.name);
      refresh();
    },
    onError: (err) => toast.error('Could not update the directory', errorMessage(err)),
  });
  if (sources.isError) return <ErrorState error={sources.error} onRetry={() => void sources.refetch()} />;
  const columns: DataTableColumn<CitationSource>[] = [
    {
      id: 'name',
      header: 'Directory',
      primary: true,
      cell: (s) => (
        <span className="stack seo-stack-xs">
          <SafeExternalLink href={s.url} className="ui-link">
            {s.name}
          </SafeExternalLink>
          <span className="text-small text-muted">{s.category}</span>
        </span>
      ),
    },
    {
      id: 'countries',
      header: 'Countries',
      hideOnMobile: true,
      cell: (s) => (s.countries.length ? s.countries.join(', ') : 'Global'),
    },
    { id: 'order', header: 'Order', align: 'right', hideOnMobile: true, cell: (s) => s.sortOrder },
    { id: 'used', header: 'Citations', align: 'right', cell: (s) => s.citationCount },
    {
      id: 'state',
      header: 'State',
      cell: (s) => (
        <span className="seo-toolbar">
          {s.isActive ? <Badge tone="success">Shown</Badge> : <Badge tone="neutral">Hidden</Badge>}
          {s.isCustom && <Badge tone="brand">Agency</Badge>}
        </span>
      ),
    },
  ];
  return (
    <div className="stack">
      <div className="seo-toolbar">
        <Button
          leadingIcon={<Plus />}
          onClick={() => setEditing('new')}
          disabled={!canEdit}
          title={canEdit ? undefined : READ_ONLY}
        >
          Add directory
        </Button>
      </div>
      <DataTable
        caption="Local directories"
        columns={columns}
        rows={sources.data ?? []}
        getRowId={(s) => s.id}
        rowLabel={(s) => s.name}
        loading={sources.isLoading}
        rowActions={(s) => [
          { id: 'edit', label: 'Edit', icon: <Pencil />, disabled: !canEdit, onSelect: () => setEditing(s) },
          {
            id: 'toggle',
            label: s.isActive ? 'Hide from trackers' : 'Show in trackers',
            icon: <EyeOff />,
            disabled: !canEdit,
            onSelect: () => toggle.mutate(s),
          },
          {
            id: 'delete',
            label: 'Delete',
            icon: <Trash2 />,
            danger: true,
            disabled: !canEdit || !s.isCustom || s.citationCount > 0,
            description: !s.isCustom
              ? 'Built-in directories can only be hidden.'
              : s.citationCount > 0
                ? 'Citations use it — hide it instead.'
                : undefined,
            onSelect: () => setDeleting(s),
          },
        ]}
        emptyState={<EmptyState compact headingLevel={3} title="No directories" />}
      />
      {editing && (
        <DirectoryDialog
          source={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={refresh}
        />
      )}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this directory?"
        description={deleting ? `${deleting.name} is removed from the list for every client.` : undefined}
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/seo/citation-sources/${deleting.id}`);
          toast.success('Directory deleted');
          refresh();
        }}
      />
    </div>
  );
}

export function DirectoryDialog({
  source,
  onClose,
  onSaved,
}: {
  source: CitationSource | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    name: source?.name ?? '',
    url: source?.url ?? 'https://',
    category: source?.category ?? 'General',
    countries: source?.countries.join(', ') ?? '',
    sortOrder: String(source?.sortOrder ?? 500),
    isActive: source?.isActive ?? true,
  });
  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: form.name,
        url: form.url,
        category: form.category,
        sortOrder: Number(form.sortOrder) || 0,
        isActive: form.isActive,
        countries: form.countries
          .split(',')
          .map((c) => c.trim())
          .filter(Boolean),
        concurrencyStamp: source?.concurrencyStamp,
      };
      return source
        ? api.put(`/agency/seo/citation-sources/${source.id}`, body)
        : api.post('/agency/seo/citation-sources', body);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  return (
    <Dialog
      open
      onClose={onClose}
      title={source ? 'Edit directory' : 'Add a directory'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-directory" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form
        id="seo-directory"
        className="stack"
        onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}
      >
        {save.isError && (
          <Alert tone="danger" title="Could not save the directory">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField label="Name" required error={firstError(errors, 'name')}>
          <Input
            value={form.name}
            maxLength={150}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
          />
        </FormField>
        <FormField label="Website" required error={firstError(errors, 'url')}>
          <Input
            value={form.url}
            inputMode="url"
            onChange={(e) => setForm({ ...form, url: e.target.value })}
          />
        </FormField>
        <div className="seo-grid-2">
          <FormField label="Category" required error={firstError(errors, 'category')}>
            <Input
              value={form.category}
              maxLength={60}
              onChange={(e) => setForm({ ...form, category: e.target.value })}
            />
          </FormField>
          <FormField label="Sort order" hint="Lower numbers are listed first.">
            <Input
              type="number"
              min={0}
              value={form.sortOrder}
              onChange={(e) => setForm({ ...form, sortOrder: e.target.value })}
            />
          </FormField>
        </div>
        <FormField
          label="Countries"
          optional
          hint="Two-letter codes, comma-separated. Empty = relevant everywhere."
          error={firstError(errors, 'countries')}
        >
          <Input value={form.countries} onChange={(e) => setForm({ ...form, countries: e.target.value })} />
        </FormField>
        <Checkbox
          label="Show in the citation tracker"
          checked={form.isActive}
          onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
        />
      </form>
    </Dialog>
  );
}
