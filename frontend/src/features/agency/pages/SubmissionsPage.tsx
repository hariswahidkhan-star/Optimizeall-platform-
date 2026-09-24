import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, Download, FileDown, Inbox, ShieldAlert, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Badge,
  Button,
  ConfirmDialog,
  DataTable,
  DateTime,
  Drawer,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { pageKeys, type FormDetail, type Submission, type SubmissionStatus } from './api';

const STATUS_LABELS: Record<SubmissionStatus, string> = { New: 'New', InProgress: 'In progress', Done: 'Done', Spam: 'Spam' };
const STATUS_TONES: Record<SubmissionStatus, 'info' | 'warning' | 'success' | 'neutral'> = { New: 'info', InProgress: 'warning', Done: 'success', Spam: 'neutral' };
const STATUSES: SubmissionStatus[] = ['New', 'InProgress', 'Done', 'Spam'];
import './pages.css';

/** Submissions of one form: table, detail drawer, file downloads and CSV export. */
export function SubmissionsPage() {
  const { formId = '' } = useParams();
  const toast = useToast();
  const [page, setPage] = useState(1);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [open, setOpen] = useState<Submission | null>(null);
  const [status, setStatus] = useState<SubmissionStatus | ''>('');
  const [selected, setSelected] = useState<string[]>([]);
  const [deleting, setDeleting] = useState<string[] | null>(null);
  const queryClient = useQueryClient();
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['agency', 'pages', 'form', formId, 'submissions'] });
  const setOne = useMutation({
    mutationFn: ({ id, next }: { id: string; next: SubmissionStatus }) => api.patch(`/agency/pages/forms/${formId}/submissions/${id}`, { status: next }),
    onSuccess: () => refresh(),
    onError: (e) => toast.error('Not updated', errorMessage(e)),
  });
  const bulk = useMutation({
    mutationFn: (next: SubmissionStatus) => api.post<{ updated: number }>(`/agency/pages/forms/${formId}/submissions/bulk`, { ids: selected, status: next }),
    onSuccess: (r, next) => {
      toast.success(`${r.updated} submission(s) marked ${STATUS_LABELS[next].toLowerCase()}`);
      setSelected([]);
      refresh();
    },
    onError: (e) => toast.error('Not updated', errorMessage(e)),
  });
  const form = useQuery({ queryKey: pageKeys.form(formId), queryFn: () => api.get<FormDetail>(`/agency/pages/forms/${formId}`) });
  const params = { from: from || undefined, to: to ? `${to}T23:59:59Z` : undefined, status: status || undefined, page, pageSize: 25 };
  const query = useQuery({
    queryKey: pageKeys.submissions(formId, params),
    queryFn: () => api.get<PagedResult<Submission>>(`/agency/pages/forms/${formId}/submissions`, { query: params }),
    placeholderData: keepPreviousData,
  });
  const [exporting, setExporting] = useState(false);
  const exportCsv = async () => {
    setExporting(true);
    try {
      await api.download(`/agency/pages/forms/${formId}/submissions/export.csv`, 'submissions.csv', { query: { from: params.from, to: params.to, status: params.status } });
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
    {
      id: 'status',
      header: 'Follow-up',
      cell: (s) => {
        const st = s.status ?? 'New';
        return (
          <span className="stack pb-stack-xs">
            <Badge tone={STATUS_TONES[st]}>{STATUS_LABELS[st]}</Badge>
            {s.note && <span className="text-small text-muted">{s.note}</span>}
          </span>
        );
      },
    },
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
          <FormField label="Follow-up">
            <Select
              value={status}
              placeholder="All except spam"
              onChange={(e) => (setStatus(e.target.value as SubmissionStatus | ''), setPage(1))}
              options={STATUSES.map((st) => ({ value: st, label: STATUS_LABELS[st] }))}
            />
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
              selectable
              selectedIds={selected}
              onSelectionChange={setSelected}
              bulkActions={(ids) => (
                <>
                  <Button size="sm" variant="secondary" leadingIcon={<CheckCircle2 />} loading={bulk.isPending} onClick={() => bulk.mutate('Done')}>
                    Mark done
                  </Button>
                  <Button size="sm" variant="secondary" leadingIcon={<ShieldAlert />} loading={bulk.isPending} onClick={() => bulk.mutate('Spam')}>
                    Mark spam
                  </Button>
                  <Button size="sm" variant="ghost" leadingIcon={<Trash2 />} onClick={() => setDeleting(ids)}>
                    Delete
                  </Button>
                </>
              )}
              rowActions={(s) => [
                { id: 'view', label: 'View details & note', onSelect: () => setOpen(s) },
                ...STATUSES.filter((st) => st !== (s.status ?? 'New')).map((st) => ({
                  id: `status-${st}`,
                  label: `Mark ${STATUS_LABELS[st].toLowerCase()}`,
                  onSelect: () => setOne.mutate({ id: s.id, next: st }),
                })),
                { id: 'delete', label: 'Delete', icon: <Trash2 />, danger: true, onSelect: () => setDeleting([s.id]) },
              ]}
              emptyState={<EmptyState icon={<Inbox />} title="No submissions yet" description="Submissions appear here as soon as visitors send the form." />}
            />
            {query.data && query.data.total > 0 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
          </>
        )}
      </div>
      {open && <SubmissionDrawer formId={formId} submission={open} fields={form.data} onClose={() => setOpen(null)} onChanged={refresh} />}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title={deleting && deleting.length > 1 ? `Delete ${deleting.length} submissions?` : 'Delete this submission?'}
        description="The answers and uploaded files are removed permanently (for spam or an erasure request). Export first if you need a copy."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          if (deleting.length === 1) await api.delete(`/agency/pages/forms/${formId}/submissions/${deleting[0]}`);
          else await api.post(`/agency/pages/forms/${formId}/submissions/bulk`, { ids: deleting, delete: true });
          toast.success('Deleted');
          setSelected([]);
          refresh();
        }}
      />
    </>
  );
}

function SubmissionDrawer({
  formId,
  submission: s,
  fields,
  onClose,
  onChanged,
}: {
  formId: string;
  submission: Submission;
  fields?: FormDetail;
  onClose: () => void;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [followUp, setFollowUp] = useState<SubmissionStatus>(s.status ?? 'New');
  const [note, setNote] = useState(s.note ?? '');
  const saveFollowUp = useMutation({
    mutationFn: () => api.patch(`/agency/pages/forms/${formId}/submissions/${s.id}`, { status: followUp, note }),
    onSuccess: () => {
      toast.success('Follow-up saved');
      onChanged();
    },
    onError: (e) => toast.error('Not saved', errorMessage(e)),
  });
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
        <section className="stack" aria-labelledby="pb-sub-followup">
          <h3 id="pb-sub-followup">Follow-up</h3>
          <FormField label="Status">
            <Select value={followUp} onChange={(e) => setFollowUp(e.target.value as SubmissionStatus)} options={STATUSES.map((st) => ({ value: st, label: STATUS_LABELS[st] }))} />
          </FormField>
          <FormField label="Internal note" optional hint="Only visible to the team.">
            <Textarea rows={2} maxLength={2000} value={note} onChange={(e) => setNote(e.target.value)} />
          </FormField>
          <div>
            <Button size="sm" loading={saveFollowUp.isPending} onClick={() => saveFollowUp.mutate()}>
              Save follow-up
            </Button>
          </div>
        </section>
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
