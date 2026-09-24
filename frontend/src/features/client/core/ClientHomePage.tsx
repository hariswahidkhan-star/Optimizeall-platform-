import { useQuery } from '@tanstack/react-query';
import { Mail } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Avatar,
  Badge,
  Card,
  CardBody,
  CardHeader,
  DateTime,
  EmptyState,
  ErrorState,
  ProgressBar,
  Skeleton,
} from '@/components/ui';
import type { ClientHome } from '@/features/agency/shared/deliveryTypes';
import { DeliverableStatusBadge, formatDateOnly, labelOf } from '@/features/agency/shared/deliveryUi';
import { api } from '@/lib/api/client';
import { ClientShell } from './ClientShell';
import { NpsSurvey } from './FeedbackPage';
import { clientKeys } from './useClientOrg';

function Home({ base, orgId, link }: { base: string; orgId: string; link: (p: string) => string }) {
  const home = useQuery({
    queryKey: clientKeys.part(orgId, 'home'),
    queryFn: ({ signal }) => api.get<ClientHome>(`${base}/home`, { signal }),
  });
  const [now] = useState(() => Date.now());
  if (home.isPending) return <Skeleton height={300} />;
  if (home.isError) return <ErrorState error={home.error} onRetry={() => void home.refetch()} />;
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
  return (
    <>
      {h.onboarding.percentComplete < 100 || doneForYou.length > 0 ? (
        <Card as="section" aria-label="Onboarding">
          <CardHeader title="Getting started" headingLevel={2} />
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
                <ul>
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
                <ul aria-label="Steps done on your behalf">
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
      <div className="dl-grid dl-grid--wide">
        <Card as="section" aria-label="Awaiting your approval">
          <CardHeader
            title="Awaiting your approval"
            headingLevel={2}
            actions={
              <Link className="ui-link" to={link('/client/approvals')}>
                All approvals
              </Link>
            }
          />
          <CardBody>
            {h.awaitingApproval.length === 0 ? (
              <EmptyState
                compact
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
              <p className="dl-muted">
                Your role is view-only; an Approver or Owner in your organization signs off.
              </p>
            ) : null}
          </CardBody>
        </Card>
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
              <p>
                <Link className="ui-link" to={link(`/client/reports/${h.latestReport.id}`)}>
                  {h.latestReport.title}
                </Link>
                <br />
                <span className="dl-meta">
                  Published{' '}
                  {h.latestReport.publishedAt ? (
                    <DateTime value={h.latestReport.publishedAt} format="date" />
                  ) : (
                    ''
                  )}
                </span>
              </p>
            ) : (
              <EmptyState
                compact
                title="No reports yet"
                description="Your monthly performance report appears here."
              />
            )}
          </CardBody>
        </Card>
        <Card as="section" aria-label="Recent deliverables">
          <CardHeader title="Recent deliverables" headingLevel={2} />
          <CardBody>
            {h.recentDeliverables.length === 0 ? (
              <EmptyState compact title="No deliverables yet" />
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
        <Card as="section" aria-label="Upcoming meetings">
          <CardHeader title="Upcoming meetings" headingLevel={2} />
          <CardBody>
            {h.upcomingMeetings.length === 0 ? (
              <EmptyState compact title="No meetings scheduled" />
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
          <CardBody>
            {h.threads.length === 0 ? (
              <EmptyState compact title="No messages yet" />
            ) : (
              <ul className="dl-list" aria-label="Recent conversations">
                {h.threads.map((t) => (
                  <li key={t.id} className="dl-list__item">
                    <span className="dl-list__main">
                      <Link className="dl-list__title ui-link" to={link(`/client/messages?thread=${t.id}`)}>
                        {t.subject}
                      </Link>
                      {t.lastMessagePreview ? <span className="dl-muted">{t.lastMessagePreview}</span> : null}
                    </span>
                    {t.unreadCount > 0 ? <Badge tone="brand">{t.unreadCount} new</Badge> : null}
                  </li>
                ))}
              </ul>
            )}
          </CardBody>
        </Card>
        <Card as="section" aria-label="Projects">
          <CardHeader
            title="Projects"
            headingLevel={2}
            actions={
              <Link className="ui-link" to={link('/client/projects')}>
                All projects
              </Link>
            }
          />
          <CardBody>
            {h.projects.length === 0 ? (
              <EmptyState compact title="No projects yet" />
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
      </div>
      {h.nps.due ? <NpsSurvey base={base} orgId={orgId} period={h.nps.period} /> : null}
      <Card as="section" aria-label="Your account team">
        <CardHeader title="Your account team" headingLevel={2} />
        <CardBody>
          <ul className="cc-team" aria-label="Account team">
            {h.team.map((p) => (
              <li key={p.userId} className="cc-person">
                <Avatar name={p.displayName} size={48} decorative />
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
    </>
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
