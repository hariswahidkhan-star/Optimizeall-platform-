import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, Plus, ShieldOff, Trash2, XCircle } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { CopyField } from '@/components/ui/CopyField';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { Dialog } from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FormField } from '@/components/ui/FormField';
import { IconButton } from '@/components/ui/IconButton';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Select } from '@/components/ui/Select';
import { SkeletonText } from '@/components/ui/Skeleton';
import { Switch } from '@/components/ui/Switch';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { humanize } from '@/lib/format/text';
import { EMAIL_API, emailKeys, useSenders, useSuppressions, useWorkspaceSettings } from '../api/queries';
import type { MessageChannel, SenderProfile, Suppression, WorkspaceSettings } from '../api/types';
import { useEmailWorkspace } from '../shared/workspace';

function fieldErrors(error: unknown): string[] {
  return isApiError(error) && error.errors ? Object.values(error.errors).flat() : error ? [errorMessage(error)] : [];
}

function SettingsForm({ settings }: { settings: WorkspaceSettings }) {
  const { key } = useEmailWorkspace();
  const { hasPermission } = useAuth();
  const canSms = hasPermission(Permissions.SmsManage);
  const queryClient = useQueryClient();
  const toast = useToast();
  const [form, setForm] = useState(settings);
  const set = (patch: Partial<WorkspaceSettings>) => setForm((f) => ({ ...f, ...patch }));
  const save = useMutation({
    mutationFn: () =>
      api.put<WorkspaceSettings>(`${EMAIL_API}/settings`, {
        clientAccountId: settings.clientAccountId,
        organizationName: form.organizationName,
        physicalAddress: form.physicalAddress,
        requireClientApproval: form.requireClientApproval,
        defaultThrottlePerMinute: form.defaultThrottlePerMinute,
        defaultTimeZone: form.defaultTimeZone,
        quietHoursStart: form.quietHoursStart,
        quietHoursEnd: form.quietHoursEnd,
        smsCostPerSegment: form.smsCostPerSegment,
        whatsAppCostPerMessage: form.whatsAppCostPerMessage,
        costCurrency: form.costCurrency,
        concurrencyStamp: settings.concurrencyStamp,
      }),
    onSuccess: (saved) => {
      toast.success('Settings saved');
      queryClient.setQueryData(emailKeys.settings(key), saved);
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  const errors = fieldErrors(save.error);
  return (
    <form className="stack" onSubmit={submit} aria-label="Sending and compliance settings">
      <FormField label="Sender organization name" required hint="Shown in every email footer.">
        <Input value={form.organizationName} onChange={(e) => set({ organizationName: e.target.value })} required maxLength={200} />
      </FormField>
      <FormField label="Postal address" hint="Required in marketing email by CAN-SPAM; campaigns are blocked without it.">
        <Input value={form.physicalAddress} onChange={(e) => set({ physicalAddress: e.target.value })} maxLength={500} />
      </FormField>
      <Switch
        checked={form.requireClientApproval}
        onCheckedChange={(v) => set({ requireClientApproval: v })}
        label="Require client approval before sending"
        description="The client approves campaigns in their portal before they can be sent or scheduled."
      />
      <div className="cluster">
        <FormField label="Sending speed (emails per minute)">
          <Input type="number" min={1} max={100000} value={form.defaultThrottlePerMinute} onChange={(e) => set({ defaultThrottlePerMinute: Number(e.target.value) || 1 })} />
        </FormField>
        <FormField label="Default time zone" hint="IANA name, e.g. Europe/London">
          <Input value={form.defaultTimeZone} onChange={(e) => set({ defaultTimeZone: e.target.value })} maxLength={64} />
        </FormField>
      </div>
      <fieldset className="stack" disabled={!canSms}>
        <legend>SMS & WhatsApp{canSms ? '' : ' (needs SMS permission)'}</legend>
        <div className="cluster">
          <FormField label="Quiet hours start (0–23)">
            <Input type="number" min={0} max={23} value={form.quietHoursStart} onChange={(e) => set({ quietHoursStart: Number(e.target.value) })} />
          </FormField>
          <FormField label="Quiet hours end (0–23)">
            <Input type="number" min={0} max={23} value={form.quietHoursEnd} onChange={(e) => set({ quietHoursEnd: Number(e.target.value) })} />
          </FormField>
          <FormField label="Cost per SMS segment">
            <Input type="number" min={0} max={10} step="0.0001" value={form.smsCostPerSegment} onChange={(e) => set({ smsCostPerSegment: Number(e.target.value) })} />
          </FormField>
          <FormField label="Cost per WhatsApp message">
            <Input type="number" min={0} max={10} step="0.0001" value={form.whatsAppCostPerMessage} onChange={(e) => set({ whatsAppCostPerMessage: Number(e.target.value) })} />
          </FormField>
          <FormField label="Cost currency">
            <Input value={form.costCurrency} onChange={(e) => set({ costCurrency: e.target.value.toUpperCase() })} maxLength={3} />
          </FormField>
        </div>
      </fieldset>
      {errors.length > 0 && <Alert tone="danger">{errors.join(' ')}</Alert>}
      <div className="cluster">
        <Button type="submit" loading={save.isPending}>
          Save settings
        </Button>
      </div>
    </form>
  );
}

function ProviderCard({ settings }: { settings: WorkspaceSettings }) {
  const { key } = useEmailWorkspace();
  const { hasPermission } = useAuth();
  const canChoose = hasPermission(Permissions.IntegrationsManage);
  const queryClient = useQueryClient();
  const [choice, setChoice] = useState(settings.emailProvider);
  const [open, setOpen] = useState(false);
  return (
    <Card>
      <CardHeader title="Delivery providers" description="Credentials live in the integrations vault; nothing is sent through a provider that is not ready." />
      <CardBody className="stack">
        <ul className="stack" aria-label="Provider readiness">
          {settings.providers.map((p) => (
            <li key={p.channel} className="cluster">
              {p.ready ? <CheckCircle2 aria-hidden="true" className="email-ok" /> : <XCircle aria-hidden="true" className="email-bad" />}
              <strong>{humanize(p.channel)}</strong>
              <Badge tone={p.ready ? 'success' : 'warning'} size="sm">
                {p.ready ? 'Ready' : 'Not ready'}
              </Badge>
              <span className="email-muted">{p.detail}</span>
            </li>
          ))}
        </ul>
        {canChoose && (
          <div className="cluster">
            <FormField label="Email provider">
              <Select value={choice} options={settings.availableProviders.map((p) => ({ value: p, label: p }))} onChange={(e) => setChoice(e.target.value)} />
            </FormField>
            <Button variant="secondary" disabled={choice === settings.emailProvider} onClick={() => setOpen(true)}>
              Switch provider
            </Button>
          </div>
        )}
      </CardBody>
      <ConfirmDialog
        open={open}
        onClose={() => setOpen(false)}
        title={`Send this workspace's email through ${choice}?`}
        description="Campaigns and journeys sent from now on use the new provider. Make sure its domain authentication (SPF/DKIM) and webhooks are set up first."
        confirmLabel="Switch provider"
        onConfirm={async () => {
          const saved = await api.put<WorkspaceSettings>(`${EMAIL_API}/settings/provider`, { clientAccountId: settings.clientAccountId, emailProvider: choice, confirm: true });
          queryClient.setQueryData(emailKeys.settings(key), saved);
        }}
      />
    </Card>
  );
}

function SenderDialog({ onClose, clientId }: { onClose: () => void; clientId: string | null }) {
  const { key } = useEmailWorkspace();
  const queryClient = useQueryClient();
  const [fromName, setFromName] = useState('');
  const [fromEmail, setFromEmail] = useState('');
  const [replyTo, setReplyTo] = useState('');
  const [isDefault, setIsDefault] = useState(false);
  const save = useMutation({
    mutationFn: () => api.post<SenderProfile>(`${EMAIL_API}/senders`, { clientAccountId: clientId, fromName, fromEmail, replyTo: replyTo || null, isDefault }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: emailKeys.senders(key) });
      onClose();
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  return (
    <Dialog
      open
      onClose={onClose}
      title="Add a sender"
      description="A 6-digit code is emailed to the address; senders must be verified before campaigns can use them."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="sender-form" loading={save.isPending}>
            Add sender
          </Button>
        </>
      }
    >
      <form id="sender-form" className="stack" onSubmit={submit}>
        <FormField label="From name" required>
          <Input value={fromName} onChange={(e) => setFromName(e.target.value)} required maxLength={100} />
        </FormField>
        <FormField label="From email" required hint="Use a domain with SPF, DKIM and DMARC set up.">
          <Input type="email" value={fromEmail} onChange={(e) => setFromEmail(e.target.value)} required maxLength={254} />
        </FormField>
        <FormField label="Reply-to" optional>
          <Input type="email" value={replyTo} onChange={(e) => setReplyTo(e.target.value)} maxLength={254} />
        </FormField>
        <Switch checked={isDefault} onCheckedChange={setIsDefault} label="Default sender for this workspace" />
        {save.isError && <Alert tone="danger">{fieldErrors(save.error).join(' ')}</Alert>}
      </form>
    </Dialog>
  );
}

function VerifyDialog({ sender, onClose }: { sender: SenderProfile; onClose: () => void }) {
  const { key } = useEmailWorkspace();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [code, setCode] = useState('');
  const verify = useMutation({
    mutationFn: () => api.post<SenderProfile>(`${EMAIL_API}/senders/${sender.id}/verify`, { code }),
    onSuccess: () => {
      toast.success('Sender verified');
      void queryClient.invalidateQueries({ queryKey: emailKeys.senders(key) });
      onClose();
    },
  });
  const resend = useMutation({
    mutationFn: () => api.post<SenderProfile>(`${EMAIL_API}/senders/${sender.id}/send-verification`),
    onSuccess: () => toast.success('Code sent', `Check the inbox of ${sender.fromEmail}.`),
    onError: (e) => toast.error('Could not send the code', errorMessage(e)),
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    verify.mutate();
  };
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Verify ${sender.fromEmail}`}
      footer={
        <>
          <Button variant="secondary" onClick={() => resend.mutate()} loading={resend.isPending}>
            Send a code
          </Button>
          <Button type="submit" form="verify-form" loading={verify.isPending} disabled={!/^\d{6}$/.test(code)}>
            Verify
          </Button>
        </>
      }
    >
      <form id="verify-form" className="stack" onSubmit={submit}>
        <FormField label="6-digit code" hint={sender.verificationSentAt ? 'The code expires 24 hours after it was sent.' : 'Send a code first.'}>
          <Input inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} />
        </FormField>
        {verify.isError && <Alert tone="danger">{errorMessage(verify.error)}</Alert>}
      </form>
    </Dialog>
  );
}

function SendersCard() {
  const { clientId, key } = useEmailWorkspace();
  const senders = useSenders(clientId);
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [verifying, setVerifying] = useState<SenderProfile | null>(null);
  const [removing, setRemoving] = useState<SenderProfile | null>(null);
  return (
    <Card>
      <CardHeader
        title="Senders"
        actions={
          <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => setAdding(true)}>
            Add sender
          </Button>
        }
      />
      <CardBody>
        {senders.isError ? (
          <ErrorState error={senders.error} onRetry={() => void senders.refetch()} />
        ) : (
          <DataTable
            caption="Sender identities"
            rows={senders.data ?? []}
            getRowId={(s) => s.id}
            loading={senders.isPending}
            columns={[
              { id: 'from', header: 'From', primary: true, cell: (s) => `${s.fromName} <${s.fromEmail}>` },
              {
                id: 'verified',
                header: 'Status',
                cell: (s) =>
                  s.verified ? (
                    <Badge tone="success" size="sm">
                      Verified
                    </Badge>
                  ) : (
                    <Button size="sm" variant="secondary" onClick={() => setVerifying(s)}>
                      Verify
                    </Button>
                  ),
              },
              { id: 'default', header: 'Default', cell: (s) => (s.isDefault ? 'Yes' : '') },
              {
                id: 'actions',
                header: <span className="visually-hidden">Actions</span>,
                cell: (s) => <IconButton label={`Remove ${s.fromEmail}`} icon={<Trash2 />} size="sm" onClick={() => setRemoving(s)} />,
              },
            ]}
            emptyState={<EmptyState headingLevel={3} title="No senders" description="Add and verify the address your emails come from." />}
          />
        )}
      </CardBody>
      {adding && <SenderDialog clientId={clientId} onClose={() => setAdding(false)} />}
      {verifying && <VerifyDialog sender={verifying} onClose={() => setVerifying(null)} />}
      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        tone="danger"
        title={`Remove ${removing?.fromEmail ?? 'sender'}?`}
        confirmLabel="Remove"
        onConfirm={async () => {
          await api.delete(`${EMAIL_API}/senders/${removing!.id}`);
          void queryClient.invalidateQueries({ queryKey: emailKeys.senders(key) });
        }}
      />
    </Card>
  );
}

function SuppressionDialog({ onClose }: { onClose: () => void }) {
  const { clientId } = useEmailWorkspace();
  const queryClient = useQueryClient();
  const [channel, setChannel] = useState<MessageChannel>('Email');
  const [value, setValue] = useState('');
  const [note, setNote] = useState('');
  const save = useMutation({
    mutationFn: () => api.post<Suppression>(`${EMAIL_API}/suppressions`, { clientAccountId: clientId, channel, value, reason: 'Manual', note: note || null }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: emailKeys.all });
      onClose();
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  return (
    <Dialog
      open
      onClose={onClose}
      title="Suppress an address"
      description="Suppressed addresses never receive messages on that channel from this workspace, whatever list they are on."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="suppression-form" loading={save.isPending}>
            Suppress
          </Button>
        </>
      }
    >
      <form id="suppression-form" className="stack" onSubmit={submit}>
        <FormField label="Channel">
          <Select
            value={channel}
            options={[
              { value: 'Email', label: 'Email' },
              { value: 'Sms', label: 'SMS' },
              { value: 'WhatsApp', label: 'WhatsApp' },
            ]}
            onChange={(e) => setChannel(e.target.value as MessageChannel)}
          />
        </FormField>
        <FormField label={channel === 'Email' ? 'Email address' : 'Phone number (E.164)'} required>
          <Input value={value} onChange={(e) => setValue(e.target.value)} required maxLength={254} />
        </FormField>
        <FormField label="Note" optional>
          <Input value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} />
        </FormField>
        {save.isError && <Alert tone="danger">{fieldErrors(save.error).join(' ')}</Alert>}
      </form>
    </Dialog>
  );
}

function SuppressionsCard() {
  const { clientId } = useEmailWorkspace();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const suppressions = useSuppressions(clientId, { search: search || undefined, page, pageSize: 25 });
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<Suppression | null>(null);
  return (
    <Card>
      <CardHeader
        title="Suppression list"
        description="Unsubscribes, hard bounces, complaints and STOP replies are added automatically."
        actions={
          <Button size="sm" variant="secondary" leadingIcon={<ShieldOff />} onClick={() => setAdding(true)}>
            Suppress address
          </Button>
        }
      />
      <CardBody className="stack">
        <FormField label="Search suppressions">
          <Input
            type="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
          />
        </FormField>
        {suppressions.isError ? (
          <ErrorState error={suppressions.error} onRetry={() => void suppressions.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Suppressed addresses"
              rows={suppressions.data?.items ?? []}
              getRowId={(s) => s.id}
              loading={suppressions.isPending}
              columns={[
                { id: 'value', header: 'Address', primary: true, cell: (s) => s.value },
                { id: 'channel', header: 'Channel', cell: (s) => s.channel },
                { id: 'reason', header: 'Reason', cell: (s) => humanize(s.reason) },
                { id: 'source', header: 'Source', hideOnMobile: true, cell: (s) => humanize(s.source) },
                { id: 'added', header: 'Added', hideOnMobile: true, cell: (s) => <DateTime value={s.createdAt} format="date" /> },
                {
                  id: 'actions',
                  header: <span className="visually-hidden">Actions</span>,
                  cell: (s) => <IconButton label={`Remove suppression for ${s.value}`} icon={<Trash2 />} size="sm" onClick={() => setRemoving(s)} />,
                },
              ]}
              emptyState={<EmptyState headingLevel={3} title="Nothing suppressed" description="Suppressed addresses appear here." />}
            />
            {suppressions.data && suppressions.data.total > 25 && (
              <Pagination page={page} pageSize={25} total={suppressions.data.total} onPageChange={setPage} label="Suppression pages" />
            )}
          </>
        )}
      </CardBody>
      {adding && <SuppressionDialog onClose={() => setAdding(false)} />}
      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        tone="danger"
        title={`Remove the suppression for ${removing?.value ?? ''}?`}
        description="Only do this when the contact asked to receive messages again. The reason is kept in the audit log."
        confirmLabel="Remove suppression"
        requireReason
        reasonLabel="Why is it safe to message this address again?"
        onConfirm={async ({ reason }) => {
          await api.delete(`${EMAIL_API}/suppressions/${removing!.id}`, undefined, { query: { reason } });
          void queryClient.invalidateQueries({ queryKey: emailKeys.all });
        }}
      />
    </Card>
  );
}

/** Workspace sending/compliance settings, providers, verified senders, suppression list and webhook endpoints. */
export function EmailSettingsPage() {
  const { clientId, key } = useEmailWorkspace();
  const settings = useWorkspaceSettings(clientId);
  return (
    <>
      <PageHeader title="Email & SMS settings" description="Compliance details, sending limits and delivery providers for this workspace." />
      {settings.isPending ? (
        <SkeletonText lines={8} />
      ) : settings.isError ? (
        <ErrorState error={settings.error} onRetry={() => void settings.refetch()} />
      ) : (
        <>
          <Card>
            <CardHeader title="Sending & compliance" />
            <CardBody>
              <SettingsForm key={`${key}-${settings.data.concurrencyStamp}`} settings={settings.data} />
            </CardBody>
          </Card>
          <ProviderCard key={`p-${key}-${settings.data.emailProvider}`} settings={settings.data} />
          <SendersCard />
          <SuppressionsCard />
          <Card>
            <CardHeader title="Webhooks & APIs" description="Paste these into your provider dashboards so bounces, complaints and STOP replies are processed." />
            <CardBody className="stack">
              <CopyField label="SendGrid event webhook" value={settings.data.webhooks.sendGrid} />
              <CopyField label="Mailgun webhook" value={settings.data.webhooks.mailgun} />
              <CopyField label="Twilio inbound SMS" value={settings.data.webhooks.twilioInbound} />
              <CopyField label="Twilio status callback" value={settings.data.webhooks.twilioStatus} />
              <CopyField label="Conversions API (signed)" value={settings.data.webhooks.conversions} />
              <CopyField label="Custom events API (signed)" value={settings.data.webhooks.events} />
            </CardBody>
          </Card>
        </>
      )}
    </>
  );
}
