import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronLeft, ChevronRight, Download, Trash2 } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  DataTable,
  EmptyState,
  ErrorState,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Stat,
  Tabs,
  Textarea,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import type { Timesheet, TimesheetStatus, Utilization } from '../shared/deliveryTypes';
import { formatDateOnly, formatMinutes, todayIso } from '../shared/deliveryUi';
import { dk, useProjectOptions } from './api';
import { TimerWidget } from './TimerWidget';

const sheetTone: Record<TimesheetStatus, 'neutral' | 'info' | 'success' | 'danger'> = {
  Open: 'neutral',
  Submitted: 'info',
  Approved: 'success',
  Rejected: 'danger',
};

function shiftDate(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const date = new Date(Date.UTC(y!, m! - 1, d! + days));
  return date.toISOString().slice(0, 10);
}

function ManualEntry({ date, onSaved }: { date: string; onSaved: () => void }) {
  const projects = useProjectOptions();
  const [form, setForm] = useState({ projectId: '', date, hours: '1', billable: true, note: '' });
  const save = useMutation({
    mutationFn: () =>
      api.post('/agency/time/entries', {
        projectId: form.projectId,
        date: form.date,
        minutes: Math.round(Number(form.hours) * 60),
        billable: form.billable,
        note: form.note || null,
      }),
    onSuccess: () => {
      setForm((f) => ({ ...f, note: '' }));
      onSaved();
    },
  });
  return (
    <form
      className="dl-form"
      aria-label="Log time"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate();
      }}
    >
      {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
      <div className="dl-form__row">
        <FormField label="Project" required>
          <Select
            value={form.projectId}
            onChange={(e) => setForm({ ...form, projectId: e.target.value })}
            placeholder="Choose…"
            options={(projects.data ?? []).map((p) => ({ value: p.id, label: `${p.clientName} — ${p.name}` }))}
          />
        </FormField>
        <FormField label="Date">
          <Input type="date" value={form.date} max={todayIso()} onChange={(e) => setForm({ ...form, date: e.target.value })} />
        </FormField>
        <FormField label="Hours">
          <Input type="number" min={0.25} max={24} step={0.25} value={form.hours} onChange={(e) => setForm({ ...form, hours: e.target.value })} />
        </FormField>
      </div>
      <FormField label="Note" optional>
        <Input value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} maxLength={1000} />
      </FormField>
      <Checkbox label="Billable" checked={form.billable} onChange={(e) => setForm({ ...form, billable: e.target.checked })} />
      <div className="dl-row">
        <Button type="submit" disabled={!form.projectId || Number(form.hours) <= 0} loading={save.isPending}>
          Log time
        </Button>
      </div>
    </form>
  );
}

function MyWeek() {
  const qc = useQueryClient();
  const [date, setDate] = useState(todayIso());
  const week = useQuery({
    queryKey: dk.week(date),
    queryFn: ({ signal }) => api.get<Timesheet>('/agency/time/timesheets/week', { query: { date }, signal }),
  });
  const refresh = () => void qc.invalidateQueries({ queryKey: ['delivery', 'week'] });
  const submit = useMutation({
    mutationFn: () => api.post<Timesheet>(`/agency/time/timesheets/submit?date=${date}`),
    onSuccess: refresh,
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/agency/time/entries/${id}`),
    onSuccess: refresh,
  });
  if (week.isPending) return <Skeleton height={200} />;
  if (week.isError) return <ErrorState error={week.error} />;
  const w = week.data;
  const locked = w.status === 'Submitted' || w.status === 'Approved';
  return (
    <div className="dl-page">
      <div className="dl-toolbar">
        <IconButton label="Previous week" icon={<ChevronLeft />} variant="secondary" onClick={() => setDate(shiftDate(w.weekStart, -7))} />
        <strong>Week of {formatDateOnly(w.weekStart)}</strong>
        <IconButton label="Next week" icon={<ChevronRight />} variant="secondary" onClick={() => setDate(shiftDate(w.weekStart, 7))} />
        <Badge tone={sheetTone[w.status]}>{w.status}</Badge>
      </div>
      {w.status === 'Rejected' && w.decisionComment ? <Alert tone="danger" title="Returned by your manager">{w.decisionComment}</Alert> : null}
      <div className="dl-week" role="list" aria-label="Hours per day">
        {w.days.map((d) => (
          <div key={d.date} className="dl-week__day" role="listitem">
            <span>{formatDateOnly(d.date, { weekday: 'short' })}</span>
            <strong>{formatMinutes(d.minutes)}</strong>
          </div>
        ))}
      </div>
      <div className="dl-stats">
        <Stat label="Total" value={formatMinutes(w.totalMinutes)} />
        <Stat label="Billable" value={formatMinutes(w.billableMinutes)} />
      </div>
      <DataTable
        caption="Entries this week"
        rows={w.entries}
        getRowId={(e) => e.id}
        columns={[
          { id: 'date', header: 'Date', cell: (e) => formatDateOnly(e.date) },
          { id: 'project', header: 'Project', primary: true, cell: (e) => `${e.clientName} — ${e.projectName}` },
          { id: 'note', header: 'Note', cell: (e) => e.note ?? '—', hideOnMobile: true },
          { id: 'time', header: 'Time', align: 'right', cell: (e) => (e.isRunning ? <Badge tone="info">Running</Badge> : formatMinutes(e.minutes)) },
          { id: 'billable', header: 'Billable', cell: (e) => (e.billable ? 'Yes' : 'No'), hideOnMobile: true },
          {
            id: 'actions',
            header: <span className="visually-hidden">Actions</span>,
            cell: (e) =>
              e.locked || e.isRunning ? null : (
                <IconButton label={`Delete entry of ${formatDateOnly(e.date)}`} icon={<Trash2 />} variant="ghost" onClick={() => remove.mutate(e.id)} />
              ),
          },
        ]}
        emptyState={<EmptyState compact title="No time logged this week" />}
      />
      {!locked ? (
        <>
          <ManualEntry key={w.weekStart} date={w.weekStart <= todayIso() && todayIso() <= shiftDate(w.weekStart, 6) ? todayIso() : w.weekStart} onSaved={refresh} />
          <div className="dl-row">
            <Button variant="highlight" disabled={w.totalMinutes === 0} loading={submit.isPending} onClick={() => submit.mutate()}>
              Submit week for approval
            </Button>
          </div>
        </>
      ) : null}
      {submit.error || remove.error ? <Alert tone="danger">{errorMessage(submit.error ?? remove.error)}</Alert> : null}
    </div>
  );
}

function Approvals() {
  const qc = useQueryClient();
  const [comments, setComments] = useState<Record<string, string>>({});
  const pending = useQuery({
    queryKey: ['delivery', 'week', 'pending'],
    queryFn: ({ signal }) => api.get<Timesheet[]>('/agency/time/timesheets/pending', { signal }),
  });
  const decide = useMutation({
    mutationFn: ({ sheet, approve }: { sheet: Timesheet; approve: boolean }) =>
      api.post(`/agency/time/timesheets/${sheet.id}/${approve ? 'approve' : 'reject'}`, {
        comment: comments[sheet.id!] || null,
        concurrencyStamp: sheet.concurrencyStamp,
      }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['delivery', 'week'] }),
  });
  if (pending.isPending) return <Skeleton height={160} />;
  if (pending.isError) return <ErrorState error={pending.error} />;
  if (pending.data.length === 0) return <EmptyState compact title="No timesheets waiting for approval" />;
  return (
    <div className="dl-page">
      {decide.error ? <Alert tone="danger">{errorMessage(decide.error)}</Alert> : null}
      {pending.data.map((s) => (
        <Card key={s.id} as="article" aria-label={`${s.userName}, week of ${formatDateOnly(s.weekStart)}`}>
          <CardHeader title={`${s.userName} · week of ${formatDateOnly(s.weekStart)}`} headingLevel={3} description={`${formatMinutes(s.totalMinutes)} total, ${formatMinutes(s.billableMinutes)} billable`} />
          <CardBody className="dl-form">
            <ul className="dl-list">
              {s.entries.map((e) => (
                <li key={e.id} className="dl-list__item">
                  <span>
                    {formatDateOnly(e.date)} · {e.clientName} — {e.projectName}
                    {e.note ? ` · ${e.note}` : ''}
                  </span>
                  <span>
                    {formatMinutes(e.minutes)}
                    {e.billable ? '' : ' (non-billable)'}
                  </span>
                </li>
              ))}
            </ul>
            <FormField label="Comment" optional hint="Required when returning a timesheet.">
              <Textarea rows={2} value={comments[s.id!] ?? ''} onChange={(e) => setComments({ ...comments, [s.id!]: e.target.value })} />
            </FormField>
            <div className="dl-row">
              <Button onClick={() => decide.mutate({ sheet: s, approve: true })}>Approve</Button>
              <Button variant="danger" disabled={!comments[s.id!]?.trim()} onClick={() => decide.mutate({ sheet: s, approve: false })}>
                Return for changes
              </Button>
            </div>
          </CardBody>
        </Card>
      ))}
    </div>
  );
}

function Reports() {
  const [from, setFrom] = useState(shiftDate(todayIso(), -29));
  const [to, setTo] = useState(todayIso());
  const util = useQuery({
    queryKey: ['delivery', 'utilization', from, to],
    queryFn: ({ signal }) => api.get<Utilization>('/agency/time/utilization', { query: { from, to }, signal }),
  });
  const [downloading, setDownloading] = useState(false);
  return (
    <div className="dl-page">
      <div className="dl-toolbar">
        <FormField label="From">
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </FormField>
        <FormField label="To">
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </FormField>
        <Button
          variant="secondary"
          leadingIcon={<Download aria-hidden="true" />}
          loading={downloading}
          onClick={async () => {
            setDownloading(true);
            try {
              await api.download(`/agency/time/entries/export.csv?from=${from}&to=${to}`, `time-${from}-${to}.csv`);
            } finally {
              setDownloading(false);
            }
          }}
        >
          Export my entries (CSV)
        </Button>
      </div>
      {util.isPending ? (
        <Skeleton height={160} />
      ) : util.isError ? (
        <ErrorState error={util.error} />
      ) : (
        <>
          <div className="dl-stats">
            <Stat label="Hours logged" value={formatMinutes(util.data.totalMinutes)} />
            <Stat label="Billable" value={formatMinutes(util.data.billableMinutes)} hint={util.data.totalMinutes ? `${Math.round((util.data.billableMinutes / util.data.totalMinutes) * 100)}% billable` : undefined} />
          </div>
          <DataTable
            caption="Utilization by person"
            rows={util.data.rows}
            getRowId={(r) => r.userId}
            columns={[
              { id: 'name', header: 'Person', primary: true, cell: (r) => r.userName },
              { id: 'total', header: 'Logged', align: 'right', cell: (r) => formatMinutes(r.totalMinutes) },
              { id: 'capacity', header: 'Capacity', align: 'right', cell: (r) => formatMinutes(r.capacityMinutes), hideOnMobile: true },
              { id: 'util', header: 'Utilization', align: 'right', cell: (r) => `${r.utilizationPercent}%` },
              { id: 'billable', header: 'Billable share', align: 'right', cell: (r) => `${r.billablePercent}%` },
            ]}
          />
        </>
      )}
    </div>
  );
}

export function TimePage() {
  const { hasPermission } = useAuth();
  const [tab, setTab] = useState('week');
  return (
    <div className="dl-page">
      <PageHeader title="Time" description="Timer, weekly timesheet, approvals and utilization." />
      <Card as="section" aria-label="Timer">
        <CardHeader title="Timer" headingLevel={2} />
        <CardBody>
          <TimerWidget />
        </CardBody>
      </Card>
      <Tabs
        label="Time sections"
        value={tab}
        onValueChange={setTab}
        tabs={[
          { id: 'week', label: 'My timesheet', content: <MyWeek /> },
          ...(hasPermission(Permissions.ProjectsManage) ? [{ id: 'approvals', label: 'Approvals', content: <Approvals /> }] : []),
          ...(hasPermission(Permissions.TimeViewAll) ? [{ id: 'reports', label: 'Utilization', content: <Reports /> }] : []),
        ]}
      />
    </div>
  );
}
