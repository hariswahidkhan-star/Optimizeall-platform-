import { useState } from 'react';
import { Card, CardBody, Checkbox, EmptyState, ErrorState, PageHeader, Pagination, Skeleton } from '@/components/ui';
import { useMyTasks } from '../api/hooks';
import { ActivityItem } from '../components/ActivityPanel';
import '@/features/agency/billing/billing.css';
import '../crm.css';

/** The signed-in user's CRM tasks, earliest due first (overdue tasks are flagged; the assignee is also notified). */
export function TasksPage() {
  const [includeCompleted, setIncludeCompleted] = useState(false);
  const [page, setPage] = useState(1);
  const query = useMyTasks({ includeCompleted, page, pageSize: 50 });
  return (
    <>
      <PageHeader title="My tasks" breadcrumbs={[{ label: 'Sales CRM', to: '/agency/crm' }, { label: 'My tasks' }]} />
      <Card>
        <CardBody className="stack">
          <Checkbox label="Show completed tasks" checked={includeCompleted} onChange={(e) => { setIncludeCompleted(e.target.checked); setPage(1); }} />
          {query.isError ? (
            <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          ) : !query.data ? (
            <Skeleton height="10rem" />
          ) : query.data.items.length === 0 ? (
            <EmptyState compact headingLevel={2} title="No open tasks" description="Tasks you add on deals, contacts or companies show up here." />
          ) : (
            <>
              <ul className="crm-list" aria-label="My tasks">
                {query.data.items.map((t) => (
                  <ActivityItem key={t.id} activity={t} showLinks />
                ))}
              </ul>
              {query.data.total > 50 && <Pagination page={page} pageSize={50} total={query.data.total} onPageChange={setPage} />}
            </>
          )}
        </CardBody>
      </Card>
    </>
  );
}
