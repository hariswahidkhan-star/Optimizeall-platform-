import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Download, FileDown, Inbox } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Badge,
  Button,
  DataTable,
  DateTime,
  Drawer,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Pagination,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { pageKeys, type FormDetail, type Submission } from './api';
import './pages.css';

/** Submissions of one form: table, detail drawer, file downloads and CSV export. */
export function SubmissionsPage() {
  const { formId = '' } = useParams();
  const toast = useToast();
  const [page, setPage] = useState(1);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [open, setOpen] = useState<Submission | null>(null);
  const form = useQuery({ queryKey: pageKeys.form(formId), queryFn: () => api.get<FormDetail>(`/agency/pages/forms/${formId}`) });
  const params = { from: from || undefined, to: to ? `${to}T23:59:59Z` : undefined, page, pageSize: 25 };
  const query = useQuery({
    queryKey: pageKeys.submissions(formId, params),
    queryFn: () => api.get<PagedResult<Submission>>(`/agency/pages/forms/${formId}/submissions`, { query: params }),
    placeholderData: keepPreviousData,
  });
  const [exporting, setExporting] = useState(false);
  const exportCsv = async () => {
    setExporting(true);
    try {
      await api.download(`/agency/pages/forms/${formId}/submissions/export.csv`, 'submissions.csv', { query: { from: params.from, to: params.to } });
    } catch (err) {
      toast.error('Export failed', errorMessage(err));
    } finally {
      setExporting(false);
    }
  };

  const name = form.data?.name ?? 'Form';
  const columns: DataTableColumn<Submission>[] = [
    { id: 'when', header: 'Submitted', primary: true, cell: (s) => <DateTime value={s.submittedAt} format="datetime" /> },
    { id: 'name', header: 'Name', cell: (s) => s.name ?? '—' },
    { id: 'email', header: 'Email', cell: (s) => s.email ?? '—' },
    { id: 'source', header: 'Source', hideOnMobile: true, cell: (s) => s.landingPageName ?? s.embedOrigin ?? s.utmSource ?? 'Direct' },
    {
      id: 'variant',
      header: 'Variant',
      hideOnMobile: true,
      cell: (s) => (s.variantKey ? <Badge tone="neutral">{s.variantKey}</Badge> : '—'),
    },
    { id: 'files', header: 'Files', align: 'right', hideOnMobile: true, cell: (s) => s.files.length || '—' },
  ];

  return (
    <>
      <PageHeader
        title={`${name} submissions`}
        breadcrumbs={[
          { label: 'Landing pages', to: '/agency/pages' },
          { label: 'Forms', to: '/agency/pages/forms' },
          { label: name, to: `/agency/pages/forms/${formId}` },
          { label: 'Submissions' },
        ]}
        actions={
          <Button variant="secondary" leadingIcon={<FileDown />} onClick={() => void exportCsv()} loading={exporting}>
            Export CSV
          </Button>
        }
      />
      <div className="stack">
        <div className="pb-toolbar" role="group" aria-label="Filter by date">
          <FormField label="From">
            <Input type="date" value={from} onChange={(e) => (setFrom(e.target.value), setPage(1))} />
          </FormField>
          <FormField label="To">
            <Input type="date" value={to} onChange={(e) => (setTo(e.target.value), setPage(1))} />
          </FormField>
        </div>
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <DataTable
              caption={`Submissions for ${name}`}
              columns={columns}
              rows={query.data?.items ?? []}
              getRowId={(s) => s.id}
              rowLabel={(s) => `submission from ${s.name ?? s.email ?? 'anonymous'}`}
              loading={query.isLoading}
              rowActions={(s) => [{ id: 'view', label: 'View details', onSelect: () => setOpen(s) }]}
              emptyState={<EmptyState icon={<Inbox />} title="No submissions yet" description="Submissions appear here as soon as visitors send the form." />}
            />
            {query.data && query.data.total > 0 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
          </>
        )}
      </div>
      {open && <SubmissionDrawer formId={formId} submission={open} fields={form.data} onClose={() => setOpen(null)} />}
    </>
  );
}

function SubmissionDrawer({ formId, submission: s, fields, onClose }: { formId: string; submission: Submission; fields?: FormDetail; onClose: () => void }) {
  const toast = useToast();
  const labels = new Map((fields?.schema.steps ?? []).flatMap((st) => st.fields.map((f) => [f.key, f.label] as const)));
  const download = async (fileId: string, fileName: string) => {
    try {
      await api.download(`/agency/pages/forms/${formId}/submissions/${s.id}/files/${fileId}`, fileName);
    } catch (err) {
      toast.error('Download failed', errorMessage(err));
    }
  };
  const utm = [
    ['Source', s.utmSource],
    ['Medium', s.utmMedium],
    ['Campaign', s.utmCampaign],
    ['Term', s.utmTerm],
    ['Content', s.utmContent],
  ].filter(([, v]) => v);
  return (
    <Drawer open onClose={onClose} title={s.name ?? s.email ?? 'Submission'}>
      <div className="stack">
        <p className="text-small text-muted">
          Submitted <DateTime value={s.submittedAt} format="datetime" />
        </p>
        <section className="stack" aria-labelledby="pb-sub-answers">
          <h3 id="pb-sub-answers">Answers</h3>
          <dl className="pb-kv">
            {Object.entries(s.values).map(([key, value]) => (
              <div key={key}>
                <dt>{labels.get(key) ?? key}</dt>
                <dd>{value || '—'}</dd>
              </div>
            ))}
          </dl>
        </section>
        {s.files.length > 0 && (
          <section className="stack" aria-labelledby="pb-sub-files">
            <h3 id="pb-sub-files">Files</h3>
            <ul className="pb-list">
              {s.files.map((f) => (
                <li key={f.id} className="pb-field-row">
                  <span>
                    {f.fileName} <span className="text-small text-muted">({Math.ceil(f.sizeBytes / 1024)} KB)</span>
                  </span>
                  <Button size="sm" variant="secondary" leadingIcon={<Download />} onClick={() => void download(f.id, f.fileName)}>
                    Download
                  </Button>
                </li>
              ))}
            </ul>
          </section>
        )}
        <section className="stack" aria-labelledby="pb-sub-source">
          <h3 id="pb-sub-source">Source</h3>
          <dl className="pb-kv">
            <dt>Landing page</dt>
            <dd>{s.landingPageName ? `${s.landingPageName}${s.variantKey ? ` (variant ${s.variantKey})` : ''}` : '—'}</dd>
            <dt>Embedded on</dt>
            <dd>{s.embedOrigin ?? '—'}</dd>
            <dt>Referrer</dt>
            <dd>{s.referrer ?? '—'}</dd>
            {utm.map(([label, value]) => (
              <div key={label}>
                <dt>UTM {label}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
        </section>
        <section className="stack" aria-labelledby="pb-sub-consent">
          <h3 id="pb-sub-consent">Consent</h3>
          {s.consentGiven ? (
            <p>
              <Badge tone="success">Given</Badge> Version {s.consentVersion}: “{s.consentText}”
            </p>
          ) : (
            <p className="text-muted">No consent recorded.</p>
          )}
          <p className="text-small text-muted">{s.eventPublished ? 'Sent to CRM and automations.' : 'Waiting to be sent to CRM and automations.'}</p>
        </section>
      </div>
    </Drawer>
  );
}
