import { useQuery } from '@tanstack/react-query';
import { ListTodo } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Badge, Card, CardBody, DataTable, EmptyState, ErrorState, PageHeader, Tabs } from '@/components/ui';
import { api } from '@/lib/api/client';
import type { TaskSummary } from '../shared/deliveryTypes';
import { formatDateOnly, TaskStatusBadge } from '../shared/deliveryUi';
import { dk } from './api';

const FILTERS = [
  { id: 'open', label: 'Open' },
  { id: 'today', label: 'Due today' },
  { id: 'week', label: 'This week' },
  { id: 'overdue', label: 'Overdue' },
  { id: 'done', label: 'Done' },
];

function TaskTable({ filter }: { filter: string }) {
  const tasks = useQuery({
    queryKey: dk.myTasks(filter),
    queryFn: ({ signal }) => api.get<TaskSummary[]>('/agency/tasks/mine', { query: { filter }, signal }),
  });
  if (tasks.isError) return <ErrorState error={tasks.error} onRetry={() => void tasks.refetch()} />;
  return (
    <DataTable
      caption="My tasks"
      rows={tasks.data ?? []}
      loading={tasks.isPending}
      getRowId={(t) => t.id}
      columns={[
        {
          id: 'title',
          header: 'Task',
          primary: true,
          cell: (t) => (
            <span className="dl-list__main">
              <Link className="ui-link" to={`/agency/projects/${t.projectId}?task=${t.id}`}>
                {t.title}
              </Link>
              <span className="dl-meta">
                {t.clientName} · {t.projectName}
              </span>
            </span>
          ),
        },
        { id: 'status', header: 'Status', cell: (t) => <TaskStatusBadge status={t.status} /> },
        { id: 'priority', header: 'Priority', cell: (t) => t.priority, hideOnMobile: true },
        {
          id: 'due',
          header: 'Due',
          cell: (t) => (t.isOverdue ? <Badge tone="danger">{formatDateOnly(t.dueDate)}</Badge> : formatDateOnly(t.dueDate)),
        },
      ]}
      emptyState={<EmptyState icon={<ListTodo aria-hidden="true" />} title="No tasks here" />}
    />
  );
}

export function MyTasksPage() {
  const [filter, setFilter] = useState('open');
  return (
    <div className="dl-page">
      <PageHeader title="My tasks" description="Everything assigned to you across clients and projects." />
      <Card>
        <CardBody>
          <Tabs
            label="Task filters"
            value={filter}
            onValueChange={setFilter}
            tabs={FILTERS.map((f) => ({ id: f.id, label: f.label, content: filter === f.id ? <TaskTable filter={f.id} /> : null }))}
          />
        </CardBody>
      </Card>
    </div>
  );
}
