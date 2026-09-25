import { Share2, Users } from 'lucide-react';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { CopyField } from '@/components/ui/CopyField';
import { StatGrid } from '@/components/ui/Dashboard';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Stat } from '@/components/ui/Stat';
import { StatusBadge } from '@/components/ui/StatusBadge';
import type { Tone } from '@/components/ui/tones';
import { useToast } from '@/components/ui/toastContext';
import { pluralize } from '@/lib/format/text';
import { useReferrals } from '../api/queries';
import type { Referrals } from '../api/types';
import { QueryState } from '../components/QueryState';
import { qualifyingActionLabel } from '../lib/labels';
import '../participant.css';

const REFERRAL_TONE: Record<string, Tone> = {
  Registered: 'info',
  Qualified: 'success',
  Rejected: 'danger',
  Expired: 'neutral',
};

function ShareButton({ link }: { link: string }) {
  const toast = useToast();
  if (typeof navigator === 'undefined' || typeof navigator.share !== 'function') return null;
  return (
    <Button
      variant="secondary"
      leadingIcon={<Share2 />}
      onClick={async () => {
        try {
          await navigator.share({ title: 'Join me on Optimize All', url: link });
        } catch (error) {
          if (!(error instanceof DOMException && error.name === 'AbortError')) {
            toast.error('Couldn’t open the share sheet', 'Copy the link instead.');
          }
        }
      }}
    >
      Share
    </Button>
  );
}

function ReferralsView({ data }: { data: Referrals }) {
  const p = data.program;
  return (
    <div className="pp-page">
      <PageHeader title="Referrals" description="Invite people who’d enjoy sharing brands they believe in." />

      <Card as="section" aria-labelledby="referral-link-title" className="pp-invite">
        <CardHeader titleId="referral-link-title" title="Your referral link" />
        <CardBody className="stack">
          <CopyField label="Referral link" value={data.link} />
          <div className="pp-form-grid" style={{ alignItems: 'end' }}>
            <CopyField label="Referral code" value={data.code} />
            <div className="pp-actions">
              <ShareButton link={data.link} />
            </div>
          </div>
        </CardBody>
      </Card>

      <Card as="section" aria-labelledby="program-title">
        <CardHeader titleId="program-title" title="Program terms" />
        <CardBody className="stack">
          {p.enabled ? (
            <>
              <p>
                You earn <Money amount={p.rewardAmount} currency={p.currency} /> for each person you refer,
                but <strong>only after they {qualifyingActionLabel(p.qualifyingAction)}</strong> within{' '}
                {pluralize(p.qualifyWithinDays, 'day')} of signing up.
              </p>
              <ul className="text-small pp-muted" style={{ paddingLeft: 'var(--space-5)' }}>
                <li>Signing up alone doesn’t earn a reward.</li>
                <li>
                  Rewards may be reviewed before approval, and self-referrals or duplicate accounts are
                  rejected.
                </li>
                <li>Approved rewards are paid with your regular payouts.</li>
              </ul>
            </>
          ) : (
            <Alert tone="neutral">
              Referral rewards are paused right now. You can still share your link; people who join will be
              linked to you.
            </Alert>
          )}
        </CardBody>
      </Card>

      <section aria-labelledby="referral-stats-title" className="pp-section">
        <h2 id="referral-stats-title" className="pp-section__title">
          Your referrals
        </h2>
        <StatGrid strip min="160px">
          <Stat label="Signed up" measurement="Count" value={data.stats.registered} />
          <Stat label="Qualified" measurement="Count" value={data.stats.qualified} />
          <Stat label="Rewarded" measurement="Count" value={data.stats.rewarded} />
          <Stat label="Reward pending" measurement="Count" value={data.stats.pendingReward} />
        </StatGrid>
        <DataTable
          caption="People you referred"
          rows={data.items}
          getRowId={(r) => r.id}
          columns={[
            { id: 'name', header: 'Person', primary: true, cell: (r) => r.maskedName },
            {
              id: 'status',
              header: 'Status',
              cell: (r) => (
                <Badge tone={REFERRAL_TONE[r.status] ?? 'neutral'} dot>
                  {r.status}
                </Badge>
              ),
            },
            {
              id: 'registered',
              header: 'Signed up',
              cell: (r) => <DateTime value={r.registeredAt} format="date" />,
            },
            {
              id: 'qualify',
              header: 'Qualified',
              cell: (r) =>
                r.qualifiedAt ? (
                  <DateTime value={r.qualifiedAt} format="date" />
                ) : r.status === 'Registered' ? (
                  <span className="text-small">
                    By <DateTime value={r.qualifyBy} format="date" />
                  </span>
                ) : (
                  '—'
                ),
            },
            {
              id: 'reward',
              header: 'Your reward',
              cell: (r) => (r.rewardStatus ? <StatusBadge kind="earning" status={r.rewardStatus} /> : '—'),
            },
          ]}
          emptyState={
            <EmptyState
              icon={<Users />}
              headingLevel={3}
              title="No referrals yet"
              description="Share your link — people who sign up with it appear here."
            />
          }
        />
      </section>
    </div>
  );
}

export function ReferralsPage() {
  const query = useReferrals();
  return (
    <QueryState query={query} errorTitle="Your referrals couldn’t be loaded">
      {(data) => <ReferralsView data={data} />}
    </QueryState>
  );
}
