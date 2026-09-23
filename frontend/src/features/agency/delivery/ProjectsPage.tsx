import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { FolderKanban, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  DataTable,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  Input,
  PageHeader,
  Pagination,
  ProgressBar,
  Select,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import {
  PROJECT_STATUSES,
  PROJECT_TYPES,
  type ProjectDetail,
  type ProjectSummary,
  type ProjectTemplate,
} from '../shared/deliveryTypes';
import { formatDateOnly, labelOf, ProjectStatusBadge } from '../shared/deliveryUi';
import { dk, useClientOptions, useStaff } from './api';

const columns: DataTableColumn<ProjectSummary>[] = [
  {
    id: 'name',
    header: 'Project',
    primary: true,
    cell: (p) => (
      <span className="dl-list__main">
        <Link className="ui-link" to={`/agency/projects/${p.id}`}>
          {p.name}
        </Link>
        <span className="dl-meta">{p.clientName}</span>
      </span>
    ),
  },
  {
    id: 'status',
    header: 'Status',
    cell: (p) => (
      <span className="dl-row">
        <ProjectStatusBadge status={p.status} />
        {p.atRisk ? <Badge tone="danger">At risk</Badge> : null}
      </span>
    ),
  },
  { id: 'type', header: 'Type', cell: (p) => labelOf(p.type), hideOnMobile: true },
  {
    id: 'progress',
    header: 'Progress',
    cell: (p) => (
      <ProgressBar
        value={p.totalTasks === 0 ? 0 : Math.round((p.doneTasks / p.totalTasks) * 100)}
        label={`${p.name} progress`}
        hideLabel
        valueText={`${p.doneTasks} of ${p.totalTasks} tasks done`}
      />
    ),
  },
  { id: 'overdue', header: 'Overdue', align: 'right', cell: (p) => (p.overdueTasks > 0 ? <Badge tone="danger">{p.overdueTasks}</Badge> : '0') },
  { id: 'hours', header: 'Hours', align: 'right', cell: (p) => `${p.hoursLogged}${p.budgetHours ? ` / ${p.budgetHours}` : ''}`, hideOnMobile: true },
  { id: 'end', header: 'Ends', cell: (p) => formatDateOnly(p.endDate), hideOnMobile: true },
];

function NewProjectDialog({ onClose, clientId: initialClient }: { onClose: () => void; clientId?: string }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const clients = useClientOptions();
  const staff = useStaff();
  const templates = useQuery({
    queryKey: dk.templates,
    queryFn: ({ signal }) => api.get<ProjectTemplate[]>('/agency/templates/projects', { signal }),
  });
  const [form, setForm] = useState({
    clientId: initialClient ?? '',
    name: '',
    type: 'RetainerMonth',
    templateKey: '',
    startDate: '',
    budgetHours: '',
    budgetAmount: '',
    defaultHourlyRate: '',
    ownerUserId: '',
    status: 'Active',
  });
  const set = (k: keyof typeof form, v: string) => setForm((f) => ({ ...f, [k]: v }));
  const chooseTemplate = (key: string) => {
    const t = templates.data?.find((x) => x.key === key);
    setForm((f) => ({
      ...f,
      templateKey: key,
      type: t?.projectType ?? f.type,
      name: f.name || (t?.name ?? ''),
      budgetHours: f.budgetHours || (t?.defaultBudgetHours?.toString() ?? ''),
    }));
  };
  const num = (v: string) => (v.trim() === '' ? null : Number(v));
  const create = useMutation({
    mutationFn: () =>
      api.post<ProjectDetail>('/agency/projects', {
        ...form,
        templateKey: form.templateKey || null,
        startDate: form.startDate || null,
        budgetHours: num(form.budgetHours),
        budgetAmount: num(form.budgetAmount),
        defaultHourlyRate: num(form.defaultHourlyRate),
        ownerUserId: form.ownerUserId || null,
      }),
    onSuccess: (p) => {
      void qc.invalidateQueries({ queryKey: ['delivery', 'projects'] });
      onClose();
      navigate(`/agency/projects/${p.id}`);
    },
  });
  const client = clients.data?.find((c) => c.id === form.clientId);
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title="New project"
      description="Start from a service template to create its milestones, tasks and recurring tasks."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="new-project" loading={create.isPending} disabled={!form.clientId}>
            Create project
          </Button>
        </>
      }
    >
      <form
        id="new-project"
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        {create.error ? <Alert tone="danger">{errorMessage(create.error)}</Alert> : null}
        <FormField label="Client" required>
          <Select value={form.clientId} onChange={(e) => set('clientId', e.target.value)} placeholder="Choose a client…" options={(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))} />
        </FormField>
        <FormField label="Template" optional>
          <Select
            value={form.templateKey}
            onChange={(e) => chooseTemplate(e.target.value)}
            placeholder="Blank project"
            options={(templates.data ?? []).map((t) => ({ value: t.key, label: `${t.name} (${t.tasks.length} tasks)` }))}
          />
        </FormField>
        <FormField label="Name" required>
          <Input value={form.name} onChange={(e) => set('name', e.target.value)} required minLength={2} maxLength={200} />
        </FormField>
        <div className="dl-form__row">
          <FormField label="Type">
            <Select value={form.type} onChange={(e) => set('type', e.target.value)} options={PROJECT_TYPES.map((t) => ({ value: t, label: labelOf(t) }))} />
          </FormField>
          <FormField label="Status">
            <Select value={form.status} onChange={(e) => set('status', e.target.value)} options={PROJECT_STATUSES.map((s) => ({ value: s, label: labelOf(s) }))} />
          </FormField>
          <FormField label="Start date" optional>
            <Input type="date" value={form.startDate} onChange={(e) => set('startDate', e.target.value)} />
          </FormField>
        </div>
        <div className="dl-form__row">
          <FormField label="Budget hours" optional>
            <Input type="number" min={0} step="0.5" value={form.budgetHours} onChange={(e) => set('budgetHours', e.target.value)} />
          </FormField>
          <FormField label={`Budget amount${client ? ` (${client.currency})` : ''}`} optional>
            <Input type="number" min={0} step="0.01" value={form.budgetAmount} onChange={(e) => set('budgetAmount', e.target.value)} />
          </FormField>
          <FormField label="Default hourly rate" optional hint="Used when no user or role rate applies.">
            <Input type="number" min={0} step="0.01" value={form.defaultHourlyRate} onChange={(e) => set('defaultHourlyRate', e.target.value)} />
          </FormField>
        </div>
        <FormField label="Owner" optional>
          <Select value={form.ownerUserId} onChange={(e) => set('ownerUserId', e.target.value)} placeholder="Me" options={(staff.data ?? []).map((s) => ({ value: s.id, label: s.displayName }))} />
        </FormField>
      </form>
    </Dialog>
  );
}

export function ProjectsPage() {
  const { hasPermission } = useAuth();
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const creating = params.get('new') === '1';
  const query = { search, status: filters.status, member: filters.member, page, pageSize: 25 };
  const projects = useQuery({
    queryKey: dk.projects(query),
    queryFn: ({ signal }) => api.get<PagedResult<ProjectSummary>>('/agency/projects', { query, signal }),
    placeholderData: keepPreviousData,
  });
  return (
    <div className="dl-page">
      <PageHeader
        title="Projects"
        description="Retainers, campaigns and builds across every client."
        actions={
          hasPermission(Permissions.ProjectsManage) ? (
            <Button leadingIcon={<Plus aria-hidden="true" />} onClick={() => setParams({ new: '1' })}>
              New project
            </Button>
          ) : null
        }
      />
      <Card>
        <CardBody className="dl-page">
          <FilterBar
            search={search}
            onSearchChange={(s) => {
              setSearch(s);
              setPage(1);
            }}
            searchLabel="Search projects"
            filters={[
              { id: 'status', label: 'Status', options: PROJECT_STATUSES.map((s) => ({ value: s, label: labelOf(s) })) },
              { id: 'member', label: 'Team', allLabel: 'All projects', options: [{ value: 'me', label: 'My projects' }] },
            ]}
            values={filters}
            onFilterChange={(id, v) => {
              setFilters((f) => ({ ...f, [id]: v || undefined }));
              setPage(1);
            }}
            onReset={() => {
              setFilters({});
              setSearch('');
            }}
          />
          {projects.isError ? (
            <ErrorState error={projects.error} onRetry={() => void projects.refetch()} />
          ) : (
            <DataTable
              caption="Projects"
              columns={columns}
              rows={projects.data?.items ?? []}
              getRowId={(p) => p.id}
              loading={projects.isPending}
              emptyState={<EmptyState icon={<FolderKanban aria-hidden="true" />} title="No projects found" />}
            />
          )}
          {projects.data && projects.data.total > projects.data.pageSize ? (
            <Pagination page={page} pageSize={projects.data.pageSize} total={projects.data.total} onPageChange={setPage} />
          ) : null}
        </CardBody>
      </Card>
      {creating ? <NewProjectDialog clientId={params.get('clientId') ?? undefined} onClose={() => setParams({})} /> : null}
    </div>
  );
}
