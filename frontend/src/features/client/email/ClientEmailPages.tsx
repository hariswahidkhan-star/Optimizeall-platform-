import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Mail } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { Money } from '@/components/ui/Money';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Select } from '@/components/ui/Select';
import { SkeletonText } from '@/components/ui/Skeleton';
import { Stat } from '@/components/ui/Stat';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { formatNumber } from '@/lib/format/money';
import type { CampaignReport, ClientCampaignItem, EmailKpis, PagedResult, RenderResult } from '@/features/agency/email/api/types';
import { CampaignReportView } from '@/features/agency/email/reports/CampaignReportView';
import { ApprovalBadge, CampaignStatusBadge, rate } from '@/features/agency/email/shared/ui';
import '@/features/agency/email/email.css';

/** Client portal API for this area (relative to /api/v1). */
export const CLIENT_EMAIL_API = '/client/email';

const keys = {
  all: ['client-email'] as const,
  clients: ['client-email', 'clients'] as const,
  kpis: (clientId: string) => ['client-email', 'kpis', clientId] as const,
  campaigns: (clientId: string, page: number) => ['client-email', 'campaigns', clientId, page] as const,
  approvals: (clientId: string) => ['client-email', 'approvals', clientId] as const,
  report: (id: string) => ['client-email', 'report', id] as const,
  preview: (id: string) => ['client-email', 'preview', id] as const,
};

function useClients() {
  return useQuery({
    queryKey: keys.clients,
    queryFn: ({ signal }) => api.get<{ id: string; name: string }[]>(`${CLIENT_EMAIL_API}/clients`, { signal }),
    staleTime: 5 * 60_000,
  });
}

function KpiCards({ clientId }: { clientId: string }) {
  const kpis = useQuery({
    queryKey: keys.kpis(clientId),
    queryFn: ({ signal }) => api.get<EmailKpis>(`${CLIENT_EMAIL_API}/kpis`, { query: { clientId }, signal }),
  });
  if (kpis.isError) return <ErrorState error={kpis.error} onRetry={() => void kpis.refetch()} />;
  const k = kpis.data;
  const loading = kpis.isPending;
  return (
    <section aria-labelledby="client-email-kpis" className="stack">
      <h2 id="client-email-kpis" className="email-section-title">
        Last 30 days
      </h2>
      <div className="email-grid">
        <Stat label="Emails sent" value={formatNumber(k?.emailsSent ?? 0)} measurement="Count" loading={loading} />
        <Stat label="Open rate" value={rate(k?.openRate ?? 0)} measurement="Measured" hint="Machine opens (e.g. Apple Mail Privacy) excluded" loading={loading} />
        <Stat label="Click rate" value={rate(k?.clickRate ?? 0)} measurement="Measured" loading={loading} />
        <Stat label="New subscribers" value={formatNumber(k?.newSubscribers ?? 0)} measurement="Count" loading={loading} />
        <Stat label="Unsubscribes" value={formatNumber(k?.unsubscribes ?? 0)} measurement="Count" loading={loading} />
        <Stat label="Conversions" value={formatNumber(k?.conversions ?? 0)} measurement="Estimated" loading={loading} />
        <Stat
          label="Attributed revenue"
          value={
            k && k.revenue.length > 0 ? (
              <span className="email-cell-stack">
                {k.revenue.map((r) => (
                  <Money key={r.currency} amount={r.amount} currency={r.currency} />
                ))}
              </span>
            ) : (
              '—'
            )
          }
          measurement="Estimated"
          hint="Last click within 7 days"
          loading={loading}
        />
        {(k?.smsSent ?? 0) > 0 && <Stat label="SMS sent" value={formatNumber(k!.smsSent)} measurement="Count" />}
      </div>
    </section>
  );
}

function CampaignTable({ rows, loading, caption, empty }: { rows: ClientCampaignItem[]; loading: boolean; caption: string; empty: React.ReactNode }) {
  return (
    <DataTable
      caption={caption}
      rows={rows}
      getRowId={(c) => c.id}
      loading={loading}
      columns={[
        {
          id: 'name',
          header: 'Campaign',
          primary: true,
          cell: (c) => (
            <span className="email-cell-stack">
              <Link className="ui-link" to={`/client/email/campaigns/${c.id}`}>
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
        {
          id: 'when',
          header: 'When',
          hideOnMobile: true,
          cell: (c) =>
            c.completedAt ? <DateTime value={c.completedAt} format="datetime" /> : c.scheduledAt ? <DateTime value={c.scheduledAt} format="datetime" /> : c.scheduledLocalTime ?? '—',
        },
        { id: 'sent', header: 'Sent', align: 'right', cell: (c) => formatNumber(c.sent) },
        { id: 'opens', header: 'Unique opens', align: 'right', hideOnMobile: true, cell: (c) => formatNumber(c.uniqueOpens) },
        { id: 'clicks', header: 'Unique clicks', align: 'right', hideOnMobile: true, cell: (c) => formatNumber(c.uniqueClicks) },
      ]}
      emptyState={empty}
    />
  );
}

/** Client portal: email performance and campaigns awaiting the client's approval. */
export function ClientEmailPage() {
  const clients = useClients();
  const [selected, setSelected] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const clientId = selected ?? clients.data?.[0]?.id ?? null;
  const campaigns = useQuery({
    queryKey: keys.campaigns(clientId ?? '', page),
    queryFn: ({ signal }) =>
      api.get<PagedResult<ClientCampaignItem>>(`${CLIENT_EMAIL_API}/campaigns`, { query: { clientId: clientId ?? undefined, page, pageSize: 20 }, signal }),
    enabled: !!clientId,
  });
  const approvals = useQuery({
    queryKey: keys.approvals(clientId ?? ''),
    queryFn: ({ signal }) => api.get<ClientCampaignItem[]>(`${CLIENT_EMAIL_API}/approvals`, { query: { clientId: clientId ?? undefined }, signal }),
    enabled: !!clientId,
  });

  if (clients.isPending) return <SkeletonText lines={6} />;
  if (clients.isError) return <ErrorState error={clients.error} onRetry={() => void clients.refetch()} />;
  if (!clientId)
    return (
      <>
        <PageHeader title="Email marketing" />
        <EmptyState icon={<Mail />} headingLevel={2} title="No organization yet" description="Once your agency adds you to an organization, its email results appear here." />
      </>
    );

  return (
    <>
      <PageHeader
        title="Email marketing"
        description="Results of the campaigns your agency sends for you. Figures exclude automated (machine) opens."
        actions={
          clients.data.length > 1 && (
            <FormField label="Organization">
              <Select
                size="sm"
                value={clientId}
                options={clients.data.map((c) => ({ value: c.id, label: c.name }))}
                onChange={(e) => {
                  setSelected(e.target.value);
                  setPage(1);
                }}
              />
            </FormField>
          )
        }
      />
      {(approvals.data?.length ?? 0) > 0 && (
        <Card>
          <CardHeader title="Waiting for your approval" description="These campaigns will not be sent until they are approved." />
          <CardBody>
            <CampaignTable rows={approvals.data ?? []} loading={false} caption="Campaigns awaiting approval" empty={null} />
          </CardBody>
        </Card>
      )}
      <KpiCards clientId={clientId} />
      <Card>
        <CardHeader title="Campaigns" />
        <CardBody className="stack">
          {campaigns.isError ? (
            <ErrorState error={campaigns.error} onRetry={() => void campaigns.refetch()} />
          ) : (
            <>
              <CampaignTable
                rows={campaigns.data?.items ?? []}
                loading={campaigns.isPending}
                caption="Scheduled and sent campaigns"
                empty={<EmptyState icon={<Mail />} headingLevel={3} title="No campaigns yet" description="Scheduled and sent campaigns appear here." />}
              />
              {campaigns.data && campaigns.data.total > 20 && (
                <Pagination page={page} pageSize={20} total={campaigns.data.total} onPageChange={setPage} label="Campaign pages" />
              )}
            </>
          )}
        </CardBody>
      </Card>
    </>
  );
}

function Preview({ id }: { id: string }) {
  const preview = useQuery({
    queryKey: keys.preview(id),
    queryFn: ({ signal }) => api.get<RenderResult>(`${CLIENT_EMAIL_API}/campaigns/${id}/preview`, { signal }),
  });
  if (preview.isError) return <ErrorState error={preview.error} onRetry={() => void preview.refetch()} />;
  if (!preview.data) return <SkeletonText lines={6} />;
  return (
    <div className="stack">
      <p className="email-muted">
        Subject: <strong>{preview.data.subject || '(no subject)'}</strong> · shown with sample contact details
      </p>
      <div className="email-preview__frame-wrap">
        <iframe title="Email preview" className="email-preview__frame" sandbox="" srcDoc={preview.data.html} />
      </div>
    </div>
  );
}

/** One campaign for the client: approval (if pending and the member may approve), preview and report. */
export function ClientCampaignPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const toast = useToast();
  const report = useQuery({
    queryKey: keys.report(id),
    queryFn: ({ signal }) => api.get<CampaignReport>(`${CLIENT_EMAIL_API}/campaigns/${id}/report`, { signal }),
  });
  const approvals = useQuery({
    queryKey: [...keys.all, 'approvals-all'],
    queryFn: ({ signal }) => api.get<ClientCampaignItem[]>(`${CLIENT_EMAIL_API}/approvals`, { signal }),
  });
  const pending = approvals.data?.find((a) => a.id === id);
  const [deciding, setDeciding] = useState<'approve' | 'reject' | null>(null);
  const navigate = useNavigate();
  const decide = useMutation({
    mutationFn: (body: { approve: boolean; note: string | null }) => api.post(`${CLIENT_EMAIL_API}/campaigns/${id}/approval`, body),
    onSuccess: (_, body) => {
      toast.success(body.approve ? 'Campaign approved' : 'Changes requested', body.approve ? 'Your agency can now send it as scheduled.' : 'Your agency has been told.');
      if (body.approve) {
        void queryClient.invalidateQueries({ queryKey: keys.all });
        return;
      }
      // Changes requested: the campaign goes back to the agency as a draft, which the portal does not show, so this page
      // would only answer "not found". Back to the list, without refetching this campaign.
      navigate('/client/email');
      const gone = [keys.report(id), keys.preview(id)].map((k) => k.join('/'));
      void queryClient.invalidateQueries({ queryKey: keys.all, predicate: (query) => !gone.includes(query.queryKey.join('/')) });
    },
  });

  return (
    <>
      <PageHeader
        title={report.data?.name ?? 'Campaign'}
        breadcrumbs={[{ label: 'Email marketing', to: '/client/email' }, { label: report.data?.name ?? 'Campaign' }]}
        meta={report.data && <CampaignStatusBadge status={report.data.status} />}
        description={report.data?.subject ?? undefined}
        actions={
          pending?.canApprove && (
            <>
              <Button variant="secondary" onClick={() => setDeciding('reject')}>
                Request changes
              </Button>
              <Button onClick={() => setDeciding('approve')}>Approve</Button>
            </>
          )
        }
      />
      {pending && !pending.canApprove && <Alert tone="info">This campaign is waiting for approval by an approver or owner of your organization.</Alert>}
      {pending && report.data?.channel === 'Email' && (
        <Card>
          <CardHeader title="Preview" />
          <CardBody>
            <Preview id={id} />
          </CardBody>
        </Card>
      )}
      {report.isError ? (
        <ErrorState error={report.error} onRetry={() => void report.refetch()} />
      ) : report.data ? (
        report.data.sent > 0 ? (
          <CampaignReportView report={report.data} />
        ) : (
          <p className="email-muted">Results appear here once the campaign has been sent.</p>
        )
      ) : (
        <SkeletonText lines={8} />
      )}
      <ConfirmDialog
        open={deciding === 'approve'}
        onClose={() => setDeciding(null)}
        title="Approve this campaign?"
        description="Your agency can then send it as scheduled."
        confirmLabel="Approve"
        onConfirm={async () => {
          await decide.mutateAsync({ approve: true, note: null });
        }}
      />
      <ConfirmDialog
        open={deciding === 'reject'}
        onClose={() => setDeciding(null)}
        title="Request changes?"
        description="The campaign will not be sent. Tell your agency what to change."
        confirmLabel="Request changes"
        requireReason
        reasonLabel="What should change?"
        onConfirm={async ({ reason }) => {
          await decide.mutateAsync({ approve: false, note: reason });
        }}
      />
    </>
  );
}
