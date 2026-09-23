import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { FileBarChart, Plus, Printer, RefreshCw, Send, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  Switch,
  Tabs,
  Textarea,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { ReportView } from '../shared/ReportView';
import type { KpiMeasurement, Report, ReportKpi, ReportSection, ReportSummary } from '../shared/deliveryTypes';
import { formatDateOnly, MeasurementLabel } from '../shared/deliveryUi';
import { dk, useClientOptions } from './api';

function lastMonth(): string {
  const d = new Date();
  d.setUTCDate(1);
  d.setUTCMonth(d.getUTCMonth() - 1);
  return d.toISOString().slice(0, 7);
}

function NewReportDialog({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const clients = useClientOptions();
  const [clientId, setClientId] = useState('');
  const [period, setPeriod] = useState(lastMonth());
  const templates = useQuery({
    queryKey: ['delivery', 'report-templates'],
    queryFn: ({ signal }) => api.get<{ key: string; name: string }[]>('/agency/templates/reports', { signal }),
  });
  const [templateKey, setTemplateKey] = useState('monthly-performance');
  const create = useMutation({
    mutationFn: () => api.post<Report>('/agency/reports', { clientId, period, templateKey }),
    onSuccess: (r) => navigate(`/agency/reports/${r.id}`),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="New client report"
      description="Sections with a connected data source are filled automatically; everything else is entered by you."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="new-report" loading={create.isPending} disabled={!clientId}>
            Create draft
          </Button>
        </>
      }
    >
      <form
        id="new-report"
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          create.mutate();
        }}
      >
        {create.error ? <Alert tone="danger">{errorMessage(create.error)}</Alert> : null}
        <FormField label="Client" required>
          <Select value={clientId} onChange={(e) => setClientId(e.target.value)} placeholder="Choose…" options={(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))} />
        </FormField>
        <div className="dl-form__row">
          <FormField label="Month">
            <Input type="month" value={period} onChange={(e) => setPeriod(e.target.value)} />
          </FormField>
          <FormField label="Template">
            <Select value={templateKey} onChange={(e) => setTemplateKey(e.target.value)} options={(templates.data ?? []).map((t) => ({ value: t.key, label: t.name }))} />
          </FormField>
        </div>
      </form>
    </Dialog>
  );
}

export function ReportsPage() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('');
  const [creating, setCreating] = useState(false);
  const query = { status: status || undefined, page, pageSize: 25 };
  const reports = useQuery({
    queryKey: dk.reports(query),
    queryFn: ({ signal }) => api.get<PagedResult<ReportSummary>>('/agency/reports', { query, signal }),
    placeholderData: keepPreviousData,
  });
  return (
    <div className="dl-page">
      <PageHeader
        title="Client reports"
        description="Monthly performance reports. Drafts are created automatically for active retainers on the 1st."
        actions={
          <Button leadingIcon={<Plus aria-hidden="true" />} onClick={() => setCreating(true)}>
            New report
          </Button>
        }
      />
      <Card>
        <CardBody className="dl-page">
          <div className="dl-toolbar">
            <FormField label="Status">
              <Select value={status} onChange={(e) => setStatus(e.target.value)} placeholder="All" options={[{ value: 'Draft', label: 'Draft' }, { value: 'Published', label: 'Published' }]} />
            </FormField>
          </div>
          {reports.isError ? (
            <ErrorState error={reports.error} />
          ) : (
            <DataTable
              caption="Reports"
              rows={reports.data?.items ?? []}
              loading={reports.isPending}
              getRowId={(r) => r.id}
              columns={[
                {
                  id: 'title',
                  header: 'Report',
                  primary: true,
                  cell: (r) => (
                    <Link className="ui-link" to={`/agency/reports/${r.id}`}>
                      {r.title}
                    </Link>
                  ),
                },
                { id: 'client', header: 'Client', cell: (r) => r.clientName },
                { id: 'period', header: 'Period', cell: (r) => formatDateOnly(r.periodStart, { month: 'long', year: 'numeric' }) },
                {
                  id: 'status',
                  header: 'Status',
                  cell: (r) => (
                    <span className="dl-row">
                      <Badge tone={r.status === 'Published' ? 'success' : 'neutral'}>{r.status}</Badge>
                      {r.autoGenerated ? <Badge size="sm">Auto draft</Badge> : null}
                    </span>
                  ),
                },
                { id: 'updated', header: 'Updated', cell: (r) => <DateTime value={r.updatedAt} format="relative" />, hideOnMobile: true },
              ]}
              emptyState={<EmptyState icon={<FileBarChart aria-hidden="true" />} title="No reports yet" />}
            />
          )}
          {reports.data && reports.data.total > reports.data.pageSize ? (
            <Pagination page={page} pageSize={reports.data.pageSize} total={reports.data.total} onPageChange={setPage} />
          ) : null}
        </CardBody>
      </Card>
      {creating ? <NewReportDialog onClose={() => setCreating(false)} /> : null}
    </div>
  );
}

const MEASUREMENTS: KpiMeasurement[] = ['Manual', 'Measured', 'Estimated'];

function KpiEditor({ kpi, onChange, onRemove }: { kpi: ReportKpi; onChange: (k: ReportKpi) => void; onRemove: () => void }) {
  const num = (v: string) => (v.trim() === '' ? null : Number(v));
  return (
    <div className="dl-section-kpi-row" role="group" aria-label={`KPI ${kpi.label || 'untitled'}`}>
      <FormField label="Label">
        <Input value={kpi.label} onChange={(e) => onChange({ ...kpi, label: e.target.value })} />
      </FormField>
      <FormField label="Value">
        <Input type="number" value={kpi.value ?? ''} onChange={(e) => onChange({ ...kpi, value: num(e.target.value) })} />
      </FormField>
      <FormField label="Previous">
        <Input type="number" value={kpi.previousValue ?? ''} onChange={(e) => onChange({ ...kpi, previousValue: num(e.target.value) })} />
      </FormField>
      <FormField label="Unit">
        <Input value={kpi.unit ?? ''} onChange={(e) => onChange({ ...kpi, unit: e.target.value || null })} placeholder="%, USD, h…" />
      </FormField>
      <FormField label="Source" required>
        <Input value={kpi.source} onChange={(e) => onChange({ ...kpi, source: e.target.value })} placeholder="e.g. Google Analytics 4" />
      </FormField>
      <FormField label="Measurement">
        <Select value={kpi.measurement} onChange={(e) => onChange({ ...kpi, measurement: e.target.value as KpiMeasurement })} options={MEASUREMENTS.map((m) => ({ value: m, label: m }))} />
      </FormField>
      <IconButton label={`Remove KPI ${kpi.label}`} icon={<Trash2 />} variant="ghost" onClick={onRemove} />
    </div>
  );
}

function SectionEditor({
  section,
  onChange,
  onRefresh,
  refreshing,
}: {
  section: ReportSection;
  onChange: (s: ReportSection) => void;
  onRefresh: () => void;
  refreshing: boolean;
}) {
  return (
    <section className="dl-editor-section" aria-labelledby={`edit-${section.key}`}>
      <div className="dl-row">
        <h3 id={`edit-${section.key}`}>{section.title}</h3>
        {section.providerKey ? (
          <Button size="sm" variant="ghost" leadingIcon={<RefreshCw aria-hidden="true" />} loading={refreshing} onClick={onRefresh}>
            Refresh data
          </Button>
        ) : null}
      </div>
      {section.providerNote ? <Alert tone="info">{section.providerNote}</Alert> : null}
      <FormField label="Text" hint="Plain text; line breaks are kept.">
        <Textarea rows={4} value={(section.body ?? '').replace(/<!--[\s\S]*?-->/g, '')} onChange={(e) => onChange({ ...section, body: e.target.value })} />
      </FormField>
      {section.kpis.map((k, i) => (
        <KpiEditor
          key={`${k.key}-${i}`}
          kpi={k}
          onChange={(kpi) => onChange({ ...section, kpis: section.kpis.map((x, j) => (j === i ? kpi : x)) })}
          onRemove={() => onChange({ ...section, kpis: section.kpis.filter((_, j) => j !== i) })}
        />
      ))}
      <div className="dl-row">
        <Button
          size="sm"
          variant="secondary"
          leadingIcon={<Plus aria-hidden="true" />}
          onClick={() =>
            onChange({
              ...section,
              kpis: [...section.kpis, { key: '', label: '', value: null, unit: null, previousValue: null, source: '', measurement: 'Manual' }],
            })
          }
        >
          Add KPI
        </Button>
        {section.kpis.map((k) => (
          <MeasurementLabel key={k.key + k.label} measurement={k.measurement} />
        ))}
      </div>
    </section>
  );
}

/** Report builder: edit sections and KPIs (every KPI needs a source and a measurement label), preview, publish, print. */
export function ReportBuilderPage() {
  const { reportId = '' } = useParams();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [tab, setTab] = useState('edit');
  const [draft, setDraft] = useState<Report | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [notify, setNotify] = useState(true);
  const report = useQuery({
    queryKey: dk.report(reportId),
    queryFn: ({ signal }) => api.get<Report>(`/agency/reports/${reportId}`, { signal }),
  });
  const current = draft ?? report.data;
  const onSaved = (r: Report) => {
    qc.setQueryData(dk.report(reportId), r);
    setDraft(null);
    void qc.invalidateQueries({ queryKey: ['delivery', 'reports'] });
  };
  const save = useMutation({
    mutationFn: (r: Report) => api.put<Report>(`/agency/reports/${reportId}`, { title: r.title, sections: r.sections, concurrencyStamp: r.concurrencyStamp }),
    onSuccess: onSaved,
  });
  const refresh = useMutation({
    mutationFn: (key: string) => api.post<Report>(`/agency/reports/${reportId}/refresh-section`, { sectionKey: key, concurrencyStamp: current!.concurrencyStamp }),
    onSuccess: onSaved,
  });
  const remove = useMutation({
    mutationFn: () => api.delete(`/agency/reports/${reportId}`),
    onSuccess: () => navigate('/agency/reports'),
  });
  if (report.isPending) return <Skeleton height={300} />;
  if (report.isError) return <ErrorState error={report.error} onRetry={() => void report.refetch()} />;
  const r = current!;
  const editable = r.status === 'Draft';
  return (
    <div className="dl-page">
      <PageHeader
        title={r.title}
        breadcrumbs={[{ label: 'Reports', to: '/agency/reports' }, { label: r.clientName, to: `/agency/clients/${r.clientId}` }, { label: r.title }]}
        meta={<Badge tone={r.status === 'Published' ? 'success' : 'neutral'}>{r.status}</Badge>}
        actions={
          <span className="dl-row dl-no-print">
            <Button variant="secondary" leadingIcon={<Printer aria-hidden="true" />} onClick={() => window.print()}>
              Print
            </Button>
            {editable ? (
              <>
                <Button variant="secondary" disabled={!draft} loading={save.isPending} onClick={() => save.mutate(r)}>
                  Save draft
                </Button>
                <Button leadingIcon={<Send aria-hidden="true" />} disabled={Boolean(draft)} onClick={() => setPublishing(true)}>
                  Publish to client
                </Button>
              </>
            ) : null}
          </span>
        }
      />
      {save.error || refresh.error ? <Alert tone="danger">{errorMessage(save.error ?? refresh.error)}</Alert> : null}
      {draft ? <Alert tone="warning">You have unsaved changes. Save the draft before publishing.</Alert> : null}
      <Tabs
        label="Report views"
        value={editable ? tab : 'preview'}
        onValueChange={setTab}
        tabs={[
          ...(editable
            ? [
                {
                  id: 'edit',
                  label: 'Edit',
                  content: (
                    <div className="dl-page dl-no-print">
                      <FormField label="Title">
                        <Input value={r.title} onChange={(e) => setDraft({ ...r, title: e.target.value })} />
                      </FormField>
                      {r.sections.map((s, i) => (
                        <SectionEditor
                          key={s.key}
                          section={s}
                          refreshing={refresh.isPending && refresh.variables === s.key}
                          onRefresh={() => refresh.mutate(s.key)}
                          onChange={(next) => setDraft({ ...r, sections: r.sections.map((x, j) => (j === i ? next : x)) })}
                        />
                      ))}
                      <div>
                        <Button variant="danger" onClick={() => remove.mutate()} loading={remove.isPending}>
                          Delete draft
                        </Button>
                      </div>
                    </div>
                  ),
                },
              ]
            : []),
          { id: 'preview', label: 'Preview', content: <ReportView report={r} audience="staff" /> },
        ]}
      />
      <ConfirmDialog
        open={publishing}
        onClose={() => setPublishing(false)}
        title="Publish this report?"
        description="It appears in the client portal immediately and can't be edited afterwards."
        confirmLabel="Publish"
        tone="primary"
        onConfirm={async () => {
          const published = await api.post<Report>(`/agency/reports/${reportId}/publish`, { concurrencyStamp: r.concurrencyStamp, notifyByEmail: notify });
          onSaved(published);
          setPublishing(false);
        }}
      >
        <Switch checked={notify} onCheckedChange={setNotify} label="Email the client's users" description="They always get an in-app notification." />
      </ConfirmDialog>
    </div>
  );
}
