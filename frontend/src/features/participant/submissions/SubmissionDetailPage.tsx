import { ExternalLink } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';
import { ProtectedImage } from '@/components/ProtectedImage';
import { Alert } from '@/components/ui/Alert';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { Timeline, type TimelineItem } from '@/components/ui/Timeline';
import { Badge } from '@/components/ui/Badge';
import type { Tone } from '@/components/ui/tones';
import { useSubmission } from '../api/queries';
import type { SubmissionDetail } from '../api/types';
import { PlatformTag } from '../components/Platform';
import { QueryState } from '../components/QueryState';
import { earningTypeLabel, timelineActionLabel, timelineTone } from '../lib/labels';
import { AppealForm, ResubmitForm, WithdrawAction } from './SubmissionForms';
import '../participant.css';
import { SafeExternalLink } from '@/components/SafeExternalLink';

const DECISION_TONE: Record<string, Tone> = {
  NeedsCorrection: 'warning',
  Rejected: 'danger',
  Reversed: 'danger',
  Approved: 'success',
};

const LIVE_CHECK_TEXT: Record<string, { tone: Tone; label: string }> = {
  NotRequired: { tone: 'neutral', label: 'Not required' },
  Pending: { tone: 'info', label: 'Scheduled' },
  ConfirmedLive: { tone: 'success', label: 'Confirmed live' },
  Removed: { tone: 'danger', label: 'Post removed' },
};

const APPEAL_TONE: Record<string, Tone> = {
  Open: 'info',
  Upheld: 'neutral',
  Overturned: 'success',
  Withdrawn: 'neutral',
};

const APPEAL_TEXT: Record<string, string> = {
  Open: 'Under review',
  Upheld: 'Decision upheld',
  Overturned: 'Decision overturned',
  Withdrawn: 'Withdrawn',
};

function SubmissionView({ s }: { s: SubmissionDetail }) {
  const timeline: TimelineItem[] = s.timeline.map((entry, i) => ({
    id: `${entry.at}-${i}`,
    title: timelineActionLabel(entry.action),
    description: entry.reason ? <p className="pp-prewrap">{entry.reason}</p> : undefined,
    timestamp: entry.at,
    actor: entry.actor,
    tone: timelineTone(entry.toStatus),
  }));
  const live = LIVE_CHECK_TEXT[s.liveCheck.status] ?? { tone: 'neutral' as Tone, label: s.liveCheck.status };

  return (
    <div className="pp-page">
      <PageHeader
        title={s.campaign.title}
        eyebrow="Submission"
        breadcrumbs={[{ label: 'My submissions', to: '/app/submissions' }, { label: s.campaign.title }]}
        meta={
          <>
            <StatusBadge kind="submission" status={s.status} />
            <PlatformTag platform={s.platform} />
            {s.correctionCount > 0 && <Badge tone="neutral">Corrected {s.correctionCount}×</Badge>}
          </>
        }
        actions={
          <>
            {s.canWithdraw && <WithdrawAction submission={s} />}
            <Link to={`/app/campaigns/${s.campaign.slug}`} className="ui-link">
              View campaign
            </Link>
          </>
        }
      />

      {s.status === 'Withdrawn' && (
        <Alert tone="neutral" title="You withdrew this submission">
          <p>It won’t be reviewed or earn a reward. You can submit the post again from the campaign page.</p>
        </Alert>
      )}

      {s.decisionReason &&
        (s.status === 'NeedsCorrection' || s.status === 'Rejected' || s.status === 'Reversed') && (
          <Alert
            tone={DECISION_TONE[s.status] ?? 'info'}
            title={
              s.status === 'NeedsCorrection'
                ? 'The reviewer asked for a correction'
                : 'Reason for the decision'
            }
          >
            <p className="pp-prewrap">{s.decisionReason}</p>
          </Alert>
        )}

      <div className="pp-two-col">
        <div className="pp-page" style={{ gap: 'var(--space-6)' }}>
          {s.canEdit && <ResubmitForm submission={s} />}
          {s.canAppeal && <AppealForm submission={s} />}

          <Card as="section" aria-labelledby="post-title">
            <CardHeader titleId="post-title" title="Your post" />
            <CardBody className="stack">
              <KeyValueList
                items={[
                  {
                    label: 'Post link',
                    value: (
                      <SafeExternalLink
                        href={s.postUrl}
                        className="ui-link pp-break pp-link-icon"
                        fallback={<span className="pp-break">{s.postUrl}</span>}
                      >
                        {s.postUrl}
                        <ExternalLink aria-hidden="true" className="pp-inline-icon" />
                        <span className="visually-hidden"> (opens in a new tab)</span>
                      </SafeExternalLink>
                    ),
                  },
                  { label: 'Profile', value: `@${s.socialAccount.handle}` },
                  { label: 'Went live', value: <DateTime value={s.postedAt} withZone /> },
                  { label: 'Submitted', value: <DateTime value={s.submittedAt} withZone /> },
                ]}
              />
              {s.captionText && (
                <div>
                  <h3 className="ui-card__title">Caption</h3>
                  <p className="pp-caption" style={{ marginTop: 'var(--space-2)' }}>
                    {s.captionText}
                  </p>
                </div>
              )}
              <div className="stack" style={{ ['--stack-gap' as string]: 'var(--space-2)' }}>
                <h3 className="ui-card__title">Screenshot</h3>
                {s.screenshotUrl ? (
                  <ProtectedImage
                    src={s.screenshotUrl}
                    alt="Screenshot you submitted of the post"
                    className="pp-screenshot"
                  />
                ) : (
                  <p className="pp-muted">No screenshot was attached.</p>
                )}
                <p className="pp-note">
                  Screenshots are evidence for the reviewer; the live post at your link is what’s checked.
                </p>
              </div>
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="history-title">
            <CardHeader titleId="history-title" title="Status history" />
            <CardBody>
              <Timeline items={timeline} label="Submission status history" />
            </CardBody>
          </Card>
        </div>

        <aside className="pp-page" style={{ gap: 'var(--space-6)' }} aria-label="Reward and checks">
          <Card as="section" aria-labelledby="reward-title">
            <CardHeader titleId="reward-title" title="Reward" />
            <CardBody className="stack">
              <KeyValueList
                layout="inline"
                items={[
                  {
                    label: 'Estimated reward',
                    value: <Money amount={s.estimatedReward} currency={s.currency} />,
                  },
                  { label: 'Reward rules version', value: `v${s.rewardRuleSetVersion}` },
                  ...(s.rateKind
                    ? [
                        {
                          label: 'Rate',
                          value:
                            s.rateKind === 'Personal'
                              ? 'Your personal rate (locked when you submitted)'
                              : 'Your special rate (locked when you submitted)',
                        },
                      ]
                    : []),
                  ...(s.decidedAt ? [{ label: 'Decided', value: <DateTime value={s.decidedAt} /> }] : []),
                ]}
              />
              <h3 className="ui-card__title">Earnings created</h3>
              {s.earnings.length === 0 ? (
                <p className="pp-muted text-small">
                  Earnings are created when the post is approved. The estimate above can change with caps and
                  bonuses.
                </p>
              ) : (
                <ul className="pp-list">
                  {s.earnings.map((e) => (
                    <li key={e.id} className="pp-list__item pp-list__item--row">
                      <span className="pp-list__main">
                        <span>{earningTypeLabel(e.type)}</span>
                        <StatusBadge kind="earning" status={e.status} size="sm" />
                      </span>
                      <Money amount={e.amount} currency={e.currency} colored />
                    </li>
                  ))}
                </ul>
              )}
            </CardBody>
          </Card>

          <Card as="section" aria-labelledby="live-check-title">
            <CardHeader titleId="live-check-title" title="Live check" />
            <CardBody className="stack">
              <Badge tone={live.tone} dot>
                {live.label}
              </Badge>
              {s.liveCheck.status === 'Pending' && s.liveCheck.dueAt && (
                <p className="text-small">
                  We’ll confirm the post is still public after{' '}
                  <strong>
                    <DateTime value={s.liveCheck.dueAt} withZone />
                  </strong>{' '}
                  (<DateTime value={s.liveCheck.dueAt} format="relative" />
                  ). Keep it online until then — earnings stay pending until the check passes.
                </p>
              )}
              {s.liveCheck.checkedAt && (
                <p className="text-small pp-muted">
                  Checked <DateTime value={s.liveCheck.checkedAt} />
                </p>
              )}
            </CardBody>
          </Card>

          {s.appeal && (
            <Card as="section" aria-labelledby="appeal-status-title">
              <CardHeader titleId="appeal-status-title" title="Your appeal" />
              <CardBody className="stack">
                <Badge tone={APPEAL_TONE[s.appeal.status] ?? 'neutral'} dot>
                  {APPEAL_TEXT[s.appeal.status] ?? s.appeal.status}
                </Badge>
                <p className="text-small pp-prewrap">{s.appeal.reason}</p>
                <p className="text-small pp-muted">
                  Filed <DateTime value={s.appeal.createdAt} />
                </p>
                {s.appeal.resolutionNote && (
                  <Alert tone="neutral" title="Reviewer’s note">
                    <p className="pp-prewrap">{s.appeal.resolutionNote}</p>
                  </Alert>
                )}
              </CardBody>
            </Card>
          )}

          <p className="text-small pp-muted">
            Questions about this submission?{' '}
            <Link to={`/app/support/new?submission=${s.id}`} className="ui-link">
              Contact support
            </Link>
          </p>
        </aside>
      </div>
    </div>
  );
}

export function SubmissionDetailPage() {
  const { id = '' } = useParams();
  const query = useSubmission(id);
  return (
    <QueryState
      query={query}
      errorTitle="This submission isn’t available"
      loading={
        <div className="pp-page">
          <Skeleton height={48} width="50%" />
          <Skeleton height={300} />
        </div>
      }
    >
      {(data) => <SubmissionView s={data} />}
    </QueryState>
  );
}
