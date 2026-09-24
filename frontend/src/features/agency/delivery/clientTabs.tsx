import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Checkbox,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  IconButton,
  Input,
  KeyValueList,
  ProgressBar,
  Select,
  Skeleton,
  Stat,
  Textarea,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { BrandKitView } from '../shared/BrandKitView';
import {
  CLIENT_DUTIES,
  SERVICE_ROLES,
  type BrandAsset,
  type BrandKit,
  type Brief,
  type ClientDetail,
  type ClientDuty,
  type ClientHealth,
  type ClientMember,
  type FeedbackSummary,
  type Meeting,
  type Onboarding,
  type OnboardingStatus,
  type ProjectSummary,
  type ReportSummary,
  type TeamAssignment,
  type TimeEntry,
} from '../shared/deliveryTypes';
import {
  formatDateOnly,
  formatMinutes,
  HealthBadge,
  labelOf,
  ProjectStatusBadge,
} from '../shared/deliveryUi';
import { dk, useProjectOptions, useStaff } from './api';

function useClientPart<T>(clientId: string, part: string, path: string, enabled = true) {
  return useQuery({
    queryKey: dk.clientPart(clientId, part),
    queryFn: ({ signal }) => api.get<T>(path, { signal }),
    enabled,
  });
}

function Loading() {
  return <Skeleton height={160} />;
}

// ---------------------------------------------------------------- overview

export function OverviewTab({ client }: { client: ClientDetail }) {
  const health = useClientPart<ClientHealth>(client.id, 'health', `/agency/clients/${client.id}/health`);
  const onboarding = useClientPart<Onboarding>(
    client.id,
    'onboarding',
    `/agency/clients/${client.id}/onboarding`,
  );
  return (
    <div className="dl-grid dl-grid--wide">
      <Card as="section" aria-label="Health">
        <CardHeader
          title="Health"
          headingLevel={2}
          actions={health.data ? <HealthBadge level={health.data.level} score={health.data.score} /> : null}
        />
        <CardBody>
          {health.isPending ? (
            <Loading />
          ) : health.isError ? (
            <ErrorState error={health.error} compact />
          ) : health.data.reasons.length === 0 ? (
            <p>No issues: tasks on time, approvals moving and recent activity.</p>
          ) : (
            <ul className="dl-list" aria-label="Health reasons">
              {health.data.reasons.map((r) => (
                <li key={r.code} className="dl-list__item">
                  <span>{r.message}</span>
                  <HealthBadge level={r.level} />
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>
      <Card as="section" aria-label="Onboarding progress">
        <CardHeader title="Onboarding" headingLevel={2} />
        <CardBody>
          {onboarding.data ? (
            <ProgressBar
              value={onboarding.data.percentComplete}
              label="Onboarding complete"
              valueText={`${onboarding.data.done} of ${onboarding.data.total} steps`}
              showValue
            />
          ) : (
            <Loading />
          )}
        </CardBody>
      </Card>
      <Card as="section" aria-label="Profile">
        <CardHeader title="Profile" headingLevel={2} />
        <CardBody>
          <KeyValueList
            items={[
              { label: 'Industry', value: client.industry ?? '—' },
              { label: 'Website', value: client.website ?? '—' },
              { label: 'Country / time zone', value: `${client.countryCode} · ${client.timeZone}` },
              { label: 'Currency', value: client.currency },
              { label: 'Account manager', value: client.accountManager?.displayName ?? '—' },
              {
                label: 'Billing contact',
                value: [client.billingContactName, client.billingEmail].filter(Boolean).join(' · ') || '—',
              },
              { label: 'Client feedback SLA', value: `${client.approvalSlaDays} business day(s)` },
              {
                label: 'Auto-approve',
                value: client.autoApproveAfterDays ? `After ${client.autoApproveAfterDays} days` : 'Off',
              },
              {
                label: 'Last invoice paid',
                value: client.lastInvoicePaidAt ? (
                  <DateTime value={client.lastInvoicePaidAt} format="date" />
                ) : (
                  '—'
                ),
              },
            ]}
          />
          {client.statusReason ? (
            <Alert tone="warning" title="Status reason">
              {client.statusReason}
            </Alert>
          ) : null}
        </CardBody>
      </Card>
    </div>
  );
}

// ---------------------------------------------------------------- team

export function TeamTab({ clientId }: { clientId: string }) {
  const qc = useQueryClient();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.ClientsManage);
  const team = useClientPart<TeamAssignment[]>(clientId, 'team', `/agency/clients/${clientId}/team`);
  const staff = useStaff();
  const [userId, setUserId] = useState('');
  const [role, setRole] = useState('Strategist');
  const onSaved = (data: TeamAssignment[]) => qc.setQueryData(dk.clientPart(clientId, 'team'), data);
  const add = useMutation({
    mutationFn: () =>
      api.post<TeamAssignment[]>(`/agency/clients/${clientId}/team`, {
        userId,
        serviceRole: role,
        isPrimary: false,
      }),
    onSuccess: onSaved,
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.delete<TeamAssignment[]>(`/agency/clients/${clientId}/team/${id}`),
    onSuccess: onSaved,
  });
  if (team.isPending) return <Loading />;
  if (team.isError) return <ErrorState error={team.error} />;
  return (
    <div className="dl-page">
      <ul className="dl-list" aria-label="Account team">
        {team.data.map((a) => (
          <li key={a.id} className="dl-list__item">
            <span className="dl-list__main">
              <span className="dl-list__title">{a.user.displayName}</span>
              <span className="dl-meta">{a.user.email}</span>
            </span>
            <span className="dl-row">
              <Badge tone="brand">{labelOf(a.serviceRole)}</Badge>
              {a.isPrimary ? <Badge>Primary</Badge> : null}
              {canManage ? (
                <IconButton
                  label={`Remove ${a.user.displayName} as ${labelOf(a.serviceRole)}`}
                  icon={<Trash2 />}
                  variant="ghost"
                  onClick={() => remove.mutate(a.id)}
                />
              ) : null}
            </span>
          </li>
        ))}
      </ul>
      {canManage ? (
        <form
          className="dl-toolbar"
          aria-label="Add to account team"
          onSubmit={(e) => {
            e.preventDefault();
            add.mutate();
          }}
        >
          <FormField label="Staff member">
            <Select
              value={userId}
              onChange={(e) => setUserId(e.target.value)}
              placeholder="Choose…"
              options={(staff.data ?? []).map((s) => ({ value: s.id, label: s.displayName }))}
            />
          </FormField>
          <FormField label="Service role">
            <Select
              value={role}
              onChange={(e) => setRole(e.target.value)}
              options={SERVICE_ROLES.map((r) => ({ value: r, label: labelOf(r) }))}
            />
          </FormField>
          <Button
            type="submit"
            leadingIcon={<Plus aria-hidden="true" />}
            disabled={!userId}
            loading={add.isPending}
          >
            Add
          </Button>
        </form>
      ) : null}
      {add.error ? <Alert tone="danger">{errorMessage(add.error)}</Alert> : null}
    </div>
  );
}

// ---------------------------------------------------------------- client users

export function UsersTab({ clientId }: { clientId: string }) {
  const qc = useQueryClient();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.ClientsManage);
  const members = useClientPart<ClientMember[]>(clientId, 'members', `/agency/clients/${clientId}/members`);
  const [inviting, setInviting] = useState(false);
  const [form, setForm] = useState({ email: '', displayName: '', role: 'Approver' as ClientDuty });
  const invalidate = () => void qc.invalidateQueries({ queryKey: dk.clientPart(clientId, 'members') });
  const invite = useMutation({
    mutationFn: () => api.post(`/agency/clients/${clientId}/members`, form),
    onSuccess: () => {
      invalidate();
      setInviting(false);
      setForm({ email: '', displayName: '', role: 'Approver' });
    },
  });
  const change = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: ClientDuty }) =>
      api.put<ClientMember[]>(`/agency/clients/${clientId}/members/${userId}`, { role }),
    onSuccess: (data) => qc.setQueryData(dk.clientPart(clientId, 'members'), data),
  });
  const remove = useMutation({
    mutationFn: (userId: string) =>
      api.delete<ClientMember[]>(`/agency/clients/${clientId}/members/${userId}`),
    onSuccess: (data) => qc.setQueryData(dk.clientPart(clientId, 'members'), data),
  });
  const columns: DataTableColumn<ClientMember>[] = [
    { id: 'name', header: 'Name', primary: true, cell: (m) => m.displayName },
    { id: 'email', header: 'Email', cell: (m) => m.email },
    {
      id: 'role',
      header: 'Duty',
      cell: (m) =>
        canManage ? (
          <Select
            aria-label={`Duty of ${m.displayName}`}
            size="sm"
            value={m.role}
            onChange={(e) => change.mutate({ userId: m.userId, role: e.target.value as ClientDuty })}
            options={CLIENT_DUTIES.map((d) => ({ value: d, label: d }))}
          />
        ) : (
          m.role
        ),
    },
    {
      id: 'signed',
      header: 'Signed in',
      cell: (m) =>
        m.hasSignedIn ? (
          <DateTime value={m.lastLoginAt} format="relative" />
        ) : (
          <Badge tone="warning">Invitation pending</Badge>
        ),
    },
  ];
  if (members.isPending) return <Loading />;
  if (members.isError) return <ErrorState error={members.error} />;
  return (
    <div className="dl-page">
      <DataTable
        caption="Client users"
        columns={columns}
        rows={members.data}
        getRowId={(m) => m.userId}
        rowActions={
          canManage
            ? (m) => [
                {
                  id: 'remove',
                  label: 'Remove from organization',
                  danger: true,
                  onSelect: () => remove.mutate(m.userId),
                },
              ]
            : undefined
        }
        emptyState={<EmptyState compact title="No client users yet" />}
      />
      {change.error || remove.error ? (
        <Alert tone="danger">{errorMessage(change.error ?? remove.error)}</Alert>
      ) : null}
      {canManage ? (
        <div>
          <Button leadingIcon={<Plus aria-hidden="true" />} onClick={() => setInviting(true)}>
            Invite client user
          </Button>
        </div>
      ) : null}
      <Dialog
        open={inviting}
        onClose={() => setInviting(false)}
        title="Invite a client user"
        description="New people get an email to set their password. Existing client users are simply added to this organization."
        footer={
          <>
            <Button variant="secondary" onClick={() => setInviting(false)}>
              Cancel
            </Button>
            <Button type="submit" form="invite-client-user" loading={invite.isPending}>
              Send invitation
            </Button>
          </>
        }
      >
        <form
          id="invite-client-user"
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
            label="Duty"
            hint="Viewer: read-only. Approver: approves deliverables and submits briefs. Billing: invoices. Owner: everything, including users."
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

// ---------------------------------------------------------------- onboarding

type OnboardingItemRow = Onboarding['items'][number];

function OnboardingItemDialog({
  clientId,
  item,
  onClose,
  onSaved,
}: {
  clientId: string;
  item: OnboardingItemRow;
  onClose: () => void;
  onSaved: (d: Onboarding) => void;
}) {
  const [form, setForm] = useState({
    title: item.title,
    description: item.description ?? '',
    category: item.category,
    owner: item.owner,
  });
  const save = useMutation({
    mutationFn: () =>
      api.put<Onboarding>(`/agency/clients/${clientId}/onboarding/${item.id}/details`, {
        ...form,
        description: form.description || null,
      }),
    onSuccess: (d) => {
      onSaved(d);
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Edit onboarding step"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="onboarding-item-form"
            loading={save.isPending}
            disabled={form.title.trim().length < 2}
          >
            Save
          </Button>
        </>
      }
    >
      <form
        id="onboarding-item-form"
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
        <FormField label="Title" required>
          <Input
            value={form.title}
            maxLength={200}
            onChange={(e) => setForm({ ...form, title: e.target.value })}
          />
        </FormField>
        <FormField label="Description" optional>
          <Textarea
            rows={3}
            value={form.description}
            maxLength={2000}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
          />
        </FormField>
        <div className="dl-form__row">
          <FormField label="Category">
            <Input
              value={form.category}
              maxLength={64}
              onChange={(e) => setForm({ ...form, category: e.target.value })}
            />
          </FormField>
          <FormField label="Who completes it">
            <Select
              value={form.owner}
              options={[
                { value: 'Agency', label: 'Agency' },
                { value: 'Client', label: 'Client (the client can tick it off)' },
              ]}
              onChange={(e) => setForm({ ...form, owner: e.target.value as OnboardingItemRow['owner'] })}
            />
          </FormField>
        </div>
      </form>
    </Dialog>
  );
}

export function OnboardingTab({ clientId }: { clientId: string }) {
  const qc = useQueryClient();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(Permissions.ClientsManage);
  const data = useClientPart<Onboarding>(clientId, 'onboarding', `/agency/clients/${clientId}/onboarding`);
  const [title, setTitle] = useState('');
  const [owner, setOwner] = useState<'Agency' | 'Client'>('Agency');
  const [editing, setEditing] = useState<OnboardingItemRow | null>(null);
  const [removing, setRemoving] = useState<OnboardingItemRow | null>(null);
  // A client-owned step ticked (or un-ticked) by staff is done on the client's behalf: confirmed, audited, shown to the client.
  const [onBehalf, setOnBehalf] = useState<{ item: OnboardingItemRow; done: boolean } | null>(null);
  const save = (d: Onboarding) => qc.setQueryData(dk.clientPart(clientId, 'onboarding'), d);
  const changeStatus = (item: OnboardingItemRow, status: OnboardingStatus) => {
    if (item.owner === 'Client' && (status === 'Done' || (status === 'Pending' && item.status === 'Done')))
      setOnBehalf({ item, done: status === 'Done' });
    else update.mutate({ id: item.id, status });
  };
  const update = useMutation({
    mutationFn: ({ id, status }: { id: string; status: OnboardingStatus }) =>
      api.put<Onboarding>(`/agency/clients/${clientId}/onboarding/${id}`, { status }),
    onSuccess: save,
  });
  const add = useMutation({
    mutationFn: () => api.post<Onboarding>(`/agency/clients/${clientId}/onboarding`, { title, owner }),
    onSuccess: (d) => {
      save(d);
      setTitle('');
    },
  });
  if (data.isPending) return <Loading />;
  if (data.isError) return <ErrorState error={data.error} />;
  return (
    <div className="dl-page">
      <ProgressBar
        value={data.data.percentComplete}
        label="Onboarding complete"
        valueText={`${data.data.done} of ${data.data.total} steps`}
        showValue
      />
      <ul className="dl-list" aria-label="Onboarding checklist">
        {data.data.items.map((i) => (
          <li key={i.id} className="dl-list__item">
            <span className="dl-list__main">
              <span className="dl-list__title">{i.title}</span>
              <span className="dl-meta">
                <span>{i.category}</span>
                <span>{i.owner === 'Client' ? 'Client action' : 'Agency action'}</span>
                {i.completedAt ? (
                  <span>
                    Done <DateTime value={i.completedAt} format="date" />
                    {i.completedBy ? ` by ${i.completedBy}` : ''}
                    {i.completedOnBehalfOfClient ? ' on behalf of the client' : ''}
                  </span>
                ) : null}
              </span>
              {i.description ? <span className="dl-muted">{i.description}</span> : null}
            </span>
            {canManage ? (
              <span className="dl-row">
                <Select
                  size="sm"
                  aria-label={`Status of ${i.title}`}
                  value={i.status}
                  onChange={(e) => changeStatus(i, e.target.value as OnboardingStatus)}
                  options={[
                    { value: 'Pending', label: 'Pending' },
                    { value: 'Done', label: 'Done' },
                    { value: 'NotApplicable', label: 'Not applicable' },
                  ]}
                />
                <IconButton
                  size="sm"
                  variant="ghost"
                  label={`Edit ${i.title}`}
                  icon={<Pencil />}
                  onClick={() => setEditing(i)}
                />
                <IconButton
                  size="sm"
                  variant="ghost"
                  label={`Remove ${i.title}`}
                  icon={<Trash2 />}
                  onClick={() => setRemoving(i)}
                />
              </span>
            ) : (
              <Badge tone={i.status === 'Done' ? 'success' : 'neutral'}>{labelOf(i.status)}</Badge>
            )}
          </li>
        ))}
      </ul>
      {canManage ? (
        <form
          className="dl-toolbar"
          onSubmit={(e) => {
            e.preventDefault();
            add.mutate();
          }}
        >
          <FormField label="Add a step">
            <Input value={title} onChange={(e) => setTitle(e.target.value)} minLength={2} maxLength={200} />
          </FormField>
          <FormField label="Who completes it">
            <Select
              value={owner}
              options={[
                { value: 'Agency', label: 'Agency' },
                { value: 'Client', label: 'Client' },
              ]}
              onChange={(e) => setOwner(e.target.value as 'Agency' | 'Client')}
            />
          </FormField>
          <Button type="submit" disabled={title.trim().length < 2} loading={add.isPending}>
            Add step
          </Button>
        </form>
      ) : null}
      {update.error || add.error ? (
        <Alert tone="danger">{errorMessage(update.error ?? add.error)}</Alert>
      ) : null}
      {editing ? (
        <OnboardingItemDialog
          clientId={clientId}
          item={editing}
          onClose={() => setEditing(null)}
          onSaved={save}
        />
      ) : null}
      <ConfirmDialog
        open={onBehalf !== null}
        onClose={() => setOnBehalf(null)}
        title={
          onBehalf?.done
            ? `Mark “${onBehalf.item.title}” done on the client’s behalf?`
            : `Mark “${onBehalf?.item.title ?? ''}” not done again?`
        }
        description={
          onBehalf?.done
            ? 'This is the client’s step. The client will see that you completed it for them, with your name and the date. The change is audited.'
            : 'The step goes back to the client’s to-do list. The change is audited.'
        }
        confirmLabel={onBehalf?.done ? 'Mark done for the client' : 'Mark not done'}
        onConfirm={async () => {
          if (!onBehalf) return;
          save(
            await api.post<Onboarding>(
              `/agency/clients/${clientId}/onboarding/${onBehalf.item.id}/on-behalf`,
              { done: onBehalf.done },
            ),
          );
        }}
      />
      <ConfirmDialog
        open={removing !== null}
        onClose={() => setRemoving(null)}
        tone="danger"
        title={`Remove “${removing?.title ?? ''}” from this client’s checklist?`}
        description="To keep it visible but excluded from progress, mark it Not applicable instead. The removal is audited."
        confirmLabel="Remove step"
        onConfirm={async () => {
          if (!removing) return;
          save(await api.delete<Onboarding>(`/agency/clients/${clientId}/onboarding/${removing.id}`));
        }}
      />
    </div>
  );
}

// ---------------------------------------------------------------- brand kit

const splitLines = (value: string) =>
  value
    .split('\n')
    .map((v) => v.trim())
    .filter(Boolean);

export function BrandKitTab({ clientId }: { clientId: string }) {
  const qc = useQueryClient();
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.DeliverablesSubmit);
  const canRemove = hasPermission(Permissions.ClientsManage);
  const kit = useClientPart<BrandKit>(clientId, 'brand', `/agency/clients/${clientId}/brand-kit`);
  const [removingAsset, setRemovingAsset] = useState<BrandAsset | null>(null);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [file, setFile] = useState<File | null>(null);
  const save = useMutation({
    mutationFn: () =>
      api.put<BrandKit>(`/agency/clients/${clientId}/brand-kit`, {
        colors: splitLines(draft.colors ?? '').map((l) => {
          const [name, hex] = l.split(/[:=]/).map((s) => s.trim());
          return { name, hex };
        }),
        fonts: splitLines(draft.fonts ?? ''),
        toneOfVoice: draft.tone ?? '',
        personas: splitLines(draft.personas ?? '').map((l) => {
          const [name, ...rest] = l.split(':');
          return { name: name?.trim() ?? '', description: rest.join(':').trim() };
        }),
        competitors: splitLines(draft.competitors ?? ''),
        dos: splitLines(draft.dos ?? ''),
        donts: splitLines(draft.donts ?? ''),
        keyMessages: splitLines(draft.messages ?? ''),
        concurrencyStamp: kit.data?.concurrencyStamp,
      }),
    onSuccess: (data) => {
      qc.setQueryData(dk.clientPart(clientId, 'brand'), data);
      setEditing(false);
    },
  });
  const upload = useMutation({
    mutationFn: () => {
      const form = new FormData();
      form.append('file', file!);
      form.append('kind', file!.type.startsWith('image/') ? 'Image' : 'Guideline');
      return api.upload<BrandKit>(`/agency/clients/${clientId}/brand-kit/assets`, form);
    },
    onSuccess: (data) => {
      qc.setQueryData(dk.clientPart(clientId, 'brand'), data);
      setFile(null);
    },
  });
  if (kit.isPending) return <Loading />;
  if (kit.isError) return <ErrorState error={kit.error} />;
  const k = kit.data;
  const startEdit = () => {
    setDraft({
      colors: k.colors.map((c) => `${c.name}: ${c.hex}`).join('\n'),
      fonts: k.fonts.join('\n'),
      tone: k.toneOfVoice ?? '',
      personas: k.personas.map((p) => `${p.name}: ${p.description}`).join('\n'),
      competitors: k.competitors.join('\n'),
      dos: k.dos.join('\n'),
      donts: k.donts.join('\n'),
      messages: k.keyMessages.join('\n'),
    });
    setEditing(true);
  };
  const field = (key: string, label: string, hint?: string) => (
    <FormField label={label} hint={hint}>
      <Textarea
        rows={4}
        value={draft[key] ?? ''}
        onChange={(e) => setDraft({ ...draft, [key]: e.target.value })}
      />
    </FormField>
  );
  return (
    <div className="dl-page">
      {canEdit && !editing ? (
        <div className="dl-row">
          <Button variant="secondary" onClick={startEdit}>
            Edit brand kit
          </Button>
        </div>
      ) : null}
      {editing ? (
        <form
          className="dl-form"
          aria-label="Edit brand kit"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
          <div className="dl-form__row">
            {field('colors', 'Colours', 'One per line: Name: #RRGGBB')}
            {field('fonts', 'Fonts', 'One per line')}
          </div>
          {field('tone', 'Tone of voice')}
          {field('personas', 'Audience personas', 'One per line: Name: description')}
          <div className="dl-form__row">
            {field('dos', 'Do', 'One per line')}
            {field('donts', "Don't", 'One per line')}
          </div>
          <div className="dl-form__row">
            {field('messages', 'Key messages', 'One per line')}
            {field('competitors', 'Competitors', 'One per line')}
          </div>
          <div className="dl-row">
            <Button type="submit" loading={save.isPending}>
              Save brand kit
            </Button>
            <Button variant="secondary" onClick={() => setEditing(false)}>
              Cancel
            </Button>
          </div>
        </form>
      ) : (
        <BrandKitView kit={k} audience="staff" onRemoveAsset={canRemove ? setRemovingAsset : undefined} />
      )}
      <ConfirmDialog
        open={removingAsset !== null}
        onClose={() => setRemovingAsset(null)}
        tone="danger"
        title={`Remove “${removingAsset?.label ?? ''}” from the brand kit?`}
        description="The client and your team will no longer see it. The file is deleted unless it is also used elsewhere (a message or a task). The removal is audited."
        confirmLabel="Remove asset"
        onConfirm={async () => {
          if (!removingAsset) return;
          try {
            qc.setQueryData(
              dk.clientPart(clientId, 'brand'),
              await api.delete<BrandKit>(`/agency/clients/${clientId}/brand-kit/assets/${removingAsset.id}`),
            );
          } catch (error) {
            // Someone else may have removed it already: show the current kit behind the error.
            void qc.invalidateQueries({ queryKey: dk.clientPart(clientId, 'brand') });
            throw error;
          }
        }}
      />
      {canEdit ? (
        <form
          className="dl-toolbar"
          aria-label="Upload brand asset"
          onSubmit={(e) => {
            e.preventDefault();
            if (file) upload.mutate();
          }}
        >
          <FormField label="Upload an asset" hint="PNG, JPEG, WebP, PDF or MP4 up to 50 MB.">
            <Input
              type="file"
              accept="image/png,image/jpeg,image/webp,application/pdf,video/mp4"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
          </FormField>
          <Button type="submit" disabled={!file} loading={upload.isPending}>
            Upload
          </Button>
          {upload.error ? <Alert tone="danger">{errorMessage(upload.error)}</Alert> : null}
        </form>
      ) : null}
    </div>
  );
}

// ---------------------------------------------------------------- projects / time / reports

export function ProjectsTab({ clientId }: { clientId: string }) {
  const projects = useProjectOptions(clientId);
  if (projects.isPending) return <Loading />;
  if (projects.isError) return <ErrorState error={projects.error} />;
  if (projects.data.length === 0)
    return (
      <EmptyState
        compact
        title="No projects yet"
        action={
          <Link className="ui-link" to={`/agency/projects?new=1&clientId=${clientId}`}>
            Create a project
          </Link>
        }
      />
    );
  return (
    <ul className="dl-list" aria-label="Projects">
      {projects.data.map((p: ProjectSummary) => (
        <li key={p.id} className="dl-list__item">
          <span className="dl-list__main">
            <Link className="dl-list__title ui-link" to={`/agency/projects/${p.id}`}>
              {p.name}
            </Link>
            <span className="dl-meta">
              {labelOf(p.type)} · {p.doneTasks}/{p.totalTasks} tasks done · {p.hoursLogged}h logged
            </span>
          </span>
          <span className="dl-row">
            <ProjectStatusBadge status={p.status} />
            {p.atRisk ? <Badge tone="danger">At risk</Badge> : null}
          </span>
        </li>
      ))}
    </ul>
  );
}

export function TimeTab({ clientId }: { clientId: string }) {
  const { hasPermission } = useAuth();
  const canSee = hasPermission(Permissions.TimeViewAll);
  const entries = useQuery({
    queryKey: dk.clientPart(clientId, 'time'),
    queryFn: ({ signal }) => {
      const to = new Date();
      const from = new Date(Date.now() - 30 * 86_400_000);
      const iso = (d: Date) => d.toISOString().slice(0, 10);
      return api.get<TimeEntry[]>('/agency/time/entries', {
        query: { clientId, from: iso(from), to: iso(to) },
        signal,
      });
    },
    enabled: canSee,
  });
  if (!canSee) return <Alert tone="info">Viewing everyone's time needs the time.view_all permission.</Alert>;
  if (entries.isPending) return <Loading />;
  if (entries.isError) return <ErrorState error={entries.error} />;
  const total = entries.data.reduce((s, e) => s + e.minutes, 0);
  const billable = entries.data.filter((e) => e.billable).reduce((s, e) => s + e.minutes, 0);
  return (
    <div className="dl-page">
      <div className="dl-stats">
        <Stat label="Last 30 days" value={formatMinutes(total)} />
        <Stat label="Billable" value={formatMinutes(billable)} />
      </div>
      <DataTable
        caption="Time entries, last 30 days"
        rows={entries.data}
        getRowId={(e) => e.id}
        columns={[
          { id: 'date', header: 'Date', cell: (e) => formatDateOnly(e.date) },
          { id: 'user', header: 'Person', primary: true, cell: (e) => e.userName },
          { id: 'project', header: 'Project', cell: (e) => e.projectName },
          { id: 'time', header: 'Time', align: 'right', cell: (e) => formatMinutes(e.minutes) },
          {
            id: 'billable',
            header: 'Billable',
            cell: (e) => (e.billable ? 'Yes' : 'No'),
            hideOnMobile: true,
          },
        ]}
        emptyState={<EmptyState compact title="No time logged" />}
      />
    </div>
  );
}

export function ReportsTab({ clientId }: { clientId: string }) {
  const { hasPermission } = useAuth();
  const can = hasPermission(Permissions.ReportsManage);
  const reports = useQuery({
    queryKey: dk.reports({ clientId }),
    queryFn: ({ signal }) =>
      api.get<PagedResult<ReportSummary>>('/agency/reports', { query: { clientId, pageSize: 50 }, signal }),
    enabled: can,
  });
  if (!can) return <Alert tone="info">Reports need the reports.manage permission.</Alert>;
  if (reports.isPending) return <Loading />;
  if (reports.isError) return <ErrorState error={reports.error} />;
  if (reports.data.items.length === 0)
    return (
      <EmptyState
        compact
        title="No reports yet"
        action={
          <Link className="ui-link" to="/agency/reports">
            Create a report
          </Link>
        }
      />
    );
  return (
    <ul className="dl-list" aria-label="Reports">
      {reports.data.items.map((r) => (
        <li key={r.id} className="dl-list__item">
          <Link className="dl-list__title ui-link" to={`/agency/reports/${r.id}`}>
            {r.title}
          </Link>
          <Badge tone={r.status === 'Published' ? 'success' : 'neutral'}>{r.status}</Badge>
        </li>
      ))}
    </ul>
  );
}

// ---------------------------------------------------------------- briefs

export function BriefsTab({ clientId }: { clientId: string }) {
  const qc = useQueryClient();
  const { hasPermission } = useAuth();
  const canConvert = hasPermission(Permissions.ProjectsManage);
  const briefs = useClientPart<Brief[]>(clientId, 'briefs', `/agency/briefs?clientId=${clientId}`);
  const projects = useProjectOptions(clientId, canConvert);
  const [converting, setConverting] = useState<Brief | null>(null);
  const [projectId, setProjectId] = useState('');
  const [taskTitles, setTaskTitles] = useState('');
  const setStatus = useMutation({
    mutationFn: ({ brief, status }: { brief: Brief; status: string }) =>
      api.post<Brief>(`/agency/briefs/${brief.id}/status`, {
        status,
        concurrencyStamp: brief.concurrencyStamp,
      }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: dk.clientPart(clientId, 'briefs') }),
  });
  const convert = useMutation({
    mutationFn: () =>
      api.post<Brief>(`/agency/briefs/${converting!.id}/convert`, {
        projectId,
        tasks: splitLines(taskTitles).map((title) => ({ title })),
        deliverables: [],
        concurrencyStamp: converting!.concurrencyStamp,
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: dk.clientPart(clientId, 'briefs') });
      setConverting(null);
    },
  });
  if (briefs.isPending) return <Loading />;
  if (briefs.isError) return <ErrorState error={briefs.error} />;
  if (briefs.data.length === 0)
    return <EmptyState compact title="No briefs yet" description="Clients submit briefs in their portal." />;
  return (
    <div className="dl-page">
      {setStatus.error ? <Alert tone="danger">{errorMessage(setStatus.error)}</Alert> : null}
      {briefs.data.map((b) => (
        <Card key={b.id} as="article" aria-label={b.title}>
          <CardHeader
            title={b.title}
            headingLevel={2}
            description={`${b.templateName} · ${b.submittedByClient ? 'from the client' : 'by the agency'} · ${b.submittedBy.displayName}`}
            actions={
              <span className="dl-row">
                {canConvert && b.status !== 'Converted' ? (
                  <Select
                    size="sm"
                    aria-label={`Status of ${b.title}`}
                    value={b.status}
                    disabled={setStatus.isPending}
                    onChange={(e) => setStatus.mutate({ brief: b, status: e.target.value })}
                    options={[
                      { value: 'Submitted', label: 'Submitted' },
                      { value: 'InReview', label: 'In review' },
                      { value: 'Accepted', label: 'Accepted' },
                      { value: 'Declined', label: 'Declined' },
                    ]}
                  />
                ) : (
                  <Badge tone={b.status === 'Converted' ? 'success' : 'info'}>{labelOf(b.status)}</Badge>
                )}
                {canConvert && b.status !== 'Converted' ? (
                  <Button
                    size="sm"
                    onClick={() => {
                      setConverting(b);
                      setTaskTitles(b.title);
                    }}
                  >
                    Convert to tasks
                  </Button>
                ) : null}
              </span>
            }
          />
          <CardBody>
            <KeyValueList
              items={b.answers.map((a) => ({
                label: a.label,
                value: <span className="dl-report__body">{a.value}</span>,
              }))}
            />
            {b.deadline ? <p className="dl-meta">Deadline {formatDateOnly(b.deadline)}</p> : null}
          </CardBody>
        </Card>
      ))}
      <Dialog
        open={converting !== null}
        onClose={() => setConverting(null)}
        title="Convert brief into tasks"
        footer={
          <>
            <Button variant="secondary" onClick={() => setConverting(null)}>
              Cancel
            </Button>
            <Button type="submit" form="convert-brief" loading={convert.isPending} disabled={!projectId}>
              Convert
            </Button>
          </>
        }
      >
        <form
          id="convert-brief"
          className="dl-form"
          onSubmit={(e) => {
            e.preventDefault();
            convert.mutate();
          }}
        >
          {convert.error ? <Alert tone="danger">{errorMessage(convert.error)}</Alert> : null}
          <FormField label="Project" required>
            <Select
              value={projectId}
              onChange={(e) => setProjectId(e.target.value)}
              placeholder="Choose…"
              options={(projects.data ?? []).map((p) => ({ value: p.id, label: p.name }))}
            />
          </FormField>
          <FormField label="Tasks" hint="One task per line.">
            <Textarea rows={4} value={taskTitles} onChange={(e) => setTaskTitles(e.target.value)} />
          </FormField>
        </form>
      </Dialog>
    </div>
  );
}

// ---------------------------------------------------------------- meetings

type EditableActionItem = {
  id: string | null;
  text: string;
  assigneeUserId: string | null;
  dueDate: string | null;
  taskId: string | null;
};

const toLocalInput = (iso: string) => {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

/** Edit a meeting: details, notes, status (held / cancelled) and action items. */
function MeetingEditor({
  clientId,
  meeting,
  onClose,
}: {
  clientId: string;
  meeting: Meeting;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [form, setForm] = useState({
    title: meeting.title,
    kind: meeting.kind as string,
    startsAt: toLocalInput(meeting.startsAt),
    durationMinutes: meeting.durationMinutes,
    location: meeting.location ?? '',
    agenda: meeting.agenda ?? '',
    notes: meeting.notes ?? '',
    status: meeting.status as string,
  });
  const [items, setItems] = useState<EditableActionItem[]>(meeting.actionItems.map((a) => ({ ...a })));
  const [newItem, setNewItem] = useState('');
  const save = useMutation({
    mutationFn: () =>
      api.put(`/agency/meetings/${meeting.id}`, {
        ...form,
        clientId,
        projectId: meeting.projectId,
        startsAt: new Date(form.startsAt).toISOString(),
        attendeeUserIds: meeting.attendees.map((a) => a.id),
        actionItems: items
          .filter((i) => i.text.trim())
          .map((i) => ({ id: i.id, text: i.text, assigneeUserId: i.assigneeUserId, dueDate: i.dueDate })),
        concurrencyStamp: meeting.concurrencyStamp,
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: dk.clientPart(clientId, 'meetings') });
      onClose();
    },
  });
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title={`Edit ${meeting.title}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="edit-meeting" loading={save.isPending}>
            Save meeting
          </Button>
        </>
      }
    >
      <form
        id="edit-meeting"
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
        <FormField label="Title" required>
          <Input
            value={form.title}
            onChange={(e) => setForm({ ...form, title: e.target.value })}
            required
            minLength={2}
            maxLength={200}
          />
        </FormField>
        <div className="dl-form__row">
          <FormField label="Status">
            <Select
              value={form.status}
              onChange={(e) => setForm({ ...form, status: e.target.value })}
              options={[
                { value: 'Scheduled', label: 'Scheduled' },
                { value: 'Held', label: 'Held' },
                { value: 'Cancelled', label: 'Cancelled' },
              ]}
            />
          </FormField>
          <FormField label="Type">
            <Select
              value={form.kind}
              onChange={(e) => setForm({ ...form, kind: e.target.value })}
              options={['Kickoff', 'MonthlyReview', 'Strategy', 'Creative', 'Other'].map((k) => ({
                value: k,
                label: labelOf(k),
              }))}
            />
          </FormField>
          <FormField label="Starts" required>
            <Input
              type="datetime-local"
              value={form.startsAt}
              onChange={(e) => setForm({ ...form, startsAt: e.target.value })}
              required
            />
          </FormField>
          <FormField label="Minutes">
            <Input
              type="number"
              min={5}
              max={600}
              value={form.durationMinutes}
              onChange={(e) => setForm({ ...form, durationMinutes: Number(e.target.value) })}
            />
          </FormField>
        </div>
        <FormField label="Location or link" optional>
          <Input
            value={form.location}
            maxLength={500}
            onChange={(e) => setForm({ ...form, location: e.target.value })}
          />
        </FormField>
        <FormField label="Agenda" optional>
          <Textarea
            rows={3}
            value={form.agenda}
            maxLength={8000}
            onChange={(e) => setForm({ ...form, agenda: e.target.value })}
          />
        </FormField>
        <FormField label="Notes" optional>
          <Textarea
            rows={4}
            value={form.notes}
            maxLength={20000}
            onChange={(e) => setForm({ ...form, notes: e.target.value })}
          />
        </FormField>
        <fieldset className="dl-form">
          <legend>Action items</legend>
          {items.map((item, index) => (
            <div key={item.id ?? `new-${index}`} className="dl-toolbar">
              <FormField label={`Action item ${index + 1}`}>
                <Input
                  value={item.text}
                  maxLength={500}
                  onChange={(e) =>
                    setItems(items.map((x, i) => (i === index ? { ...x, text: e.target.value } : x)))
                  }
                />
              </FormField>
              {item.taskId ? <Badge tone="success">Task created</Badge> : null}
              <IconButton
                size="sm"
                variant="ghost"
                label={`Remove action item ${index + 1}`}
                icon={<Trash2 />}
                onClick={() => setItems(items.filter((_, i) => i !== index))}
              />
            </div>
          ))}
          <div className="dl-toolbar">
            <FormField label="New action item">
              <Input value={newItem} maxLength={500} onChange={(e) => setNewItem(e.target.value)} />
            </FormField>
            <Button
              size="sm"
              variant="secondary"
              disabled={!newItem.trim()}
              onClick={() => {
                setItems([
                  ...items,
                  { id: null, text: newItem.trim(), assigneeUserId: null, dueDate: null, taskId: null },
                ]);
                setNewItem('');
              }}
            >
              Add
            </Button>
          </div>
        </fieldset>
      </form>
    </Dialog>
  );
}

export function MeetingsTab({ clientId }: { clientId: string }) {
  const qc = useQueryClient();
  const { hasPermission } = useAuth();
  // Scheduling and editing meetings need projects.view; turning an action item into a task also needs
  // deliverables.submit (and the project list). Staff with only clients.view (finance, sales) read the meetings.
  const canSchedule = hasPermission(Permissions.ProjectsView);
  const canConvert = canSchedule && hasPermission(Permissions.DeliverablesSubmit);
  const [editing, setEditing] = useState<Meeting | null>(null);
  const projects = useProjectOptions(clientId, canConvert);
  const [convertProject, setConvertProject] = useState('');
  const convert = useMutation({
    mutationFn: ({ meeting, itemId }: { meeting: Meeting; itemId: string }) =>
      api.post(`/agency/meetings/${meeting.id}/action-items/${itemId}/convert`, {
        projectId: meeting.projectId ?? convertProject,
      }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: dk.clientPart(clientId, 'meetings') }),
  });
  const meetings = useClientPart<Meeting[]>(clientId, 'meetings', `/agency/meetings?clientId=${clientId}`);
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({
    title: '',
    kind: 'MonthlyReview',
    startsAt: '',
    durationMinutes: 30,
    location: '',
    agenda: '',
  });
  const create = useMutation({
    mutationFn: () =>
      api.post('/agency/meetings', { ...form, clientId, startsAt: new Date(form.startsAt).toISOString() }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: dk.clientPart(clientId, 'meetings') });
      setOpen(false);
    },
  });
  if (meetings.isPending) return <Loading />;
  if (meetings.isError) return <ErrorState error={meetings.error} />;
  return (
    <div className="dl-page">
      {canSchedule ? (
        <div>
          <Button leadingIcon={<Plus aria-hidden="true" />} onClick={() => setOpen(true)}>
            Schedule meeting
          </Button>
        </div>
      ) : null}
      {meetings.data.length === 0 ? (
        <EmptyState compact title="No meetings yet" />
      ) : (
        meetings.data.map((m) => (
          <Card key={m.id} as="article" aria-label={m.title}>
            <CardHeader
              title={m.title}
              headingLevel={2}
              description={<DateTime value={m.startsAt} format="both" />}
              actions={
                <span className="dl-row">
                  <Badge>{labelOf(m.status)}</Badge>
                  {canSchedule ? (
                    <Button size="sm" variant="secondary" onClick={() => setEditing(m)}>
                      Edit<span className="visually-hidden"> {m.title}</span>
                    </Button>
                  ) : null}
                </span>
              }
            />
            <CardBody>
              {m.agenda ? <p className="dl-report__body">{m.agenda}</p> : null}
              {m.notes ? <p className="dl-report__body">{m.notes}</p> : null}
              {m.actionItems.length > 0 ? (
                <ul className="dl-checklist" aria-label="Action items">
                  {m.actionItems.map((a) => (
                    <li key={a.id} className="dl-row">
                      <Checkbox
                        label={a.text}
                        checked={Boolean(a.taskId)}
                        readOnly
                        description={a.taskId ? 'Task created' : undefined}
                      />
                      {!a.taskId && canConvert ? (
                        <>
                          {!m.projectId ? (
                            <Select
                              size="sm"
                              aria-label={`Project for “${a.text}”`}
                              value={convertProject}
                              placeholder="Project…"
                              options={(projects.data ?? []).map((p) => ({ value: p.id, label: p.name }))}
                              onChange={(e) => setConvertProject(e.target.value)}
                            />
                          ) : null}
                          <Button
                            size="sm"
                            variant="ghost"
                            disabled={!m.projectId && !convertProject}
                            loading={convert.isPending && convert.variables?.itemId === a.id}
                            onClick={() => convert.mutate({ meeting: m, itemId: a.id })}
                          >
                            Create task<span className="visually-hidden"> for {a.text}</span>
                          </Button>
                        </>
                      ) : null}
                    </li>
                  ))}
                </ul>
              ) : null}
              {convert.error ? <Alert tone="danger">{errorMessage(convert.error)}</Alert> : null}
            </CardBody>
          </Card>
        ))
      )}
      {editing ? (
        <MeetingEditor clientId={clientId} meeting={editing} onClose={() => setEditing(null)} />
      ) : null}
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title="Schedule a meeting"
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" form="new-meeting" loading={create.isPending}>
              Schedule
            </Button>
          </>
        }
      >
        <form
          id="new-meeting"
          className="dl-form"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          {create.error ? <Alert tone="danger">{errorMessage(create.error)}</Alert> : null}
          <FormField label="Title" required>
            <Input
              value={form.title}
              onChange={(e) => setForm({ ...form, title: e.target.value })}
              required
              minLength={2}
            />
          </FormField>
          <div className="dl-form__row">
            <FormField label="Type">
              <Select
                value={form.kind}
                onChange={(e) => setForm({ ...form, kind: e.target.value })}
                options={['Kickoff', 'MonthlyReview', 'Strategy', 'Creative', 'Other'].map((k) => ({
                  value: k,
                  label: labelOf(k),
                }))}
              />
            </FormField>
            <FormField label="Starts" required>
              <Input
                type="datetime-local"
                value={form.startsAt}
                onChange={(e) => setForm({ ...form, startsAt: e.target.value })}
                required
              />
            </FormField>
            <FormField label="Minutes">
              <Input
                type="number"
                min={5}
                max={600}
                value={form.durationMinutes}
                onChange={(e) => setForm({ ...form, durationMinutes: Number(e.target.value) })}
              />
            </FormField>
          </div>
          <FormField label="Location or link" optional>
            <Input value={form.location} onChange={(e) => setForm({ ...form, location: e.target.value })} />
          </FormField>
          <FormField label="Agenda" optional>
            <Textarea
              rows={3}
              value={form.agenda}
              onChange={(e) => setForm({ ...form, agenda: e.target.value })}
            />
          </FormField>
        </form>
      </Dialog>
    </div>
  );
}

// ---------------------------------------------------------------- feedback

export function FeedbackTab({ clientId }: { clientId: string }) {
  const data = useClientPart<FeedbackSummary>(clientId, 'feedback', `/agency/clients/${clientId}/feedback`);
  if (data.isPending) return <Loading />;
  if (data.isError) return <ErrorState error={data.error} />;
  const f = data.data;
  return (
    <div className="dl-page">
      <div className="dl-stats">
        <Stat
          label="Average CSAT"
          value={f.averageCsat !== null ? `${f.averageCsat.toFixed(1)} / 5` : '—'}
          measurement="Measured"
          hint={`${f.csatResponses} responses`}
        />
        <Stat
          label="NPS"
          value={f.npsScore ?? '—'}
          measurement="Measured"
          hint={`${f.promoters} promoters · ${f.passives} passives · ${f.detractors} detractors`}
        />
      </div>
      {f.recent.length === 0 ? (
        <EmptyState compact title="No feedback yet" />
      ) : (
        <ul className="dl-list" aria-label="Recent feedback">
          {f.recent.map((r) => (
            <li key={r.id} className="dl-list__item">
              <span className="dl-list__main">
                <span className="dl-list__title">
                  {r.kind === 'Nps' ? `NPS ${r.score}/10` : `CSAT ${r.score}/5`}
                  {r.deliverableTitle ? ` · ${r.deliverableTitle}` : ''}
                </span>
                {r.comment ? <span>{r.comment}</span> : null}
                <span className="dl-meta">
                  {r.user.displayName} · <DateTime value={r.createdAt} format="date" />
                </span>
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
