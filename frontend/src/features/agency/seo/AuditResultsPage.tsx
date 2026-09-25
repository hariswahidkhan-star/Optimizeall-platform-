import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  PageHeader,
  Pagination,
  Select,
  FormField,
  Stat,
  Tabs,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { seoKeys, severityTone, type AuditDetail, type AuditDiff, type AuditIssue, type AuditPage, type DiffIssue } from './api';
import { AuditIssueGroups } from './AuditIssueGroups';
import { healthTone } from './common';
import './seo.css';

export function AuditResultsPage() {
  const { auditId = '' } = useParams();
  const queryClient = useQueryClient();
  const toast = useToast();
  const detail = useQuery({ queryKey: seoKeys.audit(auditId), queryFn: () => api.get<AuditDetail>(`/agency/seo/audits/${auditId}`) });
  // Replace the triaged issue in the cached audit so the list updates without a refetch.
  const onIssueChanged = (issue: AuditIssue) =>
    queryClient.setQueryData<AuditDetail>(seoKeys.audit(auditId), (old) =>
      old ? { ...old, issues: old.issues.map((i) => (i.ruleKey === issue.ruleKey ? issue : i)) } : old,
    );
  if (detail.isError) return <ErrorState error={detail.error} onRetry={() => void detail.refetch()} />;
  const d = detail.data;
  const a = d?.audit;
  return (
    <>
      <PageHeader
        title={d ? `Audit: ${d.siteName}` : 'Audit results'}
        description={d?.siteBaseUrl}
        breadcrumbs={[{ label: 'SEO', to: '/agency/seo' }, ...(a ? [{ label: d!.siteName, to: `/agency/seo/sites/${a.siteId}` }] : []), { label: 'Audit' }]}
        meta={a && <span className="text-small text-muted">Finished <DateTime value={a.finishedAt ?? a.queuedAt} format="datetime" /></span>}
        actions={
          <Button
            variant="secondary"
            leadingIcon={<Download />}
            onClick={() => api.download(`/agency/seo/audits/${auditId}/export.csv`, 'seo-audit.csv').catch((e) => toast.error('Export failed', errorMessage(e)))}
          >
            Export CSV
          </Button>
        }
      />
      {a?.failureMessage && <Alert tone="warning" title="Note">{a.failureMessage}</Alert>}
      <div className="grid-auto seo-stats">
        <Stat label="Health score" value={a?.healthScore != null ? <Badge tone={healthTone(a.healthScore)}>{a.healthScore}/100</Badge> : '—'} loading={detail.isLoading} />
        <Stat label="Errors" value={a?.errorCount ?? '—'} measurement="Count" loading={detail.isLoading} />
        <Stat label="Warnings" value={a?.warningCount ?? '—'} measurement="Count" loading={detail.isLoading} />
        <Stat label="Notices" value={a?.noticeCount ?? '—'} measurement="Count" loading={detail.isLoading} />
        <Stat label="Pages crawled" value={a?.pagesCrawled ?? '—'} measurement="Count" loading={detail.isLoading} hint={a && `robots.txt ${a.robotsTxtFound ? 'found' : 'missing'} · sitemap ${a.sitemapFound ? 'found' : 'missing'}`} />
      </div>
      {d && (
        <Tabs
          label="Audit results"
          tabs={[
            { id: 'issues', label: 'Issues', badge: d.issues.length, content: (
                <AuditIssueGroups
                  issues={d.issues}
                  auditId={auditId}
                  onChanged={onIssueChanged}
                  triageDisabledReason={d.audit.status === 'Completed' ? undefined : 'Issues can be marked fixed or ignored once the audit has completed.'}
                />
              ),
            },
            { id: 'changes', label: 'Changes since last audit', content: <DiffView auditId={auditId} hasPrevious={!!d.previousAuditId} /> },
            { id: 'pages', label: 'Crawled pages', content: <PagesView auditId={auditId} /> },
          ]}
        />
      )}
    </>
  );
}

function DiffView({ auditId, hasPrevious }: { auditId: string; hasPrevious: boolean }) {
  const diff = useQuery({
    queryKey: [...seoKeys.audit(auditId), 'diff'],
    queryFn: () => api.get<AuditDiff>(`/agency/seo/audits/${auditId}/diff`),
    enabled: hasPrevious,
  });
  if (!hasPrevious) return <EmptyState compact headingLevel={2} title="First audit" description="Run another audit later to see new and fixed issues." />;
  if (diff.isError) return <ErrorState error={diff.error} />;
  const data = diff.data;
  return (
    <div className="stack">
      {data && (
        <p>
          <strong>{data.fixedCount}</strong> issue URL(s) fixed, <strong>{data.newCount}</strong> new
          {data.healthScoreChange !== null && (
            <>
              ; health score {data.healthScoreChange >= 0 ? 'up' : 'down'} <strong>{Math.abs(data.healthScoreChange)}</strong> points
            </>
          )}
          .
        </p>
      )}
      <div className="seo-grid-2">
        <DiffList title="New issues" items={data?.newIssues ?? []} empty="Nothing new — nice." />
        <DiffList title="Fixed issues" items={data?.fixedIssues ?? []} empty="Nothing fixed since the previous audit." />
      </div>
    </div>
  );
}

function DiffList({ title, items, empty }: { title: string; items: DiffIssue[]; empty: string }) {
  return (
    <section aria-label={title} className="seo-diff">
      <h2 className="seo-h3">{title}</h2>
      {items.length === 0 ? (
        <p className="text-muted">{empty}</p>
      ) : (
        <ul className="seo-diff__list">
          {items.map((i) => (
            <li key={i.ruleKey}>
              <Badge tone={severityTone[i.severity]} size="sm">
                {i.severity}
              </Badge>{' '}
              <span className="seo-strong">{i.title}</span> <span className="text-muted">({i.urls.length})</span>
              <ul className="seo-url-list">
                {i.urls.slice(0, 10).map((u) => (
                  <li key={u}>
                    <SafeExternalLink href={u}>{u}</SafeExternalLink>
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function PagesView({ auditId }: { auditId: string }) {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('');
  const params = { page, pageSize: 50, status: status || undefined };
  const pages = useQuery({
    queryKey: [...seoKeys.audit(auditId), 'pages', params],
    queryFn: () => api.get<PagedResult<AuditPage>>(`/agency/seo/audits/${auditId}/pages`, { query: params }),
    placeholderData: keepPreviousData,
  });
  const columns: DataTableColumn<AuditPage>[] = [
    {
      id: 'url',
      header: 'URL',
      primary: true,
      cell: (p) => (
        <span className="stack seo-stack-xs">
          <SafeExternalLink href={p.url}>{p.url}</SafeExternalLink>
          {p.title && <span className="text-small text-muted">{p.title}</span>}
          {p.redirectChain && <span className="text-small text-muted seo-pre">{p.redirectChain}</span>}
          {p.fetchError && <span className="text-small seo-down">{p.fetchError}</span>}
        </span>
      ),
    },
    { id: 'status', header: 'Status', cell: (p) => p.statusCode ?? '—' },
    { id: 'time', header: 'Response', align: 'right', hideOnMobile: true, cell: (p) => `${p.responseTimeMs} ms` },
    { id: 'words', header: 'Words', align: 'right', hideOnMobile: true, cell: (p) => p.wordCount },
    { id: 'links', header: 'Inbound links', align: 'right', hideOnMobile: true, cell: (p) => p.inboundLinks },
    { id: 'flags', header: 'Flags', hideOnMobile: true, cell: (p) => [p.isNoindex && 'noindex', p.inSitemap && 'in sitemap'].filter(Boolean).join(', ') || '—' },
  ];
  return (
    <div className="stack">
      <FormField label="Response" className="seo-inline-filter">
        <Select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value);
            setPage(1);
          }}
          options={[
            { value: '', label: 'All pages' },
            { value: 'ok', label: '2xx OK' },
            { value: 'redirect', label: 'Redirected' },
            { value: 'client-error', label: '4xx errors' },
            { value: 'server-error', label: '5xx errors' },
            { value: 'failed', label: 'Not fetched' },
          ]}
        />
      </FormField>
      <DataTable caption="Crawled pages" columns={columns} rows={pages.data?.items ?? []} getRowId={(p) => p.id} loading={pages.isLoading} />
      {pages.data && pages.data.total > 50 && <Pagination page={page} pageSize={50} total={pages.data.total} onPageChange={setPage} />}
    </div>
  );
}
