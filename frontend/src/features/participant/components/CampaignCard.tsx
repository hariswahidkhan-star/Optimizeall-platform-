import { CalendarClock, Sparkles } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { Money } from '@/components/ui/Money';
import { pluralize } from '@/lib/format/text';
import type { CampaignCard as CampaignCardData } from '../api/types';
import { EligibilityBadge } from './Badges';
import { PlatformList } from './Platform';

export interface CampaignCardProps {
  campaign: CampaignCardData;
  /** Recommendation reason ("Matches your interest in fitness"). */
  reason?: string;
  headingLevel?: 2 | 3;
}

/** Campaign summary card; the title link covers the whole card. */
export function CampaignCard({ campaign, reason, headingLevel = 3 }: CampaignCardProps) {
  const Heading = `h${headingLevel}` as const;
  const reward = campaign.reward;
  return (
    <Card as="article" interactive className="pp-campaign-card">
      {campaign.heroImageUrl && (
        <img className="pp-campaign-card__hero" src={campaign.heroImageUrl} alt="" loading="lazy" />
      )}
      <div className="pp-campaign-card__body">
        <div className="pp-campaign-card__top">
          {campaign.category && <span className="eyebrow">{campaign.category.name}</span>}
          {campaign.upcoming && <Badge tone="info">Starts soon</Badge>}
        </div>
        <Heading className="pp-campaign-card__title">
          <Link to={`/app/campaigns/${campaign.slug}`} className="ui-card__link">
            {campaign.title}
          </Link>
        </Heading>
        {reason && (
          <p className="pp-campaign-card__reason">
            <Sparkles aria-hidden="true" />
            {reason}
          </p>
        )}
        <p className="pp-campaign-card__summary">{campaign.summary}</p>

        <dl className="pp-campaign-card__facts">
          <div>
            <dt>Reward per post</dt>
            <dd className="pp-campaign-card__reward">
              {reward ? (
                <>
                  <Money amount={reward.baseAmount} currency={reward.currency} />
                  {reward.maxAmount > reward.baseAmount && (
                    <span className="text-small text-muted">
                      {' '}
                      up to <Money amount={reward.maxAmount} currency={reward.currency} />
                    </span>
                  )}
                  {reward.hasBonuses && <span className="text-small text-muted"> + bonuses</span>}
                </>
              ) : (
                <span className="text-muted">Reward to be announced</span>
              )}
            </dd>
          </div>
          <div>
            <dt>Submit by</dt>
            <dd>
              <CalendarClock aria-hidden="true" className="pp-inline-icon" />
              <DateTime value={campaign.submissionDeadline} format="relative" />
            </dd>
          </div>
          <div>
            <dt>Your submissions left</dt>
            <dd className="tabular">{pluralize(campaign.remainingSubmissions, 'post')}</dd>
          </div>
        </dl>

        <PlatformList platforms={campaign.platforms} label={`Platforms for ${campaign.title}`} />
        <EligibilityBadge
          isEligible={campaign.eligibility.isEligible}
          reasons={campaign.eligibility.reasons}
        />
      </div>
    </Card>
  );
}
