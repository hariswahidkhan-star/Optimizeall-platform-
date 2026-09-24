import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Mail, Plus, Printer } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Alert,
  Avatar,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  KeyValueList,
  ProgressBar,
  Select,
  Skeleton,
  Textarea,
} from '@/components/ui';
import { BrandKitView } from '@/features/agency/shared/BrandKitView';
import { MessagesPanel } from '@/features/agency/shared/MessagesPanel';
import { ReportView } from '@/features/agency/shared/ReportView';
import {
  CLIENT_DUTIES,
  type AccountTeamMember,
  type BrandKit,
  type Brief,
  type BriefTemplate,
  type ClientDuty,
  type ClientMember,
  type ClientProjectDetail,
  type ClientProjectSummary,
  type Report,
  type ReportSummary,
} from '@/features/agency/shared/deliveryTypes';
import {
  formatDateOnly,
  labelOf,
  ProjectStatusBadge,
  TaskStatusBadge,
} from '@/features/agency/shared/deliveryUi';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { ClientShell } from './ClientShell';
import { clientKeys } from './useClientOrg';

// ---------------------------------------------------------------- projects

export function ClientProjectsPage() {
  return (
    <ClientShell title="Projects" description="Progress and milestones of the work we're doing for you.">
      {({ base, org, link }) => (
        <ProjectList key={org.clientId} base={base} orgId={org.clientId} link={link} />
      )}
    </ClientShell>
  );
}

function ProjectList({ base, orgId, link }: { base: string; orgId: string; link: (p: string) => string }) {
  const projects = useQuery({
    queryKey: clientKeys.part(orgId, 'projects'),
    queryFn: ({ signal }) => api.get<ClientProjectSummary[]>(`${base}/projects`, { signal }),
  });
  if (projects.isPending) return <Skeleton height={200} />;
  if (projects.isError) return <ErrorState error={projects.error} />;
  if (projects.data.length === 0) return <EmptyState title="No projects yet" />;
  return (
    <div className="dl-grid dl-grid--wide">
      {projects.data.map((p) => (
        <Card key={p.id} as="article" aria-label={p.name}>
          <CardHeader
            title={
              <Link className="ui-link" to={link(`/client/projects/${p.id}`)}>
                {p.name}
              </Link>
            }
            headingLevel={2}
            actions={<ProjectStatusBadge status={p.status} />}
          />
          <CardBody className="dl-page">
            <ProgressBar
              value={p.progressPercent}
              label="Progress"
              valueText={`${p.doneTasks} of ${p.visibleTasks} shared tasks done`}
              showValue
            />
            <span className="dl-meta">
              {labelOf(p.type)} · {formatDateOnly(p.startDate)} – {formatDateOnly(p.endDate)}
            </span>
            {p.nextMilestone ? (
              <span>
                Next milestone: <strong>{p.nextMilestone.title}</strong> (
                {formatDateOnly(p.nextMilestone.dueDate)})
              </span>
            ) : null}
          </CardBody>
        </Card>
      ))}
    </div>
  );
}

export function ClientProjectPage() {
  const { projectId = '' } = useParams();
  return (
    <ClientShell
      title="Project"
      breadcrumbs={[{ label: 'Projects', to: '/client/projects' }, { label: 'Project' }]}
    >
      {({ base, org }) => (
        <ProjectDetail key={org.clientId + projectId} base={base} orgId={org.clientId} id={projectId} />
      )}
    </ClientShell>
  );
}

function ProjectDetail({ base, orgId, id }: { base: string; orgId: string; id: string }) {
  const detail = useQuery({
    queryKey: clientKeys.part(orgId, 'project', id),
    queryFn: ({ signal }) => api.get<ClientProjectDetail>(`${base}/projects/${id}`, { signal }),
  });
  if (detail.isPending) return <Skeleton height={200} />;
  if (detail.isError) return <ErrorState error={detail.error} />;
  const d = detail.data;
  return (
    <div className="dl-page">
      <h2>{d.project.name}</h2>
      {d.description ? <p>{d.description}</p> : null}
      <ProgressBar
        value={d.project.progressPercent}
        label="Overall progress"
        valueText={`${d.project.progressPercent}%`}
        showValue
      />
      <Card as="section" aria-label="Milestones">
        <CardHeader title="Milestones" headingLevel={3} />
        <CardBody>
          {d.milestones.length === 0 ? (
            <p className="dl-muted">No milestones shared yet.</p>
          ) : (
            <ul className="dl-list" aria-label="Milestones">
              {d.milestones.map((m) => (
                <li key={m.id} className="dl-list__item">
                  <span className="dl-list__main">
                    <span className="dl-list__title">{m.title}</span>
                    <span className="dl-meta">
                      Due {formatDateOnly(m.dueDate)} · {m.doneCount}/{m.taskCount} done
                    </span>
                  </span>
                  <Badge tone={m.status === 'Done' ? 'success' : 'neutral'}>
                    {m.status === 'Done' ? 'Done' : 'In progress'}
                  </Badge>
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>
      <DataTable
        caption="Tasks"
        rows={d.tasks}
        getRowId={(t) => t.id}
        columns={[
          { id: 'title', header: 'Task', primary: true, cell: (t) => t.title },
          { id: 'status', header: 'Status', cell: (t) => <TaskStatusBadge status={t.status} /> },
          { id: 'due', header: 'Due', cell: (t) => formatDateOnly(t.dueDate) },
          {
            id: 'milestone',
            header: 'Milestone',
            cell: (t) => d.milestones.find((m) => m.id === t.milestoneId)?.title ?? '—',
            hideOnMobile: true,
          },
        ]}
        emptyState={<EmptyState compact title="No tasks shared yet" />}
      />
    </div>
  );
}

// ---------------------------------------------------------------- briefs

function BriefForm({ base, orgId, onClose }: { base: string; orgId: string; onClose: () => void }) {
  const qc = useQueryClient();
  const templates = useQuery({
    queryKey: clientKeys.part(orgId, 'brief-templates'),
    queryFn: ({ signal }) => api.get<BriefTemplate[]>(`${base}/brief-templates`, { signal }),
  });
  const [key, setKey] = useState('');
  const [title, setTitle] = useState('');
  const [deadline, setDeadline] = useState('');
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const template = templates.data?.find((t) => t.key === key);
  const submit = useMutation({
    mutationFn: () =>
      api.post<Brief>(`${base}/briefs`, { templateKey: key, title, deadline: deadline || null, answers }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: clientKeys.part(orgId, 'briefs') });
      onClose();
    },
  });
  const fieldErrors = isApiError(submit.error) ? submit.error.errors : undefined;
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title="Submit a brief"
      description="Tell your team what you need. They'll turn it into tasks and deliverables."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="brief-form"
            loading={submit.isPending}
            disabled={!template || title.trim().length < 2}
          >
            Submit brief
          </Button>
        </>
      }
    >
      <form
        id="brief-form"
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          submit.mutate();
        }}
      >
        {submit.error ? <Alert tone="danger">{errorMessage(submit.error)}</Alert> : null}
        <FormField label="Type of work" required>
          <Select
            value={key}
            onChange={(e) => setKey(e.target.value)}
            placeholder="Choose…"
            options={(templates.data ?? []).map((t) => ({ value: t.key, label: t.name }))}
          />
        </FormField>
        <FormField label="Title" required>
          <Input
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            required
            minLength={2}
            maxLength={200}
          />
        </FormField>
        {template?.fields.map((f) => {
          const error = fieldErrors?.[`answers.${f.key}`] ?? fieldErrors?.[`answers.${f.key.toLowerCase()}`];
          const value = answers[f.key] ?? '';
          const set = (v: string) => setAnswers({ ...answers, [f.key]: v });
          return (
            <FormField
              key={f.key}
              label={f.label}
              required={f.required}
              optional={!f.required}
              hint={f.help ?? (f.type === 'List' || f.type === 'Url' ? 'One per line.' : undefined)}
              error={error}
            >
              {f.type === 'LongText' || f.type === 'List' || f.type === 'Url' ? (
                <Textarea rows={3} value={value} onChange={(e) => set(e.target.value)} />
              ) : f.type === 'Select' ? (
                <Select
                  value={value}
                  onChange={(e) => set(e.target.value)}
                  placeholder="Choose…"
                  options={f.options.map((o) => ({ value: o, label: o }))}
                />
              ) : f.type === 'Date' ? (
                <Input type="date" value={value} onChange={(e) => set(e.target.value)} />
              ) : (
                <Input value={value} onChange={(e) => set(e.target.value)} />
              )}
            </FormField>
          );
        })}
        {template ? (
          <FormField label="Needed by" optional>
            <Input type="date" value={deadline} onChange={(e) => setDeadline(e.target.value)} />
          </FormField>
        ) : null}
      </form>
    </Dialog>
  );
}

function Briefs({ base, orgId, canSubmit }: { base: string; orgId: string; canSubmit: boolean }) {
  const [open, setOpen] = useState(false);
  const briefs = useQuery({
    queryKey: clientKeys.part(orgId, 'briefs'),
    queryFn: ({ signal }) => api.get<Brief[]>(`${base}/briefs`, { signal }),
  });
  return (
    <div className="dl-page">
      {canSubmit ? (
        <div>
          <Button leadingIcon={<Plus aria-hidden="true" />} onClick={() => setOpen(true)}>
            Submit a brief
          </Button>
        </div>
      ) : (
        <Alert tone="info">Approvers and Owners can submit briefs.</Alert>
      )}
      {briefs.isPending ? (
        <Skeleton height={160} />
      ) : briefs.isError ? (
        <ErrorState error={briefs.error} />
      ) : briefs.data.length === 0 ? (
        <EmptyState title="No briefs yet" />
      ) : (
        briefs.data.map((b) => (
          <Card key={b.id} as="article" aria-label={b.title}>
            <CardHeader
              title={b.title}
              headingLevel={2}
              description={`${b.templateName} · submitted by ${b.submittedBy.displayName}`}
              actions={
                <Badge tone={b.status === 'Converted' ? 'success' : 'info'}>
                  {b.status === 'Converted' ? 'In progress' : labelOf(b.status)}
                </Badge>
              }
            />
            <CardBody>
              <KeyValueList
                items={b.answers.map((a) => ({
                  label: a.label,
                  value: <span className="dl-report__body">{a.value}</span>,
                }))}
              />
            </CardBody>
          </Card>
        ))
      )}
      {open ? <BriefForm base={base} orgId={orgId} onClose={() => setOpen(false)} /> : null}
    </div>
  );
}

export function ClientBriefsPage() {
  return (
    <ClientShell title="Briefs" description="Request new work from your agency team.">
      {({ base, org, canApprove }) => (
        <Briefs key={org.clientId} base={base} orgId={org.clientId} canSubmit={canApprove} />
      )}
    </ClientShell>
  );
}

// ---------------------------------------------------------------- reports

export function ClientReportsPage() {
  return (
    <ClientShell title="Reports" description="Monthly performance reports from your agency team.">
      {({ base, org, link }) => <Reports key={org.clientId} base={base} orgId={org.clientId} link={link} />}
    </ClientShell>
  );
}

function Reports({ base, orgId, link }: { base: string; orgId: string; link: (p: string) => string }) {
  const reports = useQuery({
    queryKey: clientKeys.part(orgId, 'reports'),
    queryFn: ({ signal }) => api.get<ReportSummary[]>(`${base}/reports`, { signal }),
  });
  if (reports.isPending) return <Skeleton height={160} />;
  if (reports.isError) return <ErrorState error={reports.error} />;
  if (reports.data.length === 0)
    return (
      <EmptyState
        title="No reports yet"
        description="Your first monthly report appears here once it's published."
      />
    );
  return (
    <ul className="dl-list" aria-label="Reports">
      {reports.data.map((r) => (
        <li key={r.id} className="dl-list__item">
          <span className="dl-list__main">
            <Link className="dl-list__title ui-link" to={link(`/client/reports/${r.id}`)}>
              {r.title}
            </Link>
            <span className="dl-meta">
              {formatDateOnly(r.periodStart, { month: 'long', year: 'numeric' })}
            </span>
          </span>
          {r.publishedAt ? <DateTime value={r.publishedAt} format="date" /> : null}
        </li>
      ))}
    </ul>
  );
}

export function ClientReportPage() {
  const { reportId = '' } = useParams();
  return (
    <ClientShell
      title="Report"
      breadcrumbs={[{ label: 'Reports', to: '/client/reports' }, { label: 'Report' }]}
      actions={
        <Button
          variant="secondary"
          className="dl-no-print"
          leadingIcon={<Printer aria-hidden="true" />}
          onClick={() => window.print()}
        >
          Print
        </Button>
      }
    >
      {({ base, org }) => (
        <ReportDetail key={org.clientId + reportId} base={base} orgId={org.clientId} id={reportId} />
      )}
    </ClientShell>
  );
}

function ReportDetail({ base, orgId, id }: { base: string; orgId: string; id: string }) {
  const report = useQuery({
    queryKey: clientKeys.part(orgId, 'report', id),
    queryFn: ({ signal }) => api.get<Report>(`${base}/reports/${id}`, { signal }),
  });
  if (report.isPending) return <Skeleton height={300} />;
  if (report.isError) return <ErrorState error={report.error} />;
  return (
    <>
      <h2>{report.data.title}</h2>
      <ReportView report={report.data} audience="client" />
    </>
  );
}

// ---------------------------------------------------------------- brand kit

function Brand({ base, orgId, isOwner }: { base: string; orgId: string; isOwner: boolean }) {
  const qc = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [label, setLabel] = useState('');
  const kit = useQuery({
    queryKey: clientKeys.part(orgId, 'brand'),
    queryFn: ({ signal }) => api.get<BrandKit>(`${base}/brand-kit`, { signal }),
  });
  const upload = useMutation({
    mutationFn: () => {
      const form = new FormData();
      form.append('file', file!);
      form.append('kind', file!.type.startsWith('image/') ? 'Logo' : 'Guideline');
      if (label) form.append('label', label);
      return api.upload<BrandKit>(`${base}/brand-kit/assets`, form);
    },
    onSuccess: (d) => {
      qc.setQueryData(clientKeys.part(orgId, 'brand'), d);
      setFile(null);
      setLabel('');
    },
  });
  if (kit.isPending) return <Skeleton height={300} />;
  if (kit.isError) return <ErrorState error={kit.error} />;
  return (
    <div className="dl-page">
      <BrandKitView kit={kit.data} audience="client" />
      {isOwner ? (
        <Card as="section" aria-label="Upload brand assets">
          <CardHeader
            title="Upload brand assets"
            headingLevel={2}
            description="Logos, brand guidelines (PDF), fonts previews and photography."
          />
          <CardBody>
            <form
              className="dl-toolbar"
              onSubmit={(e) => {
                e.preventDefault();
                if (file) upload.mutate();
              }}
            >
              <FormField label="File" hint="PNG, JPEG, WebP, PDF or MP4, up to 50 MB.">
                <Input
                  type="file"
                  accept="image/png,image/jpeg,image/webp,application/pdf,video/mp4"
                  onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                />
              </FormField>
              <FormField label="Label" optional>
                <Input value={label} onChange={(e) => setLabel(e.target.value)} maxLength={200} />
              </FormField>
              <Button type="submit" disabled={!file} loading={upload.isPending}>
                Upload
              </Button>
            </form>
            {upload.error ? <Alert tone="danger">{errorMessage(upload.error)}</Alert> : null}
          </CardBody>
        </Card>
      ) : null}
    </div>
  );
}

export function ClientBrandKitPage() {
  return (
    <ClientShell
      title="Brand kit"
      description="Colours, fonts, voice and assets your agency team works with."
    >
      {({ base, org, isOwner }) => (
        <Brand key={org.clientId} base={base} orgId={org.clientId} isOwner={isOwner} />
      )}
    </ClientShell>
  );
}

// ---------------------------------------------------------------- messages

export function ClientMessagesPage() {
  return (
    <ClientShell title="Messages" description="Talk to your account team. Attach files up to 50 MB.">
      {({ base, org, canApprove }) => (
        <>
          {canApprove ? null : (
            <Alert tone="info">
              Your role is read-only here: an Approver or Owner in your organization can send messages.
            </Alert>
          )}
          <MessagesPanel key={org.clientId} base={base} audience="client" canWrite={canApprove} />
        </>
      )}
    </ClientShell>
  );
}

// ---------------------------------------------------------------- team & members

function Team({ base, orgId, isOwner }: { base: string; orgId: string; isOwner: boolean }) {
  const qc = useQueryClient();
  const team = useQuery({
    queryKey: clientKeys.part(orgId, 'team'),
    queryFn: ({ signal }) => api.get<AccountTeamMember[]>(`${base}/team`, { signal }),
  });
  const members = useQuery({
    queryKey: clientKeys.part(orgId, 'members'),
    queryFn: ({ signal }) => api.get<ClientMember[]>(`${base}/members`, { signal }),
  });
  const [inviting, setInviting] = useState(false);
  const [form, setForm] = useState({ email: '', displayName: '', role: 'Viewer' as ClientDuty });
  const setMembers = (d: ClientMember[]) => qc.setQueryData(clientKeys.part(orgId, 'members'), d);
  const invite = useMutation({
    mutationFn: () => api.post(`${base}/members`, form),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: clientKeys.part(orgId, 'members') });
      setInviting(false);
      setForm({ email: '', displayName: '', role: 'Viewer' });
    },
  });
  const change = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: ClientDuty }) =>
      api.put<ClientMember[]>(`${base}/members/${userId}`, { role }),
    onSuccess: setMembers,
  });
  const remove = useMutation({
    mutationFn: (userId: string) => api.delete<ClientMember[]>(`${base}/members/${userId}`),
    onSuccess: setMembers,
  });
  return (
    <div className="dl-page">
      <Card as="section" aria-label="Your account team">
        <CardHeader title="Your account team" headingLevel={2} />
        <CardBody>
          {team.isPending ? (
            <Skeleton height={120} />
          ) : team.isError ? (
            <ErrorState error={team.error} />
          ) : (
            <ul className="cc-team" aria-label="Account team">
              {team.data.map((p) => (
                <li key={p.userId} className="cc-person">
                  <Avatar name={p.displayName} size={48} decorative />
                  <span className="cc-person__text">
                    <strong>{p.displayName}</strong>
                    <span className="dl-meta">
                      {p.isAccountManager ? 'Account manager' : p.roles.map(labelOf).join(', ')}
                    </span>
                    <a className="ui-link" href={`mailto:${p.email}`}>
                      <Mail aria-hidden="true" size={14} /> {p.email}
                    </a>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>
      <Card as="section" aria-label="People in your organization">
        <CardHeader
          title="People in your organization"
          headingLevel={2}
          actions={
            isOwner ? (
              <Button size="sm" leadingIcon={<Plus aria-hidden="true" />} onClick={() => setInviting(true)}>
                Invite
              </Button>
            ) : null
          }
        />
        <CardBody>
          {change.error || remove.error ? (
            <Alert tone="danger">{errorMessage(change.error ?? remove.error)}</Alert>
          ) : null}
          {members.isPending ? (
            <Skeleton height={120} />
          ) : members.isError ? (
            <ErrorState error={members.error} />
          ) : (
            <DataTable
              caption="Organization members"
              rows={members.data}
              getRowId={(m) => m.userId}
              columns={[
                { id: 'name', header: 'Name', primary: true, cell: (m) => m.displayName },
                { id: 'email', header: 'Email', cell: (m) => m.email },
                {
                  id: 'role',
                  header: 'Role',
                  cell: (m) =>
                    isOwner ? (
                      <Select
                        size="sm"
                        aria-label={`Role of ${m.displayName}`}
                        value={m.role}
                        onChange={(e) =>
                          change.mutate({ userId: m.userId, role: e.target.value as ClientDuty })
                        }
                        options={CLIENT_DUTIES.map((d) => ({ value: d, label: d }))}
                      />
                    ) : (
                      m.role
                    ),
                },
              ]}
              rowActions={
                isOwner
                  ? (m) => [
                      {
                        id: 'remove',
                        label: 'Remove',
                        danger: true,
                        onSelect: () => remove.mutate(m.userId),
                      },
                    ]
                  : undefined
              }
            />
          )}
        </CardBody>
      </Card>
      <Dialog
        open={inviting}
        onClose={() => setInviting(false)}
        title="Invite a colleague"
        description="They'll get an email to set their password."
        footer={
          <>
            <Button variant="secondary" onClick={() => setInviting(false)}>
              Cancel
            </Button>
            <Button type="submit" form="client-invite" loading={invite.isPending}>
              Send invitation
            </Button>
          </>
        }
      >
        <form
          id="client-invite"
          className="dl-form"
          onSubmit={(e) => {
            e.preventDefault();
            invite.mutate();
          }}
        >
          {invite.error ? <Alert tone="danger">{errorMessage(invite.error)}</Alert> : null}
          <FormField label="Email" required>
            <Input
              type="email"
              value={form.email}
              onChange={(e) => setForm({ ...form, email: e.target.value })}
              required
            />
          </FormField>
          <FormField label="Name" required>
            <Input
              value={form.displayName}
              onChange={(e) => setForm({ ...form, displayName: e.target.value })}
              required
              minLength={2}
            />
          </FormField>
          <FormField
            label="Role"
            hint="Viewer: read-only. Approver: approves work and submits briefs. Billing: invoices. Owner: everything."
          >
            <Select
              value={form.role}
              onChange={(e) => setForm({ ...form, role: e.target.value as ClientDuty })}
              options={CLIENT_DUTIES.map((d) => ({ value: d, label: d }))}
            />
          </FormField>
        </form>
      </Dialog>
    </div>
  );
}

export function ClientTeamPage() {
  return (
    <ClientShell title="Team" description="Your agency team and the people in your organization.">
      {({ base, org, isOwner }) => (
        <Team key={org.clientId} base={base} orgId={org.clientId} isOwner={isOwner} />
      )}
    </ClientShell>
  );
}
