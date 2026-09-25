import { CircleCheck, CircleSlash, ExternalLink, Link2, Megaphone, ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { YourRateCard } from '../components/YourRate';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { CopyField } from '@/components/ui/CopyField';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { humanize, pluralize } from '@/lib/format/text';
import {
  useCampaign,
  useCreateTrackingLink,
  useExperimentVariants,
  useMyTrackingLinks,
} from '../api/queries';
import type {
  CampaignAsset,
  CampaignDetail,
  ExperimentVariant,
  RewardTerms,
  TrackingLink,
} from '../api/types';
import { CopyButton } from '../components/CopyButton';
import { PlatformList, PlatformTag, platformLabel } from '../components/Platform';
import { QueryState } from '../components/QueryState';
import { earningTypeLabel } from '../lib/labels';
import { SubmitProofDialog } from './SubmitProofDialog';
import '../participant.css';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { isSafeHref } from '@/lib/safeHref';

/** Applies the participant's sticky experiment variants on top of the campaign content. */
export function applyVariants(campaign: CampaignDetail, variants: ExperimentVariant[]) {
  let title = campaign.title;
  let instructions = campaign.postingInstructions;
  let assets = campaign.assets;
  for (const v of variants) {
    if (v.element === 'Title' && v.title) title = v.title;
    if (v.element === 'Instructions' && v.instructions) instructions = v.instructions;
    if (v.element === 'CreativeAsset' && v.asset) {
      const overlay: CampaignAsset = {
        id: v.asset.id,
        type: v.asset.type,
        title: v.asset.title,
        url: v.asset.url,
        fileId: null,
        body: null,
        platform: null,
        templateId: null,
        sortOrder: -1,
      };
      assets = [overlay, ...assets.filter((a) => a.id !== v.asset!.id)];
    }
  }
  // One variant id travels with a submission; prefer the element that changes what is posted.
  const priority = ['CreativeAsset', 'Instructions', 'Title', 'LandingPage'];
  const attributed = [...variants].sort(
    (a, b) => priority.indexOf(a.element) - priority.indexOf(b.element),
  )[0];
  return { title, instructions, assets, experimentVariantId: attributed?.variantId ?? null };
}

function AssetCard({ asset }: { asset: CampaignAsset }) {
  return (
    <li className="pp-asset">
      <div className="pp-asset__head">
        <h3 className="pp-asset__title">{asset.title}</h3>
        <span className="cluster" style={{ ['--cluster-gap' as string]: 'var(--space-2)' }}>
          <Badge size="sm">{humanize(asset.type)}</Badge>
          {asset.platform && <PlatformTag platform={asset.platform} />}
        </span>
      </div>
      {asset.type === 'Image' && isSafeHref(asset.url) && (
        <img src={asset.url} alt={asset.title} className="pp-asset__image" loading="lazy" />
      )}
      {asset.type === 'Caption' && asset.body && (
        <>
          <p className="pp-caption">{asset.body}</p>
          <div className="pp-actions">
            <CopyButton text={asset.body} label={`caption “${asset.title}”`} />
          </div>
        </>
      )}
      {asset.type !== 'Caption' && asset.body && <p className="pp-caption">{asset.body}</p>}
      {isSafeHref(asset.url) && (
        <div className="pp-actions">
          <SafeExternalLink className="ui-link pp-link-icon" href={asset.url}>
            {asset.type === 'Image' ? 'Open full size' : 'Open'}
            <ExternalLink aria-hidden="true" className="pp-inline-icon" />
            <span className="visually-hidden"> (opens in a new tab)</span>
          </SafeExternalLink>
        </div>
      )}
    </li>
  );
}

function RewardTermsCard({ terms }: { terms: RewardTerms }) {
  const c = terms.currency;
  const items = [
    { label: 'Base reward per approved post', value: <Money amount={terms.baseAmount} currency={c} /> },
    { label: 'Posts per participant', value: pluralize(terms.maxSubmissionsPerParticipant, 'post') },
    {
      label: 'Post must stay live',
      value: terms.minPostLiveHours > 0 ? pluralize(terms.minPostLiveHours, 'hour') : 'No minimum',
    },
    { label: 'Screenshot', value: terms.requireScreenshot ? 'Required' : 'Optional' },
  ];
  if (terms.dailyCap != null)
    items.push({ label: 'Daily cap', value: <Money amount={terms.dailyCap} currency={c} /> });
  if (terms.weeklyCap != null)
    items.push({ label: 'Weekly cap', value: <Money amount={terms.weeklyCap} currency={c} /> });
  if (terms.campaignCap != null)
    items.push({
      label: 'Campaign cap per participant',
      value: <Money amount={terms.campaignCap} currency={c} />,
    });
  items.push({ label: 'Reward rules version', value: `v${terms.ruleSetVersion}` });

  return (
    <Card as="section" aria-labelledby="reward-terms-title">
      <CardHeader titleId="reward-terms-title" title="Reward terms" headingLevel={2} />
      <CardBody className="stack">
        <KeyValueList layout="inline" items={items} />
        {terms.overrides.length > 0 && (
          <div className="stack" style={{ ['--stack-gap' as string]: 'var(--space-2)' }}>
            <h3 className="ui-card__title">Special rates</h3>
            <ul className="pp-list">
              {terms.overrides.map((o, i) => (
                <li key={i} className="pp-list__item pp-list__item--row">
                  <span className="pp-list__main">
                    <span>
                      {o.label ??
                        [o.platform && platformLabel(o.platform), o.countryCode, o.tier]
                          .filter(Boolean)
                          .join(' · ')}
                    </span>
                    {(o.validFrom || o.validTo) && (
                      <span className="pp-list__meta">
                        {o.validFrom && (
                          <span>
                            From <DateTime value={o.validFrom} format="date" />
                          </span>
                        )}
                        {o.validTo && (
                          <span>
                            Until <DateTime value={o.validTo} format="date" />
                          </span>
                        )}
                      </span>
                    )}
                  </span>
                  <Money amount={o.amount} currency={c} />
                </li>
              ))}
            </ul>
          </div>
        )}
        {terms.bonuses.length > 0 && (
          <div className="stack" style={{ ['--stack-gap' as string]: 'var(--space-2)' }}>
            <h3 className="ui-card__title">Bonuses</h3>
            <ul className="pp-list">
              {terms.bonuses.map((b, i) => (
                <li key={i} className="pp-list__item pp-list__item--row">
                  <span className="pp-list__main">
                    <span>{b.label ?? earningTypeLabel(b.type)}</span>
                    <span className="pp-list__meta">
                      {b.approvalMode === 'ManualApproval'
                        ? 'Awarded at the reviewer’s discretion'
                        : 'Added automatically'}
                      {b.validTo && (
                        <span>
                          Until <DateTime value={b.validTo} format="date" />
                        </span>
                      )}
                    </span>
                  </span>
                  <Money amount={b.amount} currency={c} signDisplay="always" />
                </li>
              ))}
            </ul>
          </div>
        )}
        <p className="pp-note">
          Rewards are calculated by Optimize All when your post is approved, using the rules version in force
          when you submitted.
        </p>
      </CardBody>
    </Card>
  );
}

/**
 * Personal tracking link. Shown only for campaigns with tracking enabled; an existing link is read from
 * `GET /me/tracking-links`, and a new one is created only when the participant asks for it.
 */
function TrackingSection({ campaignId }: { campaignId: string }) {
  const links = useMyTrackingLinks(true);
  const create = useCreateTrackingLink();
  const existing = links.data?.find((l) => l.campaignId === campaignId) ?? null;
  const link = existing ?? (create.data?.campaignId === campaignId ? create.data : null);
  return (
    <Card as="section" aria-labelledby="tracking-title">
      <CardHeader
        titleId="tracking-title"
        title="Your tracking link"
        description="Add this link to your post or bio. Clicks and verified conversions are measured and shown here."
      />
      <CardBody className="stack">
        {!link ? (
          <>
            {create.isError && (
              <Alert tone="danger" role="alert">
                {isApiError(create.error) && create.error.code === 'tracking.not_enabled'
                  ? 'Tracking links are no longer available for this campaign.'
                  : errorMessage(create.error)}
              </Alert>
            )}
            <div>
              <Button
                leadingIcon={<Link2 />}
                loading={create.isPending}
                disabled={links.isPending}
                onClick={() => create.mutate(campaignId)}
              >
                Get my tracking link
              </Button>
            </div>
          </>
        ) : (
          <TrackingLinkDetails link={link} />
        )}
      </CardBody>
    </Card>
  );
}

function TrackingLinkDetails({ link }: { link: TrackingLink }) {
  return (
    <>
      <CopyField label="Tracking link" value={link.shortUrl} />
      <KeyValueList
        layout="inline"
        items={[
          { label: 'Clicks', value: <span className="tabular">{link.stats.clicks}</span> },
          {
            label: 'Unique clicks',
            value: <span className="tabular">{link.stats.uniqueClicks}</span>,
          },
          {
            label: 'Verified conversions',
            value: <span className="tabular">{link.stats.verifiedConversions}</span>,
          },
        ]}
      />
    </>
  );
}

function submitBlocker(campaign: CampaignDetail): string | null {
  if (!campaign.isOpenForSubmissions)
    return campaign.upcoming
      ? 'This campaign hasn’t started yet.'
      : 'This campaign is not accepting submissions.';
  if (campaign.remainingSubmissions <= 0) return 'You’ve used all your submissions for this campaign.';
  if (!campaign.eligibility.isEligible)
    return campaign.eligibility.reasons[0]?.message ?? 'You’re not eligible for this campaign.';
  if (!campaign.eligibility.accounts.some((a) => a.isEligible))
    return 'None of your social profiles are eligible for this campaign yet.';
  return null;
}

function CampaignDetailView({ campaign }: { campaign: CampaignDetail }) {
  const variants = useExperimentVariants(campaign.id);
  const view = applyVariants(campaign, variants.data ?? []);
  const [submitOpen, setSubmitOpen] = useState(false);
  const blocker = submitBlocker(campaign);
  const hashtags = campaign.requiredHashtags?.trim();
  const mentions = campaign.requiredMentions?.trim();

  return (
    <div className="pp-page pp-page--cta">
      <PageHeader
        title={view.title}
        breadcrumbs={[{ label: 'Campaigns', to: '/app/campaigns' }, { label: view.title }]}
        eyebrow={campaign.category?.name}
        description={campaign.summary}
        meta={
          <>
            <StatusBadge kind="campaign" status={campaign.status} />
            {campaign.visibility === 'InviteOnly' && <Badge tone="brand">Invite only</Badge>}
            <PlatformList platforms={campaign.platforms} />
          </>
        }
        actions={
          <div className="pp-detail-cta">
            <Button
              variant="highlight"
              size="lg"
              onClick={() => setSubmitOpen(true)}
              disabled={!!blocker}
              aria-describedby={blocker ? 'submit-blocker' : undefined}
            >
              Submit proof
            </Button>
            <p className="text-small pp-muted pp-detail-cta__note" id="submit-blocker">
              {blocker ?? `${pluralize(campaign.remainingSubmissions, 'submission')} left`}
            </p>
          </div>
        }
      />

      {campaign.heroImageUrl && <img src={campaign.heroImageUrl} alt="" className="pp-hero-image" />}

      <div className="pp-two-col">
        <div className="pp-page" style={{ gap: 'var(--space-6)' }}>
          {campaign.disclosures.length > 0 && (
            <Alert
              tone="warning"
              icon={<ShieldAlert />}
              title="Paid-content disclosure required"
              className="pp-callout-strong"
            >
              <p>This is a paid collaboration. Every post must include the disclosure for its platform:</p>
              <ul
                className="stack"
                style={{
                  ['--stack-gap' as string]: 'var(--space-2)',
                  listStyle: 'none',
                  padding: 0,
                  marginTop: 'var(--space-2)',
                }}
              >
                {campaign.disclosures.map((d) => (
                  <li key={d.platform} className="pp-disclosure">
                    <PlatformTag platform={d.platform} />
                    <strong className="pp-break">{d.text}</strong>
                    <CopyButton text={d.text} label={`${platformLabel(d.platform)} disclosure`} />
                  </li>
                ))}
              </ul>
            </Alert>
          )}

          <Card as="section" aria-labelledby="about-title">
            <CardHeader titleId="about-title" title="About this campaign" />
            <CardBody>
              <p className="pp-prewrap">{campaign.landingBody ?? campaign.description}</p>
              {campaign.landingBody && campaign.description && (
                <p className="pp-prewrap" style={{ marginTop: 'var(--space-3)' }}>
                  {campaign.description}
                </p>
              )}
              {campaign.topics.length > 0 && (
                <ul className="pp-chip-list" aria-label="Topics" style={{ marginTop: 'var(--space-4)' }}>
                  {campaign.topics.map((t) => (
                    <li key={t} className="pp-chip">
                      #{t}
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="instructions-title">
            <CardHeader titleId="instructions-title" title="How to post" />
            <CardBody className="stack">
              <p className="pp-prewrap">{view.instructions}</p>
              {hashtags && <CopyField label="Required hashtags" value={hashtags} />}
              {mentions && <CopyField label="Required mentions" value={mentions} />}
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="assets-title">
            <CardHeader
              titleId="assets-title"
              title="Approved content"
              description="Only share the content provided here. Don’t edit images or change the meaning of captions."
            />
            <CardBody>
              {view.assets.length === 0 ? (
                <EmptyState
                  compact
                  headingLevel={3}
                  icon={<Megaphone />}
                  title="No assets for this campaign"
                />
              ) : (
                <ul
                  className="pp-grid"
                  style={{ listStyle: 'none', padding: 0, margin: 0, ['--pp-grid-min' as string]: '240px' }}
                >
                  {view.assets.map((asset) => (
                    <AssetCard key={asset.id} asset={asset} />
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>

          {campaign.trackingEnabled && <TrackingSection campaignId={campaign.id} />}
        </div>

        <aside
          className="pp-page"
          style={{ gap: 'var(--space-6)' }}
          aria-label="Campaign terms and your status"
        >
          {campaign.yourRate && <YourRateCard rate={campaign.yourRate} />}
          {campaign.rewardTerms ? (
            <RewardTermsCard terms={campaign.rewardTerms} />
          ) : (
            <Alert tone="neutral" title="Reward to be announced">
              This campaign has no reward rules yet, so submissions aren’t open.
            </Alert>
          )}

          <Card as="section" aria-labelledby="dates-title">
            <CardHeader
              titleId="dates-title"
              title="Dates"
              description={`Shown in your time zone. The campaign runs on ${campaign.timeZone} time.`}
            />
            <CardBody>
              <KeyValueList
                layout="inline"
                items={[
                  { label: 'Starts', value: <DateTime value={campaign.startsAt} withZone /> },
                  { label: 'Ends', value: <DateTime value={campaign.endsAt} withZone /> },
                  {
                    label: 'Submission deadline',
                    value: (
                      <span>
                        <DateTime value={campaign.submissionDeadline} withZone />
                        <br />
                        <span className="text-small pp-muted">
                          <DateTime value={campaign.submissionDeadline} format="relative" />
                        </span>
                      </span>
                    ),
                  },
                ]}
              />
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="eligibility-title">
            <CardHeader
              titleId="eligibility-title"
              title="Your eligibility"
              actions={
                <Link to="/app/social-accounts" className="ui-link text-small">
                  Manage profiles
                </Link>
              }
            />
            <CardBody className="stack">
              {campaign.eligibility.reasons.length > 0 && (
                <Alert tone="warning" title="You’re not eligible for this campaign">
                  <ul>
                    {campaign.eligibility.reasons.map((r) => (
                      <li key={r.code}>{r.message}</li>
                    ))}
                  </ul>
                </Alert>
              )}
              {campaign.eligibility.accounts.length === 0 ? (
                <p className="pp-muted">
                  You haven’t added a social profile yet.{' '}
                  <Link to="/app/social-accounts?add=1" className="ui-link">
                    Add one
                  </Link>
                  .
                </p>
              ) : (
                <ul className="pp-list">
                  {campaign.eligibility.accounts.map((a) => (
                    <li key={a.socialAccountId} className="pp-list__item pp-list__item--row">
                      <span className="pp-list__main">
                        <span className="cluster" style={{ ['--cluster-gap' as string]: 'var(--space-2)' }}>
                          <PlatformTag platform={a.platform} />
                          <span className="pp-list__title">@{a.handle}</span>
                        </span>
                        {a.reasons.map((r) => (
                          <span key={r.code} className="text-small pp-muted">
                            {r.message}
                          </span>
                        ))}
                        {a.eligibleFrom && (
                          <span className="text-small">
                            Eligible from <DateTime value={a.eligibleFrom} format="date" />
                          </span>
                        )}
                      </span>
                      {a.isEligible ? (
                        <Badge tone="success" icon={<CircleCheck size={14} />}>
                          Eligible
                        </Badge>
                      ) : (
                        <Badge tone="warning" icon={<CircleSlash size={14} />}>
                          Not eligible
                        </Badge>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="my-submissions-title">
            <CardHeader titleId="my-submissions-title" title="Your submissions" />
            <CardBody>
              {campaign.mySubmissions.length === 0 ? (
                <p className="pp-muted">You haven’t submitted a post for this campaign yet.</p>
              ) : (
                <ul className="pp-list">
                  {campaign.mySubmissions.map((s) => (
                    <li key={s.id} className="pp-list__item pp-list__item--row">
                      <Link to={`/app/submissions/${s.id}`} className="ui-link">
                        Submitted <DateTime value={s.submittedAt} format="date" />
                      </Link>
                      <StatusBadge kind="submission" status={s.status} size="sm" />
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>
        </aside>
      </div>

      {submitOpen && (
        <SubmitProofDialog
          open={submitOpen}
          onClose={() => setSubmitOpen(false)}
          campaign={campaign}
          experimentVariantId={view.experimentVariantId}
        />
      )}
    </div>
  );
}

export function CampaignDetailPage() {
  const { slug = '' } = useParams();
  const campaign = useCampaign(slug);
  return (
    <QueryState
      query={campaign}
      errorTitle="This campaign isn’t available"
      loading={
        <div className="pp-page">
          <Skeleton height={48} width="60%" />
          <Skeleton height={240} />
          <Skeleton height={180} />
        </div>
      }
    >
      {(data) => <CampaignDetailView campaign={data} />}
    </QueryState>
  );
}
