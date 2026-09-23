import { useQuery } from '@tanstack/react-query';
import { ArrowRight, CalendarClock, FileCheck2, Megaphone, Share2, Wallet } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Stat } from '@/components/ui/Stat';
import { Stepper, type Step } from '@/components/ui/Stepper';
import { api } from '@/lib/api/client';
import type { IsoDateTime, MoneyAmount } from '@/lib/api/types';
import { useAuth } from '@/lib/auth/useAuth';
import { greetingFor } from '@/lib/format/dates';
import { firstName } from '@/lib/format/text';
import './HomePage.css';

/** Response of GET /api/v1/me/home. Every section is optional so the page renders whatever the API provides. */
export interface ParticipantHome {
  onboarding?: { steps: { id: string; title: string; description?: string; completed: boolean }[] };
  earnings?: { pending?: MoneyAmount; approved?: MoneyAmount; paid?: MoneyAmount };
  nextPayout?: { date: IsoDateTime; amount?: MoneyAmount };
  activeCampaigns?: number;
}

export const participantHomeQueryKey = ['me', 'home'] as const;

/** Paths are relative to the participant portal home. */
const QUICK_ACTIONS = [
  { to: 'campaigns', icon: Megaphone, title: 'Find a campaign', text: 'See what you can share this week.' },
  {
    to: 'social-accounts',
    icon: Share2,
    title: 'Connect an account',
    text: 'Add the profiles you post from.',
  },
  { to: 'submissions', icon: FileCheck2, title: 'Track submissions', text: 'Follow reviews and feedback.' },
];

function onboardingSteps(home: ParticipantHome): Step[] {
  const steps = home.onboarding?.steps ?? [];
  const firstOpen = steps.findIndex((s) => !s.completed);
  return steps.map((s, i) => ({
    id: s.id,
    title: s.title,
    description: s.description,
    status: s.completed ? 'complete' : i === firstOpen ? 'current' : 'upcoming',
  }));
}

function Summary({ home }: { home: ParticipantHome }) {
  const tiles = [
    home.earnings?.pending && (
      <Stat
        key="pending"
        label="Pending review"
        icon={<Wallet />}
        measurement="Measured"
        value={<Money amount={home.earnings.pending.amount} currency={home.earnings.pending.currency} />}
      />
    ),
    home.earnings?.approved && (
      <Stat
        key="approved"
        label="Approved, not yet paid"
        icon={<Wallet />}
        measurement="Measured"
        value={<Money amount={home.earnings.approved.amount} currency={home.earnings.approved.currency} />}
      />
    ),
    home.earnings?.paid && (
      <Stat
        key="paid"
        label="Paid to date"
        icon={<Wallet />}
        measurement="Measured"
        value={<Money amount={home.earnings.paid.amount} currency={home.earnings.paid.currency} />}
      />
    ),
    home.nextPayout && (
      <Stat
        key="next"
        label="Next payout"
        icon={<CalendarClock />}
        value={<DateTime value={home.nextPayout.date} format="date" />}
        hint={
          home.nextPayout.amount ? (
            <Money amount={home.nextPayout.amount.amount} currency={home.nextPayout.amount.currency} />
          ) : undefined
        }
      />
    ),
    home.activeCampaigns !== undefined && (
      <Stat
        key="campaigns"
        label="Campaigns open to you"
        icon={<Megaphone />}
        measurement="Count"
        value={home.activeCampaigns}
      />
    ),
  ].filter(Boolean);

  const steps = onboardingSteps(home);

  if (tiles.length === 0 && steps.length === 0) {
    return (
      <EmptyState
        compact
        headingLevel={3}
        title="Nothing to show yet"
        description="Your earnings and payout dates will appear here once you start sharing campaigns."
      />
    );
  }

  return (
    <div className="participant-home__summary">
      {tiles.length > 0 && <div className="participant-home__stats">{tiles}</div>}
      {steps.length > 0 && (
        <Card as="section" aria-labelledby="onboarding-title">
          <CardHeader
            titleId="onboarding-title"
            title="Get set up"
            description="Finish these steps to start earning."
          />
          <CardBody>
            <Stepper steps={steps} label="Onboarding checklist" />
          </CardBody>
        </Card>
      )}
    </div>
  );
}

/** Participant portal home. The full dashboard is owned by the participant workstream. */
export function HomePage() {
  const { user } = useAuth();
  const home = useQuery({
    queryKey: participantHomeQueryKey,
    queryFn: () => api.get<ParticipantHome>('/me/home'),
  });

  const name = user ? firstName(user.displayName) : '';

  return (
    <div className="participant-home">
      <PageHeader
        title={`${greetingFor(new Date(), user?.timeZone)}${name ? `, ${name}` : ''}`}
        description="Here’s what’s happening with your campaigns and earnings."
      />

      <section aria-labelledby="quick-actions-title" className="participant-home__section">
        <h2 id="quick-actions-title" className="participant-home__heading">
          Quick actions
        </h2>
        <ul className="participant-home__actions">
          {QUICK_ACTIONS.map(({ to, icon: Icon, title, text }) => (
            <Card as="li" key={to} interactive>
              <CardBody className="participant-home__action">
                <span className="participant-home__action-icon" aria-hidden="true">
                  <Icon />
                </span>
                <span className="participant-home__action-text">
                  <Link to={to} className="ui-card__link participant-home__action-title">
                    {title}
                  </Link>
                  <span>{text}</span>
                </span>
                <ArrowRight aria-hidden="true" className="participant-home__action-arrow" />
              </CardBody>
            </Card>
          ))}
        </ul>
      </section>

      <section
        aria-labelledby="summary-title"
        className="participant-home__section"
        aria-busy={home.isPending || undefined}
      >
        <h2 id="summary-title" className="participant-home__heading">
          Your summary
        </h2>
        {home.isPending && (
          <div className="participant-home__stats">
            <Stat label="Pending review" value={null} loading />
            <Stat label="Approved" value={null} loading />
            <Stat label="Next payout" value={null} loading />
          </div>
        )}
        {home.isError && (
          <Card flat>
            <ErrorState
              compact
              headingLevel={3}
              error={home.error}
              title="Your summary isn’t available right now"
              onRetry={() => void home.refetch()}
              retrying={home.isFetching}
            />
          </Card>
        )}
        {home.isSuccess && <Summary home={home.data ?? {}} />}
      </section>
    </div>
  );
}
