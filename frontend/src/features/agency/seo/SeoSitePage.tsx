import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Ban, Pencil, Play, Settings2, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import {
  Badge,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  DataTable,
  DateTime,
  EmptyState,
  ErrorState,
  PageHeader,
  ProgressRing,
  Skeleton,
  StatusBadge,
  Tabs,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { seoKeys, useAuditHistory, useSite, type AuditSummary } from './api';
import { BacklinksPanel } from './BacklinksPanel';
import { BriefsPanel } from './BriefsPanel';
import { healthTone } from './common';
import { KeywordsPanel } from './KeywordsPanel';
import { LocalSeoPanel } from './LocalSeoPanel';
import { SiteDialog } from './SeoSitesPage';
import './seo.css';

const TABS = ['audits', 'keywords', 'backlinks', 'local', 'briefs'] as const;

export function SeoSitePage() {
  const { siteId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const tab = TABS.includes(params.get('tab') as (typeof TABS)[number]) ? params.get('tab')! : 'audits';
  const site = useSite(siteId);
  const [editing, setEditing] = useState(false);
  const queryClient = useQueryClient();
  const toast = useToast();
  const run = useMutation({
    mutationFn: () => api.post<AuditSummary>(`/agency/seo/sites/${siteId}/audits`),
    onSuccess: () => {
      toast.success('Audit queued', 'The crawl starts within a minute; results appear here when it finishes.');
      void queryClient.invalidateQueries({ queryKey: seoKeys.audits(siteId) });
    },
    onError: (err) => toast.error('Could not start the audit', errorMessage(err)),
  });

  if (site.isError) return <ErrorState error={site.error} onRetry={() => void site.refetch()} />;
  const s = site.data;
  return (
    <>
      <PageHeader
        title={s ? s.name : <Skeleton width="12rem" />}
        description={s?.baseUrl}
        breadcrumbs={[{ label: 'SEO', to: '/agency/seo' }, { label: s?.name ?? 'Site' }]}
        meta={
          s && (
            <>
              <Badge tone="neutral">{s.clientName}</Badge>
              <Badge tone="neutral">
                {s.targetCountry} · {s.targetLanguage}
              </Badge>
              {s.isArchived && <Badge tone="warning">Archived</Badge>}
            </>
          )
        }
        actions={
          s && (
            <>
              <ButtonLink variant="ghost" leadingIcon={<Settings2 />} to="/agency/seo/settings">
                Audit rules
              </ButtonLink>
              <Button variant="secondary" leadingIcon={<Pencil />} onClick={() => setEditing(true)}>
                Edit site
              </Button>
              <Button leadingIcon={<Play />} onClick={() => run.mutate()} loading={run.isPending} disabled={s.isArchived}>
                Run audit
              </Button>
            </>
          )
        }
      />
      {s && (
        <Tabs
          label="Site sections"
          value={tab}
          onValueChange={(id) => setParams({ tab: id }, { replace: true })}
          tabs={[
            { id: 'audits', label: 'Site audit', content: <AuditsPanel siteId={siteId} /> },
            { id: 'keywords', label: 'Keywords & rankings', content: <KeywordsPanel site={s} /> },
            { id: 'backlinks', label: 'Backlinks', content: <BacklinksPanel site={s} /> },
            { id: 'local', label: 'Local SEO', content: <LocalSeoPanel site={s} /> },
            { id: 'briefs', label: 'Content briefs', content: <BriefsPanel site={s} /> },
          ]}
        />
      )}
      {editing && s && <SiteDialog site={s} onClose={() => setEditing(false)} />}
    </>
  );
}

function AuditsPanel({ siteId }: { siteId: string }) {
  const history = useAuditHistory(siteId);
  const queryClient = useQueryClient();
  const toast = useToast();
  const [deleting, setDeleting] = useState<AuditSummary | null>(null);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: seoKeys.audits(siteId) });
  const cancel = useMutation({
    mutationFn: (a: AuditSummary) => api.post<AuditSummary>(`/agency/seo/audits/${a.id}/cancel`),
    onSuccess: () => {
      toast.success('Audit cancelled');
      refresh();
    },
    onError: (err) => toast.error('Could not cancel the audit', errorMessage(err)),
  });
  const audits = history.data?.items ?? [];
  const latest = audits.find((a) => a.status === 'Completed');
  const columns: DataTableColumn<AuditSummary>[] = [
    {
      id: 'when',
      header: 'Queued',
      primary: true,
      cell: (a) =>
        a.status === 'Completed' ? (
          <Link className="ui-link" to={`/agency/seo/audits/${a.id}`}>
            <DateTime value={a.queuedAt} format="datetime" />
          </Link>
        ) : (
          <DateTime value={a.queuedAt} format="datetime" />
        ),
    },
    { id: 'status', header: 'Status', cell: (a) => <StatusBadge kind="campaign" status={a.status} /> },
    { id: 'score', header: 'Health', cell: (a) => (a.healthScore === null ? '—' : <Badge tone={healthTone(a.healthScore)}>{a.healthScore}</Badge>) },
    { id: 'pages', header: 'Pages', align: 'right', cell: (a) => a.pagesCrawled },
    { id: 'errors', header: 'Errors', align: 'right', cell: (a) => a.errorCount },
    { id: 'warnings', header: 'Warnings', align: 'right', hideOnMobile: true, cell: (a) => a.warningCount },
  ];
  return (
    <div className="stack">
      {history.isError && <ErrorState error={history.error} onRetry={() => void history.refetch()} />}
      {latest && (
        <Card>
          <CardHeader title="Latest audit" description={latest.failureMessage ?? undefined} />
          <CardBody>
            <div className="seo-audit-summary">
              <ProgressRing value={latest.healthScore ?? 0} max={100} label="Health score" centerText={`${latest.healthScore ?? '—'}`} size={96} />
              <dl className="seo-audit-counts">
                <div>
                  <dt>Errors</dt>
                  <dd className="tabular">{latest.errorCount}</dd>
                </div>
                <div>
                  <dt>Warnings</dt>
                  <dd className="tabular">{latest.warningCount}</dd>
                </div>
                <div>
                  <dt>Notices</dt>
                  <dd className="tabular">{latest.noticeCount}</dd>
                </div>
                <div>
                  <dt>Pages crawled</dt>
                  <dd className="tabular">{latest.pagesCrawled}</dd>
                </div>
              </dl>
              <Link to={`/agency/seo/audits/${latest.id}`} className="ui-link">
                View results
              </Link>
            </div>
          </CardBody>
        </Card>
      )}
      <DataTable
        caption="Audit history"
        columns={columns}
        rows={audits}
        getRowId={(a) => a.id}
        rowLabel={(a) => `Audit of ${new Date(a.queuedAt).toLocaleString()}`}
        loading={history.isLoading}
        rowActions={(a) => [
          a.status === 'Queued'
            ? { id: 'cancel', label: 'Cancel audit', icon: <Ban />, onSelect: () => cancel.mutate(a) }
            : {
                id: 'delete',
                label: 'Delete audit',
                icon: <Trash2 />,
                danger: true,
                disabled: a.status === 'Running',
                description: a.status === 'Running' ? 'Wait for the crawl to finish.' : undefined,
                onSelect: () => setDeleting(a),
              },
        ]}
        emptyState={
          <EmptyState
            compact
            headingLevel={3}
            title="No audits yet"
            description="Run an audit to crawl the site (robots.txt respected, up to the page limit) and check 30+ SEO rules."
          />
        }
      />
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this audit?"
        description="Its issues, crawled pages and triage notes are removed. Other audits of the site are kept."
        confirmLabel="Delete audit"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/seo/audits/${deleting.id}`);
          toast.success('Audit deleted');
          refresh();
        }}
      />
    </div>
  );
}
