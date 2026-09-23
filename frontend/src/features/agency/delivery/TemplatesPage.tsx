import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Dialog,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Switch,
  Tabs,
  Textarea,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { PROJECT_TYPES, type BriefTemplate, type ProjectTemplate } from '../shared/deliveryTypes';
import { labelOf } from '../shared/deliveryUi';
import { dk } from './api';

/** Tasks as editable lines: "Title | milestoneKey | offsetDays | hours". */
const toLines = (t: ProjectTemplate) =>
  t.tasks.map((x) => [x.title, x.milestoneKey ?? '', x.offsetDays, x.estimateHours ?? ''].join(' | ')).join('\n');

function TemplateEditor({ template, onClose }: { template: ProjectTemplate | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [form, setForm] = useState({
    key: template?.key ?? '',
    name: template?.name ?? '',
    description: template?.description ?? '',
    projectType: template?.projectType ?? 'RetainerMonth',
    durationDays: template?.durationDays?.toString() ?? '30',
    defaultBudgetHours: template?.defaultBudgetHours?.toString() ?? '',
    milestones: (template?.milestones ?? []).map((m) => `${m.key} | ${m.title} | ${m.offsetDays}`).join('\n'),
    tasks: template ? toLines(template) : '',
    isActive: template?.isActive ?? true,
  });
  const save = useMutation({
    mutationFn: () => {
      const milestones = form.milestones.split('\n').map((l) => l.split('|').map((s) => s.trim())).filter((p) => p[0])
        .map(([key, title, offset]) => ({ key: key!, title: title || key!, offsetDays: Number(offset) || 0 }));
      const tasks = form.tasks.split('\n').map((l) => l.split('|').map((s) => s.trim())).filter((p) => p[0])
        .map(([title, milestoneKey, offset, hours]) => ({
          title: title!,
          description: null,
          milestoneKey: milestoneKey || null,
          offsetDays: Number(offset) || 0,
          estimateHours: hours ? Number(hours) : null,
          labels: [],
          clientVisible: true,
        }));
      const body = {
        ...form,
        durationDays: form.durationDays ? Number(form.durationDays) : null,
        defaultBudgetHours: form.defaultBudgetHours ? Number(form.defaultBudgetHours) : null,
        serviceLines: template?.serviceLines ?? [],
        milestones,
        tasks,
        recurring: template?.recurring ?? [],
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
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title={template ? `Edit ${template.name}` : 'New project template'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="template-form" loading={save.isPending}>
            Save template
          </Button>
        </>
      }
    >
      <form
        id="template-form"
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
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
            <Select value={form.projectType} onChange={(e) => setForm({ ...form, projectType: e.target.value as ProjectTemplate['projectType'] })} options={PROJECT_TYPES.map((t) => ({ value: t, label: labelOf(t) }))} />
          </FormField>
          <FormField label="Duration (days)">
            <Input type="number" min={1} value={form.durationDays} onChange={(e) => setForm({ ...form, durationDays: e.target.value })} />
          </FormField>
          <FormField label="Budget hours">
            <Input type="number" min={0} value={form.defaultBudgetHours} onChange={(e) => setForm({ ...form, defaultBudgetHours: e.target.value })} />
          </FormField>
        </div>
        <FormField label="Milestones" hint="One per line: key | title | day offset">
          <Textarea rows={4} value={form.milestones} onChange={(e) => setForm({ ...form, milestones: e.target.value })} />
        </FormField>
        <FormField label="Tasks" hint="One per line: title | milestone key | day offset | estimate hours">
          <Textarea rows={8} value={form.tasks} onChange={(e) => setForm({ ...form, tasks: e.target.value })} />
        </FormField>
        <Switch checked={form.isActive} onCheckedChange={(v) => setForm({ ...form, isActive: v })} label="Active" />
      </form>
    </Dialog>
  );
}

export function TemplatesPage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.ProjectsManage);
  const [tab, setTab] = useState('projects');
  const [editing, setEditing] = useState<ProjectTemplate | 'new' | null>(null);
  const projects = useQuery({
    queryKey: [...dk.templates, 'all'],
    queryFn: ({ signal }) => api.get<ProjectTemplate[]>('/agency/templates/projects', { query: { includeInactive: true }, signal }),
  });
  const briefs = useQuery({
    queryKey: ['delivery', 'brief-templates'],
    queryFn: ({ signal }) => api.get<BriefTemplate[]>('/agency/templates/briefs', { signal }),
  });
  return (
    <div className="dl-page">
      <PageHeader
        title="Templates"
        description="Service templates create a project's milestones, tasks and recurring tasks; brief templates define what clients fill in."
        actions={canManage ? <Button onClick={() => setEditing('new')}>New project template</Button> : null}
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
                          {canManage ? (
                            <Button size="sm" variant="secondary" onClick={() => setEditing(t)}>
                              Edit
                            </Button>
                          ) : null}
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
            ) : (
              <div className="dl-grid dl-grid--wide">
                {briefs.data.map((t) => (
                  <Card key={t.id} as="article" aria-label={t.name}>
                    <CardHeader title={t.name} headingLevel={2} description={t.description ?? undefined} actions={<Badge>{labelOf(t.serviceLine)}</Badge>} />
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
        ]}
      />
      {editing ? <TemplateEditor template={editing === 'new' ? null : editing} onClose={() => setEditing(null)} /> : null}
    </div>
  );
}
