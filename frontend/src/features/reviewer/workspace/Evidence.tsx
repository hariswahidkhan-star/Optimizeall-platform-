import { Expand, ExternalLink as ExternalIcon, ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ProtectedImage, useProtectedImageUrl } from '@/components/ProtectedImage';
import {
  Alert,
  Badge,
  Button,
  buttonClasses,
  Card,
  CardBody,
  CardHeader,
  DateTime,
  Dialog,
  KeyValueList,
  Money,
  StatusBadge,
  Timeline,
  type TimelineItem,
  type Tone,
} from '@/components/ui';
import { formatNumber } from '@/lib/format/money';
import { humanize, pluralize } from '@/lib/format/text';
import type { ReviewDetail, ReviewEvent, RewardQuote } from '../api/types';
import { ExternalLink } from '../components/common';
import { flagLabel, flagTone, RiskBadge } from '../components/risk';
import { workspacePath } from '../hooks/useReviewActions';

/** Risk score and every flag with its weight and detail. Shown first: flags are what a reviewer must rule out. */
export function RiskPanel({ detail }: { detail: ReviewDetail }) {
  const { flags, submission } = detail;
  const open = flags.filter((f) => !f.resolved);
  return (
    <Card
      as="section"
      aria-labelledby="rv-risk-heading"
      className={open.length > 0 ? 'rv-risk rv-risk--flagged' : 'rv-risk'}
    >
      <CardHeader
        title="Risk"
        titleId="rv-risk-heading"
        actions={<RiskBadge score={submission.riskScore} />}
        description={
          open.length > 0
            ? `${pluralize(open.length, 'unresolved flag')} — rule each one out before approving.`
            : flags.length > 0
              ? 'All flags were resolved by a decision.'
              : 'No risk flags were raised.'
        }
      />
      <CardBody className="stack">
        {flags.length > 0 && (
          <ul className="rv-flag-list">
            {flags.map((flag) => (
              <li key={flag.id} className="rv-flag" data-resolved={flag.resolved || undefined}>
                <div className="rv-flag__head">
                  <Badge tone={flag.resolved ? 'neutral' : flagTone(flag.type)} icon={<ShieldAlert />}>
                    {flagLabel(flag.type)}
                  </Badge>
                  <span className="text-small tabular">+{flag.weight}</span>
                  {flag.resolved && (
                    <Badge size="sm" tone="neutral">
                      Resolved
                    </Badge>
                  )}
                </div>
                <p className="text-small">{flag.detail}</p>
                {flag.resolved && flag.resolutionNote && (
                  <p className="text-small text-muted">Resolution: {humanize(flag.resolutionNote)}</p>
                )}
              </li>
            ))}
          </ul>
        )}
        <p className="text-small text-muted">
          A screenshot is evidence for review, not proof. Open the live post and compare it with the
          requirements.
        </p>
      </CardBody>
    </Card>
  );
}

function ScreenshotViewer({ src }: { src: string }) {
  const [zoom, setZoom] = useState(false);
  const full = useProtectedImageUrl(src);
  return (
    <div className="rv-shot">
      <button
        type="button"
        className="rv-shot__button"
        onClick={() => setZoom(true)}
        aria-label="Zoom screenshot"
      >
        <ProtectedImage
          src={src}
          alt="Screenshot submitted as evidence of the post"
          className="rv-shot__img"
        />
      </button>
      <div className="cluster">
        <Button size="sm" variant="secondary" leadingIcon={<Expand />} onClick={() => setZoom(true)}>
          Zoom
        </Button>
        {full.url && (
          <a
            href={full.url}
            target="_blank"
            rel="noopener noreferrer"
            className={buttonClasses('ghost', 'sm')}
          >
            <span className="ui-button__content">
              <ExternalIcon aria-hidden="true" />
              Open full size<span className="visually-hidden"> (opens in a new tab)</span>
            </span>
          </a>
        )}
      </div>
      <Dialog open={zoom} onClose={() => setZoom(false)} title="Screenshot" size="lg">
        <div className="rv-shot__zoom">
          <ProtectedImage src={src} alt="Screenshot submitted as evidence of the post, full size" />
        </div>
      </Dialog>
    </div>
  );
}

/** The post itself: URL, screenshot, caption and dates. */
export function PostPanel({ detail }: { detail: ReviewDetail }) {
  const { submission: s, requirements: r } = detail;
  const posted = new Date(s.postedAt).getTime();
  const outsideWindow = posted < new Date(r.startsAt).getTime() || posted > new Date(r.endsAt).getTime();
  const lagHours = (new Date(s.submittedAt).getTime() - posted) / 3_600_000;
  const normalizedDiffers = s.normalizedPostUrl !== s.postUrl;
  return (
    <Card as="section" aria-labelledby="rv-post-heading">
      <CardHeader
        title="Submitted post"
        titleId="rv-post-heading"
        actions={<StatusBadge kind="submission" status={s.status} />}
        description={`${s.platform} · ${pluralize(s.correctionCount, 'correction')}`}
      />
      <CardBody className="stack">
        <div>
          <h3 className="rv-subheading">Post URL</h3>
          <p className="rv-break">
            <ExternalLink href={s.postUrl}>{s.postUrl}</ExternalLink>
          </p>
          <p className="text-small text-muted rv-break">
            {normalizedDiffers ? 'Normalized: ' : 'Normalized URL is identical: '}
            <code>{s.normalizedPostUrl}</code>
          </p>
        </div>
        <div>
          <h3 className="rv-subheading">Screenshot</h3>
          {s.screenshotUrl ? (
            <ScreenshotViewer src={s.screenshotUrl} />
          ) : (
            <p className="text-muted">No screenshot was submitted.</p>
          )}
        </div>
        <div>
          <h3 className="rv-subheading">Caption</h3>
          {s.captionText ? (
            <blockquote className="rv-caption rv-prewrap">{s.captionText}</blockquote>
          ) : (
            <p className="text-muted">No caption provided.</p>
          )}
        </div>
        <KeyValueList
          layout="inline"
          items={[
            {
              label: 'Posted',
              value: (
                <span className="rv-inline">
                  <DateTime value={s.postedAt} format="both" />
                  {outsideWindow && (
                    <Badge size="sm" tone="warning">
                      Outside campaign window
                    </Badge>
                  )}
                </span>
              ),
            },
            {
              label: 'Submitted',
              value: (
                <span className="rv-inline">
                  <DateTime value={s.submittedAt} format="both" />
                  <span className="text-small text-muted">
                    {lagHours >= 0
                      ? `${lagHours.toFixed(1)} h after posting`
                      : 'before the stated posting time'}
                  </span>
                </span>
              ),
            },
            {
              label: 'Estimated reward',
              value: (
                <>
                  <Money amount={s.estimatedReward} currency={s.currency} /> · rules v{s.rewardRuleSetVersion}
                </>
              ),
            },
            ...(s.decidedAt
              ? [
                  {
                    label: 'Decision',
                    value: (
                      <>
                        {s.decidedBy?.displayName ?? 'System'} ·{' '}
                        <DateTime value={s.decidedAt} format="relative" />
                        {s.decisionReason && <p className="text-small">{s.decisionReason}</p>}
                      </>
                    ),
                  },
                ]
              : []),
            ...(s.liveCheckStatus !== 'NotRequired'
              ? [
                  {
                    label: 'Live check',
                    value: (
                      <>
                        {humanize(s.liveCheckStatus)}
                        {s.liveCheckDueAt && (
                          <>
                            {' · due '}
                            <DateTime value={s.liveCheckDueAt} />
                          </>
                        )}
                      </>
                    ),
                  },
                ]
              : []),
          ]}
        />
      </CardBody>
    </Card>
  );
}

const VERIFICATION_TONE: Record<string, Tone> = {
  Verified: 'success',
  Rejected: 'danger',
  PendingReview: 'info',
};

export function AccountPanel({ detail }: { detail: ReviewDetail }) {
  const { account: a, participant: p } = detail;
  const joinedDays = Math.floor((Date.now() - new Date(p.joinedAt).getTime()) / 86_400_000);
  return (
    <>
      <Card as="section" aria-labelledby="rv-account-heading">
        <CardHeader
          title="Social account"
          titleId="rv-account-heading"
          actions={
            <Badge tone={VERIFICATION_TONE[a.verificationStatus] ?? 'neutral'} dot>
              {humanize(a.verificationStatus)}
            </Badge>
          }
        />
        <CardBody>
          <KeyValueList
            layout="inline"
            items={[
              { label: 'Handle', value: <ExternalLink href={a.profileUrl}>@{a.handle}</ExternalLink> },
              { label: 'Platform', value: a.platform },
              {
                label: 'Account age',
                value: (
                  <>
                    {pluralize(a.accountAgeDays, 'day')}{' '}
                    <span className="text-muted text-small">
                      (created <DateTime value={a.accountCreatedAt} format="date" />)
                    </span>
                  </>
                ),
              },
              { label: 'Followers', value: <span className="tabular">{formatNumber(a.followerCount)}</span> },
              { label: 'Active', value: a.isActive ? 'Yes' : <Badge tone="warning">Deactivated</Badge> },
            ]}
          />
        </CardBody>
      </Card>
      <Card as="section" aria-labelledby="rv-participant-heading">
        <CardHeader title="Participant" titleId="rv-participant-heading" description={p.displayName} />
        <CardBody>
          <KeyValueList
            layout="inline"
            items={[
              { label: 'Email', value: <span className="rv-break">{p.email}</span> },
              { label: 'Tier', value: <Badge tone="brand">{p.tier}</Badge> },
              { label: 'Country', value: p.countryCode },
              {
                label: 'Joined',
                value: (
                  <>
                    <DateTime value={p.joinedAt} format="date" />{' '}
                    <span className="text-small text-muted">({joinedDays} days ago)</span>
                  </>
                ),
              },
              {
                label: 'Track record',
                value: (
                  <span className="rv-inline">
                    <Badge size="sm" tone="success">
                      {p.approvedCount} approved
                    </Badge>
                    <Badge size="sm" tone={p.rejectedCount > 0 ? 'warning' : 'neutral'}>
                      {p.rejectedCount} rejected
                    </Badge>
                    <Badge size="sm" tone={p.reversedCount > 0 ? 'danger' : 'neutral'}>
                      {p.reversedCount} reversed
                    </Badge>
                  </span>
                ),
              },
            ]}
          />
        </CardBody>
      </Card>
    </>
  );
}

const MATCH_LABEL: Record<string, string> = {
  screenshot: 'Same screenshot',
  content: 'Same content',
  screenshot_and_content: 'Same screenshot & content',
};

export function HistoryPanel({ detail }: { detail: ReviewDetail }) {
  const { relatedSubmissions: related, history } = detail;
  return (
    <>
      <Card as="section" aria-labelledby="rv-related-heading">
        <CardHeader
          title="Related submissions"
          titleId="rv-related-heading"
          description="Other submissions sharing this screenshot or content hash."
        />
        <CardBody>
          {related.length === 0 ? (
            <p className="text-muted">No other submission shares this screenshot or content.</p>
          ) : (
            <ul className="rv-list">
              {related.map((item) => (
                <li key={item.id} className="rv-list__item">
                  <div className="rv-cell-main">
                    <Link to={workspacePath(item.id)} className="ui-link">
                      {item.campaign.title}
                    </Link>
                    <span className="text-small text-muted">
                      {item.participant.displayName} · <DateTime value={item.submittedAt} format="relative" />
                    </span>
                  </div>
                  <span className="rv-inline">
                    <Badge size="sm" tone="danger">
                      {MATCH_LABEL[item.match] ?? humanize(item.match)}
                    </Badge>
                    <StatusBadge kind="submission" status={item.status} size="sm" />
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>
      <Card as="section" aria-labelledby="rv-history-heading">
        <CardHeader
          title="Participant’s submission history"
          titleId="rv-history-heading"
          description="Most recent first (up to 50)."
        />
        <CardBody>
          {history.length === 0 ? (
            <p className="text-muted">This is the participant’s first submission.</p>
          ) : (
            <ul className="rv-list">
              {history.map((item) => (
                <li key={item.id} className="rv-list__item">
                  <div className="rv-cell-main">
                    <Link to={workspacePath(item.id)} className="ui-link">
                      {item.campaign.title}
                    </Link>
                    <span className="text-small text-muted rv-break">{item.postUrl}</span>
                  </div>
                  <span className="rv-inline">
                    <StatusBadge kind="submission" status={item.status} size="sm" />
                    <span className="text-small text-muted">
                      <DateTime value={item.submittedAt} format="date" />
                    </span>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>
    </>
  );
}

const EVENT_TONE: Record<string, Tone> = {
  approved: 'success',
  appeal_overturned: 'success',
  live_check_confirmed: 'success',
  rejected: 'danger',
  reversed: 'danger',
  live_check_removed: 'danger',
  correction_requested: 'warning',
  appealed: 'info',
  claimed: 'info',
  withdrawn: 'neutral',
};

export function eventItems(events: ReviewEvent[]): TimelineItem[] {
  return events.map((e, i) => ({
    id: `${e.at}-${i}`,
    title: e.action === 'withdrawn' ? 'Withdrawn by participant' : humanize(e.action),
    timestamp: e.at,
    tone: EVENT_TONE[e.action] ?? 'neutral',
    actor: e.actor?.displayName ?? 'System',
    description: e.reason ?? undefined,
  }));
}

export function RewardQuoteView({ quote, title = 'Reward preview' }: { quote: RewardQuote; title?: string }) {
  return (
    <div className="stack">
      <h3 className="rv-subheading">{title}</h3>
      <table className="rv-quote">
        <caption className="visually-hidden">{title}</caption>
        <thead>
          <tr>
            <th scope="col">Line</th>
            <th scope="col" className="rv-num">
              Amount
            </th>
          </tr>
        </thead>
        <tbody>
          {quote.lines.map((line) => (
            <tr key={`${line.type}-${line.ruleId}`}>
              <th scope="row">
                {line.label}
                {line.requiresApproval && (
                  <Badge size="sm" tone="info">
                    Needs approval
                  </Badge>
                )}
              </th>
              <td className="rv-num">
                <Money amount={line.amount} currency={quote.currency} />
                {line.uncappedAmount !== line.amount && (
                  <span className="text-small text-muted">
                    {' '}
                    (was <Money amount={line.uncappedAmount} currency={quote.currency} />)
                  </span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row">Total</th>
            <td className="rv-num">
              <strong>
                <Money amount={quote.total} currency={quote.currency} />
              </strong>
            </td>
          </tr>
        </tfoot>
      </table>
      {quote.appliedCaps.length > 0 && (
        <Alert tone="warning" title="Caps applied">
          <ul className="rv-chips">
            {quote.appliedCaps.map((cap) => (
              <li key={cap}>
                <Badge size="sm" tone="warning">
                  {humanize(cap)}
                </Badge>
              </li>
            ))}
          </ul>
        </Alert>
      )}
      <p className="text-small text-muted">{quote.ruleSetSummary}</p>
    </div>
  );
}

export function ActivityPanel({ detail }: { detail: ReviewDetail }) {
  return (
    <>
      <Card as="section" aria-labelledby="rv-reward-heading">
        <CardHeader
          title="Reward"
          titleId="rv-reward-heading"
          description="Priced from the rule version captured at submission, with the participant’s current caps."
        />
        <CardBody className="stack">
          {detail.rewardQuote ? (
            <RewardQuoteView quote={detail.rewardQuote} />
          ) : (
            <p className="text-muted">The reward can’t be previewed for this rule version.</p>
          )}
          {detail.earnings.length > 0 && (
            <div>
              <h3 className="rv-subheading">Recorded earnings</h3>
              <ul className="rv-list">
                {detail.earnings.map((e) => (
                  <li key={e.id} className="rv-list__item">
                    <span>{humanize(e.type)}</span>
                    <span className="rv-inline">
                      <Money amount={e.amount} currency={e.currency} />
                      <StatusBadge kind="earning" status={e.status} size="sm" />
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </CardBody>
      </Card>
      {detail.appeals.length > 0 && (
        <Card as="section" aria-labelledby="rv-appeals-heading">
          <CardHeader title="Appeals" titleId="rv-appeals-heading" />
          <CardBody>
            <ul className="rv-list">
              {detail.appeals.map((a) => (
                <li key={a.id} className="rv-list__item rv-list__item--stack">
                  <span className="rv-inline">
                    <Badge
                      tone={a.status === 'Open' ? 'info' : a.status === 'Overturned' ? 'success' : 'neutral'}
                    >
                      {a.status}
                    </Badge>
                    <span className="text-small text-muted">
                      against {humanize(a.decisionAppealed)} ·{' '}
                      <DateTime value={a.createdAt} format="relative" />
                    </span>
                  </span>
                  <p className="text-small">{a.reason}</p>
                  {a.resolutionNote && (
                    <p className="text-small text-muted">
                      {a.resolvedBy?.displayName ?? 'Reviewer'}: {a.resolutionNote}
                    </p>
                  )}
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      )}
      <Card as="section" aria-labelledby="rv-events-heading">
        <CardHeader title="Event timeline" titleId="rv-events-heading" />
        <CardBody>
          {detail.events.length === 0 ? (
            <p className="text-muted">No events yet.</p>
          ) : (
            <Timeline label="Submission events" items={eventItems(detail.events)} />
          )}
        </CardBody>
      </Card>
    </>
  );
}
