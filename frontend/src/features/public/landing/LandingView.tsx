import { CalendarRange, Hash, LinkIcon, Megaphone, Wallet } from 'lucide-react';
import type { ReactNode } from 'react';
import { ButtonLink, DateTime, Money, Skeleton, SkeletonText } from '@/components/ui';
import { PlatformIcon, platformLabel } from '@/features/participant/components/Platform';
import { StatusPage } from '../StatusPage';
import type { LandingCategory, RewardTeaser } from './landingApi';
import './LandingView.css';

export interface LandingViewProps {
  eyebrow: string;
  headline: string;
  body: string;
  heroImageUrl?: string | null;
  cta: { to: string; label: string };
  secondaryCta?: { to: string; label: string };
  reward?: RewardTeaser | null;
  platforms?: string[];
  startsAt?: string;
  endsAt?: string;
  submissionDeadline?: string;
  category?: LandingCategory | null;
  /** Campaign-specific disclosure text participants must include. */
  disclosure?: string | null;
  children?: ReactNode;
}

/** Shared layout of the public invitation (/join/:code) and campaign (/c/:slug) landing pages. */
export function LandingView(props: LandingViewProps) {
  const { eyebrow, headline, body, heroImageUrl, cta, secondaryCta, reward, platforms = [], category } = props;
  return (
    <div className="campaign-landing">
      <section className="campaign-landing__hero" aria-labelledby="landing-headline">
        <div className="container campaign-landing__inner">
          <div className="campaign-landing__copy">
            <p className="campaign-landing__eyebrow">
              <Megaphone aria-hidden="true" />
              {eyebrow}
            </p>
            <h1 id="landing-headline" className="campaign-landing__title">
              {headline}
            </h1>
            <p className="campaign-landing__body">{body}</p>
            <div className="campaign-landing__ctas">
              <ButtonLink to={cta.to} variant="highlight" size="lg">
                {cta.label}
              </ButtonLink>
              {secondaryCta && (
                <ButtonLink to={secondaryCta.to} variant="secondary" size="lg">
                  {secondaryCta.label}
                </ButtonLink>
              )}
            </div>
          </div>
          {heroImageUrl && (
            <div className="campaign-landing__visual">
              <img src={heroImageUrl} alt="" className="campaign-landing__image" />
            </div>
          )}
        </div>
      </section>

      <section className="container campaign-landing__details" aria-labelledby="landing-details-title">
        <h2 id="landing-details-title" className="visually-hidden">
          Details
        </h2>
        <dl className="campaign-landing__facts">
          {reward && (
            <div className="campaign-landing__fact">
              <dt>
                <Wallet aria-hidden="true" />
                Reward per approved post
              </dt>
              <dd>
                From <Money amount={reward.baseAmount} currency={reward.currency} />
              </dd>
            </div>
          )}
          {platforms.length > 0 && (
            <div className="campaign-landing__fact">
              <dt>
                <LinkIcon aria-hidden="true" />
                Platforms
              </dt>
              <dd>
                <ul className="campaign-landing__platforms">
                  {platforms.map((p) => (
                    <li key={p}>
                      <PlatformIcon platform={p} />
                      {platformLabel(p)}
                    </li>
                  ))}
                </ul>
              </dd>
            </div>
          )}
          {props.startsAt && props.endsAt && (
            <div className="campaign-landing__fact">
              <dt>
                <CalendarRange aria-hidden="true" />
                Runs
              </dt>
              <dd>
                <DateTime value={props.startsAt} format="date" /> – <DateTime value={props.endsAt} format="date" />
                {props.submissionDeadline && (
                  <span className="campaign-landing__muted">
                    {' '}
                    (submit by <DateTime value={props.submissionDeadline} format="date" />)
                  </span>
                )}
              </dd>
            </div>
          )}
          {category && (
            <div className="campaign-landing__fact">
              <dt>
                <Hash aria-hidden="true" />
                Category
              </dt>
              <dd>{category.name}</dd>
            </div>
          )}
        </dl>
        {props.children}
        <aside className="campaign-landing__disclosure" aria-labelledby="landing-disclosure-title">
          <h2 id="landing-disclosure-title" className="campaign-landing__disclosure-title">
            Paid posts are always disclosed
          </h2>
          <p>
            Every post you’re paid for must be clearly labelled as an ad, using the platform’s paid-partnership
            label or the disclosure text of the campaign. Rewards are paid only for approved posts from established
            accounts.
          </p>
          {props.disclosure && (
            <p>
              Disclosure for this campaign: <strong className="campaign-landing__disclosure-text">{props.disclosure}</strong>
            </p>
          )}
        </aside>
      </section>
    </div>
  );
}

export function LandingSkeleton() {
  return (
    <div className="container campaign-landing__loading" aria-busy="true">
      <span className="visually-hidden" role="status">
        Loading…
      </span>
      <Skeleton height={40} width="70%" />
      <SkeletonText lines={3} />
      <Skeleton height={48} width={200} />
    </div>
  );
}

/** Friendly 404 for expired, used-up or unknown invitation links and unpublished campaigns. */
export function LandingUnavailable({ kind }: { kind: 'invitation' | 'campaign' }) {
  return (
    <StatusPage
      code="404"
      icon={<LinkIcon />}
      title={kind === 'invitation' ? 'This invitation is no longer available' : 'This campaign isn’t available'}
      description={
        kind === 'invitation'
          ? 'The link may have expired or reached its limit. You can still join Optimize All and browse the campaigns you qualify for.'
          : 'It may have ended, be invite-only or not be published yet. You can still join Optimize All and browse the campaigns you qualify for.'
      }
      actions={
        <>
          <ButtonLink to="/register">Create an account</ButtonLink>
          <ButtonLink to="/" variant="secondary">
            How Optimize All works
          </ButtonLink>
        </>
      }
    />
  );
}
