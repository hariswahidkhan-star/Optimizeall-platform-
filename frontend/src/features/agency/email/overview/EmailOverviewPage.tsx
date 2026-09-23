import { Mail, Plus } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Stat } from '@/components/ui/Stat';
import { formatNumber } from '@/lib/format/money';
import { useOverview } from '../api/queries';
import { CampaignStatusBadge, rate } from '../shared/ui';
import { useEmailWorkspace } from '../shared/workspace';

export function EmailOverviewPage() {
  const { clientId } = useEmailWorkspace();
  const overview = useOverview(clientId);
  const k = overview.data?.last30Days;
  return (
    <>
      <PageHeader
        title="Email & SMS marketing"
        description="Consent-based email, SMS and WhatsApp programs for the agency and its clients."
        actions={
          <ButtonLink to="campaigns/new" leadingIcon={<Plus />}>
            New campaign
          </ButtonLink>
        }
      />
      {overview.isError ? (
        <ErrorState error={overview.error} onRetry={() => void overview.refetch()} />
      ) : (
        <>
          <div className="email-grid">
            <Stat label="Contacts" value={formatNumber(overview.data?.contacts ?? 0)} measurement="Count" loading={overview.isPending} />
            <Stat
              label="Can receive email"
              value={formatNumber(overview.data?.subscribed ?? 0)}
              measurement="Count"
              hint="Subscribed with consent"
              loading={overview.isPending}
            />
            <Stat label="Emails sent (30 days)" value={formatNumber(k?.emailsSent ?? 0)} measurement="Count" loading={overview.isPending} />
            <Stat label="Open rate (30 days)" value={rate(k?.openRate ?? 0)} measurement="Measured" hint="Machine opens excluded" loading={overview.isPending} />
            <Stat label="Click rate (30 days)" value={rate(k?.clickRate ?? 0)} measurement="Measured" loading={overview.isPending} />
            <Stat label="Active journeys" value={formatNumber(overview.data?.activeAutomations ?? 0)} measurement="Count" loading={overview.isPending} />
            {(k?.revenue ?? []).map((r) => (
              <Stat key={r.currency} label={`Attributed revenue (${r.currency})`} value={<Money amount={r.amount} currency={r.currency} />} measurement="Measured" />
            ))}
          </div>
          <Card>
            <CardHeader title="Recent campaigns" actions={<Link className="ui-link" to="campaigns">All campaigns</Link>} />
            <CardBody>
              <DataTable
                caption="Recent campaigns"
                rows={overview.data?.recentCampaigns ?? []}
                getRowId={(c) => c.id}
                loading={overview.isPending}
                columns={[
                  {
                    id: 'name',
                    header: 'Campaign',
                    primary: true,
                    cell: (c) => (
                      <Link className="ui-link" to={c.channel === 'Email' ? `campaigns/${c.id}` : `/agency/sms/${c.id}`}>
                        {c.name}
                      </Link>
                    ),
                  },
                  { id: 'channel', header: 'Channel', cell: (c) => c.channel },
                  { id: 'status', header: 'Status', cell: (c) => <CampaignStatusBadge status={c.status} /> },
                  { id: 'sent', header: 'Sent', align: 'right', cell: (c) => formatNumber(c.sent) },
                  { id: 'opens', header: 'Unique opens', align: 'right', cell: (c) => formatNumber(c.uniqueOpens), hideOnMobile: true },
                  { id: 'updated', header: 'Updated', cell: (c) => <DateTime value={c.updatedAt} format="relative" />, hideOnMobile: true },
                ]}
                emptyState={
                  <EmptyState
                    icon={<Mail />}
                    headingLevel={3}
                    title="No campaigns yet"
                    description="Import a list with consent, then create your first campaign."
                    action={<ButtonLink to="lists">Go to audience</ButtonLink>}
                  />
                }
              />
            </CardBody>
          </Card>
        </>
      )}
    </>
  );
}
