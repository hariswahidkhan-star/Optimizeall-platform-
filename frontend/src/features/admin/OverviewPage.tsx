import { useQuery } from '@tanstack/react-query';
import {
  AlertTriangle,
  ArrowRight,
  BellOff,
  LifeBuoy,
  MessageSquareReply,
  ShieldCheck,
  Timer,
} from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useCurrentPortal } from '@/app/portalContext';
import { Badge } from '@/components/ui/Badge';
import { Card, CardBody } from '@/components/ui/Card';
import { StatGrid } from '@/components/ui/Dashboard';
import { PageHeader } from '@/components/ui/PageHeader';
import { Stat } from '@/components/ui/Stat';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { meetsRequirement, Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { greetingFor } from '@/lib/format/dates';
import { metricTrend, periodDelta, previousRange } from '@/lib/format/delta';
import { formatNumber } from '@/lib/format/money';
import { firstName } from '@/lib/format/text';
import type { Metric } from './api/types';
import { useAnalyticsOverview } from './analytics/AnalyticsPage';
import { MetricStat, metricsByKey } from './analytics/metrics';
import { roleLabel } from './shared/badges';
import { QueryError, useCan } from './shared/common';

function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/** Total of a paged list endpoint (asks for a single row). */
function useCount(key: string, path: string, query: Record<string, string>, enabled: boolean) {
  return useQuery({
    queryKey: ['admin', 'overview', key],
    enabled,
    queryFn: ({ signal }) =>
      api
        .get<PagedResult<unknown>>(path, { query: { ...query, page: 1, pageSize: 1 }, signal })
        .then((r) => r.total),
  });
}

function CountStat({
  label,
  count,
  to,
  icon,
  hint,
}: {
  label: string;
  count: ReturnType<typeof useCount>;
  to: string;
  icon: ReactNode;
  hint: string;
}) {
  if (count.isError) return <QueryError error={count.error} compact headingLevel={3} />;
  return (
    <div className="admin-link-stat">
      <Stat
        label={label}
        icon={icon}
        loading={count.isPending}
        value={count.data !== undefined ? formatNumber(count.data) : ''}
        measurement="Count"
        hint={hint}
      />
      <Link className="ui-link admin-link-stat__link" to={to}>
        View {label.toLowerCase()}
        <ArrowRight aria-hidden="true" />
      </Link>
    </div>
  );
}

const KPI_KEYS: { key: string; section: 'funnel' | 'posts' | 'spend'; label?: string }[] = [
  { key: 'registrations', section: 'funnel' },
  { key: 'eligibleAccounts', section: 'funnel' },
  { key: 'postsSubmitted', section: 'posts', label: 'Submissions' },
  { key: 'approvalRate', section: 'posts' },
  { key: 'spend', section: 'spend' },
];

export function OverviewPage() {
  const portal = useCurrentPortal();
  const { user, permissions } = useAuth();
  const canAnalytics = useCan(Permissions.AnalyticsView);
  const canSupport = useCan(Permissions.SupportManage);
  const canJobs = useCan(Permissions.JobsView);

  const to = isoDate(new Date());
  const from = isoDate(new Date(Date.now() - 30 * 86_400_000));
  const analytics = useAnalyticsOverview(from, to, canAnalytics);
  // The same-length window before it, for "vs previous 30 days" deltas.
  const previous = previousRange(from, to);
  const before = useAnalyticsOverview(previous.from, previous.to, canAnalytics);
  const openTickets = useCount('openTickets', '/admin/support/tickets', { status: 'Open' }, canSupport);
  const awaitingStaff = useCount(
    'awaitingStaff',
    '/admin/support/tickets',
    { status: 'AwaitingStaff' },
    canSupport,
  );
  const failedDeliveries = useCount(
    'failedDeliveries',
    '/admin/notifications/deliveries',
    { status: 'Failed' },
    canJobs,
  );
  const failedRuns = useCount('failedRuns', '/admin/jobs/runs', { status: 'Failed' }, canJobs);

  const kpis: { metric: Metric; section: (typeof KPI_KEYS)[number]['section'] }[] = analytics.data
    ? KPI_KEYS.flatMap(({ key, section, label }) =>
        metricsByKey(analytics.data[section]?.metrics, key).map((m) => ({
          metric: label ? { ...m, label } : m,
          section,
        })),
      )
    : [];

  const links = portal.nav.filter(
    (n) => n.to !== '' && (!n.requires || meetsRequirement(permissions, n.requires)),
  );
  const name = user ? firstName(user.displayName) : '';

  return (
    <div className="ui-dash">
      <PageHeader
        eyebrow={portal.label}
        title={`${greetingFor(new Date(), user?.timeZone)}${name ? `, ${name}` : ''}`}
        description="Platform health at a glance."
        meta={user?.roles.map((role) => (
          <Badge key={role} tone="brand" icon={<ShieldCheck />}>
            {roleLabel(role)}
          </Badge>
        ))}
      />

      {canAnalytics && (
        <section aria-labelledby="overview-kpis">
          <div className="ui-dash-head">
            <div>
              <h2 id="overview-kpis" className="ui-dash-head__title">
                Last 30 days
              </h2>
              <p className="ui-dash-head__description">
                Each figure is labelled Count, Measured or Estimated exactly as the analytics API reports it.
              </p>
            </div>
            <Link to="analytics">Open analytics</Link>
          </div>
          {analytics.isError ? (
            <Card flat>
              <QueryError
                error={analytics.error}
                onRetry={() => void analytics.refetch()}
                headingLevel={3}
                compact
              />
            </Card>
          ) : (
            <StatGrid strip min="200px" aria-busy={analytics.isPending || undefined}>
              {analytics.isPending
                ? KPI_KEYS.map((k) => <Stat key={k.key} label={k.label ?? '…'} value="" loading />)
                : kpis.map(({ metric: m, section }, i) => (
                    <MetricStat
                      key={`${m.key}-${m.currency ?? i}`}
                      metric={m}
                      delta={periodDelta(m, before.data?.[section]?.metrics, 'vs previous 30 days')}
                      trend={metricTrend(m.key, m.label, analytics.data?.timeseries)}
                    />
                  ))}
            </StatGrid>
          )}
        </section>
      )}

      {(canSupport || canJobs) && (
        <section aria-labelledby="overview-attention">
          <div className="ui-dash-head">
            <h2 id="overview-attention" className="ui-dash-head__title">
              Needs attention
            </h2>
          </div>
          <StatGrid min="200px">
            {canSupport && (
              <>
                <CountStat
                  label="Open tickets"
                  count={openTickets}
                  to="support?status=Open"
                  icon={<LifeBuoy />}
                  hint="New, not yet answered"
                />
                <CountStat
                  label="Awaiting staff"
                  count={awaitingStaff}
                  to="support?status=AwaitingStaff"
                  icon={<MessageSquareReply />}
                  hint="Participant replied"
                />
              </>
            )}
            {canJobs && (
              <>
                <CountStat
                  label="Failed deliveries"
                  count={failedDeliveries}
                  to="jobs?tab=deliveries"
                  icon={<BellOff />}
                  hint="Notifications that need a retry"
                />
                <CountStat
                  label="Failed job runs"
                  count={failedRuns}
                  to="jobs?tab=runs"
                  icon={failedRuns.data ? <AlertTriangle /> : <Timer />}
                  hint="All time"
                />
              </>
            )}
          </StatGrid>
        </section>
      )}

      <section aria-labelledby="overview-links">
        <div className="ui-dash-head">
          <h2 id="overview-links" className="ui-dash-head__title">
            Quick links
          </h2>
        </div>
        <ul className="ui-quicklinks">
          {links.map((item) => {
            const Icon = item.icon;
            return (
              <Card as="li" key={item.to} interactive>
                <CardBody className="ui-quicklink">
                  <span className="ui-quicklink__icon" aria-hidden="true">
                    <Icon />
                  </span>
                  <div className="ui-quicklink__text">
                    <h3 className="ui-quicklink__title">
                      <Link className="ui-card__link" to={item.to}>
                        {item.label}
                      </Link>
                    </h3>
                    {item.description && <p className="ui-quicklink__description">{item.description}</p>}
                  </div>
                  <ArrowRight aria-hidden="true" className="ui-quicklink__arrow" />
                </CardBody>
              </Card>
            );
          })}
        </ul>
      </section>
    </div>
  );
}
