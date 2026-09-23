import { FileCheck2 } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card } from '@/components/ui/Card';
import { DataTable, type DataTableColumn } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FilterBar } from '@/components/ui/FilterBar';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { useSubmissions } from '../api/queries';
import type { SubmissionListItem } from '../api/types';
import { PlatformTag } from '../components/Platform';
import { submissionStatusOptions } from '../lib/labels';
import '../participant.css';

const PAGE_SIZE = 20;

const columns: DataTableColumn<SubmissionListItem>[] = [
  {
    id: 'campaign',
    header: 'Campaign',
    primary: true,
    cell: (s) => (
      <Link to={`/app/submissions/${s.id}`} className="ui-link">
        {s.campaign.title}
      </Link>
    ),
  },
  { id: 'status', header: 'Status', cell: (s) => <StatusBadge kind="submission" status={s.status} /> },
  { id: 'platform', header: 'Platform', cell: (s) => <PlatformTag platform={s.platform} /> },
  {
    id: 'submitted',
    header: 'Submitted',
    nowrap: true,
    cell: (s) => <DateTime value={s.submittedAt} format="date" />,
  },
  {
    id: 'reward',
    header: 'Estimated reward',
    align: 'right',
    cell: (s) => <Money amount={s.estimatedReward} currency={s.currency} />,
  },
];

export function SubmissionsPage() {
  const [params, setParams] = useSearchParams();
  const status = params.get('status') ?? undefined;
  const campaignId = params.get('campaign') ?? undefined;
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1);
  const list = useSubmissions({ status, campaignId, page, pageSize: PAGE_SIZE });

  const setParam = (key: string, value: string | undefined) =>
    setParams((current) => {
      const next = new URLSearchParams(current);
      if (value) next.set(key, value);
      else next.delete(key);
      if (key !== 'page') next.delete('page');
      return next;
    });

  return (
    <div className="pp-page">
      <PageHeader
        title="My submissions"
        description="Every post you submitted, its review status, and what happens next."
        actions={
          <ButtonLink to="/app/campaigns" variant="secondary">
            Find a campaign
          </ButtonLink>
        }
      />

      <FilterBar
        filters={[{ id: 'status', label: 'Status', options: submissionStatusOptions }]}
        values={{ status }}
        onFilterChange={(_id, value) => setParam('status', value)}
        onReset={status || campaignId ? () => setParams(new URLSearchParams()) : undefined}
      />

      {list.isError ? (
        <Card flat>
          <ErrorState
            error={list.error}
            title="Your submissions couldn’t be loaded"
            onRetry={() => void list.refetch()}
            retrying={list.isFetching}
          />
        </Card>
      ) : (
        <>
          <DataTable
            caption="Your submissions"
            columns={columns}
            rows={list.data?.items ?? []}
            getRowId={(s) => s.id}
            loading={list.isPending}
            emptyState={
              <EmptyState
                icon={<FileCheck2 />}
                headingLevel={2}
                title={status ? 'No submissions with this status' : 'You haven’t submitted any posts yet'}
                description={
                  status
                    ? 'Try another status filter.'
                    : 'Pick a campaign, share the approved content, then submit the link to your post here.'
                }
                action={
                  !status && (
                    <ButtonLink to="/app/campaigns" variant="primary">
                      Browse campaigns
                    </ButtonLink>
                  )
                }
              />
            }
          />
          {list.data && list.data.total > PAGE_SIZE && (
            <Pagination
              page={page}
              pageSize={PAGE_SIZE}
              total={list.data.total}
              onPageChange={(p) => setParam('page', String(p))}
              label="Submission pages"
            />
          )}
        </>
      )}
    </div>
  );
}
