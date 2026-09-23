import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ClipboardCheck, Send } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Badge,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  FilterBar,
  PageHeader,
  Pagination,
  type DataTableColumn,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { NETWORK_LABELS, socialKeys, type PostSummary, type PublishingRow } from './api';
import { ClientPicker, NetworkChip, PostStatusBadge, useClientParam } from './shared';
import './social.css';

const postColumns: DataTableColumn<PostSummary>[] = [
  {
    id: 'title',
    header: 'Post',
    primary: true,
    cell: (p) => (
      <span className="stack" style={{ gap: 2 }}>
        <Link className="ui-link" to={`/agency/social/posts/${p.id}`}>
          {p.title}
        </Link>
        <span className="sm-muted">{p.preview}</span>
      </span>
    ),
  },
  { id: 'client', header: 'Client', cell: (p) => p.clientName },
  {
    id: 'networks',
    header: 'Networks',
    cell: (p) => (
      <span className="sm-network-list">
        {p.networks.map((n) => (
          <NetworkChip key={n} network={n} />
        ))}
      </span>
    ),
  },
  { id: 'status', header: 'Waiting for', cell: (p) => (p.status === 'ClientApproval' ? 'Client' : 'Internal reviewer') },
  {
    id: 'planned',
    header: 'Planned',
    hideOnMobile: true,
    cell: (p) => (p.scheduledAt ? <DateTime value={p.scheduledAt} format="datetime" /> : '—'),
  },
];

/** Posts waiting for an internal reviewer or for the client. */
export function ApprovalsPage() {
  const [clientId, setClientId] = useClientParam();
  const query = useQuery({
    queryKey: socialKeys.approvals(clientId),
    queryFn: () => api.get<PostSummary[]>('/agency/social/approvals', { query: { clientId } }),
  });
  const internal = (query.data ?? []).filter((p) => p.status === 'InternalReview');
  const client = (query.data ?? []).filter((p) => p.status === 'ClientApproval');
  return (
    <>
      <PageHeader title="Approvals" description="Posts waiting for internal review and posts sent to clients for approval." />
      <div className="sm-toolbar">
        <ClientPicker value={clientId} onChange={setClientId} allowAll />
      </div>
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <div className="stack">
          <section className="stack" aria-labelledby="sm-internal">
            <h2 id="sm-internal" className="sm-h2">
              Internal review <Badge>{internal.length}</Badge>
            </h2>
            <DataTable
              caption="Posts awaiting internal review"
              columns={postColumns}
              rows={internal}
              getRowId={(p) => p.id}
              loading={query.isLoading}
              emptyState={<EmptyState compact icon={<ClipboardCheck />} headingLevel={3} title="Nothing to review" />}
            />
          </section>
          <section className="stack" aria-labelledby="sm-client">
            <h2 id="sm-client" className="sm-h2">
              Waiting for the client <Badge tone="warning">{client.length}</Badge>
            </h2>
            <DataTable
              caption="Posts awaiting client approval"
              columns={postColumns}
              rows={client}
              getRowId={(p) => p.id}
              loading={query.isLoading}
              emptyState={<EmptyState compact icon={<Send />} headingLevel={3} title="No posts with clients" />}
            />
          </section>
        </div>
      )}
    </>
  );
}

const PUBLISHING_STATUSES = ['Scheduled', 'Publishing', 'Published', 'Failed'];

/** Publishing log: what is scheduled, in flight, live or failed (with reasons). */
export function PublishingPage() {
  const [clientId, setClientId] = useClientParam();
  const [status, setStatus] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const params = { clientId, status, page, pageSize: 25 };
  const query = useQuery({
    queryKey: socialKeys.publishing(params),
    queryFn: () => api.get<PagedResult<PublishingRow>>('/agency/social/publishing', { query: params }),
    placeholderData: keepPreviousData,
  });
  const columns: DataTableColumn<PublishingRow>[] = [
    {
      id: 'post',
      header: 'Post',
      primary: true,
      cell: (r) => (
        <span className="stack" style={{ gap: 2 }}>
          <Link className="ui-link" to={`/agency/social/posts/${r.post.id}`}>
            {r.post.title}
          </Link>
          <span className="sm-muted">{r.post.clientName}</span>
        </span>
      ),
    },
    {
      id: 'when',
      header: 'Scheduled',
      cell: (r) => (r.post.scheduledAt ? <DateTime value={r.post.scheduledAt} format="datetime" /> : '—'),
    },
    { id: 'status', header: 'Status', cell: (r) => <PostStatusBadge status={r.post.status} /> },
    {
      id: 'variants',
      header: 'Networks',
      cell: (r) => (
        <ul className="sm-issues" style={{ listStyle: 'none', paddingLeft: 0 }}>
          {r.variants.map((v) => (
            <li key={v.id}>
              <NetworkChip network={v.network} /> {v.publishStatus}
              {v.publishedManually ? ' (marked manually)' : ''}
              {v.publishedUrl && (
                <>
                  {' · '}
                  <SafeExternalLink href={v.publishedUrl}>View on {NETWORK_LABELS[v.network]}</SafeExternalLink>
                </>
              )}
              {v.failureReason && <span className="sm-issue--Error"> — {v.failureReason}</span>}
              {v.nextAttemptAt && (
                <span className="sm-muted">
                  {' '}
                  (retry <DateTime value={v.nextAttemptAt} format="relative" />)
                </span>
              )}
            </li>
          ))}
        </ul>
      ),
    },
  ];
  return (
    <>
      <PageHeader
        title="Publishing log"
        description="Scheduled, published and failed posts. Posts are only marked published after the network confirms, or when a person records the live URL."
      />
      <div className="sm-toolbar">
        <ClientPicker value={clientId} onChange={setClientId} allowAll />
      </div>
      <FilterBar
        filters={[{ id: 'status', label: 'Status', options: PUBLISHING_STATUSES.map((s) => ({ value: s, label: s })) }]}
        values={{ status }}
        onFilterChange={(_, value) => {
          setStatus(value);
          setPage(1);
        }}
        onReset={() => setStatus(undefined)}
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <>
          <DataTable
            caption="Publishing log"
            columns={columns}
            rows={query.data?.items ?? []}
            getRowId={(r) => r.post.id}
            loading={query.isLoading}
            emptyState={<EmptyState icon={<Send />} headingLevel={2} title="Nothing scheduled or published yet" />}
          />
          {query.data && query.data.total > 25 && (
            <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />
          )}
        </>
      )}
    </>
  );
}
