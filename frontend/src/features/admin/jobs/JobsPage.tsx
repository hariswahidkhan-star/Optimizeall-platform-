import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronDown, ChevronRight, Info, Play, RotateCw } from 'lucide-react';
import { useId, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { FilterBar } from '@/components/ui/FilterBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Tabs } from '@/components/ui/Tabs';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { humanize } from '@/lib/format/text';
import {
  CHANNELS,
  DELIVERY_STATUSES,
  JOB_RUN_STATUSES,
  type Delivery,
  type Job,
  type JobRun,
} from '../api/types';
import { AdminBadge } from '../shared/badges';
import { enumOptions, QueryError, useCan } from '../shared/common';
import { adminErrorMessage, toDisplayError } from '../shared/errors';

export function formatInterval(seconds: number): string {
  if (seconds < 60) return `Every ${seconds} s`;
  if (seconds < 3600) return `Every ${Math.round(seconds / 60)} min`;
  if (seconds < 86400) return `Every ${+(seconds / 3600).toFixed(1)} h`;
  return `Every ${+(seconds / 86400).toFixed(1)} days`;
}

function Collapsible({ label, text, tone }: { label: string; text: string; tone?: 'danger' }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  return (
    <div className="admin-collapsible">
      <button
        type="button"
        className="admin-collapsible__toggle"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen((v) => !v)}
      >
        {open ? <ChevronDown aria-hidden="true" /> : <ChevronRight aria-hidden="true" />}
        {label}
      </button>
      <pre id={id} hidden={!open} className={tone === 'danger' ? 'admin-pre admin-pre--danger' : 'admin-pre'}>
        {text}
      </pre>
    </div>
  );
}

// ---------- Registered jobs ----------

function JobsTab() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const canRun = useCan(Permissions.SettingsManage);
  const [running, setRunning] = useState<Job | null>(null);
  const jobs = useQuery({
    queryKey: ['admin', 'jobs'],
    queryFn: ({ signal }) => api.get<Job[]>('/admin/jobs', { signal }),
  });

  const run = useMutation({
    mutationFn: (job: Job) => api.post<JobRun>(`/admin/jobs/${encodeURIComponent(job.name)}/run`),
    onSuccess: (result, job) => {
      void queryClient.invalidateQueries({ queryKey: ['admin', 'jobs'] });
      void queryClient.invalidateQueries({ queryKey: ['admin', 'jobRuns'] });
      if (result.status === 'Failed')
        toast.error(`${job.name} failed`, result.error ?? result.summary ?? undefined);
      else toast.success(`${job.name} finished`, result.summary ?? humanize(result.status));
    },
  });

  return (
    <Card>
      <CardHeader
        title="Registered jobs"
        description="Background jobs run on a fixed interval on every API instance; a lease makes sure only one instance runs each job at a time."
      />
      <CardBody>
        {!canRun && (
          <Alert tone="info" className="admin-mb">
            Running a job manually needs the settings permission.
          </Alert>
        )}
        {jobs.isError ? (
          <QueryError error={jobs.error} onRetry={() => void jobs.refetch()} headingLevel={3} />
        ) : (
          <DataTable
            caption="Registered jobs"
            rows={jobs.data ?? []}
            loading={jobs.isPending}
            getRowId={(j) => j.name}
            columns={[
              {
                id: 'name',
                header: 'Job',
                primary: true,
                cell: (j) => (
                  <div className="admin-cell-stack">
                    <strong>{j.name}</strong>
                    {j.jobName !== j.name && <code className="text-small">{j.jobName}</code>}
                  </div>
                ),
              },
              { id: 'interval', header: 'Interval', cell: (j) => formatInterval(j.intervalSeconds) },
              {
                id: 'last',
                header: 'Last run',
                cell: (j) =>
                  j.lastRun ? (
                    <div className="admin-cell-stack">
                      <AdminBadge kind="jobRun" value={j.lastRun.status} />
                      <span className="text-small text-muted">
                        <DateTime value={j.lastRun.startedAt} format="relative" />
                      </span>
                    </div>
                  ) : (
                    <span className="text-muted">Never run</span>
                  ),
              },
              {
                id: 'summary',
                header: 'Last result',
                hideOnMobile: true,
                cell: (j) =>
                  j.lastRun?.error ? (
                    <Collapsible label="Show error" text={j.lastRun.error} tone="danger" />
                  ) : (
                    <span className="text-small">{j.lastRun?.summary ?? '—'}</span>
                  ),
              },
              ...(canRun
                ? [
                    {
                      id: 'run',
                      header: <span className="visually-hidden">Run</span>,
                      align: 'right' as const,
                      cell: (j: Job) => (
                        <Button
                          size="sm"
                          variant="secondary"
                          leadingIcon={<Play />}
                          loading={run.isPending && run.variables?.name === j.name}
                          onClick={() => setRunning(j)}
                          aria-label={`Run ${j.name} now`}
                        >
                          Run now
                        </Button>
                      ),
                    },
                  ]
                : []),
            ]}
          />
        )}
      </CardBody>
      <ConfirmDialog
        open={running !== null}
        onClose={() => setRunning(null)}
        title={`Run ${running?.name ?? 'job'} now?`}
        description="The job runs immediately on this API instance, outside its schedule. The request is recorded in the audit log."
        confirmLabel="Run now"
        onConfirm={async () => {
          if (!running) return;
          try {
            await run.mutateAsync(running);
          } catch (error) {
            toast.error('Job not started', adminErrorMessage(error));
            throw toDisplayError(error);
          }
        }}
      />
    </Card>
  );
}

// ---------- Run history ----------

function RunsTab({ jobNames }: { jobNames: string[] }) {
  const [jobName, setJobName] = useState<string | undefined>();
  const [status, setStatus] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const query = { jobName, status, page, pageSize: 25 };
  const runs = useQuery({
    queryKey: ['admin', 'jobRuns', query],
    queryFn: ({ signal }) => api.get<PagedResult<JobRun>>('/admin/jobs/runs', { query, signal }),
    placeholderData: keepPreviousData,
  });

  return (
    <Card>
      <CardHeader
        title="Run history"
        description="Every job execution, newest first. Failed runs include the error."
      />
      <CardBody className="stack">
        <FilterBar
          filters={[
            { id: 'jobName', label: 'Job', options: jobNames.map((n) => ({ value: n, label: n })) },
            { id: 'status', label: 'Status', options: enumOptions(JOB_RUN_STATUSES) },
          ]}
          values={{ jobName, status }}
          onFilterChange={(id, value) => {
            if (id === 'jobName') setJobName(value);
            else setStatus(value);
            setPage(1);
          }}
          onReset={() => {
            setJobName(undefined);
            setStatus(undefined);
            setPage(1);
          }}
        />
        {runs.isError ? (
          <QueryError error={runs.error} onRetry={() => void runs.refetch()} headingLevel={3} />
        ) : (
          <>
            <DataTable
              caption="Job runs"
              rows={runs.data?.items ?? []}
              loading={runs.isPending}
              getRowId={(r) => r.id}
              columns={[
                {
                  id: 'job',
                  header: 'Job',
                  primary: true,
                  cell: (r) => (
                    <div className="admin-cell-stack">
                      <strong>{r.jobName}</strong>
                      <code className="text-small">{r.runKey}</code>
                    </div>
                  ),
                },
                {
                  id: 'status',
                  header: 'Status',
                  cell: (r) => <AdminBadge kind="jobRun" value={r.status} />,
                },
                { id: 'started', header: 'Started', cell: (r) => <DateTime value={r.startedAt} /> },
                {
                  id: 'duration',
                  header: 'Duration',
                  align: 'right',
                  hideOnMobile: true,
                  cell: (r) =>
                    r.finishedAt
                      ? `${((new Date(r.finishedAt).getTime() - new Date(r.startedAt).getTime()) / 1000).toFixed(1)} s`
                      : '—',
                },
                {
                  id: 'attempt',
                  header: 'Attempt',
                  align: 'right',
                  hideOnMobile: true,
                  cell: (r) => r.attempt,
                },
                {
                  id: 'result',
                  header: 'Result',
                  cell: (r) => (
                    <div className="admin-cell-stack">
                      {r.summary && <span className="text-small">{r.summary}</span>}
                      {r.error && <Collapsible label="Show error" text={r.error} tone="danger" />}
                      {!r.summary && !r.error && '—'}
                    </div>
                  ),
                },
              ]}
              emptyState={<EmptyState compact headingLevel={3} title="No runs match" />}
            />
            {runs.data && runs.data.total > 25 && (
              <Pagination
                page={page}
                pageSize={25}
                total={runs.data.total}
                onPageChange={setPage}
                label="Run pages"
              />
            )}
          </>
        )}
      </CardBody>
    </Card>
  );
}

// ---------- Notification deliveries ----------

function DeliveriesTab() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [values, setValues] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [retrying, setRetrying] = useState<Delivery | null>(null);
  const query = { search, ...values, page, pageSize: 25, desc: true };
  const deliveries = useQuery({
    queryKey: ['admin', 'deliveries', query],
    queryFn: ({ signal }) =>
      api.get<PagedResult<Delivery>>('/admin/notifications/deliveries', { query, signal }),
    placeholderData: keepPreviousData,
  });

  const retry = useMutation({
    mutationFn: (d: Delivery) => api.post<Delivery>(`/admin/notifications/deliveries/${d.id}/retry`),
    onSuccess: (d) => {
      void queryClient.invalidateQueries({ queryKey: ['admin', 'deliveries'] });
      toast.success(
        'Delivery queued for retry',
        `${humanize(d.channel)} to ${d.userEmail ?? 'the user'} will be sent shortly.`,
      );
    },
  });

  return (
    <Card>
      <CardHeader
        title="Notification deliveries"
        description="The outbox: one row per notification per channel. Failed deliveries retry automatically (1 min, 5 min, 30 min, 2 h, 12 h) and become permanently failed after 6 attempts."
      />
      <CardBody className="stack">
        <Alert tone="info" icon={<Info />} title="WhatsApp deliveries are skipped for now">
          WhatsApp deliveries show as <strong>Skipped — credentials not configured</strong> until WhatsApp
          Business API credentials are provided in the server configuration. Nothing is lost: in-app and email
          copies are still sent.
        </Alert>
        <FilterBar
          search={search}
          onSearchChange={(v) => {
            setSearch(v);
            setPage(1);
          }}
          searchLabel="Search deliveries"
          searchPlaceholder="Email, type or title…"
          filters={[
            { id: 'status', label: 'Status', options: enumOptions(DELIVERY_STATUSES) },
            {
              id: 'channel',
              label: 'Channel',
              options: enumOptions(CHANNELS, (c) => (c === 'InApp' ? 'In-app' : c)),
            },
          ]}
          values={values}
          onFilterChange={(id, value) => {
            setValues((v) => ({ ...v, [id]: value }));
            setPage(1);
          }}
          onReset={() => {
            setSearch('');
            setValues({});
            setPage(1);
          }}
        />
        {deliveries.isError ? (
          <QueryError error={deliveries.error} onRetry={() => void deliveries.refetch()} headingLevel={3} />
        ) : (
          <>
            <DataTable
              caption="Notification deliveries"
              rows={deliveries.data?.items ?? []}
              loading={deliveries.isPending}
              getRowId={(d) => d.id}
              columns={[
                {
                  id: 'notification',
                  header: 'Notification',
                  primary: true,
                  cell: (d) => (
                    <div className="admin-cell-stack">
                      <strong>{d.title ?? d.type ?? 'Notification'}</strong>
                      <span className="text-small text-muted">
                        {d.type} · {d.userEmail ?? d.userId}
                      </span>
                    </div>
                  ),
                },
                {
                  id: 'channel',
                  header: 'Channel',
                  cell: (d) => (d.channel === 'InApp' ? 'In-app' : d.channel),
                },
                {
                  id: 'status',
                  header: 'Status',
                  cell: (d) => (
                    <div className="admin-cell-stack">
                      <AdminBadge kind="delivery" value={d.status} />
                      {d.status === 'Skipped' &&
                        d.channel === 'WhatsApp' &&
                        /not configured/i.test(d.lastError ?? '') && (
                          <span className="text-small text-muted">Skipped — credentials not configured</span>
                        )}
                    </div>
                  ),
                },
                { id: 'attempts', header: 'Attempts', align: 'right', cell: (d) => d.attempts },
                {
                  id: 'when',
                  header: 'Sent / next attempt',
                  hideOnMobile: true,
                  cell: (d) =>
                    d.sentAt ? (
                      <DateTime value={d.sentAt} />
                    ) : d.status === 'Pending' || d.status === 'Failed' ? (
                      <span className="text-small">
                        Next: <DateTime value={d.nextAttemptAt} format="relative" />
                      </span>
                    ) : (
                      '—'
                    ),
                },
                {
                  id: 'error',
                  header: 'Last error',
                  cell: (d) =>
                    d.lastError ? (
                      <Collapsible
                        label="Show error"
                        text={d.lastError}
                        tone={d.status === 'Failed' ? 'danger' : undefined}
                      />
                    ) : (
                      '—'
                    ),
                },
                {
                  id: 'retry',
                  header: <span className="visually-hidden">Retry</span>,
                  align: 'right',
                  cell: (d) =>
                    d.status === 'Failed' ? (
                      <Button
                        size="sm"
                        variant="secondary"
                        leadingIcon={<RotateCw />}
                        aria-label={`Retry ${d.channel} delivery to ${d.userEmail ?? d.userId}`}
                        onClick={() => setRetrying(d)}
                      >
                        Retry
                      </Button>
                    ) : null,
                },
              ]}
              emptyState={<EmptyState compact headingLevel={3} title="No deliveries match" />}
            />
            {deliveries.data && deliveries.data.total > 25 && (
              <Pagination
                page={page}
                pageSize={25}
                total={deliveries.data.total}
                onPageChange={setPage}
                label="Delivery pages"
              />
            )}
          </>
        )}
      </CardBody>
      <ConfirmDialog
        open={retrying !== null}
        onClose={() => setRetrying(null)}
        title="Retry this delivery?"
        description={
          retrying
            ? `The ${humanize(retrying.channel)} delivery to ${retrying.userEmail ?? 'this user'} goes back to Pending with its attempts reset, and is sent on the next dispatch (within about 30 seconds).`
            : undefined
        }
        confirmLabel="Retry delivery"
        onConfirm={async () => {
          if (!retrying) return;
          try {
            await retry.mutateAsync(retrying);
          } catch (error) {
            throw toDisplayError(error);
          }
        }}
      />
    </Card>
  );
}

const TABS = ['jobs', 'runs', 'deliveries'] as const;

export function JobsPage() {
  const [params, setParams] = useSearchParams();
  const current = TABS.find((t) => t === params.get('tab')) ?? 'jobs';
  const jobs = useQuery({
    queryKey: ['admin', 'jobs'],
    queryFn: ({ signal }) => api.get<Job[]>('/admin/jobs', { signal }),
  });
  const jobNames = [...new Set((jobs.data ?? []).map((j) => j.jobName))].sort();

  return (
    <>
      <PageHeader
        title="Jobs & notifications"
        description="Background job health and the notification outbox."
      />
      <Tabs
        label="Jobs and notifications"
        value={current}
        onValueChange={(tab) => setParams({ tab }, { replace: true })}
        tabs={[
          { id: 'jobs', label: 'Jobs', content: <JobsTab /> },
          { id: 'runs', label: 'Run history', content: <RunsTab jobNames={jobNames} /> },
          { id: 'deliveries', label: 'Notification deliveries', content: <DeliveriesTab /> },
        ]}
      />
    </>
  );
}
