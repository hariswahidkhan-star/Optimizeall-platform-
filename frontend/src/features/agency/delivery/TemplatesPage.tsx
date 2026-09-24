import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Switch,
  Tabs,
  Textarea,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { PROJECT_TYPES, type BriefFieldType, type BriefTemplate, type ProjectTemplate } from '../shared/deliveryTypes';
import { labelOf } from '../shared/deliveryUi';
import { dk } from './api';

/** Report template as returned by GET /agency/templates/reports. */
export interface ReportTemplate {
  id: string;
  key: string;
  name: string;
  description: string | null;
  sections: { key: string; kind: string; title: string; providerKey: string | null; prompt: string | null }[];
  isActive: boolean;
  builtIn: boolean;
  concurrencyStamp: string;
}

type ManagedBrief = BriefTemplate & { isActive?: boolean; builtIn?: boolean; concurrencyStamp?: string };
type ManagedProject = ProjectTemplate & { builtIn?: boolean };

interface OnboardingTemplate {
  items: { key: string; title: string; description: string | null; category: string; owner: 'Agency' | 'Client' }[];
  version: string;
}

const SECTION_KINDS = ['summary', 'kpis', 'channel', 'wins', 'plan', 'custom'];
const FIELD_TYPES: BriefFieldType[] = ['Text', 'LongText', 'Date', 'Url', 'List', 'Select'];
const splitLine = (line: string) => line.split('|').map((s) => s.trim());
const lines = (text: string) => text.split('\n').map(splitLine).filter((p) => p[0]);

/** Tasks as editable lines: "Title | milestoneKey | offsetDays | hours". */
const toLines = (t: ProjectTemplate) =>
  t.tasks.map((x) => [x.title, x.milestoneKey ?? '', x.offsetDays, x.estimateHours ?? ''].join(' | ')).join('\n');

function EditorDialog({
  title,
  formId,
  pending,
  error,
  onClose,
  onSubmit,
  children,
}: {
  title: string;
  formId: string;
  pending: boolean;
  error: unknown;
  onClose: () => void;
  onSubmit: () => void;
  children: React.ReactNode;
}) {
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title={title}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={pending}>
            Save template
          </Button>
        </>
      }
    >
      <form
        id={formId}
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          onSubmit();
        }}
      >
        {error ? <Alert tone="danger">{errorMessage(error)}</Alert> : null}
        {children}
      </form>
    </Dialog>
  );
}

function ProjectTemplateEditor({ template, onClose }: { template: ProjectTemplate | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [form, setForm] = useState({
    key: template?.key ?? '',
    name: template?.name ?? '',
    description: template?.description ?? '',
    projectType: template?.projectType ?? 'RetainerMonth',
    durationDays: template?.durationDays?.toString() ?? '30',
    defaultBudgetHours: template?.defaultBudgetHours?.toString() ?? '',
    serviceLines: (template?.serviceLines ?? []).join(', '),
    milestones: (template?.milestones ?? []).map((m) => `${m.key} | ${m.title} | ${m.offsetDays}`).join('\n'),
    tasks: template ? toLines(template) : '',
    recurring: (template?.recurring ?? []).map((r) => [r.title, r.dayOfMonth, r.dueInDays, r.estimateHours ?? ''].join(' | ')).join('\n'),
    isActive: template?.isActive ?? true,
  });
  const save = useMutation({
    mutationFn: () => {
      const milestones = lines(form.milestones).map(([key, title, offset]) => ({ key: key!, title: title || key!, offsetDays: Number(offset) || 0 }));
      const tasks = lines(form.tasks).map(([title, milestoneKey, offset, hours]) => ({
        title: title!,
        description: null,
        milestoneKey: milestoneKey || null,
        offsetDays: Number(offset) || 0,
        estimateHours: hours ? Number(hours) : null,
        labels: [],
        clientVisible: true,
      }));
      const recurring = lines(form.recurring).map(([title, day, due, hours]) => ({
        title: title!,
        description: null,
        dayOfMonth: Number(day) || 1,
        dueInDays: Number(due) || 5,
        estimateHours: hours ? Number(hours) : null,
        labels: [],
      }));
      const body = {
        ...form,
        durationDays: form.durationDays ? Number(form.durationDays) : null,
        defaultBudgetHours: form.defaultBudgetHours ? Number(form.defaultBudgetHours) : null,
        serviceLines: form.serviceLines.split(',').map((s) => s.trim().toLowerCase()).filter(Boolean),
        milestones,
        tasks,
        recurring,
        concurrencyStamp: template?.concurrencyStamp,
      };
      return template ? api.put(`/agency/templates/projects/${template.id}`, body) : api.post('/agency/templates/projects', body);
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: dk.templates });
      onClose();
    },
  });
  return (
    <EditorDialog
      title={template ? `Edit ${template.name}` : 'New project template'}
      formId="project-template-form"
      pending={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <div className="dl-form__row">
        <FormField label="Key" required hint="Lower-case letters, digits and hyphens.">
          <Input value={form.key} disabled={Boolean(template)} onChange={(e) => setForm({ ...form, key: e.target.value })} pattern="[a-z0-9-]{2,64}" required />
        </FormField>
        <FormField label="Name" required>
          <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
        </FormField>
      </div>
      <FormField label="Description" optional>
        <Textarea rows={2} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
      </FormField>
      <div className="dl-form__row">
        <FormField label="Project type">
          <Select
            value={form.projectType}
            onChange={(e) => setForm({ ...form, projectType: e.target.value as ProjectTemplate['projectType'] })}
            options={PROJECT_TYPES.map((t) => ({ value: t, label: labelOf(t) }))}
          />
        </FormField>
        <FormField label="Duration (days)">
          <Input type="number" min={1} value={form.durationDays} onChange={(e) => setForm({ ...form, durationDays: e.target.value })} />
        </FormField>
        <FormField label="Budget hours">
          <Input type="number" min={0} value={form.defaultBudgetHours} onChange={(e) => setForm({ ...form, defaultBudgetHours: e.target.value })} />
        </FormField>
      </div>
      <FormField label="Service lines" optional hint="Comma separated, e.g. seo, content">
        <Input value={form.serviceLines} onChange={(e) => setForm({ ...form, serviceLines: e.target.value })} />
      </FormField>
      <FormField label="Milestones" hint="One per line: key | title | day offset">
        <Textarea rows={4} value={form.milestones} onChange={(e) => setForm({ ...form, milestones: e.target.value })} />
      </FormField>
      <FormField label="Tasks" hint="One per line: title | milestone key | day offset | estimate hours">
        <Textarea rows={8} value={form.tasks} onChange={(e) => setForm({ ...form, tasks: e.target.value })} />
      </FormField>
      <FormField label="Recurring tasks" optional hint="One per line: title | day of month (1–28) | due in days | estimate hours">
        <Textarea rows={3} value={form.recurring} onChange={(e) => setForm({ ...form, recurring: e.target.value })} />
      </FormField>
      <Switch checked={form.isActive} onCheckedChange={(v) => setForm({ ...form, isActive: v })} label="Active" />
    </EditorDialog>
  );
}

function BriefTemplateEditor({ template, onClose }: { template: ManagedBrief | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [form, setForm] = useState({
    key: template?.key ?? '',
    name: template?.name ?? '',
    serviceLine: template?.serviceLine ?? 'content',
    description: template?.description ?? '',
    fields: (template?.fields ?? [])
      .map((f) => [f.key, f.label, f.type, f.required ? 'required' : 'optional', f.options.join(', '), f.help ?? ''].join(' | '))
      .join('\n'),
    isActive: template?.isActive ?? true,
  });
  const save = useMutation({
    mutationFn: () => {
      const fields = lines(form.fields).map(([key, label, type, required, options, help]) => ({
        key: key!,
        label: label || key!,
        type: (FIELD_TYPES.find((t) => t.toLowerCase() === (type ?? '').toLowerCase()) ?? 'Text') as BriefFieldType,
        required: (required ?? '').toLowerCase().startsWith('req'),
        options: (options ?? '').split(',').map((o) => o.trim()).filter(Boolean),
        help: help || null,
      }));
      const body = { ...form, fields, concurrencyStamp: template?.concurrencyStamp };
      return template ? api.put(`/agency/templates/briefs/${template.id}`, body) : api.post('/agency/templates/briefs', body);
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['delivery', 'brief-templates'] });
      onClose();
    },
  });
  return (
    <EditorDialog
      title={template ? `Edit ${template.name}` : 'New brief template'}
      formId="brief-template-form"
      pending={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <div className="dl-form__row">
        <FormField label="Key" required hint="Lower-case letters, digits and hyphens.">
          <Input value={form.key} disabled={Boolean(template)} onChange={(e) => setForm({ ...form, key: e.target.value })} required />
        </FormField>
        <FormField label="Name" required>
          <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
        </FormField>
        <FormField label="Service line" required>
          <Input value={form.serviceLine} onChange={(e) => setForm({ ...form, serviceLine: e.target.value })} required />
        </FormField>
      </div>
      <FormField label="Description" optional>
        <Textarea rows={2} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
      </FormField>
      <FormField
        label="Fields"
        hint={`One per line: key | label | type (${FIELD_TYPES.join(', ')}) | required or optional | choices (comma separated, for Select) | help`}
      >
        <Textarea rows={8} value={form.fields} onChange={(e) => setForm({ ...form, fields: e.target.value })} />
      </FormField>
      <Switch checked={form.isActive} onCheckedChange={(v) => setForm({ ...form, isActive: v })} label="Active (offered to clients)" />
    </EditorDialog>
  );
}

function ReportTemplateEditor({ template, onClose }: { template: ReportTemplate | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [form, setForm] = useState({
    key: template?.key ?? '',
    name: template?.name ?? '',
    description: template?.description ?? '',
    sections: (template?.sections ?? []).map((s) => [s.key, s.kind, s.title, s.providerKey ?? '', s.prompt ?? ''].join(' | ')).join('\n'),
    isActive: template?.isActive ?? true,
  });
  const save = useMutation({
    mutationFn: () => {
      const sections = lines(form.sections).map(([key, kind, title, provider, prompt]) => ({
        key: key!,
        kind: kind || 'custom',
        title: title || key!,
        providerKey: provider || null,
        prompt: prompt || null,
      }));
      const body = { ...form, sections, concurrencyStamp: template?.concurrencyStamp };
      return template ? api.put(`/agency/templates/reports/${template.id}`, body) : api.post('/agency/templates/reports', body);
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['delivery', 'report-templates'] });
      onClose();
    },
  });
  return (
    <EditorDialog
      title={template ? `Edit ${template.name}` : 'New report template'}
      formId="report-template-form"
      pending={save.isPending}
      error={save.error}
      onClose={onClose}
      onSubmit={() => save.mutate()}
    >
      <div className="dl-form__row">
        <FormField label="Key" required hint="Lower-case letters, digits and hyphens.">
          <Input value={form.key} disabled={Boolean(template)} onChange={(e) => setForm({ ...form, key: e.target.value })} required />
        </FormField>
        <FormField label="Name" required>
          <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
        </FormField>
      </div>
      <FormField label="Description" optional>
        <Textarea rows={2} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
      </FormField>
      <FormField
        label="Sections"
        hint={`One per line: key | kind (${SECTION_KINDS.join(', ')}) | title | data source key (optional) | guidance (optional)`}
      >
        <Textarea rows={8} value={form.sections} onChange={(e) => setForm({ ...form, sections: e.target.value })} />
      </FormField>
      <Switch
        checked={form.isActive}
        onCheckedChange={(v) => setForm({ ...form, isActive: v })}
        label="Active (offered when creating reports)"
      />
    </EditorDialog>
  );
}

function OnboardingTemplateEditor({ canManage }: { canManage: boolean }) {
  const qc = useQueryClient();
  const toast = useToast();
  const query = useQuery({
    queryKey: ['delivery', 'onboarding-template'],
    queryFn: ({ signal }) => api.get<OnboardingTemplate>('/agency/settings/onboarding-template', { signal }),
  });
  const [text, setText] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: (body: { items: unknown[]; version: string }) => api.put<OnboardingTemplate>('/agency/settings/onboarding-template', body),
    onSuccess: (data) => {
      qc.setQueryData(['delivery', 'onboarding-template'], data);
      setText(null);
      toast.success('Onboarding checklist saved', 'New clients start with this checklist; existing clients keep theirs.');
    },
  });
  if (query.isPending) return <Skeleton height={200} />;
  if (query.isError) return <ErrorState error={query.error} />;
  const current = query.data.items.map((i) => [i.title, i.category, i.owner, i.description ?? '', i.key].join(' | ')).join('\n');
  const value = text ?? current;
  return (
    <form
      className="dl-form"
      onSubmit={(e) => {
        e.preventDefault();
        const items = lines(value).map(([title, category, owner, description, key]) => ({
          title: title!,
          category: category || 'Other',
          owner: (owner ?? '').toLowerCase() === 'client' ? 'Client' : 'Agency',
          description: description || null,
          key: key || null,
        }));
        save.mutate({ items, version: query.data.version });
      }}
    >
      {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
      <FormField
        label="Checklist for new clients"
        hint="One step per line: title | category | Agency or Client | description | key (keep the key to keep a step’s identity)"
      >
        <Textarea rows={14} value={value} disabled={!canManage} onChange={(e) => setText(e.target.value)} />
      </FormField>
      {canManage && (
        <div className="dl-row">
          <Button type="submit" loading={save.isPending}>
            Save checklist
          </Button>
          {text !== null && (
            <Button variant="ghost" onClick={() => setText(null)}>
              Discard changes
            </Button>
          )}
        </div>
      )}
    </form>
  );
}

type Deleting = { kind: 'projects' | 'briefs' | 'reports'; id: string; name: string };

export function TemplatesPage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.ProjectsManage);
  const canManageReports = hasPermission(Permissions.ReportsManage);
  const canManageClients = hasPermission(Permissions.ClientsManage);
  const qc = useQueryClient();
  const toast = useToast();
  const [tab, setTab] = useState('projects');
  const [editing, setEditing] = useState<ProjectTemplate | 'new' | null>(null);
  const [editingBrief, setEditingBrief] = useState<ManagedBrief | 'new' | null>(null);
  const [editingReport, setEditingReport] = useState<ReportTemplate | 'new' | null>(null);
  const [deleting, setDeleting] = useState<Deleting | null>(null);
  const projects = useQuery({
    queryKey: [...dk.templates, 'all'],
    queryFn: ({ signal }) => api.get<ManagedProject[]>('/agency/templates/projects', { query: { includeInactive: true }, signal }),
  });
  const briefs = useQuery({
    queryKey: ['delivery', 'brief-templates', 'all'],
    queryFn: ({ signal }) => api.get<ManagedBrief[]>('/agency/templates/briefs', { query: { includeInactive: true }, signal }),
  });
  const reports = useQuery({
    queryKey: ['delivery', 'report-templates', 'all'],
    queryFn: ({ signal }) => api.get<ReportTemplate[]>('/agency/templates/reports', { query: { includeInactive: true }, signal }),
  });

  const actions = (kind: Deleting['kind'], item: { id: string; name: string; builtIn?: boolean }, onEdit: () => void, allowed: boolean) =>
    allowed ? (
      <span className="dl-row">
        <Button size="sm" variant="secondary" onClick={onEdit}>
          Edit<span className="visually-hidden"> {item.name}</span>
        </Button>
        {item.builtIn ? (
          <Badge tone="neutral" title="Built-in templates can be edited or deactivated, not deleted.">
            Built-in
          </Badge>
        ) : (
          <Button size="sm" variant="ghost" onClick={() => setDeleting({ kind, id: item.id, name: item.name })}>
            Delete<span className="visually-hidden"> {item.name}</span>
          </Button>
        )}
      </span>
    ) : null;

  return (
    <div className="dl-page">
      <PageHeader
        title="Templates"
        description="Service templates create a project's milestones, tasks and recurring tasks; brief templates define what clients fill in; report templates set a report's sections; the onboarding checklist is what every new client starts with."
        actions={
          tab === 'projects' && canManage ? (
            <Button onClick={() => setEditing('new')}>New project template</Button>
          ) : tab === 'briefs' && canManage ? (
            <Button onClick={() => setEditingBrief('new')}>New brief template</Button>
          ) : tab === 'reports' && canManageReports ? (
            <Button onClick={() => setEditingReport('new')}>New report template</Button>
          ) : null
        }
      />
      <Tabs
        label="Template types"
        value={tab}
        onValueChange={setTab}
        tabs={[
          {
            id: 'projects',
            label: 'Project templates',
            content: projects.isPending ? (
              <Skeleton height={200} />
            ) : projects.isError ? (
              <ErrorState error={projects.error} />
            ) : projects.data.length === 0 ? (
              <EmptyState title="No project templates" />
            ) : (
              <div className="dl-grid dl-grid--wide">
                {projects.data.map((t) => (
                  <Card key={t.id} as="article" aria-label={t.name}>
                    <CardHeader
                      title={t.name}
                      headingLevel={2}
                      description={t.description ?? undefined}
                      actions={
                        <span className="dl-row">
                          {!t.isActive ? <Badge>Inactive</Badge> : null}
                          {actions('projects', t, () => setEditing(t), canManage)}
                        </span>
                      }
                    />
                    <CardBody>
                      <p className="dl-meta">
                        {labelOf(t.projectType)} · {t.milestones.length} milestones · {t.tasks.length} tasks · {t.recurring.length} recurring
                        {t.defaultBudgetHours ? ` · ${t.defaultBudgetHours}h` : ''}
                      </p>
                      <ol>
                        {t.tasks.slice(0, 6).map((task) => (
                          <li key={task.title}>{task.title}</li>
                        ))}
                      </ol>
                    </CardBody>
                  </Card>
                ))}
              </div>
            ),
          },
          {
            id: 'briefs',
            label: 'Brief templates',
            content: briefs.isPending ? (
              <Skeleton height={200} />
            ) : briefs.isError ? (
              <ErrorState error={briefs.error} />
            ) : briefs.data.length === 0 ? (
              <EmptyState title="No brief templates" />
            ) : (
              <div className="dl-grid dl-grid--wide">
                {briefs.data.map((t) => (
                  <Card key={t.id} as="article" aria-label={t.name}>
                    <CardHeader
                      title={t.name}
                      headingLevel={2}
                      description={t.description ?? undefined}
                      actions={
                        <span className="dl-row">
                          <Badge>{labelOf(t.serviceLine)}</Badge>
                          {t.isActive === false ? <Badge>Inactive</Badge> : null}
                          {actions('briefs', t, () => setEditingBrief(t), canManage)}
                        </span>
                      }
                    />
                    <CardBody>
                      <ul>
                        {t.fields.map((f) => (
                          <li key={f.key}>
                            {f.label}
                            {f.required ? ' (required)' : ''}
                          </li>
                        ))}
                      </ul>
                    </CardBody>
                  </Card>
                ))}
              </div>
            ),
          },
          {
            id: 'reports',
            label: 'Report templates',
            content: reports.isPending ? (
              <Skeleton height={200} />
            ) : reports.isError ? (
              <ErrorState error={reports.error} />
            ) : reports.data.length === 0 ? (
              <EmptyState title="No report templates" />
            ) : (
              <div className="dl-grid dl-grid--wide">
                {reports.data.map((t) => (
                  <Card key={t.id} as="article" aria-label={t.name}>
                    <CardHeader
                      title={t.name}
                      headingLevel={2}
                      description={t.description ?? undefined}
                      actions={
                        <span className="dl-row">
                          {!t.isActive ? <Badge>Inactive</Badge> : null}
                          {actions('reports', t, () => setEditingReport(t), canManageReports)}
                        </span>
                      }
                    />
                    <CardBody>
                      <ol>
                        {t.sections.map((s) => (
                          <li key={s.key}>
                            {s.title} <span className="dl-meta">({s.kind}{s.providerKey ? ` · ${s.providerKey}` : ''})</span>
                          </li>
                        ))}
                      </ol>
                    </CardBody>
                  </Card>
                ))}
              </div>
            ),
          },
          {
            id: 'onboarding',
            label: 'Onboarding checklist',
            content: (
              <Card>
                <CardBody>
                  <OnboardingTemplateEditor canManage={canManageClients} />
                </CardBody>
              </Card>
            ),
          },
        ]}
      />
      {editing ? <ProjectTemplateEditor template={editing === 'new' ? null : editing} onClose={() => setEditing(null)} /> : null}
      {editingBrief ? <BriefTemplateEditor template={editingBrief === 'new' ? null : editingBrief} onClose={() => setEditingBrief(null)} /> : null}
      {editingReport ? <ReportTemplateEditor template={editingReport === 'new' ? null : editingReport} onClose={() => setEditingReport(null)} /> : null}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title={`Delete the template “${deleting?.name ?? ''}”?`}
        description="Projects, briefs and reports created from it keep their content. Deactivate it instead if you may use it again."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/templates/${deleting.kind}/${deleting.id}`);
          void qc.invalidateQueries({ queryKey: deleting.kind === 'projects' ? dk.templates : ['delivery', `${deleting.kind === 'briefs' ? 'brief' : 'report'}-templates`] });
          toast.success('Template deleted');
        }}
      />
    </div>
  );
}
