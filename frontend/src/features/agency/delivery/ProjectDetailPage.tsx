import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  Money,
  PageHeader,
  ProgressBar,
  Select,
  Skeleton,
  Stat,
  Switch,
  Tabs,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatBytes } from '@/lib/format/text';
import {
  DELIVERABLE_TYPES,
  PROJECT_STATUSES,
  type DeliverableDetail,
  type DeliverableSummary,
  type DeliveryFile,
  type Milestone,
  type ProjectDetail,
  type ProjectStatus,
  type TaskSummary,
  type TimeEntry,
} from '../shared/deliveryTypes';
import {
  DeliverableStatusBadge,
  FilePreview,
  formatDateOnly,
  formatMinutes,
  labelOf,
  ProjectStatusBadge,
  TaskStatusBadge,
} from '../shared/deliveryUi';
import { dk, useStaff } from './api';
import { Kanban } from './Kanban';
import { TaskDrawer } from './TaskDrawer';

function NewTaskForm({ projectId, milestones }: { projectId: string; milestones: Milestone[] }) {
  const qc = useQueryClient();
  const staff = useStaff();
  const [title, setTitle] = useState('');
  const [assignee, setAssignee] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [milestoneId, setMilestoneId] = useState('');
  const create = useMutation({
    mutationFn: () =>
      api.post(`/agency/projects/${projectId}/tasks`, {
        title,
        dueDate: dueDate || null,
        milestoneId: milestoneId || null,
        assigneeUserIds: assignee ? [assignee] : [],
      }),
    onSuccess: () => {
      setTitle('');
      void qc.invalidateQueries({ queryKey: dk.tasks(projectId) });
    },
  });
  return (
    <form
      className="dl-toolbar"
      aria-label="Add a task"
      onSubmit={(e) => {
        e.preventDefault();
        create.mutate();
      }}
    >
      <FormField label="New task">
        <Input value={title} onChange={(e) => setTitle(e.target.value)} minLength={2} maxLength={300} />
      </FormField>
      <FormField label="Assignee" optional>
        <Select value={assignee} onChange={(e) => setAssignee(e.target.value)} placeholder="Nobody" options={(staff.data ?? []).map((s) => ({ value: s.id, label: s.displayName }))} />
      </FormField>
      <FormField label="Due" optional>
        <Input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} />
      </FormField>
      {milestones.length > 0 ? (
        <FormField label="Milestone" optional>
          <Select value={milestoneId} onChange={(e) => setMilestoneId(e.target.value)} placeholder="None" options={milestones.map((m) => ({ value: m.id, label: m.title }))} />
        </FormField>
      ) : null}
      <Button type="submit" leadingIcon={<Plus aria-hidden="true" />} disabled={title.trim().length < 2} loading={create.isPending}>
        Add task
      </Button>
      {create.error ? <Alert tone="danger">{errorMessage(create.error)}</Alert> : null}
    </form>
  );
}

function TaskList({ tasks, onOpen }: { tasks: TaskSummary[]; onOpen: (id: string) => void }) {
  return (
    <DataTable
      caption="Tasks"
      rows={tasks}
      getRowId={(t) => t.id}
      columns={[
        {
          id: 'title',
          header: 'Task',
          primary: true,
          sortable: true,
          sortValue: (t) => t.title,
          cell: (t) => (
            <button type="button" className="dl-card__title" onClick={() => onOpen(t.id)}>
              {t.title}
            </button>
          ),
        },
        { id: 'status', header: 'Status', sortable: true, sortValue: (t) => t.status, cell: (t) => <TaskStatusBadge status={t.status} /> },
        { id: 'assignees', header: 'Assignees', cell: (t) => t.assignees.map((a) => a.displayName).join(', ') || '—' },
        {
          id: 'due',
          header: 'Due',
          sortable: true,
          sortValue: (t) => t.dueDate ?? '9999',
          cell: (t) => (t.isOverdue ? <Badge tone="danger">{formatDateOnly(t.dueDate)}</Badge> : formatDateOnly(t.dueDate)),
        },
        { id: 'priority', header: 'Priority', cell: (t) => t.priority, hideOnMobile: true },
      ]}
      emptyState={<EmptyState compact title="No tasks yet" />}
    />
  );
}

function MilestonesTab({ project, canManage }: { project: ProjectDetail; canManage: boolean }) {
  const qc = useQueryClient();
  const [title, setTitle] = useState('');
  const [due, setDue] = useState('');
  const add = useMutation({
    mutationFn: () => api.post<Milestone[]>(`/agency/projects/${project.id}/milestones`, { title, dueDate: due || null, clientVisible: true }),
    onSuccess: () => {
      setTitle('');
      void qc.invalidateQueries({ queryKey: dk.project(project.id) });
    },
  });
  const toggle = useMutation({
    mutationFn: (m: Milestone) =>
      api.put(`/agency/projects/${project.id}/milestones/${m.id}`, { title: m.title, dueDate: m.dueDate, clientVisible: m.clientVisible, status: m.status === 'Done' ? 'Open' : 'Done' }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: dk.project(project.id) }),
  });
  return (
    <div className="dl-page">
      {project.milestones.length === 0 ? (
        <EmptyState compact title="No milestones" />
      ) : (
        <ul className="dl-list" aria-label="Milestones">
          {project.milestones.map((m) => (
            <li key={m.id} className="dl-list__item">
              <span className="dl-list__main">
                <span className="dl-list__title">{m.title}</span>
                <span className="dl-meta">
                  Due {formatDateOnly(m.dueDate)} · {m.doneCount}/{m.taskCount} tasks done {m.clientVisible ? '· visible to client' : ''}
                </span>
              </span>
              <span className="dl-row">
                <Badge tone={m.status === 'Done' ? 'success' : 'neutral'}>{m.status}</Badge>
                {canManage ? (
                  <Button size="sm" variant="secondary" onClick={() => toggle.mutate(m)}>
                    {m.status === 'Done' ? 'Reopen' : 'Mark done'}
                  </Button>
                ) : null}
              </span>
            </li>
          ))}
        </ul>
      )}
      {canManage ? (
        <form
          className="dl-toolbar"
          onSubmit={(e) => {
            e.preventDefault();
            add.mutate();
          }}
        >
          <FormField label="New milestone">
            <Input value={title} onChange={(e) => setTitle(e.target.value)} minLength={2} />
          </FormField>
          <FormField label="Due" optional>
            <Input type="date" value={due} onChange={(e) => setDue(e.target.value)} />
          </FormField>
          <Button type="submit" disabled={title.trim().length < 2} loading={add.isPending}>
            Add milestone
          </Button>
        </form>
      ) : null}
    </div>
  );
}

function DeliverablesTab({ project, canSubmit }: { project: ProjectDetail; canSubmit: boolean }) {
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({ title: '', type: 'Design' });
  const list = useQuery({
    queryKey: dk.deliverables({ projectId: project.id }),
    queryFn: ({ signal }) => api.get<PagedResult<DeliverableSummary>>('/agency/deliverables', { query: { projectId: project.id, pageSize: 100 }, signal }),
  });
  const create = useMutation({
    mutationFn: () => api.post<DeliverableDetail>('/agency/deliverables', { ...form, projectId: project.id }),
    onSuccess: () => {
      setOpen(false);
      setForm({ title: '', type: 'Design' });
      void qc.invalidateQueries({ queryKey: ['delivery', 'deliverables'] });
    },
  });
  return (
    <div className="dl-page">
      {canSubmit ? (
        <div>
          <Button leadingIcon={<Plus aria-hidden="true" />} onClick={() => setOpen(true)}>
            New deliverable
          </Button>
        </div>
      ) : null}
      {list.isPending ? (
        <Skeleton height={120} />
      ) : list.isError ? (
        <ErrorState error={list.error} />
      ) : list.data.items.length === 0 ? (
        <EmptyState compact title="No deliverables yet" />
      ) : (
        <ul className="dl-list" aria-label="Deliverables">
          {list.data.items.map((d) => (
            <li key={d.id} className="dl-list__item">
              <span className="dl-list__main">
                <Link className="dl-list__title ui-link" to={`/agency/deliverables/${d.id}`}>
                  {d.title}
                </Link>
                <span className="dl-meta">
                  {labelOf(d.type)} · v{d.currentVersion} · updated <DateTime value={d.updatedAt} format="relative" />
                </span>
              </span>
              <DeliverableStatusBadge status={d.status} />
            </li>
          ))}
        </ul>
      )}
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title="New deliverable"
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" form="new-deliverable" loading={create.isPending}>
              Create
            </Button>
          </>
        }
      >
        <form
          id="new-deliverable"
          className="dl-form"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          {create.error ? <Alert tone="danger">{errorMessage(create.error)}</Alert> : null}
          <FormField label="Title" required>
            <Input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} required minLength={2} />
          </FormField>
          <FormField label="Type">
            <Select value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value })} options={DELIVERABLE_TYPES.map((t) => ({ value: t, label: labelOf(t) }))} />
          </FormField>
        </form>
      </Dialog>
    </div>
  );
}

function TimeTab({ project }: { project: ProjectDetail }) {
  const { hasPermission } = useAuth();
  const canSee = hasPermission(Permissions.TimeViewAll);
  const entries = useQuery({
    queryKey: [...dk.project(project.id), 'time'],
    queryFn: ({ signal }) => api.get<TimeEntry[]>(`/agency/projects/${project.id}/time`, { signal }),
    enabled: canSee,
  });
  const b = project.budget;
  return (
    <div className="dl-page">
      <div className="dl-stats">
        <Stat label="Hours logged" value={`${b.hoursLogged}h`} hint={b.budgetHours ? `of ${b.budgetHours}h budget` : 'No hour budget'} />
        <Stat label="Billable hours" value={`${b.billableHours}h`} />
        <Stat label="Budget burned" value={<Money amount={b.amountBurned} currency={b.currency} />} hint={b.budgetAmount ? `of ${b.budgetAmount} ${b.currency}` : 'No amount budget'} />
        {b.unpricedHours > 0 ? <Stat label="Unpriced hours" value={`${b.unpricedHours}h`} hint="No user, role or project rate in this currency" /> : null}
      </div>
      {b.hoursBurnPercent !== null ? <ProgressBar value={Math.min(100, b.hoursBurnPercent)} label="Hours budget used" valueText={`${b.hoursBurnPercent}%`} showValue /> : null}
      {b.amountBurnPercent !== null ? <ProgressBar value={Math.min(100, b.amountBurnPercent)} label="Amount budget used" valueText={`${b.amountBurnPercent}%`} showValue tone="accent" /> : null}
      {canSee ? (
        entries.isPending ? (
          <Skeleton height={120} />
        ) : entries.isError ? (
          <ErrorState error={entries.error} />
        ) : (
          <DataTable
            caption="Time entries"
            rows={entries.data}
            getRowId={(e) => e.id}
            columns={[
              { id: 'date', header: 'Date', cell: (e) => formatDateOnly(e.date) },
              { id: 'who', header: 'Person', primary: true, cell: (e) => e.userName },
              { id: 'task', header: 'Task', cell: (e) => e.taskTitle ?? '—', hideOnMobile: true },
              { id: 'time', header: 'Time', align: 'right', cell: (e) => formatMinutes(e.minutes) },
              { id: 'billable', header: 'Billable', cell: (e) => (e.billable ? 'Yes' : 'No') },
            ]}
            emptyState={<EmptyState compact title="No time logged yet" />}
          />
        )
      ) : null}
    </div>
  );
}

function FilesTab({ projectId }: { projectId: string }) {
  const files = useQuery({
    queryKey: [...dk.project(projectId), 'files'],
    queryFn: ({ signal }) => api.get<{ file: DeliveryFile; source: string; deliverableId: string | null; taskId: string | null }[]>(`/agency/projects/${projectId}/files`, { signal }),
  });
  if (files.isPending) return <Skeleton height={120} />;
  if (files.isError) return <ErrorState error={files.error} />;
  if (files.data.length === 0) return <EmptyState compact title="No files yet" description="Deliverable versions and task attachments appear here." />;
  return (
    <ul className="dl-grid" aria-label="Project files">
      {files.data.map((f) => (
        <li key={f.file.id + f.source} className="dl-compare__pane">
          <strong>{f.file.fileName}</strong>
          <span className="dl-meta">
            {f.source} · {formatBytes(f.file.sizeBytes)}
          </span>
          <FilePreview file={f.file} url={f.file.staffUrl} alt={f.file.fileName} />
        </li>
      ))}
    </ul>
  );
}

function SettingsTab({ project }: { project: ProjectDetail }) {
  const qc = useQueryClient();
  const [status, setStatus] = useState<ProjectStatus>(project.status);
  const [form, setForm] = useState({
    name: project.name,
    budgetHours: project.budgetHours?.toString() ?? '',
    budgetAmount: project.budgetAmount?.toString() ?? '',
    defaultHourlyRate: project.defaultHourlyRate?.toString() ?? '',
    startDate: project.startDate ?? '',
    endDate: project.endDate ?? '',
  });
  const num = (v: string) => (v.trim() === '' ? null : Number(v));
  const save = useMutation({
    mutationFn: async () => {
      let saved = await api.put<ProjectDetail>(`/agency/projects/${project.id}`, {
        name: form.name,
        description: project.description,
        type: project.type,
        serviceLines: project.serviceLines,
        startDate: form.startDate || null,
        endDate: form.endDate || null,
        budgetHours: num(form.budgetHours),
        budgetAmount: num(form.budgetAmount),
        defaultHourlyRate: num(form.defaultHourlyRate),
        ownerUserId: project.owner?.id ?? null,
        memberUserIds: project.members.map((m) => m.id),
        concurrencyStamp: project.concurrencyStamp,
      });
      if (status !== project.status)
        saved = await api.post<ProjectDetail>(`/agency/projects/${project.id}/status`, { status, concurrencyStamp: saved.concurrencyStamp });
      return saved;
    },
    onSuccess: (p) => qc.setQueryData(dk.project(project.id), p),
  });
  return (
    <form
      className="dl-form"
      aria-label="Project settings"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate();
      }}
    >
      {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
      {save.isSuccess ? <Alert tone="success">Saved.</Alert> : null}
      <FormField label="Name" required>
        <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required minLength={2} />
      </FormField>
      <div className="dl-form__row">
        <FormField label="Status">
          <Select value={status} onChange={(e) => setStatus(e.target.value as ProjectStatus)} options={PROJECT_STATUSES.map((s) => ({ value: s, label: labelOf(s) }))} />
        </FormField>
        <FormField label="Start">
          <Input type="date" value={form.startDate} onChange={(e) => setForm({ ...form, startDate: e.target.value })} />
        </FormField>
        <FormField label="End">
          <Input type="date" value={form.endDate} onChange={(e) => setForm({ ...form, endDate: e.target.value })} />
        </FormField>
      </div>
      <div className="dl-form__row">
        <FormField label="Budget hours">
          <Input type="number" min={0} value={form.budgetHours} onChange={(e) => setForm({ ...form, budgetHours: e.target.value })} />
        </FormField>
        <FormField label={`Budget amount (${project.currency})`}>
          <Input type="number" min={0} step="0.01" value={form.budgetAmount} onChange={(e) => setForm({ ...form, budgetAmount: e.target.value })} />
        </FormField>
        <FormField label="Default hourly rate">
          <Input type="number" min={0} step="0.01" value={form.defaultHourlyRate} onChange={(e) => setForm({ ...form, defaultHourlyRate: e.target.value })} />
        </FormField>
      </div>
      <div className="dl-row">
        <Button type="submit" loading={save.isPending}>
          Save settings
        </Button>
      </div>
      <RecurringSection projectId={project.id} />
    </form>
  );
}

function RecurringSection({ projectId }: { projectId: string }) {
  const qc = useQueryClient();
  const rules = useQuery({
    queryKey: [...dk.project(projectId), 'recurring'],
    queryFn: ({ signal }) =>
      api.get<{ id: string; title: string; dayOfMonth: number; dueInDays: number; isActive: boolean; assignee: { displayName: string } | null }[]>(
        `/agency/projects/${projectId}/recurring-tasks`,
        { signal },
      ),
  });
  const [title, setTitle] = useState('');
  const [day, setDay] = useState('1');
  const add = useMutation({
    mutationFn: () => api.post(`/agency/projects/${projectId}/recurring-tasks`, { title, dayOfMonth: Number(day), dueInDays: 5, isActive: true }),
    onSuccess: () => {
      setTitle('');
      void qc.invalidateQueries({ queryKey: [...dk.project(projectId), 'recurring'] });
    },
  });
  return (
    <Card as="section" aria-label="Recurring tasks">
      <CardHeader title="Recurring tasks" headingLevel={3} description="Created automatically every month on the chosen day (once per month)." />
      <CardBody className="dl-page">
        <ul className="dl-list" aria-label="Recurring task rules">
          {(rules.data ?? []).map((r) => (
            <li key={r.id} className="dl-list__item">
              <span className="dl-list__title">{r.title}</span>
              <span className="dl-meta">
                Day {r.dayOfMonth} · due {r.dueInDays} days later {r.assignee ? `· ${r.assignee.displayName}` : ''} {r.isActive ? '' : '· paused'}
              </span>
            </li>
          ))}
        </ul>
        <div className="dl-toolbar">
          <FormField label="Recurring task">
            <Input value={title} onChange={(e) => setTitle(e.target.value)} />
          </FormField>
          <FormField label="Day of month">
            <Input type="number" min={1} max={28} value={day} onChange={(e) => setDay(e.target.value)} />
          </FormField>
          <Button type="button" variant="secondary" disabled={title.trim().length < 2} loading={add.isPending} onClick={() => add.mutate()}>
            Add recurring task
          </Button>
        </div>
      </CardBody>
    </Card>
  );
}

const TABS = ['board', 'list', 'milestones', 'deliverables', 'time', 'files', 'settings'] as const;

export function ProjectDetailPage() {
  const { projectId = '' } = useParams();
  const { hasPermission } = useAuth();
  const [params, setParams] = useSearchParams();
  const [showDone, setShowDone] = useState(true);
  const tab = (TABS as readonly string[]).includes(params.get('tab') ?? '') ? params.get('tab')! : 'board';
  const openTask = params.get('task');
  const project = useQuery({
    queryKey: dk.project(projectId),
    queryFn: ({ signal }) => api.get<ProjectDetail>(`/agency/projects/${projectId}`, { signal }),
  });
  const tasks = useQuery({
    queryKey: dk.tasks(projectId),
    queryFn: ({ signal }) => api.get<TaskSummary[]>(`/agency/projects/${projectId}/tasks`, { signal }),
  });
  const setParam = (key: string, value: string | null) =>
    setParams(
      (p) => {
        const next = new URLSearchParams(p);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      },
      { replace: true },
    );
  if (project.isPending) return <Skeleton height={240} />;
  if (project.isError) return <ErrorState error={project.error} onRetry={() => void project.refetch()} />;
  const p = project.data;
  const canSubmit = hasPermission(Permissions.DeliverablesSubmit);
  const canManage = hasPermission(Permissions.ProjectsManage);
  const visibleTasks = (tasks.data ?? []).filter((t) => showDone || t.status !== 'Done');
  const taskArea = (content: ReactNode) =>
    tasks.isPending ? <Skeleton height={200} /> : tasks.isError ? <ErrorState error={tasks.error} /> : content;
  return (
    <div className="dl-page">
      <PageHeader
        title={p.name}
        breadcrumbs={[{ label: 'Projects', to: '/agency/projects' }, { label: p.clientName, to: `/agency/clients/${p.clientId}` }, { label: p.name }]}
        meta={
          <span className="dl-row">
            <ProjectStatusBadge status={p.status} />
            <Badge>{labelOf(p.type)}</Badge>
            <span className="dl-meta">
              {formatDateOnly(p.startDate)} – {formatDateOnly(p.endDate)} · owner {p.owner?.displayName ?? '—'}
            </span>
          </span>
        }
      />
      {canSubmit && (tab === 'board' || tab === 'list') ? <NewTaskForm projectId={p.id} milestones={p.milestones} /> : null}
      <Tabs
        label="Project sections"
        value={tab}
        onValueChange={(id) => setParam('tab', id)}
        tabs={[
          {
            id: 'board',
            label: 'Board',
            content: taskArea(<Kanban projectId={p.id} tasks={visibleTasks} canEdit={canSubmit} onOpen={(id) => setParam('task', id)} />),
          },
          {
            id: 'list',
            label: 'List',
            content: taskArea(
              <div className="dl-page">
                <Switch checked={showDone} onCheckedChange={setShowDone} label="Show done tasks" />
                <TaskList tasks={visibleTasks} onOpen={(id) => setParam('task', id)} />
              </div>,
            ),
          },
          { id: 'milestones', label: 'Milestones', content: <MilestonesTab project={p} canManage={canManage} /> },
          { id: 'deliverables', label: 'Deliverables', content: <DeliverablesTab project={p} canSubmit={canSubmit} /> },
          { id: 'time', label: 'Time & budget', content: <TimeTab project={p} /> },
          { id: 'files', label: 'Files', content: <FilesTab projectId={p.id} /> },
          ...(canManage ? [{ id: 'settings', label: 'Settings', content: <SettingsTab key={p.concurrencyStamp} project={p} /> }] : []),
        ]}
      />
      {openTask ? <TaskDrawer taskId={openTask} projectId={p.id} clientId={p.clientId} onClose={() => setParam('task', null)} /> : null}
    </div>
  );
}
