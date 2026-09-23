import { useQuery } from '@tanstack/react-query';
import { AlertTriangle, ArrowRight, BellOff, LifeBuoy, ShieldCheck, Timer } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useCurrentPortal } from '@/app/portalContext';
import { Badge } from '@/components/ui/Badge';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { PageHeader } from '@/components/ui/PageHeader';
import { Stat } from '@/components/ui/Stat';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { meetsRequirement, Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { greetingFor } from '@/lib/format/dates';
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

  const kpis: Metric[] = analytics.data
    ? KPI_KEYS.flatMap(({ key, section, label }) =>
        metricsByKey(analytics.data[section]?.metrics, key).map((m) => (label ? { ...m, label } : m)),
      )
    : [];

  const links = portal.nav.filter(
    (n) => n.to !== '' && (!n.requires || meetsRequirement(permissions, n.requires)),
  );
  const name = user ? firstName(user.displayName) : '';

  return (
    <>
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
        <Card as="section" aria-labelledby="overview-kpis">
          <CardHeader
            titleId="overview-kpis"
            title="Last 30 days"
            description="Each figure is labelled Count, Measured or Estimated exactly as the analytics API reports it."
            actions={
              <Link className="ui-link" to="analytics">
                Open analytics
              </Link>
            }
          />
          <CardBody>
            {analytics.isError ? (
              <QueryError
                error={analytics.error}
                onRetry={() => void analytics.refetch()}
                headingLevel={3}
                compact
              />
            ) : (
              <div className="grid-auto admin-stat-grid" aria-busy={analytics.isPending || undefined}>
                {analytics.isPending
                  ? KPI_KEYS.map((k) => <Stat key={k.key} label={k.label ?? '…'} value="" loading />)
                  : kpis.map((m, i) => <MetricStat key={`${m.key}-${m.currency ?? i}`} metric={m} />)}
              </div>
            )}
          </CardBody>
        </Card>
      )}

      {(canSupport || canJobs) && (
        <Card as="section" aria-labelledby="overview-attention">
          <CardHeader titleId="overview-attention" title="Needs attention" />
          <CardBody>
            <div className="grid-auto admin-stat-grid">
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
                    icon={<LifeBuoy />}
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
            </div>
          </CardBody>
        </Card>
      )}

      <section aria-labelledby="overview-links">
        <h2 id="overview-links" className="admin-section-title">
          Quick links
        </h2>
        <ul className="admin-quick-links">
          {links.map((item) => {
            const Icon = item.icon;
            return (
              <Card as="li" key={item.to} interactive>
                <CardBody className="admin-quick-link">
                  <span className="admin-quick-link__icon" aria-hidden="true">
                    <Icon />
                  </span>
                  <div>
                    <h3 className="admin-quick-link__title">
                      <Link className="ui-card__link" to={item.to}>
                        {item.label}
                      </Link>
                    </h3>
                    {item.description && <p className="text-small text-muted">{item.description}</p>}
                  </div>
                </CardBody>
              </Card>
            );
          })}
        </ul>
      </section>
    </>
  );
}
