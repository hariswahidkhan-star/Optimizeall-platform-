import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, ArchiveRestore, Link2, Pencil, Plug, Plus, Unplug } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
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
  Input,
  PageHeader,
  Select,
  Spinner,
  Switch,
  useToast,
  type DataTableColumn,
  type MenuEntry,
  type Tone,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import {
  NETWORKS,
  NETWORK_LABELS,
  socialKeys,
  useProfiles,
  type Profile,
  type SocialNetwork,
  type SocialSettings,
} from './api';
import { ClientPicker, NetworkChip, useClientParam } from './shared';
import './social.css';

const STATE_TONES: Record<Profile['connectionState'], Tone> = {
  Connected: 'success',
  Error: 'danger',
  AppCredentialsRequired: 'warning',
  Disconnected: 'neutral',
  NotConnected: 'neutral',
};

const STATE_LABELS: Record<Profile['connectionState'], string> = {
  Connected: 'Connected',
  Error: 'Error — reconnect',
  AppCredentialsRequired: 'App credentials required',
  Disconnected: 'Disconnected',
  NotConnected: 'Not connected',
};

const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

/** Brand profiles & connections, queue settings and the client's approval setting. */
export function ProfilesPage() {
  const [clientId, setClientId] = useClientParam();
  const { hasPermission } = useAuth();
  const canPublish = hasPermission(Permissions.SocialPublish);
  const canTokens = hasPermission(Permissions.IntegrationsManage);
  const [showArchived, setShowArchived] = useState(false);
  const active = useProfiles(clientId);
  const all = useQuery({
    queryKey: [...socialKeys.profiles(clientId ?? ''), 'all'],
    queryFn: () => api.get<Profile[]>(`/agency/social/clients/${clientId}/profiles`, { query: { includeArchived: true } }),
    enabled: !!clientId && showArchived,
  });
  const profiles = showArchived ? all : active;
  const [editing, setEditing] = useState<Profile | null>(null);
  const [archiving, setArchiving] = useState<Profile | null>(null);
  const toast = useToast();
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [queueFor, setQueueFor] = useState<Profile | null>(null);
  const [tokenFor, setTokenFor] = useState<Profile | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: socialKeys.profiles(clientId ?? '') });
  const restore = useMutation({
    mutationFn: (p: Profile) => api.post(`/agency/social/profiles/${p.id}/restore`),
    onSuccess: () => {
      toast.success('Profile restored');
      void refresh();
    },
    onError: (e) => toast.error('Could not restore', errorMessage(e)),
  });

  const connect = useMutation({
    mutationFn: (p: Profile) => api.post<{ authorizationUrl: string }>(`/agency/social/profiles/${p.id}/connect/start`),
    onSuccess: (res) => window.location.assign(res.authorizationUrl),
    onError: (e) => toast.error('Cannot start the connection', errorMessage(e)),
  });
  const disconnect = useMutation({
    mutationFn: (p: Profile) => api.post<Profile>(`/agency/social/profiles/${p.id}/disconnect`),
    onSuccess: () => {
      toast.success('Profile disconnected');
      void refresh();
    },
    onError: (e) => toast.error('Could not disconnect', errorMessage(e)),
  });

  const columns: DataTableColumn<Profile>[] = [
    {
      id: 'profile',
      header: 'Profile',
      primary: true,
      cell: (p) => (
        <span className="cluster">
          <NetworkChip network={p.network} />
          <span className="stack" style={{ gap: 0 }}>
            <strong>{p.displayName}</strong>
            <span className="sm-muted">
              @{p.handle}
              {p.profileUrl && (
                <>
                  {' · '}
                  <SafeExternalLink href={p.profileUrl}>open</SafeExternalLink>
                </>
              )}
            </span>
          </span>
        </span>
      ),
    },
    {
      id: 'state',
      header: 'Connection',
      cell: (p) => (
        <span className="stack" style={{ gap: 2 }}>
          <Badge tone={STATE_TONES[p.connectionState]} dot>
            {STATE_LABELS[p.connectionState]}
          </Badge>
          {p.statusMessage && <span className="sm-muted">{p.statusMessage}</span>}
          {!p.publishingSupported && <span className="sm-muted">No publishing adapter: publish manually.</span>}
        </span>
      ),
    },
    {
      id: 'queue',
      header: 'Queue',
      hideOnMobile: true,
      cell: (p) => (p.queueSlots.length === 0 ? '—' : p.queueSlots.map((s) => `${s.day.slice(0, 3)} ${s.time}`).join(', ')),
    },
    {
      id: 'connected',
      header: 'Connected',
      hideOnMobile: true,
      cell: (p) => (p.connectedAt ? <DateTime value={p.connectedAt} format="date" /> : '—'),
    },
  ];

  const menu = (p: Profile): MenuEntry[] => [
    ...(canPublish
      ? [
          {
            id: 'connect',
            label: p.connectionState === 'AppCredentialsRequired' ? 'Connect (app credentials required)' : 'Connect with OAuth',
            icon: <Plug />,
            disabled: p.connectionState === 'AppCredentialsRequired',
            onSelect: () => connect.mutate(p),
          } satisfies MenuEntry,
          { id: 'queue', label: 'Queue slots…', icon: <Link2 />, onSelect: () => setQueueFor(p) } satisfies MenuEntry,
        ]
      : []),
    ...(canTokens ? [{ id: 'token', label: 'Paste an access token…', icon: <Plug />, onSelect: () => setTokenFor(p) } satisfies MenuEntry] : []),
    { id: 'edit', label: 'Edit details…', icon: <Pencil />, onSelect: () => setEditing(p) } satisfies MenuEntry,
    ...(canPublish && p.connectionState === 'Connected'
      ? [{ id: 'disconnect', label: 'Disconnect', icon: <Unplug />, danger: true, onSelect: () => disconnect.mutate(p) } satisfies MenuEntry]
      : []),
    ...(canPublish
      ? [
          p.isActive
            ? ({ id: 'archive', label: 'Archive profile', icon: <Archive />, danger: true, onSelect: () => setArchiving(p) } satisfies MenuEntry)
            : ({ id: 'restore', label: 'Restore profile', icon: <ArchiveRestore />, onSelect: () => restore.mutate(p) } satisfies MenuEntry),
        ]
      : [
          {
            id: 'archive',
            label: 'Archive profile',
            icon: <Archive />,
            disabled: true,
            description: 'Archiving needs the social.publish permission.',
          } satisfies MenuEntry,
        ]),
  ];

  const missingCreds = (profiles.data ?? []).some((p) => p.connectionState === 'AppCredentialsRequired');

  return (
    <>
      <PageHeader
        title="Profiles & connections"
        description="Each client's brand profiles, how they connect, queue slots for scheduling and the client approval setting."
        actions={
          clientId && (
            <Button leadingIcon={<Plus />} onClick={() => setAdding(true)}>
              Add profile
            </Button>
          )
        }
      />
      <div className="sm-toolbar">
        <ClientPicker value={clientId} onChange={setClientId} />
        {clientId && <Checkbox label="Show archived profiles" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} />}
      </div>
      {!clientId ? (
        <EmptyState icon={<Plug />} title="Choose a client" description="Profiles belong to a client." />
      ) : profiles.isError ? (
        <ErrorState error={profiles.error} onRetry={() => void profiles.refetch()} />
      ) : (
        <div className="stack">
          {missingCreds && (
            <Alert tone="warning" title="App credentials required">
              Some networks have no developer app configured. An administrator adds the app id and secret under Integrations; until then publish
              manually and use “Mark as published”.
            </Alert>
          )}
          <DataTable
            caption="Brand profiles"
            columns={columns}
            rows={profiles.data ?? []}
            getRowId={(p) => p.id}
            rowLabel={(p) => `${NETWORK_LABELS[p.network]} @${p.handle}`}
            rowActions={menu}
            loading={profiles.isLoading}
            emptyState={<EmptyState icon={<Plug />} headingLevel={2} title="No profiles yet" description="Add the client's Facebook Page, Instagram account and others." />}
          />
          <ClientSettingsCard clientId={clientId} canEdit={canPublish} />
        </div>
      )}
      {adding && clientId && (
        <AddProfileDialog
          clientId={clientId}
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false);
            void refresh();
          }}
        />
      )}
      {queueFor && <QueueDialog profile={queueFor} onClose={() => setQueueFor(null)} onSaved={() => void refresh()} />}
      {tokenFor && <TokenDialog profile={tokenFor} onClose={() => setTokenFor(null)} onSaved={() => void refresh()} />}
      {editing && <EditProfileDialog profile={editing} onClose={() => setEditing(null)} onSaved={() => void refresh()} />}
      <ConfirmDialog
        open={archiving !== null}
        onClose={() => setArchiving(null)}
        tone="danger"
        title="Archive this profile?"
        description="It disappears from the composer and queue; its posts and metrics are kept for reporting. You can restore it later."
        confirmLabel="Archive"
        onConfirm={async () => {
          if (!archiving) return;
          await api.delete(`/agency/social/profiles/${archiving.id}`);
          toast.success('Profile archived');
          void refresh();
        }}
      />
    </>
  );
}

export function EditProfileDialog({ profile, onClose, onSaved }: { profile: Profile; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    handle: profile.handle,
    displayName: profile.displayName,
    profileUrl: profile.profileUrl ?? '',
    avatarUrl: profile.avatarUrl ?? '',
    externalId: profile.externalId ?? '',
  });
  const save = useMutation({
    mutationFn: () =>
      api.put<Profile>(`/agency/social/profiles/${profile.id}`, {
        network: profile.network,
        handle: form.handle,
        displayName: form.displayName,
        profileUrl: form.profileUrl || null,
        avatarUrl: form.avatarUrl || null,
        externalId: form.externalId || null,
        concurrencyStamp: profile.concurrencyStamp,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Edit ${NETWORK_LABELS[profile.network]} profile`}
      description="The network cannot change; add a new profile for another network."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={save.isPending} disabled={!form.handle || !form.displayName} onClick={() => save.mutate()}>
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && <Alert tone="danger">{errorMessage(save.error)}</Alert>}
        <FormField label="Handle" required>
          <Input value={form.handle} maxLength={150} onChange={(e) => setForm({ ...form, handle: e.target.value })} />
        </FormField>
        <FormField label="Display name" required>
          <Input value={form.displayName} maxLength={200} onChange={(e) => setForm({ ...form, displayName: e.target.value })} />
        </FormField>
        <FormField label="Profile URL" optional>
          <Input type="url" value={form.profileUrl} onChange={(e) => setForm({ ...form, profileUrl: e.target.value })} />
        </FormField>
        <FormField label="Avatar URL" optional>
          <Input type="url" value={form.avatarUrl} onChange={(e) => setForm({ ...form, avatarUrl: e.target.value })} />
        </FormField>
        <FormField
          label="Account id"
          optional
          hint={profile.connectionState === 'Connected' ? 'Changing the account id of a connected profile needs the integrations.manage permission.' : undefined}
        >
          <Input value={form.externalId} maxLength={100} onChange={(e) => setForm({ ...form, externalId: e.target.value })} />
        </FormField>
      </div>
    </Dialog>
  );
}

function ClientSettingsCard({ clientId, canEdit }: { clientId: string; canEdit: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const settings = useQuery({
    queryKey: socialKeys.settings(clientId),
    queryFn: () => api.get<SocialSettings>(`/agency/social/clients/${clientId}/settings`),
  });
  const save = useMutation({
    mutationFn: (s: SocialSettings) => api.put<SocialSettings>(`/agency/social/clients/${clientId}/settings`, s),
    onSuccess: () => {
      toast.success('Settings saved');
      void queryClient.invalidateQueries({ queryKey: socialKeys.settings(clientId) });
    },
    onError: (e) => toast.error('Settings not saved', errorMessage(e)),
  });
  return (
    <Card as="section" aria-labelledby="sm-client-settings">
      <CardHeader title="Client settings" titleId="sm-client-settings" />
      <CardBody className="stack">
        {settings.data && (
          <UtmMediumField
            key={settings.data.concurrencyStamp}
            value={settings.data.defaultUtmMedium}
            disabled={!canEdit || save.isPending}
            onSave={(medium) => save.mutate({ ...settings.data!, defaultUtmMedium: medium })}
          />
        )}
        {settings.data ? (
          <Switch
            checked={settings.data.requireClientApproval}
            disabled={!canEdit || save.isPending}
            onCheckedChange={(checked) => save.mutate({ ...settings.data!, requireClientApproval: checked })}
            label="Require client approval"
            description="Approved posts go to the client portal (Approver or Owner) before they can be scheduled."
          />
        ) : (
          <Spinner label="Loading settings" />
        )}
      </CardBody>
    </Card>
  );
}

function UtmMediumField({ value, disabled, onSave }: { value: string; disabled: boolean; onSave: (value: string) => void }) {
  const [medium, setMedium] = useState(value);
  return (
    <div className="cluster">
      <FormField label="Default utm_medium" hint="Used when a post's campaign has no utm_medium.">
        <Input value={medium} maxLength={100} disabled={disabled} onChange={(e) => setMedium(e.target.value)} />
      </FormField>
      <Button variant="secondary" size="sm" disabled={disabled || !medium.trim() || medium === value} onClick={() => onSave(medium.trim())}>
        Save
      </Button>
    </div>
  );
}

function AddProfileDialog({ clientId, onClose, onSaved }: { clientId: string; onClose: () => void; onSaved: () => void }) {
  const [network, setNetwork] = useState<SocialNetwork>('Instagram');
  const [handle, setHandle] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [profileUrl, setProfileUrl] = useState('');
  const [externalId, setExternalId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () =>
      api.post<Profile>(`/agency/social/clients/${clientId}/profiles`, {
        network,
        handle,
        displayName,
        profileUrl: profileUrl || null,
        externalId: externalId || null,
      }),
    onSuccess: onSaved,
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Add brand profile"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={save.isPending} onClick={() => save.mutate()} disabled={!handle || !displayName}>
            Add profile
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Network" required>
          <Select value={network} onChange={(e) => setNetwork(e.target.value as SocialNetwork)} options={NETWORKS.map((n) => ({ value: n, label: NETWORK_LABELS[n] }))} />
        </FormField>
        <FormField label="Handle" required>
          <Input value={handle} onChange={(e) => setHandle(e.target.value)} />
        </FormField>
        <FormField label="Display name" required>
          <Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
        </FormField>
        <FormField label="Profile URL" optional hint="https only">
          <Input type="url" value={profileUrl} onChange={(e) => setProfileUrl(e.target.value)} />
        </FormField>
        <FormField label="Platform id" optional hint="Facebook Page id or Instagram business account id; filled automatically on connect.">
          <Input value={externalId} onChange={(e) => setExternalId(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

function QueueDialog({ profile, onClose, onSaved }: { profile: Profile; onClose: () => void; onSaved: () => void }) {
  const [slots, setSlots] = useState(profile.queueSlots.map((s) => ({ day: s.day, time: s.time })));
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => api.put<Profile>(`/agency/social/profiles/${profile.id}/queue-slots`, { slots }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Queue slots — ${NETWORK_LABELS[profile.network]} @${profile.handle}`}
      description="Weekly times (client's time zone) used by “Add to queue”."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={save.isPending} onClick={() => save.mutate()}>
            Save slots
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        {slots.map((s, i) => (
          <div key={i} className="cluster">
            <FormField label={`Slot ${i + 1} day`}>
              <Select value={s.day} onChange={(e) => setSlots((all) => all.map((x, j) => (j === i ? { ...x, day: e.target.value } : x)))} options={DAYS.map((d) => ({ value: d, label: d }))} />
            </FormField>
            <FormField label={`Slot ${i + 1} time`}>
              <Input type="time" value={s.time} onChange={(e) => setSlots((all) => all.map((x, j) => (j === i ? { ...x, time: e.target.value } : x)))} />
            </FormField>
            <Button variant="ghost" size="sm" onClick={() => setSlots((all) => all.filter((_, j) => j !== i))}>
              Remove slot {i + 1}
            </Button>
          </div>
        ))}
        <div>
          <Button variant="secondary" size="sm" leadingIcon={<Plus />} onClick={() => setSlots((all) => [...all, { day: 'Monday', time: '09:00' }])}>
            Add slot
          </Button>
        </div>
      </div>
    </Dialog>
  );
}

function TokenDialog({ profile, onClose, onSaved }: { profile: Profile; onClose: () => void; onSaved: () => void }) {
  const [token, setToken] = useState('');
  const [externalId, setExternalId] = useState(profile.externalId ?? '');
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => api.post<Profile>(`/agency/social/profiles/${profile.id}/token`, { accessToken: token, externalId: externalId || null }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Paste an access token"
      description="Stored encrypted; never shown again. Use a long-lived Page or system-user token."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={save.isPending} disabled={token.length < 10} onClick={() => save.mutate()}>
            Save token
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Access token" required>
          <Input type="password" autoComplete="off" value={token} onChange={(e) => setToken(e.target.value)} />
        </FormField>
        <FormField label="Platform id" hint="Page id / Instagram business account id">
          <Input value={externalId} onChange={(e) => setExternalId(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

/** OAuth redirect target: forwards the provider's code and state to the API (which verifies the signed state). */
export function ConnectCallbackPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const sent = useRef(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    if (sent.current) return;
    sent.current = true;
    api
      .post<Profile>('/agency/social/oauth/callback', {
        code: params.get('code'),
        state: params.get('state') ?? '',
        error: params.get('error_description') ?? params.get('error'),
      })
      .then((profile) => navigate(`/agency/social/profiles?client=${profile.clientAccountId}`, { replace: true }))
      .catch((e: unknown) => setError(errorMessage(e)));
  }, [navigate, params]);
  return (
    <>
      <PageHeader title="Connecting profile" />
      {error ? (
        <Alert tone="danger" title="The profile was not connected">
          {error}
        </Alert>
      ) : (
        <Spinner label="Finishing the connection…" />
      )}
    </>
  );
}
