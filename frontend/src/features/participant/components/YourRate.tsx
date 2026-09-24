import { BadgeDollarSign } from 'lucide-react';
import { DateTime } from '@/components/ui/DateTime';
import { Money } from '@/components/ui/Money';
import type { YourRate as YourRateData } from '../api/types';

const FORMAT_LABELS: Record<string, string> = {
  Post: 'feed post',
  Story: 'story',
  ShortVideo: 'reel / short',
  LongVideo: 'long video',
  Carousel: 'carousel',
};

export function yourRateTitle(rate: YourRateData): string {
  return rate.kind === 'Personal' ? 'Your personal rate' : 'Your rate';
}

/** Compact "Your rate: …" line for campaign cards. */
export function YourRateInline({ rate }: { rate: YourRateData }) {
  return (
    <span className="pp-your-rate-inline">
      <BadgeDollarSign aria-hidden="true" className="pp-inline-icon" />
      <span>
        {yourRateTitle(rate)}:{' '}
        <strong>
          <Money amount={rate.minAmount} currency={rate.currency} />
          {rate.maxAmount > rate.minAmount && (
            <>
              {' – '}
              <Money amount={rate.maxAmount} currency={rate.currency} />
            </>
          )}
        </strong>
        {rate.validTo && (
          <span className="text-small text-muted">
            {' '}
            · until <DateTime value={rate.validTo} format="date" />
          </span>
        )}
      </span>
    </span>
  );
}

/** Campaign detail: the participant's own rate per platform/format (never names cards or groups). */
export function YourRateCard({ rate }: { rate: YourRateData }) {
  return (
    <section className="pp-your-rate" aria-labelledby="your-rate-title">
      <h2 id="your-rate-title" className="pp-your-rate__title">
        <BadgeDollarSign aria-hidden="true" /> {yourRateTitle(rate)}
      </h2>
      <p className="pp-your-rate__lead">
        {rate.kind === 'Personal'
          ? 'You have a personal deal for this campaign. It replaces the standard reward per post below.'
          : 'A special rate applies to you in this campaign. It replaces the standard reward per post below.'}
        {rate.validTo && (
          <>
            {' '}
            It applies to posts made until <DateTime value={rate.validTo} format="both" />; posts you submit
            before then keep this rate.
          </>
        )}
      </p>
      <ul className="pp-your-rate__list">
        {rate.entries.map((e) => (
          <li key={`${e.platform}-${e.format ?? 'any'}`}>
            <span>
              {e.platform === 'X' ? 'X (Twitter)' : e.platform}
              {e.format ? ` · ${FORMAT_LABELS[e.format] ?? e.format}` : ''}
            </span>
            <strong>
              <Money amount={e.amount} currency={rate.currency} />
            </strong>
          </li>
        ))}
      </ul>
      <p className="text-small text-muted">
        Per approved post. Campaign caps, bonuses and the budget still apply.
      </p>
    </section>
  );
}
