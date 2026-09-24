import { useQuery } from '@tanstack/react-query';
import {
  ArrowRight,
  BadgeCheck,
  CheckCheck,
  Clock,
  Hand,
  Hourglass,
  Inbox,
  PenLine,
  Radar,
  Scale,
  TriangleAlert,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { ButtonLink, Card, CardBody, ErrorState, PageHeader, Stat, StatGrid } from '@/components/ui';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { greetingFor } from '@/lib/format/dates';
import { firstName } from '@/lib/format/text';
import { reviewApi, reviewKeys } from '../api/reviewApi';
import { formatAgeHours } from '../components/common';

export function OverviewPage() {
  const { user, hasPermission } = useAuth();
  const stats = useQuery({
    queryKey: reviewKeys.stats(),
    queryFn: ({ signal }) => reviewApi.stats(signal),
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
  });
  const data = stats.data;
  const loading = stats.isPending;
  const byStatus = data?.queueByStatus ?? {};
  const canAppeals = hasPermission(Permissions.AppealsResolve);
  const canSocial = hasPermission(Permissions.SocialAccountsVerify);
  const name = user ? firstName(user.displayName) : '';

  const links = [
    {
      to: '/review/queue',
      icon: Inbox,
      title: 'Review queue',
      text: `${(byStatus.Pending ?? 0) + (byStatus.UnderReview ?? 0)} open submissions, oldest first.`,
      show: true,
    },
    {
      to: '/review/queue?mine=1',
      icon: Hand,
      title: 'Assigned to me',
      text: 'Submissions a lead assigned to you.',
      show: true,
    },
    {
      to: '/review/queue?sort=risk&flagged=true',
      icon: TriangleAlert,
      title: 'Highest risk first',
      text: 'Flagged submissions sorted by risk score.',
      show: true,
    },
    {
      to: '/review/live-checks',
      icon: Radar,
      title: 'Live checks',
      text: `${data?.dueLiveChecks ?? 0} due — confirm posts stayed public.`,
      show: true,
    },
    {
      to: '/review/appeals',
      icon: Scale,
      title: 'Appeals',
      text: `${data?.openAppeals ?? 0} open — needs a second reviewer.`,
      show: canAppeals,
    },
    {
      to: '/review/social-verification',
      icon: BadgeCheck,
      title: 'Social verification',
      text: 'Profiles waiting for verification.',
      show: canSocial,
    },
  ].filter((l) => l.show);

  return (
    <div className="ui-dash">
      <PageHeader
        eyebrow="Review"
        title={`${greetingFor(new Date(), user?.timeZone)}${name ? `, ${name}` : ''}`}
        description="Your review workload at a glance. Numbers refresh every minute."
        actions={
          <ButtonLink to="/review/queue" trailingIcon={<ArrowRight />}>
            Start reviewing
          </ButtonLink>
        }
      />
      {stats.isError ? (
        <Card flat>
          <ErrorState error={stats.error} onRetry={() => void stats.refetch()} retrying={stats.isFetching} />
        </Card>
      ) : (
        <section aria-labelledby="rv-stats-heading" className="stack">
          <h2 id="rv-stats-heading" className="visually-hidden">
            Workload
          </h2>
          <StatGrid strip min="190px">
            <Stat
              label="Pending"
              value={byStatus.Pending ?? 0}
              icon={<Inbox />}
              measurement="Count"
              loading={loading}
            />
            <Stat
              label="Under review"
              value={byStatus.UnderReview ?? 0}
              icon={<Hourglass />}
              measurement="Count"
              hint={data ? `${data.claimedByMe} claimed by you` : undefined}
              loading={loading}
            />
            <Stat
              label="Oldest pending"
              value={formatAgeHours(data?.oldestPendingAgeHours)}
              icon={<Clock />}
              measurement="Measured"
              loading={loading}
            />
            <Stat
              label="My decisions today"
              value={data?.myDecisionsToday ?? 0}
              icon={<CheckCheck />}
              measurement="Count"
              loading={loading}
            />
          </StatGrid>
          <StatGrid min="190px">
            <Stat
              label="Needs correction"
              value={byStatus.NeedsCorrection ?? 0}
              icon={<PenLine />}
              measurement="Count"
              hint="Waiting for participants"
              loading={loading}
            />
            <Stat
              label="Due live checks"
              value={data?.dueLiveChecks ?? 0}
              icon={<Radar />}
              measurement="Count"
              loading={loading}
            />
            {canAppeals && (
              <Stat
                label="Open appeals"
                value={data?.openAppeals ?? 0}
                icon={<Scale />}
                measurement="Count"
                loading={loading}
              />
            )}
          </StatGrid>
        </section>
      )}

      <section aria-labelledby="rv-links-heading">
        <div className="ui-dash-head">
          <h2 id="rv-links-heading" className="ui-dash-head__title">
            Quick links
          </h2>
        </div>
        <ul className="ui-quicklinks">
          {links.map((link) => {
            const Icon = link.icon;
            return (
              <Card as="li" key={link.to} interactive>
                <CardBody className="ui-quicklink">
                  <span className="ui-quicklink__icon" aria-hidden="true">
                    <Icon />
                  </span>
                  <div className="ui-quicklink__text">
                    <h3 className="ui-quicklink__title">
                      <Link className="ui-card__link" to={link.to}>
                        {link.title}
                      </Link>
                    </h3>
                    <p className="ui-quicklink__description">{link.text}</p>
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
