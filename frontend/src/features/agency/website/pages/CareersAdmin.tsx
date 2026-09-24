import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download } from 'lucide-react';
import { useState } from 'react';
import { Alert, Badge, Button, ConfirmDialog, Drawer, ErrorState, FormField, KeyValueList, PageHeader, Select, Tabs, Textarea, Timeline, useToast } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatDate, formatDateTime } from '@/lib/format/dates';
import { formatBytes } from '@/lib/format/text';
import { isExternalHref } from '@/lib/safeHref';
import { type Application, type ApplicationStage, type ApplicationSummary, type Job, type Paged, W } from '../api';
import { AreaField, LinesField, MarkdownField, SelectField, TextField, errorFor } from '../shared/fields';
import { ResourcePage } from '../shared/ResourcePage';
import '../website.css';

export const STAGES: ApplicationStage[] = ['New', 'Screening', 'Interview', 'Offer', 'Hired', 'Rejected'];

type JobDraft = Omit<Job, 'id' | 'updatedAt' | 'concurrencyStamp' | 'applicationCount' | 'postedAt'>;

function JobsTab() {
  return (
    <ResourcePage<Job, Job, JobDraft>
      title="Job openings"
      description="Open roles appear on /careers with JobPosting structured data. Close a role instead of deleting it once people have applied."
      singular="Job"
      queryKey={['agency', 'website', 'jobs']}
      list={() => api.get<Job[]>(`${W}/careers/jobs`)}
      getId={(r) => r.id}
      rowLabel={(r) => r.title}
      publicUrl={(r) => (r.status === 'Open' ? `/careers/${r.slug}` : null)}
      columns={[
        { id: 'title', header: 'Role', primary: true, cell: (r) => r.title },
        { id: 'dept', header: 'Department', cell: (r) => r.department, hideOnMobile: true },
        { id: 'status', header: 'Status', cell: (r) => <Badge tone={r.status === 'Open' ? 'success' : 'neutral'}>{r.status}</Badge> },
        { id: 'apps', header: 'Applications', align: 'right', cell: (r) => r.applicationCount },
      ]}
      toDraft={(d) =>
        d
          ? { ...d }
          : {
              slug: '', title: '', department: '', location: '', countryCode: null, workplace: 'Remote', employmentType: 'FullTime', summary: '',
              descriptionMarkdown: '', requirements: [], benefits: [], salaryMin: null, salaryMax: null, salaryCurrency: null, salaryPeriod: null,
              status: 'Draft', closesAt: null,
            }
      }
      save={(draft, existing) =>
        existing ? api.put(`${W}/careers/jobs/${existing.id}`, { ...draft, concurrencyStamp: existing.concurrencyStamp }) : api.post(`${W}/careers/jobs`, draft)
      }
      remove={(r) => api.delete(`${W}/careers/jobs/${r.id}`)}
      Form={({ draft, setDraft, errors }) => {
        const set = <K extends keyof JobDraft>(k: K, v: JobDraft[K]) => setDraft({ ...draft, [k]: v });
        const num = (v: string) => (v.trim() === '' ? null : Number(v));
        return (
          <>
            <div className="cms-grid-2">
              <TextField label="Title" required value={draft.title} onChange={(v) => set('title', v)} error={errors.title} />
              <TextField label="Slug" required value={draft.slug} onChange={(v) => set('slug', v)} error={errors.slug} />
              <TextField label="Department" required value={draft.department} onChange={(v) => set('department', v)} error={errors.department} />
              <TextField label="Location" required value={draft.location} onChange={(v) => set('location', v)} error={errors.location} />
              <SelectField label="Workplace" required value={draft.workplace} onChange={(v) => set('workplace', v as Job['workplace'])} options={[{ value: 'OnSite', label: 'On-site' }, { value: 'Hybrid', label: 'Hybrid' }, { value: 'Remote', label: 'Remote' }]} error={errors.workplace} />
              <SelectField
                label="Employment type"
                required
                value={draft.employmentType}
                onChange={(v) => set('employmentType', v as Job['employmentType'])}
                options={['FullTime', 'PartTime', 'Contract', 'Internship', 'Temporary'].map((t) => ({ value: t, label: t.replace(/([a-z])([A-Z])/g, '$1-$2') }))}
                error={errors.employmentType}
              />
              <TextField label="Country code" value={draft.countryCode} onChange={(v) => set('countryCode', v.toUpperCase() || null)} maxLength={2} error={errors.countryCode} hint="ISO code, e.g. GB" />
              <SelectField label="Status" required value={draft.status} onChange={(v) => set('status', v as Job['status'])} options={[{ value: 'Draft', label: 'Draft' }, { value: 'Open', label: 'Open' }, { value: 'Closed', label: 'Closed' }]} error={errors.status} />
            </div>
            <AreaField label="Summary" required value={draft.summary} onChange={(v) => set('summary', v)} maxLength={500} rows={2} error={errors.summary} />
            <MarkdownField label="Description" required value={draft.descriptionMarkdown} onChange={(v) => set('descriptionMarkdown', v)} error={errors.descriptionMarkdown} />
            <LinesField label="Requirements" value={draft.requirements} onChange={(v) => set('requirements', v)} error={errorFor(errors, 'requirements')} />
            <LinesField label="Benefits" value={draft.benefits} onChange={(v) => set('benefits', v)} error={errorFor(errors, 'benefits')} />
            <fieldset className="cms-fieldset">
              <legend>Salary (optional)</legend>
              <div className="cms-grid-2">
                <TextField label="Minimum" type="number" value={draft.salaryMin?.toString() ?? ''} onChange={(v) => set('salaryMin', num(v))} error={errors.salaryMin} />
                <TextField label="Maximum" type="number" value={draft.salaryMax?.toString() ?? ''} onChange={(v) => set('salaryMax', num(v))} error={errors.salaryMax} />
                <TextField label="Currency" value={draft.salaryCurrency} onChange={(v) => set('salaryCurrency', v.toUpperCase() || null)} maxLength={3} error={errors.salaryCurrency} />
                <SelectField label="Per" value={draft.salaryPeriod} onChange={(v) => set('salaryPeriod', (v || null) as Job['salaryPeriod'])} placeholder="—" options={[{ value: 'Hour', label: 'Hour' }, { value: 'Month', label: 'Month' }, { value: 'Year', label: 'Year' }]} error={errors.salaryPeriod} />
              </div>
            </fieldset>
          </>
        );
      }}
    />
  );
}

function ApplicationDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const detail = useQuery({ queryKey: ['agency', 'website', 'application', id], queryFn: () => api.get<Application>(`${W}/careers/applications/${id}`), enabled: !!id });
  const [note, setNote] = useState('');
  const [erasing, setErasing] = useState(false);
  const refresh = async (saved: Application) => {
    client.setQueryData(['agency', 'website', 'application', id], saved);
    await client.invalidateQueries({ queryKey: ['agency', 'website', 'applications'] });
  };
  const move = useMutation({
    mutationFn: (stage: ApplicationStage) => api.post<Application>(`${W}/careers/applications/${id}/move`, { stage, concurrencyStamp: detail.data!.concurrencyStamp }),
    onSuccess: async (saved) => {
      toast.success(`Moved to ${saved.stage}`);
      await refresh(saved);
    },
    onError: (e) => toast.error("Couldn't move the application", errorMessage(e)),
  });
  const addNote = useMutation({
    mutationFn: () => api.post<Application>(`${W}/careers/applications/${id}/notes`, { body: note }),
    onSuccess: async (saved) => {
      setNote('');
      await refresh(saved);
    },
    onError: (e) => toast.error("Couldn't add the note", errorMessage(e)),
  });
  const a = detail.data;
  return (
    <Drawer open={!!id} onClose={onClose} title={a ? a.name : 'Application'} side="right" className="cms-drawer">
      {detail.isError && <ErrorState error={detail.error} />}
      {a && (
        <div className="cms-form">
          <KeyValueList
            items={[
              { label: 'Role', value: a.jobTitle },
              { label: 'Email', value: <a href={`mailto:${a.email}`}>{a.email}</a> },
              { label: 'Phone', value: a.phone ?? '—' },
              {
                label: 'Portfolio',
                value: a.portfolioUrl && isExternalHref(a.portfolioUrl) ? (
                  <a href={a.portfolioUrl} target="_blank" rel="noopener noreferrer">
                    {a.portfolioUrl}
                  </a>
                ) : (
                  '—'
                ),
              },
              { label: 'Applied', value: formatDateTime(a.createdAt) },
              { label: 'Consent', value: `${a.consentVersion}, ${formatDate(a.consentAt)}` },
            ]}
          />
          <div>
            <Button variant="secondary" leadingIcon={<Download />} onClick={() => void api.download(`${W}/careers/applications/${a.id}/cv`, a.cvFileName)}>
              Download CV ({formatBytes(a.cvSizeBytes)})
            </Button>
          </div>
          {a.coverLetter && (
            <section aria-label="Cover letter">
              <h3 className="public-footer__heading">Cover letter</h3>
              <p style={{ whiteSpace: 'pre-wrap' }}>{a.coverLetter}</p>
            </section>
          )}
          <FormField label="Stage">
            <Select value={a.stage} onChange={(e) => move.mutate(e.target.value as ApplicationStage)} options={STAGES.map((s) => ({ value: s, label: s }))} />
          </FormField>
          <Timeline
            label="Notes and stage changes"
            items={a.notes.map((n) => ({ id: n.id, title: n.stageTo ? `Moved to ${n.stageTo}` : 'Note', description: n.body, timestamp: n.createdAt }))}
          />
          <FormField label="Add a note">
            <Textarea rows={3} value={note} onChange={(e) => setNote(e.target.value)} maxLength={4000} />
          </FormField>
          <div className="cms-toolbar">
            <Button onClick={() => addNote.mutate()} disabled={!note.trim()} loading={addNote.isPending}>
              Add note
            </Button>
            <Button variant="ghost" onClick={() => setErasing(true)}>
              Erase application
            </Button>
          </div>
          <ConfirmDialog
            open={erasing}
            onClose={() => setErasing(false)}
            title={`Erase ${a.name}'s application?`}
            description="Deletes the application, its notes and the CV permanently — for a data-erasure request or when the retention period ends. This can't be undone."
            confirmLabel="Erase application"
            tone="danger"
            onConfirm={async () => {
              await api.delete(`${W}/careers/applications/${a.id}`);
              toast.success('Application erased');
              setErasing(false);
              onClose();
              await client.invalidateQueries({ queryKey: ['agency', 'website', 'applications'] });
            }}
          />
        </div>
      )}
    </Drawer>
  );
}

/** Kanban-style pipeline: one column per stage; each card can be moved with a select (keyboard friendly). */
function ApplicationsTab() {
  const [jobId, setJobId] = useState('');
  const [open, setOpen] = useState<string | null>(null);
  const toast = useToast();
  const client = useQueryClient();
  const jobs = useQuery({ queryKey: ['agency', 'website', 'jobs'], queryFn: () => api.get<Job[]>(`${W}/careers/jobs`) });
  const apps = useQuery({
    queryKey: ['agency', 'website', 'applications', jobId],
    queryFn: () => api.get<Paged<ApplicationSummary>>(`${W}/careers/applications`, { query: { jobOpeningId: jobId || undefined, pageSize: 200 } }),
  });
  const move = useMutation({
    mutationFn: async ({ id, stage }: { id: string; stage: ApplicationStage }) => {
      const detail = await api.get<Application>(`${W}/careers/applications/${id}`);
      return api.post<Application>(`${W}/careers/applications/${id}/move`, { stage, concurrencyStamp: detail.concurrencyStamp });
    },
    onSuccess: () => client.invalidateQueries({ queryKey: ['agency', 'website', 'applications'] }),
    onError: (e) => toast.error("Couldn't move the application", errorMessage(e)),
  });
  return (
    <div className="cms-page">
      <PageHeader title="Applications" description="Move candidates through New → Screening → Interview → Offer → Hired or Rejected. Every move is logged." />
      <FormField label="Role">
        <Select value={jobId} onChange={(e) => setJobId(e.target.value)} placeholder="All roles" options={(jobs.data ?? []).map((j) => ({ value: j.id, label: j.title }))} />
      </FormField>
      {apps.isError && <ErrorState error={apps.error} />}
      <div className="cms-board" aria-label="Applications pipeline">
        {STAGES.map((stage) => {
          const items = (apps.data?.items ?? []).filter((a) => a.stage === stage);
          return (
            <section key={stage} className="cms-board__col" aria-label={`${stage} (${items.length})`}>
              <h3>
                {stage} <Badge>{items.length}</Badge>
              </h3>
              {items.map((a) => (
                <article key={a.id} className="cms-board__card">
                  <button type="button" className="site-linkbutton" onClick={() => setOpen(a.id)}>
                    {a.name}
                  </button>
                  <span className="text-muted">{a.jobTitle}</span>
                  <span className="text-muted">Applied {formatDate(a.createdAt)}</span>
                  <label className="visually-hidden" htmlFor={`stage-${a.id}`}>
                    Stage for {a.name}
                  </label>
                  <Select id={`stage-${a.id}`} size="sm" value={a.stage} onChange={(e) => move.mutate({ id: a.id, stage: e.target.value as ApplicationStage })} options={STAGES.map((s) => ({ value: s, label: s }))} />
                </article>
              ))}
            </section>
          );
        })}
      </div>
      {apps.data && apps.data.items.length === 0 && <Alert tone="info">No applications yet.</Alert>}
      <ApplicationDrawer id={open} onClose={() => setOpen(null)} />
    </div>
  );
}

/** Careers: job openings and the applications pipeline. */
export function CareersAdminPage() {
  return (
    <Tabs
      label="Careers"
      tabs={[
        { id: 'applications', label: 'Applications', content: <ApplicationsTab /> },
        { id: 'jobs', label: 'Job openings', content: <JobsTab /> },
      ]}
    />
  );
}
