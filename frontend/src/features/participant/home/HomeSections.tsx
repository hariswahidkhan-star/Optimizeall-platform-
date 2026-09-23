import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, Award, Bell, Hourglass, LifeBuoy, MailCheck, UserPlus, Wallet } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { buttonClasses } from '@/components/ui/buttonStyles';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { Money } from '@/components/ui/Money';
import { ProgressBar } from '@/components/ui/Progress';
import { Stat } from '@/components/ui/Stat';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { Stepper, type Step } from '@/components/ui/Stepper';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { MessageResponse } from '@/lib/api/types';
import { formatMoney } from '@/lib/format/money';
import { pluralize } from '@/lib/format/text';
import { qk } from '../api/queries';
import type {
  Achievement,
  Announcement,
  EarningsSummary,
  HomeBanner,
  OnboardingStep,
  ParticipantHome,
  RecommendedCampaign,
  SubmissionListItem,
} from '../api/types';
import { CampaignCard } from '../components/CampaignCard';
import { PlatformTag } from '../components/Platform';
import { AchievementIcon } from '../achievements/AchievementIcon';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { isInternalHref } from '@/lib/safeHref';

// ---------------------------------------------------------------- state heroes

function Hero({
  icon,
  title,
  children,
  actions,
  titleId,
}: {
  icon: ReactNode;
  title: string;
  children: ReactNode;
  actions?: ReactNode;
  titleId: string;
}) {
  return (
    <section className="pp-hero" aria-labelledby={titleId}>
      <span className="pp-hero__icon" aria-hidden="true">
        {icon}
      </span>
      <div className="pp-hero__body">
        <h2 id={titleId} className="pp-hero__title">
          {title}
        </h2>
        {children}
        {actions && <div className="pp-actions">{actions}</div>}
      </div>
    </section>
  );
}

export function VerifyEmailHero({ email }: { email: string }) {
  const toast = useToast();
  const resend = useMutation({
    mutationFn: () => api.post<MessageResponse>('/auth/resend-verification', { email }),
    onSuccess: (response) => toast.success('Verification email sent', response?.message ?? `Check ${email}.`),
    onError: (error) => toast.error('Couldn’t send the email', errorMessage(error)),
  });
  return (
    <Hero
      titleId="hero-verify"
      icon={<MailCheck />}
      title="Verify your email to get started"
      actions={
        <Button variant="highlight" loading={resend.isPending} onClick={() => resend.mutate()}>
          Resend verification email
        </Button>
      }
    >
      <p className="pp-muted">
        We sent a verification link to <strong>{email}</strong>. You need a verified email to join campaigns,
        submit posts and receive payouts.
      </p>
      <p className="visually-hidden" role="status" aria-live="polite">
        {resend.isSuccess ? 'Verification email sent.' : ''}
      </p>
    </Hero>
  );
}

export function AddSocialAccountHero({ minAgeDays }: { minAgeDays: number }) {
  return (
    <Hero
      titleId="hero-add-social"
      icon={<UserPlus />}
      title="Add the social profile you post from"
      actions={
        <ButtonLink to="/app/social-accounts?add=1" variant="highlight" trailingIcon={<ArrowRight />}>
          Add a social profile
        </ButtonLink>
      }
    >
      <p className="pp-muted">
        Campaigns are shared from established accounts. A profile qualifies once it is at least{' '}
        <strong>{pluralize(minAgeDays, 'day')} old</strong> — we use the date the account was created on the
        platform, not the date you add it here. You can add it now and we’ll tell you when it qualifies.
      </p>
    </Hero>
  );
}

function splitDuration(ms: number) {
  const total = Math.max(0, Math.floor(ms / 60_000));
  return { days: Math.floor(total / 1440), hours: Math.floor((total % 1440) / 60), minutes: total % 60 };
}

export function AwaitingEligibilityHero({
  eligibleFrom,
  minAgeDays,
}: {
  eligibleFrom: string | null;
  minAgeDays: number;
}) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!eligibleFrom) return;
    const timer = setInterval(() => setNow(Date.now()), 60_000);
    return () => clearInterval(timer);
  }, [eligibleFrom]);

  const remaining = eligibleFrom ? splitDuration(new Date(eligibleFrom).getTime() - now) : null;

  return (
    <Hero
      titleId="hero-awaiting"
      icon={<Hourglass />}
      title={eligibleFrom ? 'Your profile qualifies soon' : 'None of your profiles qualify yet'}
      actions={
        <ButtonLink to="/app/social-accounts" variant="secondary">
          Review your social profiles
        </ButtonLink>
      }
    >
      {eligibleFrom && remaining ? (
        <>
          <p className="pp-muted">
            Profiles must be at least {pluralize(minAgeDays, 'day')} old before they can be used for paid
            posts. Your first profile qualifies on{' '}
            <strong>
              <DateTime value={eligibleFrom} format="date" />
            </strong>
            .
          </p>
          <ul className="pp-countdown" aria-label="Time until your profile qualifies">
            <li>
              <strong>{remaining.days}</strong> {remaining.days === 1 ? 'day' : 'days'}
            </li>
            <li>
              <strong>{remaining.hours}</strong> {remaining.hours === 1 ? 'hour' : 'hours'}
            </li>
            <li>
              <strong>{remaining.minutes}</strong> {remaining.minutes === 1 ? 'minute' : 'minutes'}
            </li>
          </ul>
        </>
      ) : (
        <p className="pp-muted">
          Your profiles don’t meet the requirements yet (for example the follower minimum or a rejected
          verification). Open your social profiles to see the reasons for each one.
        </p>
      )}
    </Hero>
  );
}

// ---------------------------------------------------------------- onboarding

export function OnboardingCard({ onboarding }: { onboarding: ParticipantHome['onboarding'] }) {
  const toast = useToast();
  const client = useQueryClient();
  const dismiss = useMutation({
    mutationFn: (step: OnboardingStep) => api.post(`/me/onboarding/${step.id}/complete`),
    onSuccess: (_data, step) => {
      toast.success('Step marked as done', step.title);
      void client.invalidateQueries({ queryKey: qk.home });
    },
    onError: (error) => toast.error('Couldn’t update the checklist', errorMessage(error)),
  });

  if (onboarding.totalCount === 0 || onboarding.completedCount >= onboarding.totalCount) return null;

  const firstOpen = onboarding.steps.findIndex((s) => !s.completed);
  const steps: Step[] = onboarding.steps.map((step, index) => ({
    id: step.id,
    title: step.title,
    description: step.description,
    status: step.completed ? 'complete' : index === firstOpen ? 'current' : 'upcoming',
    action:
      step.actionUrl || step.isManual ? (
        <div className="pp-actions">
          {step.actionUrl &&
            (isInternalHref(step.actionUrl) ? (
              <ButtonLink to={step.actionUrl} size="sm" variant="secondary">
                {step.actionLabel ?? 'Open'}
              </ButtonLink>
            ) : (
              <SafeExternalLink className="ui-link" href={step.actionUrl} fallback={null}>
                {step.actionLabel ?? 'Open'}
              </SafeExternalLink>
            ))}
          {step.isManual && (
            <Button
              size="sm"
              variant="ghost"
              loading={dismiss.isPending && dismiss.variables?.id === step.id}
              disabled={dismiss.isPending && dismiss.variables?.id !== step.id}
              onClick={() => dismiss.mutate(step)}
            >
              Mark as done
            </Button>
          )}
        </div>
      ) : undefined,
  }));

  return (
    <Card as="section" aria-labelledby="onboarding-title">
      <CardHeader
        titleId="onboarding-title"
        title="Get set up"
        description={`${onboarding.completedCount} of ${onboarding.totalCount} steps done`}
      />
      <CardBody className="stack">
        <ProgressBar
          value={onboarding.progressPercent}
          max={100}
          label="Setup progress"
          hideLabel
          tone="accent"
        />
        <Stepper steps={steps} label="Onboarding checklist" />
      </CardBody>
    </Card>
  );
}

// ---------------------------------------------------------------- banners & announcements

export function Banners({ banners }: { banners: HomeBanner[] }) {
  if (banners.length === 0) return null;
  return (
    <section aria-label="Highlights" className="stack">
      {banners.map((banner) => (
        <div key={banner.id} className="pp-banner">
          {banner.imageUrl && <img src={banner.imageUrl} alt="" className="pp-banner__image" />}
          <div className="pp-banner__body">
            <h2 className="pp-banner__title">{banner.title}</h2>
            {banner.body && <p className="pp-muted text-small">{banner.body}</p>}
          </div>
          {banner.ctaLabel &&
            banner.ctaUrl &&
            (isInternalHref(banner.ctaUrl) ? (
              <ButtonLink to={banner.ctaUrl} variant="highlight" size="sm">
                {banner.ctaLabel}
              </ButtonLink>
            ) : (
              <SafeExternalLink
                className={buttonClasses('highlight', 'sm')}
                href={banner.ctaUrl}
                fallback={null}
              >
                {banner.ctaLabel}
              </SafeExternalLink>
            ))}
        </div>
      ))}
    </section>
  );
}

const SEVERITY_TONE = { Info: 'info', Success: 'success', Warning: 'warning', Critical: 'danger' } as const;

export function Announcements({ announcements }: { announcements: Announcement[] }) {
  if (announcements.length === 0) return null;
  return (
    <section aria-labelledby="announcements-title" className="pp-section">
      <h2 id="announcements-title" className="pp-section__title">
        Announcements
      </h2>
      {announcements.slice(0, 3).map((a) => (
        <Alert key={a.id} tone={SEVERITY_TONE[a.severity] ?? 'info'} title={a.title}>
          <p className="pp-prewrap">{a.body}</p>
          <p className="text-small pp-muted">
            <DateTime value={a.publishAt} format="date" />
          </p>
        </Alert>
      ))}
    </section>
  );
}

// ---------------------------------------------------------------- recommendations

export function Recommendations({
  items,
  title = 'Recommended for you',
  description,
}: {
  items: RecommendedCampaign[];
  title?: string;
  description?: string;
}) {
  return (
    <section aria-labelledby="recommended-title" className="pp-section">
      <div className="pp-section__head">
        <div>
          <h2 id="recommended-title" className="pp-section__title">
            {title}
          </h2>
          {description && <p className="pp-muted text-small">{description}</p>}
        </div>
        <Link to="/app/campaigns" className="ui-link">
          Browse all campaigns
        </Link>
      </div>
      {items.length === 0 ? (
        <Card flat>
          <EmptyState
            compact
            headingLevel={3}
            title="No recommendations right now"
            description="New campaigns open regularly. Browse everything that’s live, or add interests to your profile for better matches."
            action={
              <ButtonLink to="/app/campaigns" variant="secondary">
                Browse campaigns
              </ButtonLink>
            }
          />
        </Card>
      ) : (
        <div className="pp-grid">
          {items.map((item) => (
            <CampaignCard key={item.campaign.id} campaign={item.campaign} reason={item.reason} />
          ))}
        </div>
      )}
    </section>
  );
}

// ---------------------------------------------------------------- active participants

export function EarningsSnapshot({ summary }: { summary: EarningsSummary }) {
  const c = summary.currency;
  return (
    <section aria-labelledby="snapshot-title" className="pp-section">
      <div className="pp-section__head">
        <h2 id="snapshot-title" className="pp-section__title">
          Your earnings
        </h2>
        <Link to="/app/earnings" className="ui-link">
          See all earnings
        </Link>
      </div>
      <div className="pp-stats">
        <Stat
          label="Pending review"
          icon={<Wallet />}
          measurement="Estimated"
          value={<Money amount={summary.pending} currency={c} />}
        />
        <Stat
          label="Approved"
          icon={<Wallet />}
          value={<Money amount={summary.approved} currency={c} />}
          hint={
            summary.onHold > 0 ? (
              <>
                <Money amount={summary.onHold} currency={c} /> on hold
              </>
            ) : undefined
          }
        />
        <Stat label="Paid to date" icon={<Wallet />} value={<Money amount={summary.paid} currency={c} />} />
        <Stat
          label="Lifetime earned"
          icon={<Award />}
          value={<Money amount={summary.lifetimeEarned} currency={c} />}
        />
      </div>
    </section>
  );
}

export function NextPayoutCard({
  summary,
  headingLevel = 2,
}: {
  summary: EarningsSummary;
  headingLevel?: 2 | 3;
}) {
  const next = summary.nextPayout;
  const c = summary.currency;
  return (
    <Card as="section" aria-labelledby="next-payout-title">
      <CardHeader
        titleId="next-payout-title"
        headingLevel={headingLevel}
        title="Next payout"
        description="Payouts run every two weeks for approved earnings that are past their hold period."
      />
      <CardBody className="stack">
        {summary.activeHold && (
          <Alert tone="warning" title="Payouts are on hold">
            {summary.holdMessage ??
              'Your payouts are paused while we review your account. Contact support for help.'}
          </Alert>
        )}
        <dl className="pp-legend">
          <div>
            <dt>Period cutoff</dt>
            <dd>
              <DateTime value={next.cutoffAt} withZone />
            </dd>
          </div>
          <div>
            <dt>Payment date</dt>
            <dd>
              <DateTime value={`${next.paymentDate}T12:00:00Z`} format="date" timeZone="UTC" />
            </dd>
          </div>
          <div>
            <dt>Estimated amount</dt>
            <dd>
              <Money amount={next.estimatedAmount} currency={c} />
            </dd>
          </div>
        </dl>
        <ProgressBar
          value={Math.min(summary.availableForNextPayout, next.minimumPayoutAmount)}
          max={next.minimumPayoutAmount > 0 ? next.minimumPayoutAmount : 1}
          label="Progress to the payout minimum"
          valueText={
            next.meetsMinimum
              ? 'Minimum reached'
              : `${formatMoneyText(summary.availableForNextPayout, c)} of ${formatMoneyText(next.minimumPayoutAmount, c)}`
          }
          tone={next.meetsMinimum ? 'success' : 'accent'}
        />
        <p className="pp-note">
          {next.meetsMinimum ? (
            <>
              You’ve reached the <Money amount={next.minimumPayoutAmount} currency={c} /> minimum for this
              period.
            </>
          ) : (
            <>
              Balances below <Money amount={next.minimumPayoutAmount} currency={c} /> roll over to the next
              period.
            </>
          )}
        </p>
      </CardBody>
    </Card>
  );
}

function formatMoneyText(amount: number, currency: string) {
  return formatMoney(amount, currency);
}

export function AttentionList({ items }: { items: SubmissionListItem[] }) {
  return (
    <Card as="section" aria-labelledby="attention-title">
      <CardHeader
        titleId="attention-title"
        title="Submissions needing attention"
        actions={
          <Link to="/app/submissions" className="ui-link text-small">
            All submissions
          </Link>
        }
      />
      <CardBody flush>
        {items.length === 0 ? (
          <EmptyState
            compact
            headingLevel={3}
            title="You’re all caught up"
            description="Nothing needs your action. We’ll notify you when a reviewer responds."
          />
        ) : (
          <ul className="pp-list">
            {items.map((s) => (
              <li key={s.id} className="pp-list__item">
                <div className="pp-list__main">
                  <Link to={`/app/submissions/${s.id}`} className="pp-list__title ui-link">
                    {s.campaign.title}
                  </Link>
                  <span className="pp-list__meta">
                    <PlatformTag platform={s.platform} />
                    <span>
                      Submitted <DateTime value={s.submittedAt} format="relative" />
                    </span>
                  </span>
                  {s.status === 'NeedsCorrection' && s.decisionReason && (
                    <span className="text-small">{s.decisionReason}</span>
                  )}
                </div>
                <StatusBadge kind="submission" status={s.status} />
              </li>
            ))}
          </ul>
        )}
      </CardBody>
    </Card>
  );
}

export function AchievementStrip({ achievements }: { achievements: Achievement[] }) {
  const awarded = achievements
    .filter((a) => a.awardedAt)
    .sort((a, b) => (b.awardedAt ?? '').localeCompare(a.awardedAt ?? ''));
  const inProgress = achievements
    .filter((a) => !a.awardedAt)
    .sort((a, b) => b.progress / b.threshold - a.progress / a.threshold);
  const shown = [
    ...awarded.slice(0, 4),
    ...inProgress.slice(0, Math.max(0, 4 - Math.min(awarded.length, 4))),
  ];
  if (shown.length === 0) return null;
  return (
    <section aria-labelledby="achievements-strip-title" className="pp-section">
      <div className="pp-section__head">
        <h2 id="achievements-strip-title" className="pp-section__title">
          Achievements
        </h2>
        <Link to="/app/achievements" className="ui-link">
          View all
        </Link>
      </div>
      <ul className="pp-strip">
        {shown.map((a) => (
          <li key={a.key}>
            <div className="pp-achievement-mini">
              <span className="pp-achievement__icon" aria-hidden="true">
                <AchievementIcon name={a.icon} />
              </span>
              <div>
                <p className="pp-achievement-mini__name">{a.name}</p>
                <p className="text-small pp-muted">
                  {a.awardedAt ? (
                    <>
                      Earned <DateTime value={a.awardedAt} format="date" />
                    </>
                  ) : (
                    `${Math.min(a.progress, a.threshold)} / ${a.threshold}`
                  )}
                </p>
              </div>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

export function UnreadNotice({ count, openTickets }: { count: number; openTickets: number }) {
  if (count === 0 && openTickets === 0) return null;
  return (
    <div className="pp-actions" aria-label="Updates">
      {count > 0 && (
        <ButtonLink to="/app/notifications?unread=1" variant="secondary" size="sm" leadingIcon={<Bell />}>
          {pluralize(count, 'unread notification')}
        </ButtonLink>
      )}
      {openTickets > 0 && (
        <ButtonLink to="/app/support" variant="ghost" size="sm" leadingIcon={<LifeBuoy />}>
          {pluralize(openTickets, 'open support ticket')}
        </ButtonLink>
      )}
    </div>
  );
}
