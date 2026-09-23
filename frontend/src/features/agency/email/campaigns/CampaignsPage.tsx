import { Megaphone, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody } from '@/components/ui/Card';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FilterBar } from '@/components/ui/FilterBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { formatNumber } from '@/lib/format/money';
import { useCampaigns } from '../api/queries';
import type { CampaignListItem } from '../api/types';
import { ApprovalBadge, CampaignStatusBadge } from '../shared/ui';
import { useEmailWorkspace } from '../shared/workspace';

const STATUSES = ['Draft', 'Scheduled', 'Sending', 'Paused', 'Sent', 'Cancelled'];

/** Campaign list for email (`/agency/email/campaigns`) or SMS/WhatsApp (`/agency/sms`). */
export function CampaignsPage({ channel = 'email' }: { channel?: 'email' | 'sms' }) {
  const { clientId } = useEmailWorkspace();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const params = { search: search || undefined, status: status || undefined, page, pageSize: 25 };
  const campaigns = useCampaigns(clientId, channel, params);
  const isSms = channel === 'sms';
  const base = isSms ? '/agency/sms' : '/agency/email/campaigns';

  return (
    <>
      <PageHeader
        title={isSms ? 'SMS & WhatsApp campaigns' : 'Email campaigns'}
        description={
          isSms
            ? 'Text and WhatsApp template campaigns to contacts with SMS/WhatsApp consent, held during quiet hours.'
            : 'Regular and A/B-tested campaigns, sent by the send job within your throttle and schedule.'
        }
        actions={
          <ButtonLink to={`${base}/new`} leadingIcon={<Plus />}>
            New campaign
          </ButtonLink>
        }
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={search}
            onSearchChange={(value) => {
              setSearch(value);
              setPage(1);
            }}
            searchLabel="Search campaigns"
            searchPlaceholder="Search by name…"
            filters={[{ id: 'status', label: 'Status', options: STATUSES.map((s) => ({ value: s, label: s })) }]}
            values={{ status: status || undefined }}
            onFilterChange={(_, value) => {
              setStatus(value ?? '');
              setPage(1);
            }}
            onReset={() => {
              setSearch('');
              setStatus('');
            }}
          />
          {campaigns.isError ? (
            <ErrorState error={campaigns.error} onRetry={() => void campaigns.refetch()} />
          ) : (
            <>
              <DataTable<CampaignListItem>
                caption={isSms ? 'SMS and WhatsApp campaigns' : 'Email campaigns'}
                rows={campaigns.data?.items ?? []}
                getRowId={(c) => c.id}
                loading={campaigns.isPending}
                columns={[
                  {
                    id: 'name',
                    header: 'Campaign',
                    primary: true,
                    cell: (c) => (
                      <span className="email-cell-stack">
                        <Link className="ui-link" to={`${base}/${c.id}`}>
                          {c.name}
                        </Link>
                        {c.subject && <span className="email-muted">{c.subject}</span>}
                      </span>
                    ),
                  },
                  {
                    id: 'status',
                    header: 'Status',
                    cell: (c) => (
                      <span className="cluster">
                        <CampaignStatusBadge status={c.status} />
                        <ApprovalBadge status={c.approvalStatus} />
                      </span>
                    ),
                  },
                  ...(isSms ? [{ id: 'channel', header: 'Channel', cell: (c: CampaignListItem) => c.channel }] : []),
                  {
                    id: 'when',
                    header: 'Scheduled / sent',
                    hideOnMobile: true,
                    cell: (c) =>
                      c.completedAt ? (
                        <DateTime value={c.completedAt} format="datetime" />
                      ) : c.scheduleMode === 'RecipientTimeZone' && c.scheduledLocalTime ? (
                        `${c.scheduledLocalTime.replace('T', ' ')} recipient time`
                      ) : c.scheduledAt ? (
                        <DateTime value={c.scheduledAt} format="datetime" />
                      ) : (
                        '—'
                      ),
                  },
                  { id: 'sent', header: 'Sent', align: 'right', cell: (c) => formatNumber(c.sent) },
                  { id: 'opens', header: 'Opens', align: 'right', hideOnMobile: true, cell: (c) => formatNumber(c.uniqueOpens) },
                  { id: 'clicks', header: 'Clicks', align: 'right', hideOnMobile: true, cell: (c) => formatNumber(c.uniqueClicks) },
                ]}
                emptyState={
                  <EmptyState
                    icon={<Megaphone />}
                    headingLevel={2}
                    title="No campaigns"
                    description="Create a campaign for this workspace."
                  />
                }
              />
              {campaigns.data && campaigns.data.total > 25 && (
                <Pagination page={page} pageSize={25} total={campaigns.data.total} onPageChange={setPage} label="Campaign pages" />
              )}
            </>
          )}
        </CardBody>
      </Card>
    </>
  );
}
