import { useQuery } from '@tanstack/react-query';
import { Card } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton, SkeletonText } from '@/components/ui/Skeleton';
import { api } from '@/lib/api/client';
import { useAuth } from '@/lib/auth/useAuth';
import { greetingFor } from '@/lib/format/dates';
import { firstName } from '@/lib/format/text';
import {
  qk,
  useAchievements,
  useAnnouncements,
  useEarningsSummary,
  useHome,
  useRecommended,
} from '../api/queries';
import type { PagedResult, ParticipantHome, SubmissionListItem } from '../api/types';
import {
  AchievementStrip,
  AddSocialAccountHero,
  Announcements,
  AttentionList,
  AwaitingEligibilityHero,
  Banners,
  EarningsSnapshot,
  NextPayoutCard,
  OnboardingCard,
  Recommendations,
  UnreadNotice,
  VerifyEmailHero,
} from './HomeSections';
import '../participant.css';
import { LearningPanel } from '../learning/LearningPanel';
import { useSiteCopy } from '@/features/public/site/copy';

/** Submissions that need the participant: corrections first, then the most recent decisions. */
function useAttentionItems(enabled: boolean) {
  return useQuery({
    queryKey: [...qk.submissions, 'attention'],
    enabled,
    queryFn: async () => {
      const [corrections, recent] = await Promise.all([
        api.get<PagedResult<SubmissionListItem>>('/me/submissions', {
          query: { status: 'NeedsCorrection', page: 1, pageSize: 5 },
        }),
        api.get<PagedResult<SubmissionListItem>>('/me/submissions', { query: { page: 1, pageSize: 5 } }),
      ]);
      const seen = new Set(corrections.items.map((s) => s.id));
      return [...corrections.items, ...recent.items.filter((s) => !seen.has(s.id))].slice(0, 6);
    },
  });
}

function StateSection({ home, email }: { home: ParticipantHome; email: string }) {
  const active = home.state === 'Active';
  const wantsRecommendations = home.state === 'Ready' || active;
  const recommended = useRecommended(6, wantsRecommendations);
  const summary = useEarningsSummary(active);
  const attention = useAttentionItems(active);

  const recommendations = recommended.isSuccess ? (
    <Recommendations
      items={recommended.data}
      title={active ? 'Recommended for you' : 'You’re ready — pick your first campaign'}
      description={
        active
          ? undefined
          : 'These campaigns match your eligible profiles. Share the approved content and submit proof.'
      }
    />
  ) : recommended.isError ? (
    <Card flat>
      <ErrorState
        compact
        headingLevel={3}
        error={recommended.error}
        title="Recommendations aren’t available right now"
        onRetry={() => void recommended.refetch()}
      />
    </Card>
  ) : wantsRecommendations ? (
    <div aria-busy="true" className="pp-grid">
      <Skeleton height={220} />
      <Skeleton height={220} />
      <Skeleton height={220} />
    </div>
  ) : null;

  switch (home.state) {
    case 'VerifyEmail':
      return <VerifyEmailHero email={email} />;
    case 'AddSocialAccount':
      return <AddSocialAccountHero minAgeDays={home.socialAccounts.minAccountAgeDays} />;
    case 'AwaitingEligibility':
      return (
        <AwaitingEligibilityHero
          eligibleFrom={home.eligibleFrom ?? home.socialAccounts.soonestEligibleFrom}
          minAgeDays={home.socialAccounts.minAccountAgeDays}
        />
      );
    case 'Ready':
      return recommendations;
    case 'Active':
      return (
        <>
          {summary.isSuccess && <EarningsSnapshot summary={summary.data} />}
          <div className="pp-two-col">
            <div className="stack">
              {attention.isSuccess && <AttentionList items={attention.data} />}
              {attention.isPending && <Skeleton height={180} radius="var(--radius-xl)" />}
            </div>
            {summary.isSuccess && <NextPayoutCard summary={summary.data} />}
            {summary.isPending && <Skeleton height={240} radius="var(--radius-xl)" />}
          </div>
          {recommendations}
        </>
      );
    default:
      return recommendations;
  }
}

/** Participant home: adapts to the participant's journey state reported by the API. */
export function HomePage() {
  const { user } = useAuth();
  const home = useHome();
  const achievements = useAchievements();
  const announcements = useAnnouncements();
  const name = user ? firstName(user.displayName) : '';
  const copy = useSiteCopy();

  return (
    <div className="pp-page pp-home ui-dash">
      <PageHeader
        title={`${greetingFor(new Date(), user?.timeZone)}${name ? `, ${name}` : ''}`}
        description={copy.text('participant.home.description')}
        actions={
          home.isSuccess ? (
            <UnreadNotice
              count={home.data.unreadNotificationCount}
              openTickets={home.data.openSupportTicketCount}
            />
          ) : undefined
        }
      />

      {home.isPending && (
        <div aria-busy="true" className="pp-loading">
          <span className="visually-hidden" role="status">
            Loading your home page…
          </span>
          <Skeleton height={112} radius="var(--radius-xl)" />
          <Card flat className="pp-pad">
            <SkeletonText lines={4} />
          </Card>
        </div>
      )}

      {home.isError && (
        <Card flat>
          <ErrorState
            error={home.error}
            title="Your home page isn’t available right now"
            onRetry={() => void home.refetch()}
            retrying={home.isFetching}
          />
        </Card>
      )}

      {home.isSuccess && (
        <>
          <Banners banners={home.data.banners} />
          <StateSection home={home.data} email={user?.email ?? ''} />
          <OnboardingCard onboarding={home.data.onboarding} />
          <LearningPanel />
          {achievements.isSuccess && <AchievementStrip achievements={achievements.data} />}
          <Announcements
            announcements={announcements.isSuccess ? announcements.data : home.data.announcements}
          />
        </>
      )}
    </div>
  );
}
