import { useQuery } from '@tanstack/react-query';
import {
  CalendarDays,
  FileBarChart,
  FileCheck2,
  FolderKanban,
  Mail,
  MessagesSquare,
  Package,
} from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Avatar,
  Badge,
  Card,
  CardBody,
  CardHeader,
  DashboardCell,
  DashboardGrid,
  DateTime,
  EmptyState,
  ErrorState,
  ProgressBar,
  Skeleton,
  Stat,
  StatGrid,
} from '@/components/ui';
import type { ClientHome } from '@/features/agency/shared/deliveryTypes';
import { DeliverableStatusBadge, formatDateOnly, labelOf } from '@/features/agency/shared/deliveryUi';
import { api } from '@/lib/api/client';
import { ClientShell } from './ClientShell';
import { NpsSurvey } from './FeedbackPage';
import { clientKeys } from './useClientOrg';
import '@/features/agency/shared/dashboard.css';

function HomeSkeleton() {
  return (
    <div aria-busy="true" className="dl-dash-skeleton">
      <span className="visually-hidden" role="status">
        Loading your home page…
      </span>
      <Skeleton height={104} radius="var(--radius-xl)" />
      <div className="dl-dash-skeleton__row">
        <Skeleton height={280} radius="var(--radius-xl)" />
        <Skeleton height={280} radius="var(--radius-xl)" />
      </div>
    </div>
  );
}

function Home({ base, orgId, link }: { base: string; orgId: string; link: (p: string) => string }) {
  const home = useQuery({
    queryKey: clientKeys.part(orgId, 'home'),
    queryFn: ({ signal }) => api.get<ClientHome>(`${base}/home`, { signal }),
  });
  const [now] = useState(() => Date.now());
  if (home.isPending) return <HomeSkeleton />;
  if (home.isError)
    return (
      <Card flat>
        <ErrorState error={home.error} onRetry={() => void home.refetch()} />
      </Card>
    );
  const h = home.data;
  const pendingClientSteps = h.onboarding.items.filter((i) => i.owner === 'Client' && i.status === 'Pending');
  // Client steps the agency ticked off for the client (e.g. confirmed on a call) — always named as such.
  // Listed for 30 days after completion.
  const since = now - 30 * 86_400_000;
  const doneForYou = h.onboarding.items.filter(
    (i) =>
      i.completedOnBehalfOfClient &&
      i.status === 'Done' &&
      i.completedAt !== null &&
      new Date(i.completedAt).getTime() >= since,
  );
  const unread = h.threads.reduce((sum, t) => sum + t.unreadCount, 0);
  const overdue = h.awaitingApproval.filter((d) => d.isOverdue).length;

  return (
    <div className="ui-dash dl-dash">
      {h.onboarding.percentComplete < 100 || doneForYou.length > 0 ? (
        <Card as="section" aria-label="Onboarding">
          <CardHeader
            title="Getting started"
            description="A few steps so your agency team can get going."
            headingLevel={2}
          />
          <CardBody className="dl-page">
            <ProgressBar
              value={h.onboarding.percentComplete}
              label="Onboarding progress"
              valueText={`${h.onboarding.done} of ${h.onboarding.total} steps done`}
              showValue
            />
            {pendingClientSteps.length > 0 ? (
              <>
                <p>Steps waiting on you:</p>
                <ul className="cc-steps">
                  {pendingClientSteps.map((i) => (
                    <li key={i.id}>
                      <strong>{i.title}</strong>
                      {i.description ? ` — ${i.description}` : ''}
                    </li>
                  ))}
                </ul>
              </>
            ) : null}
            {doneForYou.length > 0 ? (
              <>
                <p>Done on your behalf by your agency team:</p>
                <ul className="cc-steps" aria-label="Steps done on your behalf">
                  {doneForYou.map((i) => (
                    <li key={i.id}>
                      <strong>{i.title}</strong>
                      {' — marked done'}
                      {i.completedBy ? ` by ${i.completedBy}` : ''}
                      {i.completedAt ? (
                        <>
                          {' on '}
                          <DateTime value={i.completedAt} format="date" />
                        </>
                      ) : null}
                    </li>
                  ))}
                </ul>
              </>
            ) : null}
          </CardBody>
        </Card>
      ) : null}

      <section aria-labelledby="cc-glance">
        <h2 id="cc-glance" className="visually-hidden">
          At a glance
        </h2>
        <StatGrid strip min="170px">
          <Stat
            label="To approve"
            icon={<FileCheck2 />}
            value={h.awaitingApproval.length}
            measurement="Count"
            hint={overdue > 0 ? `${overdue} past the review date` : undefined}
          />
          <Stat label="Projects" icon={<FolderKanban />} value={h.projects.length} measurement="Count" />
          <Stat label="Unread messages" icon={<MessagesSquare />} value={unread} measurement="Count" />
          <Stat
            label="Meetings ahead"
            icon={<CalendarDays />}
            value={h.upcomingMeetings.length}
            measurement="Count"
          />
        </StatGrid>
      </section>

      <DashboardGrid>
        <DashboardCell span={7}>
          <Card as="section" aria-label="Awaiting your approval">
            <CardHeader
              title="Awaiting your approval"
              description="Work your agency team has sent for your sign-off."
              headingLevel={2}
              actions={
                <Link className="ui-link" to={link('/client/approvals')}>
                  All approvals
                </Link>
              }
            />
            <CardBody flush>
              {h.awaitingApproval.length === 0 ? (
                <EmptyState
                  compact
                  icon={<FileCheck2 />}
                  title="You're all caught up"
                  description="Nothing is waiting for your review."
                />
              ) : (
                <ul className="dl-list" aria-label="Deliverables awaiting your approval">
                  {h.awaitingApproval.map((d) => (
                    <li key={d.id} className="dl-list__item">
                      <span className="dl-list__main">
                        <Link className="dl-list__title ui-link" to={link(`/client/approvals/${d.id}`)}>
                          {d.title}
                        </Link>
                        <span className="dl-meta">
                          {labelOf(d.type)} · version {d.currentVersion}
                          {d.clientDueAt ? (
                            <>
                              {' · please review by '}
                              <DateTime value={d.clientDueAt} format="date" />
                            </>
                          ) : null}
                        </span>
                      </span>
                      {d.isOverdue ? (
                        <Badge tone="danger">Overdue</Badge>
                      ) : (
                        <DeliverableStatusBadge status={d.status} audience="client" />
                      )}
                    </li>
                  ))}
                </ul>
              )}
              {!h.canApprove && h.awaitingApproval.length > 0 ? (
                <p className="dl-muted cc-note">
                  Your role is view-only; an Approver or Owner in your organization signs off.
                </p>
              ) : null}
            </CardBody>
          </Card>
        </DashboardCell>
        <DashboardCell span={5}>
          <Card as="section" aria-label="Latest report">
            <CardHeader
              title="Latest report"
              headingLevel={2}
              actions={
                <Link className="ui-link" to={link('/client/reports')}>
                  All reports
                </Link>
              }
            />
            <CardBody>
              {h.latestReport ? (
                <div className="cc-report">
                  <span className="cc-report__icon" aria-hidden="true">
                    <FileBarChart />
                  </span>
                  <p className="cc-report__text">
                    <Link
                      className="ui-link cc-report__title"
                      to={link(`/client/reports/${h.latestReport.id}`)}
                    >
                      {h.latestReport.title}
                    </Link>
                    <span className="dl-meta">
                      Published{' '}
                      {h.latestReport.publishedAt ? (
                        <DateTime value={h.latestReport.publishedAt} format="date" />
                      ) : (
                        ''
                      )}
                    </span>
                  </p>
                </div>
              ) : (
                <EmptyState
                  compact
                  icon={<FileBarChart />}
                  title="No reports yet"
                  description="Your monthly performance report appears here."
                />
              )}
            </CardBody>
          </Card>
          <Card as="section" aria-label="Upcoming meetings">
            <CardHeader title="Upcoming meetings" headingLevel={2} />
            <CardBody flush>
              {h.upcomingMeetings.length === 0 ? (
                <EmptyState compact icon={<CalendarDays />} title="No meetings scheduled" />
              ) : (
                <ul className="dl-list" aria-label="Upcoming meetings">
                  {h.upcomingMeetings.map((m) => (
                    <li key={m.id} className="dl-list__item">
                      <span className="dl-list__main">
                        <span className="dl-list__title">{m.title}</span>
                        <span className="dl-meta">
                          <DateTime value={m.startsAt} format="both" />
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

        <DashboardCell span={7}>
          <Card as="section" aria-label="Projects">
            <CardHeader
              title="Projects"
              description="Progress and the next milestone of each project."
              headingLevel={2}
              actions={
                <Link className="ui-link" to={link('/client/projects')}>
                  All projects
                </Link>
              }
            />
            <CardBody flush>
              {h.projects.length === 0 ? (
                <EmptyState compact icon={<FolderKanban />} title="No projects yet" />
              ) : (
                <ul className="dl-list" aria-label="Projects">
                  {h.projects.slice(0, 5).map((p) => (
                    <li key={p.id} className="dl-list__item">
                      <span className="dl-list__main">
                        <Link className="dl-list__title ui-link" to={link(`/client/projects/${p.id}`)}>
                          {p.name}
                        </Link>
                        <ProgressBar
                          value={p.progressPercent}
                          label={`${p.name} progress`}
                          valueText={`${p.progressPercent}%`}
                          showValue
                        />
                        {p.nextMilestone ? (
                          <span className="dl-meta">
                            Next: {p.nextMilestone.title} · {formatDateOnly(p.nextMilestone.dueDate)}
                          </span>
                        ) : null}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </DashboardCell>
        <DashboardCell span={5}>
          <Card as="section" aria-label="Messages">
            <CardHeader
              title="Messages"
              headingLevel={2}
              actions={
                <Link className="ui-link" to={link('/client/messages')}>
                  Open messages
                </Link>
              }
            />
            <CardBody flush>
              {h.threads.length === 0 ? (
                <EmptyState compact icon={<MessagesSquare />} title="No messages yet" />
              ) : (
                <ul className="dl-list" aria-label="Recent conversations">
                  {h.threads.map((t) => (
                    <li key={t.id} className="dl-list__item">
                      <span className="dl-list__main">
                        <Link className="dl-list__title ui-link" to={link(`/client/messages?thread=${t.id}`)}>
                          {t.subject}
                        </Link>
                        {t.lastMessagePreview ? (
                          <span className="dl-muted cc-preview">{t.lastMessagePreview}</span>
                        ) : null}
                      </span>
                      {t.unreadCount > 0 ? <Badge tone="brand">{t.unreadCount} new</Badge> : null}
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </DashboardCell>
      </DashboardGrid>

      {h.nps.due ? <NpsSurvey base={base} orgId={orgId} period={h.nps.period} /> : null}

      <DashboardGrid className="ui-dash-grid--start">
        <DashboardCell span={7}>
          <Card as="section" aria-label="Recent deliverables">
            <CardHeader title="Recent deliverables" headingLevel={2} />
            <CardBody flush>
              {h.recentDeliverables.length === 0 ? (
                <EmptyState compact icon={<Package />} title="No deliverables yet" />
              ) : (
                <ul className="dl-list" aria-label="Recent deliverables">
                  {h.recentDeliverables.map((d) => (
                    <li key={d.id} className="dl-list__item">
                      <Link className="dl-list__title ui-link" to={link(`/client/approvals/${d.id}`)}>
                        {d.title}
                      </Link>
                      <DeliverableStatusBadge status={d.status} audience="client" />
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </DashboardCell>
        <DashboardCell span={5}>
          <Card as="section" aria-label="Your account team">
            <CardHeader title="Your account team" headingLevel={2} />
            <CardBody>
              <ul className="cc-team" aria-label="Account team">
                {h.team.map((p) => (
                  <li key={p.userId} className="cc-person">
                    <Avatar name={p.displayName} size={36} decorative />
                    <span className="cc-person__text">
                      <strong>{p.displayName}</strong>
                      <span className="dl-meta">
                        {p.isAccountManager ? 'Account manager' : p.roles.map(labelOf).join(', ')}
                      </span>
                      <a className="ui-link" href={`mailto:${p.email}`}>
                        <Mail aria-hidden="true" size={14} /> {p.email}
                      </a>
                    </span>
                  </li>
                ))}
              </ul>
            </CardBody>
          </Card>
        </DashboardCell>
      </DashboardGrid>
    </div>
  );
}

/** Client portal home. */
export function ClientHomePage() {
  return (
    <ClientShell title="Welcome back" description="Approvals, progress and reports from your agency team.">
      {({ base, org, link }) => <Home key={org.clientId} base={base} orgId={org.clientId} link={link} />}
    </ClientShell>
  );
}
