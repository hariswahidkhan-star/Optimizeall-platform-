import {
  AlarmClock,
  Briefcase,
  CalendarClock,
  ClipboardCheck,
  Clock,
  FolderKanban,
  Hourglass,
  ListTodo,
  TriangleAlert,
  Users,
} from 'lucide-react';
import { useSyncExternalStore } from 'react';
import { Link } from 'react-router-dom';
import { PortalOverview } from '@/components/PortalOverview';
import {
  Badge,
  Card,
  CardBody,
  CardHeader,
  DashboardCell,
  DashboardGrid,
  DateTime,
  EmptyState,
  ErrorState,
  MeterList,
  PageHeader,
  Skeleton,
  Stat,
  StatGrid,
} from '@/components/ui';
import { meetsRequirement, Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { greetingFor } from '@/lib/format/dates';
import { firstName } from '@/lib/format/text';
import { dashboardTilesSnapshot, subscribeDashboardTiles } from '../shared/dashboardTiles';
import type { AgencyDashboard, DeliverableSummary, TaskSummary } from '../shared/deliveryTypes';
import {
  DeliverableStatusBadge,
  formatDateOnly,
  formatMinutes,
  HealthBadge,
  TaskStatusBadge,
} from '../shared/deliveryUi';
import { useDashboard } from './api';
import { TimerWidget } from './TimerWidget';
import '../shared/dashboard.css';
import './delivery.css';

function RegisteredTiles() {
  const { permissions } = useAuth();
  const tiles = useSyncExternalStore(subscribeDashboardTiles, dashboardTilesSnapshot, dashboardTilesSnapshot);
  const visible = tiles.filter((t) => !t.requires || meetsRequirement(permissions, t.requires));
  if (visible.length === 0) return null;
  return (
    <div className="dl-grid dl-dash-tiles" data-testid="dashboard-tiles">
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
  if (tasks.length === 0)
    return (
      <EmptyState
        compact
        icon={<ListTodo />}
        title="Nothing due this week"
        description="Tasks assigned to you with a due date show up here."
        action={
          <Link className="ui-link text-small" to="/agency/tasks">
            Open my tasks
          </Link>
        }
      />
    );
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
            {t.isOverdue ? (
              <Badge tone="danger">Overdue · {formatDateOnly(t.dueDate)}</Badge>
            ) : (
              <Badge>Due {formatDateOnly(t.dueDate)}</Badge>
            )}
          </span>
        </li>
      ))}
    </ul>
  );
}

function DeliverableList({
  items,
  label,
  empty,
  description,
}: {
  items: DeliverableSummary[];
  label: string;
  empty: string;
  description?: string;
}) {
  if (items.length === 0)
    return <EmptyState compact icon={<ClipboardCheck />} title={empty} description={description} />;
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
  const utilization = data.accountManager?.utilization.rows.slice(0, 8) ?? [];
  return (
    <>
      <section aria-labelledby="dl-my-work" className="dl-dash-section">
        <div className="ui-dash-head">
          <h2 id="dl-my-work" className="ui-dash-head__title">
            My work
          </h2>
        </div>
        <StatGrid strip min="180px">
          <Stat
            label="Open tasks"
            icon={<ListTodo />}
            value={data.myTaskCounts.open}
            measurement="Count"
            hint={data.myTaskCounts.dueToday > 0 ? `${data.myTaskCounts.dueToday} due today` : undefined}
          />
          <Stat label="Overdue" icon={<AlarmClock />} value={data.myTaskCounts.overdue} measurement="Count" />
          <Stat
            label="Awaiting my review"
            icon={<ClipboardCheck />}
            value={data.reviewQueueTotal}
            measurement="Count"
          />
          <Stat label="My time this week" icon={<Clock />} value={formatMinutes(data.myMinutesThisWeek)} />
        </StatGrid>
      </section>

      <DashboardGrid>
        <DashboardCell span={canTrack ? 7 : 12}>
          <Card as="section" aria-label="My tasks">
            <CardHeader
              title="My tasks due"
              description="Assigned to you and due this week."
              headingLevel={2}
              actions={
                <Link className="ui-link" to="/agency/tasks">
                  All my tasks
                </Link>
              }
            />
            <CardBody flush>
              <TaskList tasks={data.myTasks} />
            </CardBody>
          </Card>
        </DashboardCell>
        {canTrack ? (
          <DashboardCell span={5}>
            <Card as="section" aria-label="Timer">
              <CardHeader
                title="My timer"
                headingLevel={2}
                actions={
                  <Link className="ui-link" to="/agency/time">
                    Timesheet
                  </Link>
                }
              />
              <CardBody>
                <TimerWidget />
              </CardBody>
            </Card>
          </DashboardCell>
        ) : null}
        <DashboardCell span={6}>
          <Card as="section" aria-label="Awaiting my review">
            <CardHeader
              title="Deliverables awaiting my review"
              description="Internal review before anything goes to a client."
              headingLevel={2}
            />
            <CardBody flush>
              <DeliverableList
                items={data.reviewQueue}
                label="Deliverables awaiting my review"
                empty="Nothing to review"
                description="Work submitted for your review appears here."
              />
            </CardBody>
          </Card>
        </DashboardCell>
        <DashboardCell span={6}>
          <Card as="section" aria-label="Pending client approvals">
            <CardHeader
              title="Pending client approvals"
              description="Sent to clients and waiting on their sign-off."
              headingLevel={2}
            />
            <CardBody flush>
              <DeliverableList
                items={data.pendingClientApprovals}
                label="Pending client approvals"
                empty="No deliverables waiting on clients"
              />
            </CardBody>
          </Card>
        </DashboardCell>
        <DashboardCell span={12}>
          <Card as="section" aria-label="Today's meetings">
            <CardHeader title="Today's meetings" headingLevel={2} />
            <CardBody flush>
              {data.todaysMeetings.length === 0 ? (
                <EmptyState compact icon={<CalendarClock />} title="No meetings today" />
              ) : (
                <ul className="dl-list" aria-label="Today's meetings">
                  {data.todaysMeetings.map((m) => (
                    <li key={m.id} className="dl-list__item">
                      <span className="dl-list__main">
                        <Link className="dl-list__title ui-link" to={`/agency/clients/${m.clientId}?tab=meetings`}>
                          {m.title}
                        </Link>
                        <span className="dl-meta">
                          {m.clientName} · <DateTime value={m.startsAt} format="datetime" /> · {m.durationMinutes}{' '}
                          min
                          {m.location ? ` · ${m.location}` : ''}
                        </span>
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </DashboardCell>
      </DashboardGrid>

      {data.accountManager ? (
        <section aria-labelledby="dl-accounts" className="dl-dash-section">
          <div className="ui-dash-head">
            <h2 id="dl-accounts" className="ui-dash-head__title">
              Accounts
            </h2>
            <p className="ui-dash-head__description">The clients you manage and your team&apos;s week.</p>
          </div>
          <DashboardGrid>
            <DashboardCell span={7}>
              <Card as="section" aria-label="Client health">
                <CardHeader
                  title="Client health"
                  description="Score from overdue work, waiting approvals and satisfaction."
                  headingLevel={3}
                  actions={
                    <Link className="ui-link" to="/agency/clients">
                      All clients
                    </Link>
                  }
                />
                <CardBody flush>
                  <ul className="dl-list" aria-label="Client health board">
                    {data.accountManager.healthBoard.map((h) => (
                      <li key={h.clientId} className="dl-list__item">
                        <span className="dl-list__main">
                          <Link className="dl-list__title ui-link" to={`/agency/clients/${h.clientId}`}>
                            {h.clientName}
                          </Link>
                          <span className="dl-meta">
                            {h.reasons.length === 0 ? 'No issues' : h.reasons.map((r) => r.message).join(' · ')}
                          </span>
                        </span>
                        <HealthBadge level={h.level} score={h.score} />
                      </li>
                    ))}
                  </ul>
                </CardBody>
              </Card>
            </DashboardCell>
            <DashboardCell span={5}>
              <Card as="section" aria-label="Overdue by client">
                <CardHeader title="Overdue by client" headingLevel={3} />
                <CardBody flush>
                  {data.accountManager.overdueByClient.length === 0 ? (
                    <EmptyState compact icon={<Users />} title="Nothing overdue" />
                  ) : (
                    <ul className="dl-list" aria-label="Overdue items per client">
                      {data.accountManager.overdueByClient.map((o) => (
                        <li key={o.clientId} className="dl-list__item">
                          <Link className="dl-list__title ui-link" to={`/agency/clients/${o.clientId}`}>
                            {o.clientName}
                          </Link>
                          <span className="dl-meta tabular">
                            {o.overdueTasks} tasks · {o.overdueApprovals} approvals
                          </span>
                        </li>
                      ))}
                    </ul>
                  )}
                </CardBody>
              </Card>
              <Card as="section" aria-label="Utilization this week">
                <CardHeader
                  title="Utilization this week"
                  description="Logged time against capacity, with the billable share."
                  headingLevel={3}
                />
                <CardBody>
                  {utilization.length === 0 ? (
                    <EmptyState compact icon={<Clock />} title="No time logged this week" />
                  ) : (
                    <MeterList
                      label="Team utilization"
                      items={utilization.map((r) => ({
                        id: r.userId,
                        label: r.userName,
                        percent: r.utilizationPercent,
                        valueText: `${formatMinutes(r.totalMinutes)} · ${r.utilizationPercent}% utilized`,
                        meta: `${r.billablePercent}% billable`,
                      }))}
                    />
                  )}
                </CardBody>
              </Card>
            </DashboardCell>
          </DashboardGrid>
        </section>
      ) : null}

      {data.admin ? (
        <Card as="section" aria-label="Agency snapshot">
          <CardHeader
            title="Agency snapshot"
            description="Across every client and project this week."
            headingLevel={2}
          />
          <CardBody className="dl-page">
            <StatGrid strip min="170px">
              <Stat
                label="Active clients"
                icon={<Briefcase />}
                value={data.admin.activeClients}
                measurement="Count"
                hint={`${data.admin.onboardingClients} onboarding`}
              />
              <Stat
                label="Active projects"
                icon={<FolderKanban />}
                value={data.admin.activeProjects}
                measurement="Count"
              />
              <Stat
                label="Projects at risk"
                icon={<TriangleAlert />}
                value={data.admin.projectsAtRisk.length}
                measurement="Count"
              />
              <Stat
                label="Hours this week"
                icon={<Clock />}
                value={formatMinutes(data.admin.minutesThisWeek)}
                hint={`${formatMinutes(data.admin.billableMinutesThisWeek)} billable`}
              />
              <Stat
                label="Waiting on clients"
                icon={<Hourglass />}
                value={data.admin.deliverablesAwaitingClients}
                measurement="Count"
              />
            </StatGrid>
            {data.admin.projectsAtRisk.length > 0 ? (
              <ul className="dl-list dl-list--boxed" aria-label="Projects at risk">
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
    </>
  );
}

function DashboardSkeleton() {
  return (
    <div aria-busy="true" className="dl-dash-skeleton">
      <span className="visually-hidden" role="status">
        Loading your dashboard…
      </span>
      <Skeleton height={104} radius="var(--radius-xl)" />
      <div className="dl-dash-skeleton__row">
        <Skeleton height={260} radius="var(--radius-xl)" />
        <Skeleton height={260} radius="var(--radius-xl)" />
      </div>
    </div>
  );
}

/** Agency portal home: role-aware delivery dashboard plus KPI tiles registered by other areas. */
export function DashboardPage() {
  const { user, hasPermission, status } = useAuth();
  const canDelivery = hasPermission(Permissions.ProjectsView);
  const dashboard = useDashboard(canDelivery);
  const name = user ? firstName(user.displayName) : '';

  if (status === 'loading') return <DashboardSkeleton />;
  if (!canDelivery)
    return (
      <div className="dl-page">
        <PortalOverview />
        <RegisteredTiles />
      </div>
    );

  return (
    <div className="ui-dash dl-dash">
      <PageHeader
        eyebrow="Agency"
        title={`${greetingFor(new Date(), user?.timeZone)}${name ? `, ${name}` : ''}`}
        description="Your work, reviews and clients at a glance."
      />
      {dashboard.isPending ? (
        <DashboardSkeleton />
      ) : dashboard.isError ? (
        <Card flat>
          <ErrorState error={dashboard.error} onRetry={() => void dashboard.refetch()} />
        </Card>
      ) : (
        <DeliveryDashboard data={dashboard.data} canTrack={hasPermission(Permissions.TimeTrack)} />
      )}
      <RegisteredTiles />
    </div>
  );
}
