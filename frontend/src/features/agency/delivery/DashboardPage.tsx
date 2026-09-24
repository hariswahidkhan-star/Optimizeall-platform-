import { useSyncExternalStore } from 'react';
import { Link } from 'react-router-dom';
import { PortalOverview } from '@/components/PortalOverview';
import {
  Badge,
  Card,
  CardBody,
  CardHeader,
  DateTime,
  EmptyState,
  ErrorState,
  PageHeader,
  Skeleton,
  Stat,
} from '@/components/ui';
import { meetsRequirement, Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { greetingFor } from '@/lib/format/dates';
import { firstName } from '@/lib/format/text';
import { dashboardTilesSnapshot, subscribeDashboardTiles } from '../shared/dashboardTiles';
import type { AgencyDashboard, DeliverableSummary, TaskSummary } from '../shared/deliveryTypes';
import { DeliverableStatusBadge, formatDateOnly, formatMinutes, HealthBadge, TaskStatusBadge } from '../shared/deliveryUi';
import { useDashboard } from './api';
import { TimerWidget } from './TimerWidget';
import './delivery.css';

function RegisteredTiles() {
  const { permissions } = useAuth();
  const tiles = useSyncExternalStore(subscribeDashboardTiles, dashboardTilesSnapshot, dashboardTilesSnapshot);
  const visible = tiles.filter((t) => !t.requires || meetsRequirement(permissions, t.requires));
  if (visible.length === 0) return null;
  return (
    <div className="dl-grid" data-testid="dashboard-tiles">
      {visible.map(({ id, title, Component }) => (
        <Card key={id} as="section" aria-label={title}>
          <CardHeader title={title} headingLevel={2} />
          <CardBody>
            <Component />
          </CardBody>
        </Card>
      ))}
    </div>
  );
}

function TaskList({ tasks }: { tasks: TaskSummary[] }) {
  if (tasks.length === 0) return <EmptyState compact title="Nothing due this week" description="Tasks assigned to you with a due date show up here." />;
  return (
    <ul className="dl-list" aria-label="My tasks due soon">
      {tasks.map((t) => (
        <li key={t.id} className="dl-list__item">
          <span className="dl-list__main">
            <Link className="dl-list__title ui-link" to={`/agency/projects/${t.projectId}?task=${t.id}`}>
              {t.title}
            </Link>
            <span className="dl-meta">
              <span>{t.clientName}</span>
              <span>{t.projectName}</span>
            </span>
          </span>
          <span className="dl-row">
            <TaskStatusBadge status={t.status} />
            {t.isOverdue ? <Badge tone="danger">Overdue · {formatDateOnly(t.dueDate)}</Badge> : <Badge>Due {formatDateOnly(t.dueDate)}</Badge>}
          </span>
        </li>
      ))}
    </ul>
  );
}

function DeliverableList({ items, label, empty }: { items: DeliverableSummary[]; label: string; empty: string }) {
  if (items.length === 0) return <EmptyState compact title={empty} />;
  return (
    <ul className="dl-list" aria-label={label}>
      {items.map((d) => (
        <li key={d.id} className="dl-list__item">
          <span className="dl-list__main">
            <Link className="dl-list__title ui-link" to={`/agency/deliverables/${d.id}`}>
              {d.title}
            </Link>
            <span className="dl-meta">
              <span>{d.clientName}</span>
              <span>v{d.currentVersion}</span>
              {d.clientDueAt ? (
                <span>
                  Due <DateTime value={d.clientDueAt} format="relative" />
                </span>
              ) : null}
            </span>
          </span>
          <span className="dl-row">
            <DeliverableStatusBadge status={d.status} />
            {d.isOverdue ? <Badge tone="danger">Feedback overdue</Badge> : null}
          </span>
        </li>
      ))}
    </ul>
  );
}

function DeliveryDashboard({ data, canTrack }: { data: AgencyDashboard; canTrack: boolean }) {
  return (
    <>
      <div className="dl-stats">
        <Stat label="Open tasks" value={data.myTaskCounts.open} measurement="Count" />
        <Stat label="Overdue" value={data.myTaskCounts.overdue} measurement="Count" />
        <Stat label="Awaiting my review" value={data.reviewQueueTotal} measurement="Count" />
        <Stat label="My time this week" value={formatMinutes(data.myMinutesThisWeek)} />
      </div>

      {data.admin ? (
        <Card as="section" aria-label="Agency snapshot">
          <CardHeader title="Agency snapshot" headingLevel={2} />
          <CardBody className="dl-page">
            <div className="dl-stats">
              <Stat label="Active clients" value={data.admin.activeClients} measurement="Count" hint={`${data.admin.onboardingClients} onboarding`} />
              <Stat label="Active projects" value={data.admin.activeProjects} measurement="Count" />
              <Stat label="Projects at risk" value={data.admin.projectsAtRisk.length} measurement="Count" />
              <Stat label="Hours this week" value={formatMinutes(data.admin.minutesThisWeek)} hint={`${formatMinutes(data.admin.billableMinutesThisWeek)} billable`} />
              <Stat label="Waiting on clients" value={data.admin.deliverablesAwaitingClients} measurement="Count" />
            </div>
            {data.admin.projectsAtRisk.length > 0 ? (
              <ul className="dl-list" aria-label="Projects at risk">
                {data.admin.projectsAtRisk.map((p) => (
                  <li key={p.id} className="dl-list__item">
                    <span className="dl-list__main">
                      <Link className="dl-list__title ui-link" to={`/agency/projects/${p.id}`}>
                        {p.name}
                      </Link>
                      <span className="dl-meta">
                        {p.clientName} · {p.overdueTasks} overdue · {p.hoursLogged}h of {p.budgetHours ?? '—'}h
                      </span>
                    </span>
                    <Badge tone="danger">At risk</Badge>
                  </li>
                ))}
              </ul>
            ) : null}
          </CardBody>
        </Card>
      ) : null}

      <div className="dl-grid dl-grid--wide">
        <Card as="section" aria-label="My tasks">
          <CardHeader title="My tasks due" headingLevel={2} actions={<Link className="ui-link" to="/agency/tasks">All my tasks</Link>} />
          <CardBody>
            <TaskList tasks={data.myTasks} />
          </CardBody>
        </Card>
        {canTrack ? (
          <Card as="section" aria-label="Timer">
            <CardHeader title="My timer" headingLevel={2} actions={<Link className="ui-link" to="/agency/time">Timesheet</Link>} />
            <CardBody>
              <TimerWidget />
            </CardBody>
          </Card>
        ) : null}
        <Card as="section" aria-label="Awaiting my review">
          <CardHeader title="Deliverables awaiting my review" headingLevel={2} />
          <CardBody>
            <DeliverableList items={data.reviewQueue} label="Deliverables awaiting my review" empty="Nothing to review" />
          </CardBody>
        </Card>
        <Card as="section" aria-label="Pending client approvals">
          <CardHeader title="Pending client approvals" headingLevel={2} />
          <CardBody>
            <DeliverableList items={data.pendingClientApprovals} label="Pending client approvals" empty="No deliverables waiting on clients" />
          </CardBody>
        </Card>
        <Card as="section" aria-label="Today's meetings">
          <CardHeader title="Today's meetings" headingLevel={2} />
          <CardBody>
            {data.todaysMeetings.length === 0 ? (
              <EmptyState compact title="No meetings today" />
            ) : (
              <ul className="dl-list" aria-label="Today's meetings">
                {data.todaysMeetings.map((m) => (
                  <li key={m.id} className="dl-list__item">
                    <span className="dl-list__main">
                      <Link className="dl-list__title ui-link" to={`/agency/clients/${m.clientId}?tab=meetings`}>
                        {m.title}
                      </Link>
                      <span className="dl-meta">
                        {m.clientName} · <DateTime value={m.startsAt} format="datetime" /> · {m.durationMinutes} min
                        {m.location ? ` · ${m.location}` : ''}
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </CardBody>
        </Card>
      </div>

      {data.accountManager ? (
        <div className="dl-grid dl-grid--wide">
          <Card as="section" aria-label="Client health">
            <CardHeader title="Client health" headingLevel={2} actions={<Link className="ui-link" to="/agency/clients">All clients</Link>} />
            <CardBody>
              <ul className="dl-list" aria-label="Client health board">
                {data.accountManager.healthBoard.map((h) => (
                  <li key={h.clientId} className="dl-list__item">
                    <span className="dl-list__main">
                      <Link className="dl-list__title ui-link" to={`/agency/clients/${h.clientId}`}>
                        {h.clientName}
                      </Link>
                      <span className="dl-meta">{h.reasons.length === 0 ? 'No issues' : h.reasons.map((r) => r.message).join(' · ')}</span>
                    </span>
                    <HealthBadge level={h.level} score={h.score} />
                  </li>
                ))}
              </ul>
            </CardBody>
          </Card>
          <Card as="section" aria-label="Overdue by client">
            <CardHeader title="Overdue by client" headingLevel={2} />
            <CardBody>
              {data.accountManager.overdueByClient.length === 0 ? (
                <EmptyState compact title="Nothing overdue" />
              ) : (
                <ul className="dl-list" aria-label="Overdue items per client">
                  {data.accountManager.overdueByClient.map((o) => (
                    <li key={o.clientId} className="dl-list__item">
                      <Link className="dl-list__title ui-link" to={`/agency/clients/${o.clientId}`}>
                        {o.clientName}
                      </Link>
                      <span className="dl-meta">
                        {o.overdueTasks} tasks · {o.overdueApprovals} approvals
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
          <Card as="section" aria-label="Utilization this week">
            <CardHeader title="Utilization this week" headingLevel={2} />
            <CardBody>
              <ul className="dl-list" aria-label="Team utilization">
                {data.accountManager.utilization.rows.slice(0, 8).map((r) => (
                  <li key={r.userId} className="dl-list__item">
                    <span className="dl-list__title">{r.userName}</span>
                    <span className="dl-meta">
                      {formatMinutes(r.totalMinutes)} · {r.utilizationPercent}% utilized · {r.billablePercent}% billable
                    </span>
                  </li>
                ))}
              </ul>
            </CardBody>
          </Card>
        </div>
      ) : null}
    </>
  );
}

/** Agency portal home: role-aware delivery dashboard plus KPI tiles registered by other areas. */
export function DashboardPage() {
  const { user, hasPermission, status } = useAuth();
  const canDelivery = hasPermission(Permissions.ProjectsView);
  const dashboard = useDashboard(canDelivery);
  const name = user ? firstName(user.displayName) : '';

  if (status === 'loading') return <Skeleton height={200} />;
  if (!canDelivery)
    return (
      <div className="dl-page">
        <PortalOverview />
        <RegisteredTiles />
      </div>
    );

  return (
    <div className="dl-page">
      <PageHeader
        eyebrow="Agency"
        title={`${greetingFor(new Date(), user?.timeZone)}${name ? `, ${name}` : ''}`}
        description="Your work, reviews and clients at a glance."
      />
      {dashboard.isPending ? (
        <div aria-busy="true" className="dl-grid">
          <Skeleton height={140} />
          <Skeleton height={140} />
          <Skeleton height={140} />
        </div>
      ) : dashboard.isError ? (
        <ErrorState error={dashboard.error} onRetry={() => void dashboard.refetch()} />
      ) : (
        <DeliveryDashboard data={dashboard.data} canTrack={hasPermission(Permissions.TimeTrack)} />
      )}
      <RegisteredTiles />
    </div>
  );
}
