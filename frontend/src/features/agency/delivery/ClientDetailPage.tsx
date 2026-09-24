import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import {
  Alert,
  Button,
  Dialog,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Switch,
  Tabs,
  Textarea,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { MessagesPanel } from '../shared/MessagesPanel';
import { CLIENT_STATUSES, type ClientDetail, type ClientStatus } from '../shared/deliveryTypes';
import { ClientStatusBadge } from '../shared/deliveryUi';
import { dk, useStaff } from './api';
import {
  BrandKitTab,
  BriefsTab,
  FeedbackTab,
  MeetingsTab,
  OnboardingTab,
  OverviewTab,
  ProjectsTab,
  ReportsTab,
  TeamTab,
  TimeTab,
  UsersTab,
} from './clientTabs';

function StatusDialog({ client, onClose }: { client: ClientDetail; onClose: () => void }) {
  const qc = useQueryClient();
  const [status, setStatus] = useState<ClientStatus>(client.status);
  const [reason, setReason] = useState('');
  const needsReason = status === 'Paused' || status === 'Churned';
  const save = useMutation({
    mutationFn: () => api.post<ClientDetail>(`/agency/clients/${client.id}/status`, { status, reason: reason || null, concurrencyStamp: client.concurrencyStamp }),
    onSuccess: (data) => {
      qc.setQueryData(dk.client(client.id), data);
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Change client status"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="client-status" loading={save.isPending} disabled={needsReason && reason.trim().length < 3}>
            Save status
          </Button>
        </>
      }
    >
      <form
        id="client-status"
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
        <FormField label="Status">
          <Select value={status} onChange={(e) => setStatus(e.target.value as ClientStatus)} options={CLIENT_STATUSES.map((s) => ({ value: s, label: s }))} />
        </FormField>
        {needsReason ? (
          <FormField label="Reason" required hint="Recorded in the audit log and shown on the client profile.">
            <Textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={3} maxLength={1000} />
          </FormField>
        ) : null}
      </form>
    </Dialog>
  );
}

function EditDialog({ client, onClose }: { client: ClientDetail; onClose: () => void }) {
  const qc = useQueryClient();
  const staff = useStaff();
  const [form, setForm] = useState({
    name: client.name,
    summary: client.summary ?? '',
    industry: client.industry ?? '',
    website: client.website ?? '',
    countryCode: client.countryCode,
    timeZone: client.timeZone,
    currency: client.currency,
    accountManagerUserId: client.accountManager?.id ?? '',
    billingContactName: client.billingContactName ?? '',
    billingEmail: client.billingEmail ?? '',
    billingAddress: client.billingAddress ?? '',
    taxId: client.taxId ?? '',
    notes: client.notes ?? '',
    approvalSlaDays: client.approvalSlaDays,
    autoApproveAfterDays: client.autoApproveAfterDays,
  });
  const [logo, setLogo] = useState<File | null>(null);
  const save = useMutation({
    mutationFn: async () => {
      const blank = (s: string) => (s.trim() ? s.trim() : null);
      let saved = await api.put<ClientDetail>(`/agency/clients/${client.id}`, {
        ...form,
        summary: blank(form.summary),
        industry: blank(form.industry),
        website: blank(form.website),
        accountManagerUserId: form.accountManagerUserId || null,
        billingContactName: blank(form.billingContactName),
        billingEmail: blank(form.billingEmail),
        billingAddress: blank(form.billingAddress),
        taxId: blank(form.taxId),
        notes: blank(form.notes),
        concurrencyStamp: client.concurrencyStamp,
      });
      if (logo) {
        const data = new FormData();
        data.append('file', logo);
        saved = await api.upload<ClientDetail>(`/agency/clients/${client.id}/logo`, data);
      }
      return saved;
    },
    onSuccess: (data) => {
      qc.setQueryData(dk.client(client.id), data);
      void qc.invalidateQueries({ queryKey: ['delivery', 'clients'] });
      onClose();
    },
  });
  const text = (key: keyof typeof form, label: string, props: Record<string, unknown> = {}) => (
    <FormField label={label}>
      <Input value={String(form[key] ?? '')} onChange={(e) => setForm({ ...form, [key]: e.target.value })} {...props} />
    </FormField>
  );
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title="Edit client"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="edit-client" loading={save.isPending}>
            Save changes
          </Button>
        </>
      }
    >
      <form
        id="edit-client"
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
        {text('name', 'Name', { required: true, minLength: 2 })}
        <FormField label="Summary" optional>
          <Textarea rows={2} value={form.summary} onChange={(e) => setForm({ ...form, summary: e.target.value })} />
        </FormField>
        <div className="dl-form__row">
          {text('industry', 'Industry')}
          {text('website', 'Website', { type: 'url' })}
        </div>
        <FormField label="Account manager">
          <Select
            value={form.accountManagerUserId}
            onChange={(e) => setForm({ ...form, accountManagerUserId: e.target.value })}
            placeholder="Unassigned"
            options={(staff.data ?? []).map((s) => ({ value: s.id, label: s.displayName }))}
          />
        </FormField>
        <div className="dl-form__row">
          {text('billingContactName', 'Billing contact')}
          {text('billingEmail', 'Billing email', { type: 'email' })}
          {text('taxId', 'Tax ID')}
        </div>
        <FormField label="Billing address" optional>
          <Textarea rows={2} value={form.billingAddress} onChange={(e) => setForm({ ...form, billingAddress: e.target.value })} />
        </FormField>
        <div className="dl-form__row">
          <FormField label="Client feedback SLA (business days)">
            <Input type="number" min={1} max={30} value={form.approvalSlaDays} onChange={(e) => setForm({ ...form, approvalSlaDays: Number(e.target.value) })} />
          </FormField>
          <div className="dl-form">
            <Switch
              checked={form.autoApproveAfterDays !== null}
              onCheckedChange={(on) => setForm({ ...form, autoApproveAfterDays: on ? 5 : null })}
              label="Auto-approve after silence"
              description="Only when agreed with the client in writing. Every auto-approval is audited."
            />
            {form.autoApproveAfterDays !== null ? (
              <FormField label="Auto-approve after (days)">
                <Input type="number" min={1} max={60} value={form.autoApproveAfterDays} onChange={(e) => setForm({ ...form, autoApproveAfterDays: Number(e.target.value) })} />
              </FormField>
            ) : null}
          </div>
        </div>
        <FormField label="Logo" optional hint="PNG, JPEG or WebP.">
          <Input type="file" accept="image/png,image/jpeg,image/webp" onChange={(e) => setLogo(e.target.files?.[0] ?? null)} />
        </FormField>
        <FormField label="Internal notes" optional>
          <Textarea rows={3} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
        </FormField>
      </form>
    </Dialog>
  );
}

const TABS = ['overview', 'team', 'users', 'onboarding', 'brand', 'projects', 'time', 'reports', 'messages', 'briefs', 'meetings', 'feedback'] as const;

export function ClientDetailPage() {
  const { clientId = '' } = useParams();
  const { hasPermission } = useAuth();
  const [params, setParams] = useSearchParams();
  const [dialog, setDialog] = useState<'status' | 'edit' | null>(null);
  const tab = (TABS as readonly string[]).includes(params.get('tab') ?? '') ? params.get('tab')! : 'overview';
  const client = useQuery({
    queryKey: dk.client(clientId),
    queryFn: ({ signal }) => api.get<ClientDetail>(`/agency/clients/${clientId}`, { signal }),
  });
  if (client.isPending) return <Skeleton height={240} />;
  if (client.isError) return <ErrorState error={client.error} onRetry={() => void client.refetch()} />;
  const c = client.data;
  const canManage = hasPermission(Permissions.ClientsManage);
  return (
    <div className="dl-page">
      <PageHeader
        title={c.name}
        description={c.summary ?? undefined}
        breadcrumbs={[{ label: 'Clients', to: '/agency/clients' }, { label: c.name }]}
        meta={<ClientStatusBadge status={c.status} />}
        actions={
          canManage ? (
            <span className="dl-row">
              <Button variant="secondary" onClick={() => setDialog('edit')}>
                Edit
              </Button>
              <Button variant="secondary" onClick={() => setDialog('status')}>
                Change status
              </Button>
            </span>
          ) : null
        }
      />
      <Tabs
        label="Client sections"
        value={tab}
        onValueChange={(id) => setParams({ tab: id }, { replace: true })}
        tabs={[
          { id: 'overview', label: 'Overview', content: <OverviewTab client={c} /> },
          { id: 'team', label: 'Team', content: <TeamTab clientId={c.id} /> },
          { id: 'users', label: 'Users', content: <UsersTab clientId={c.id} /> },
          { id: 'onboarding', label: 'Onboarding', content: <OnboardingTab clientId={c.id} /> },
          { id: 'brand', label: 'Brand kit', content: <BrandKitTab clientId={c.id} /> },
          { id: 'projects', label: 'Projects', content: <ProjectsTab clientId={c.id} /> },
          { id: 'time', label: 'Time', content: <TimeTab clientId={c.id} /> },
          { id: 'reports', label: 'Reports', content: <ReportsTab clientId={c.id} /> },
          { id: 'messages', label: 'Messages', content: <MessagesPanel base={`/agency/clients/${c.id}`} audience="staff" /> },
          { id: 'briefs', label: 'Briefs', content: <BriefsTab clientId={c.id} /> },
          { id: 'meetings', label: 'Meetings', content: <MeetingsTab clientId={c.id} /> },
          { id: 'feedback', label: 'Feedback', content: <FeedbackTab clientId={c.id} /> },
        ]}
      />
      {dialog === 'status' ? <StatusDialog client={c} onClose={() => setDialog(null)} /> : null}
      {dialog === 'edit' ? <EditDialog client={c} onClose={() => setDialog(null)} /> : null}
    </div>
  );
}
