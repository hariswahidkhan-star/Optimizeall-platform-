import { useQuery } from '@tanstack/react-query';
import { Ban, BadgeCheck, Layers, LogIn, ShieldCheck, UserCheck } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton, SkeletonText } from '@/components/ui/Skeleton';
import { Stat } from '@/components/ui/Stat';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { Timeline } from '@/components/ui/Timeline';
import { countryName } from '@/features/auth/localeOptions';
import { api } from '@/lib/api/client';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { formatNumber } from '@/lib/format/money';
import { humanize } from '@/lib/format/text';
import type { AdminUserDetail } from '../api/types';
import { AuditEntry } from '../audit/AuditEntry';
import { AdminBadge, roleLabel } from '../shared/badges';
import { QueryError, useCan } from '../shared/common';
import {
  ReactivateDialog,
  RolesDialog,
  SuspendDialog,
  TierDialog,
  type UserAction,
} from './UserActionDialogs';
import { canImpersonateTarget, ImpersonateDialog, TestBadge } from './TestUserDialogs';
import { SafeExternalLink } from '@/components/SafeExternalLink';

const HISTORY_TONE: Record<string, 'danger' | 'success' | 'info'> = {
  'admin.user_suspended': 'danger',
  'admin.user_reactivated': 'success',
};

function Section({ title, id, children }: { title: string; id: string; children: ReactNode }) {
  return (
    <Card as="section" aria-labelledby={id}>
      <CardHeader title={title} titleId={id} />
      <CardBody>{children}</CardBody>
    </Card>
  );
}

export function UserDetailPage() {
  const { userId = '' } = useParams();
  const { user: me, impersonation } = useAuth();
  const canImpersonate = useCan(Permissions.UsersImpersonate);
  const [impersonateOpen, setImpersonateOpen] = useState(false);
  const canSuspend = useCan(Permissions.UsersSuspend);
  const canAssign = useCan(Permissions.RolesAssign);
  const canTier = useCan(Permissions.UsersManage);
  const [action, setAction] = useState<UserAction | null>(null);

  const detail = useQuery({
    queryKey: ['admin', 'user', userId],
    queryFn: ({ signal }) => api.get<AdminUserDetail>(`/admin/users/${userId}`, { signal }),
  });

  const breadcrumbs = [
    { label: 'Users', to: '/admin/users' },
    { label: detail.data?.profile.displayName ?? 'User' },
  ];

  if (detail.isPending) {
    return (
      <>
        <PageHeader title={<Skeleton width={240} height={32} />} breadcrumbs={breadcrumbs} />
        <Card>
          <CardBody>
            <SkeletonText lines={6} />
          </CardBody>
        </Card>
      </>
    );
  }
  if (detail.isError) {
    return (
      <>
        <PageHeader title="User" breadcrumbs={breadcrumbs} />
        <QueryError error={detail.error} onRetry={() => void detail.refetch()} />
      </>
    );
  }

  const user = detail.data;
  const p = user.profile;
  const isSelf = me?.id === p.id;
  const suspended = p.status === 'Suspended';
  const close = () => setAction(null);

  const showImpersonate =
    canImpersonate &&
    !impersonation &&
    canImpersonateTarget({ id: p.id, roles: user.roles, status: p.status }, me?.id);

  const actions = (
    <>
      {showImpersonate && (
        <Button variant="secondary" leadingIcon={<LogIn />} onClick={() => setImpersonateOpen(true)}>
          Log in as
        </Button>
      )}
      {canSuspend &&
        (suspended ? (
          <Button variant="secondary" leadingIcon={<UserCheck />} onClick={() => setAction('reactivate')}>
            Reactivate
          </Button>
        ) : (
          <Button
            variant="danger"
            leadingIcon={<Ban />}
            onClick={() => setAction('suspend')}
            disabled={isSelf}
            title={isSelf ? 'You can’t suspend your own account.' : undefined}
          >
            Suspend
          </Button>
        ))}
      {canAssign && (
        <Button variant="secondary" leadingIcon={<ShieldCheck />} onClick={() => setAction('roles')}>
          Change roles
        </Button>
      )}
      {canTier && (
        <Button variant="secondary" leadingIcon={<Layers />} onClick={() => setAction('tier')}>
          Change tier
        </Button>
      )}
    </>
  );

  const counts = user.submissionCounts;

  return (
    <>
      <PageHeader
        title={p.displayName}
        description={p.email}
        breadcrumbs={breadcrumbs}
        meta={
          <>
            {p.isTestAccount && <TestBadge />}
            <AdminBadge kind="user" value={p.status} />
            <Badge tone="neutral">{p.tier}</Badge>
            {user.roles.map((r) => (
              <Badge key={r} tone={r === 'Participant' ? 'neutral' : 'brand'} icon={<ShieldCheck />}>
                {roleLabel(r)}
              </Badge>
            ))}
            {p.emailVerified && (
              <Badge tone="success" icon={<BadgeCheck />}>
                Email verified
              </Badge>
            )}
          </>
        }
        actions={actions}
      />

      <div className="stack">
        {suspended && (
          <Alert tone="danger" title="This account is suspended">
            {p.statusReason && <p>Reason: {p.statusReason}</p>}
            {p.statusChangedAt && (
              <p>
                Since <DateTime value={p.statusChangedAt} />
              </p>
            )}
          </Alert>
        )}

        <Section title="Profile" id="user-profile">
          <KeyValueList
            items={[
              { label: 'Country', value: countryName(p.countryCode) },
              { label: 'Language', value: p.languageCode },
              { label: 'Time zone', value: p.timeZone },
              { label: 'Interests', value: p.interests.length ? p.interests.join(', ') : '—' },
              { label: 'Referral code', value: <code>{p.referralCode}</code> },
              { label: 'Joined', value: <DateTime value={p.createdAt} /> },
              {
                label: 'Email verified',
                value: p.emailVerifiedAt ? <DateTime value={p.emailVerifiedAt} /> : 'No',
              },
              { label: 'Last sign-in', value: p.lastLoginAt ? <DateTime value={p.lastLoginAt} /> : 'Never' },
              {
                label: 'Last active',
                value: p.lastActiveAt ? <DateTime value={p.lastActiveAt} format="both" /> : 'Never',
              },
              { label: 'Marketing email', value: p.marketingEmailOptIn ? 'Opted in' : 'Opted out' },
              {
                label: 'WhatsApp',
                value: p.whatsAppOptIn
                  ? `Opted in (${p.whatsAppNumberHint ?? 'number hidden'})`
                  : 'Opted out',
              },
              {
                label: 'Payout profile',
                value: user.payoutProfile
                  ? `${humanize(user.payoutProfile.method)} ${user.payoutProfile.destinationHint} · ${user.payoutProfile.preferredCurrency}`
                  : 'Not added',
              },
            ]}
          />
        </Section>

        <section aria-labelledby="user-submissions" className="stack">
          <h2 id="user-submissions" className="admin-section-title">
            Submissions
          </h2>
          <div className="grid-auto admin-stat-grid">
            <Stat label="Total" value={formatNumber(counts.total)} measurement="Count" />
            <Stat
              label="Pending"
              value={formatNumber(counts.pending + counts.underReview)}
              measurement="Count"
            />
            <Stat label="Approved" value={formatNumber(counts.approved)} measurement="Count" />
            <Stat label="Needs correction" value={formatNumber(counts.needsCorrection)} measurement="Count" />
            <Stat label="Rejected" value={formatNumber(counts.rejected)} measurement="Count" />
            <Stat label="Reversed" value={formatNumber(counts.reversed)} measurement="Count" />
          </div>
        </section>

        <div className="admin-two-col">
          <Section title="Earnings by status" id="user-earnings">
            {user.earnings.length === 0 ? (
              <p className="text-muted">No earnings yet.</p>
            ) : (
              <DataTable
                caption="Earnings totals by status"
                columns={[
                  {
                    id: 'status',
                    header: 'Status',
                    cell: (e) => <StatusBadge kind="earning" status={e.status} />,
                  },
                  { id: 'count', header: 'Items', align: 'right', cell: (e) => formatNumber(e.count) },
                  {
                    id: 'amount',
                    header: 'Amount',
                    align: 'right',
                    cell: (e) => <Money amount={e.amount} currency={e.currency} />,
                  },
                ]}
                rows={user.earnings}
                getRowId={(e) => `${e.status}-${e.currency}`}
              />
            )}
          </Section>

          <Section title="Active payout holds" id="user-holds">
            {user.activePayoutHolds.length === 0 ? (
              <p className="text-muted">No active holds.</p>
            ) : (
              <ul className="admin-plain-list">
                {user.activePayoutHolds.map((h) => (
                  <li key={h.id}>
                    <p>{h.reason}</p>
                    <p className="text-small text-muted">
                      Placed <DateTime value={h.createdAt} />
                    </p>
                  </li>
                ))}
              </ul>
            )}
          </Section>
        </div>

        <Section title="Social accounts" id="user-social">
          <DataTable
            caption="Social accounts"
            rows={user.socialAccounts}
            getRowId={(a) => a.id}
            emptyState={<EmptyState compact headingLevel={3} title="No social accounts added" />}
            columns={[
              {
                id: 'handle',
                header: 'Account',
                primary: true,
                cell: (a) => (
                  <div className="admin-cell-stack">
                    <SafeExternalLink className="ui-link" href={a.profileUrl} nofollow>
                      {a.handle}
                    </SafeExternalLink>
                    <span className="text-small text-muted">{a.platform}</span>
                  </div>
                ),
              },
              {
                id: 'verification',
                header: 'Verification',
                cell: (a) => <StatusBadge kind="socialVerification" status={a.verificationStatus} />,
              },
              {
                id: 'age',
                header: 'Account age',
                align: 'right',
                cell: (a) => `${formatNumber(a.accountAgeDays)} days`,
              },
              {
                id: 'followers',
                header: 'Followers',
                align: 'right',
                cell: (a) => formatNumber(a.followerCount),
              },
              { id: 'active', header: 'Active', cell: (a) => (a.isActive ? 'Yes' : 'Deactivated') },
            ]}
          />
        </Section>

        <div className="admin-two-col">
          <Section title="Status history" id="user-history">
            {user.statusHistory.length === 0 ? (
              <p className="text-muted">No status changes.</p>
            ) : (
              <Timeline
                label="Status history"
                items={user.statusHistory.map((h, i) => ({
                  id: `${h.at}-${i}`,
                  title: humanize(h.action.replace(/^admin\.user_/, '')),
                  description: h.reason ?? undefined,
                  timestamp: h.at,
                  actor: h.actorDisplayName ? `by ${h.actorDisplayName}` : undefined,
                  tone: HISTORY_TONE[h.action] ?? 'info',
                }))}
              />
            )}
          </Section>

          <Section title="Recent audit" id="user-audit">
            <p className="text-small text-muted">
              The last entries about or by this person that your permissions allow you to see.
            </p>
            {user.recentAudit.length === 0 ? (
              <p className="text-muted">Nothing to show.</p>
            ) : (
              <ul className="admin-audit-list">
                {user.recentAudit.map((entry) => (
                  <AuditEntry key={entry.id} entry={entry} />
                ))}
              </ul>
            )}
          </Section>
        </div>
      </div>

      <SuspendDialog user={user} open={action === 'suspend'} onClose={close} />
      <ReactivateDialog user={user} open={action === 'reactivate'} onClose={close} />
      <RolesDialog user={user} open={action === 'roles'} onClose={close} />
      <TierDialog user={user} open={action === 'tier'} onClose={close} />
      {impersonateOpen && (
        <ImpersonateDialog
          target={{
            id: p.id,
            displayName: p.displayName,
            email: p.email,
            roles: user.roles,
            isTestAccount: p.isTestAccount,
          }}
          open
          onClose={() => setImpersonateOpen(false)}
        />
      )}
    </>
  );
}
