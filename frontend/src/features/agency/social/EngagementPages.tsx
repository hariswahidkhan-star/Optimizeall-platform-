import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ear, Inbox, Pause, Pencil, Play, Plus, RefreshCw, Trash2, Trophy } from 'lucide-react';
import { useState } from 'react';
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
  LineChart,
  PageHeader,
  Pagination,
  Select,
  Stat,
  Textarea,
  useToast,
  type DataTableColumn,
  type MenuEntry,
  type Tone,
} from '@/components/ui';
import { SafeExternalLink } from '@/components/SafeExternalLink';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { formatNumber } from '@/lib/format/money';
import {
  NETWORKS,
  NETWORK_LABELS,
  socialKeys,
  useProfiles,
  type Competitor,
  type InboxItem,
  type ListeningQuery,
  type Mention,
  type Sentiment,
  type SocialNetwork,
} from './api';
import { ClientPicker, NetworkChip, SourceLabel, percent, useClientParam } from './shared';
import './social.css';

const SENTIMENT_TONES: Record<Sentiment, Tone> = { Positive: 'success', Neutral: 'neutral', Negative: 'danger' };
const networkOptions = NETWORKS.map((n) => ({ value: n, label: NETWORK_LABELS[n] }));

function SentimentBadge({ sentiment, automatic }: { sentiment: Sentiment | null; automatic?: boolean }) {
  if (!sentiment) return <Badge>Untagged</Badge>;
  return (
    <Badge tone={SENTIMENT_TONES[sentiment]} title={automatic ? 'Automatic estimate from a word list; tag it manually to confirm.' : 'Tagged by a person'}>
      {sentiment}
      {automatic ? ' (automatic estimate)' : ''}
    </Badge>
  );
}

// ---------------------------------------------------------------- listening

export function ListeningPage() {
  const [clientId, setClientId] = useClientParam();
  const [sentiment, setSentiment] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [logging, setLogging] = useState(false);
  const toast = useToast();
  const queryClient = useQueryClient();
  const queries = useQuery({
    queryKey: socialKeys.queries(clientId ?? ''),
    queryFn: () => api.get<ListeningQuery[]>(`/agency/social/clients/${clientId}/listening/queries`),
    enabled: !!clientId,
  });
  const params = { sentiment, page, pageSize: 25 };
  const mentions = useQuery({
    queryKey: socialKeys.mentions(clientId ?? '', params),
    queryFn: () => api.get<PagedResult<Mention>>(`/agency/social/clients/${clientId}/listening/mentions`, { query: params }),
    enabled: !!clientId,
    placeholderData: keepPreviousData,
  });
  const summary = useQuery({
    queryKey: ['social', 'mention-summary', clientId],
    queryFn: () =>
      api.get<{ total: number; positive: number; neutral: number; negative: number; untagged: number; automatic: number }>(
        `/agency/social/clients/${clientId}/listening/summary`,
      ),
    enabled: !!clientId,
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['social'] });

  const [kind, setKind] = useState('Keyword');
  const [term, setTerm] = useState('');
  const addQuery = useMutation({
    mutationFn: () => api.post(`/agency/social/clients/${clientId}/listening/queries`, { kind, term }),
    onSuccess: () => {
      setTerm('');
      void refresh();
    },
    onError: (e) => toast.error('Not added', errorMessage(e)),
  });
  const sync = useMutation({
    mutationFn: () => api.post<{ configured: boolean; imported: number; message: string }>(`/agency/social/clients/${clientId}/listening/sync`),
    onSuccess: (r) => (r.configured ? toast.success(`Imported ${r.imported} mentions`) : toast.info('Listening provider not configured', r.message)),
  });
  const tag = useMutation({
    mutationFn: ({ id, value }: { id: string; value: Sentiment }) => api.put(`/agency/social/listening/mentions/${id}/sentiment`, { sentiment: value }),
    onSuccess: () => void refresh(),
  });
  const [editingQuery, setEditingQuery] = useState<ListeningQuery | null>(null);
  const [deletingQuery, setDeletingQuery] = useState<ListeningQuery | null>(null);
  const [deletingMention, setDeletingMention] = useState<Mention | null>(null);
  const toggleQuery = useMutation({
    mutationFn: (q: ListeningQuery) =>
      api.put<ListeningQuery>(`/agency/social/listening/queries/${q.id}`, { term: q.term, networks: q.networks, isActive: !q.isActive, concurrencyStamp: q.concurrencyStamp }),
    onSuccess: (q) => {
      toast.success(q.isActive ? 'Tracking resumed' : 'Tracking paused', q.term);
      void refresh();
    },
    onError: (e) => toast.error('Not changed', errorMessage(e)),
  });

  const columns: DataTableColumn<Mention>[] = [
    {
      id: 'mention',
      header: 'Mention',
      primary: true,
      cell: (m) => (
        <span className="stack" style={{ gap: 2 }}>
          <span>
            <NetworkChip network={m.network} /> <strong>@{m.authorHandle}</strong>
          </span>
          <span>{m.text}</span>
          {m.url && <SafeExternalLink href={m.url}>Open</SafeExternalLink>}
        </span>
      ),
    },
    { id: 'when', header: 'Posted', cell: (m) => <DateTime value={m.postedAt} format="relative" /> },
    { id: 'sentiment', header: 'Sentiment', cell: (m) => <SentimentBadge sentiment={m.sentiment} automatic={m.sentimentSource === 'Automatic'} /> },
    { id: 'source', header: 'Source', hideOnMobile: true, cell: (m) => (m.source === 'Api' ? 'Provider' : 'Logged manually') },
  ];
  const menu = (m: Mention): MenuEntry[] => [
    ...(['Positive', 'Neutral', 'Negative'] as Sentiment[]).map((s) => ({
      id: s,
      label: `Tag as ${s.toLowerCase()}`,
      onSelect: () => tag.mutate({ id: m.id, value: s }),
    })),
    { id: 'delete', label: 'Delete mention', icon: <Trash2 />, danger: true, onSelect: () => setDeletingMention(m) },
  ];

  return (
    <>
      <PageHeader
        title="Social listening"
        description="Track keywords, hashtags and competitor handles. Sentiment marked “automatic estimate” comes from a simple word list; tag it to confirm."
        actions={
          clientId && (
            <div className="cluster">
              <Button variant="secondary" leadingIcon={<RefreshCw />} onClick={() => sync.mutate()} loading={sync.isPending}>
                Sync from provider
              </Button>
              <Button leadingIcon={<Plus />} onClick={() => setLogging(true)}>
                Log a mention
              </Button>
            </div>
          )
        }
      />
      <div className="sm-toolbar">
        <ClientPicker value={clientId} onChange={setClientId} />
      </div>
      {!clientId ? (
        <EmptyState icon={<Ear />} title="Choose a client" />
      ) : (
        <div className="stack">
          {summary.data && (
            <div className="sm-grid-stats">
              <Stat label="Mentions (30 days)" value={formatNumber(summary.data.total)} measurement="Count" />
              <Stat label="Positive" value={formatNumber(summary.data.positive)} measurement="Count" />
              <Stat label="Negative" value={formatNumber(summary.data.negative)} measurement="Count" />
              <Stat label="Automatic estimates" value={formatNumber(summary.data.automatic)} measurement="Estimated" />
            </div>
          )}
          <Card as="section" aria-labelledby="sm-queries">
            <CardHeader title="Tracked terms" titleId="sm-queries" />
            <CardBody className="stack">
              <ul className="stack" style={{ listStyle: 'none', padding: 0, margin: 0 }}>
                {(queries.data ?? []).map((q) => (
                  <li key={q.id} className="cluster">
                    <Badge tone={q.isActive ? 'brand' : 'neutral'}>{q.kind === 'Keyword' ? `“${q.term}”` : q.term}</Badge>
                    {!q.isActive && <span className="sm-muted">paused</span>}
                    <IconButton size="sm" label={`Edit ${q.term}`} icon={<Pencil />} onClick={() => setEditingQuery(q)} />
                    <IconButton
                      size="sm"
                      label={q.isActive ? `Pause ${q.term}` : `Resume ${q.term}`}
                      icon={q.isActive ? <Pause /> : <Play />}
                      onClick={() => toggleQuery.mutate(q)}
                    />
                    <IconButton size="sm" label={`Stop tracking ${q.term}`} icon={<Trash2 />} onClick={() => setDeletingQuery(q)} />
                  </li>
                ))}
                {(queries.data?.length ?? 0) === 0 && <li className="sm-muted">No tracked terms yet.</li>}
              </ul>
              <div className="cluster">
                <FormField label="Type">
                  <Select
                    value={kind}
                    onChange={(e) => setKind(e.target.value)}
                    options={[
                      { value: 'Keyword', label: 'Keyword' },
                      { value: 'Hashtag', label: 'Hashtag' },
                      { value: 'CompetitorHandle', label: 'Competitor handle' },
                    ]}
                  />
                </FormField>
                <FormField label="Term">
                  <Input value={term} onChange={(e) => setTerm(e.target.value)} />
                </FormField>
                <Button variant="secondary" disabled={term.trim().length < 2} onClick={() => addQuery.mutate()}>
                  Track
                </Button>
              </div>
            </CardBody>
          </Card>
          <FormField label="Sentiment filter">
            <Select
              value={sentiment ?? ''}
              placeholder="All"
              onChange={(e) => {
                setSentiment(e.target.value || undefined);
                setPage(1);
              }}
              options={['Positive', 'Neutral', 'Negative'].map((s) => ({ value: s, label: s }))}
            />
          </FormField>
          {mentions.isError ? (
            <ErrorState error={mentions.error} />
          ) : (
            <>
              <DataTable
                caption="Mentions"
                columns={columns}
                rows={mentions.data?.items ?? []}
                getRowId={(m) => m.id}
                rowActions={menu}
                rowLabel={(m) => `Mention by @${m.authorHandle}`}
                loading={mentions.isLoading}
                emptyState={<EmptyState compact icon={<Ear />} headingLevel={3} title="No mentions logged" />}
              />
              {mentions.data && mentions.data.total > 25 && <Pagination page={page} pageSize={25} total={mentions.data.total} onPageChange={setPage} />}
            </>
          )}
        </div>
      )}
      {logging && clientId && <LogMentionDialog clientId={clientId} onClose={() => setLogging(false)} onSaved={() => void refresh()} />}
      {editingQuery && <QueryDialog query={editingQuery} onClose={() => setEditingQuery(null)} onSaved={() => void refresh()} />}
      <ConfirmDialog
        open={deletingQuery !== null}
        onClose={() => setDeletingQuery(null)}
        tone="danger"
        title="Stop tracking this term?"
        description="Mentions already logged are kept. Pause it instead to stop syncing temporarily."
        confirmLabel="Stop tracking"
        onConfirm={async () => {
          if (!deletingQuery) return;
          await api.delete(`/agency/social/listening/queries/${deletingQuery.id}`);
          void refresh();
        }}
      />
      <ConfirmDialog
        open={deletingMention !== null}
        onClose={() => setDeletingMention(null)}
        tone="danger"
        title="Delete this mention?"
        description="It is removed from the mention log and the sentiment summary."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deletingMention) return;
          await api.delete(`/agency/social/listening/mentions/${deletingMention.id}`);
          void refresh();
        }}
      />
    </>
  );
}

export function QueryDialog({ query, onClose, onSaved }: { query: ListeningQuery; onClose: () => void; onSaved: () => void }) {
  const [term, setTerm] = useState(query.term);
  const [networks, setNetworks] = useState<SocialNetwork[]>(query.networks);
  const [isActive, setIsActive] = useState(query.isActive);
  const save = useMutation({
    mutationFn: () => api.put(`/agency/social/listening/queries/${query.id}`, { term, networks, isActive, concurrencyStamp: query.concurrencyStamp }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Edit tracked term"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={save.isPending} disabled={term.trim().length < 2} onClick={() => save.mutate()}>
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && <Alert tone="danger">{errorMessage(save.error)}</Alert>}
        <FormField label="Term" hint={query.kind === 'Hashtag' ? 'Saved as a hashtag.' : query.kind === 'CompetitorHandle' ? 'Saved as an @handle.' : undefined}>
          <Input value={term} maxLength={150} onChange={(e) => setTerm(e.target.value)} />
        </FormField>
        <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend>Networks (none = all)</legend>
          <div className="cluster">
            {NETWORKS.map((n) => (
              <Checkbox
                key={n}
                label={NETWORK_LABELS[n]}
                checked={networks.includes(n)}
                onChange={(e) => setNetworks(e.target.checked ? [...networks, n] : networks.filter((x) => x !== n))}
              />
            ))}
          </div>
        </fieldset>
        <Checkbox label="Active (included in syncs)" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
      </div>
    </Dialog>
  );
}

function LogMentionDialog({ clientId, onClose, onSaved }: { clientId: string; onClose: () => void; onSaved: () => void }) {
  const [network, setNetwork] = useState<SocialNetwork>('Instagram');
  const [author, setAuthor] = useState('');
  const [text, setText] = useState('');
  const [url, setUrl] = useState('');
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () =>
      api.post(`/agency/social/clients/${clientId}/listening/mentions`, {
        network,
        authorHandle: author,
        text,
        url: url || null,
        postedAt: new Date().toISOString(),
        autoSentiment: true,
      }),
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
      title="Log a mention"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={save.isPending} disabled={!author || !text} onClick={() => save.mutate()}>
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Network">
          <Select value={network} onChange={(e) => setNetwork(e.target.value as SocialNetwork)} options={networkOptions} />
        </FormField>
        <FormField label="Author handle">
          <Input value={author} onChange={(e) => setAuthor(e.target.value)} />
        </FormField>
        <FormField label="Text">
          <Textarea rows={3} value={text} onChange={(e) => setText(e.target.value)} />
        </FormField>
        <FormField label="Link" optional>
          <Input type="url" value={url} onChange={(e) => setUrl(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

// ---------------------------------------------------------------- inbox

export function InboxPage() {
  const [clientId, setClientId] = useClientParam();
  const [status, setStatus] = useState<string | undefined>('Open');
  const [page, setPage] = useState(1);
  const [replying, setReplying] = useState<InboxItem | null>(null);
  const [logging, setLogging] = useState(false);
  const toast = useToast();
  const queryClient = useQueryClient();
  const profiles = useProfiles(clientId);
  const params = { status, page, pageSize: 25 };
  const items = useQuery({
    queryKey: socialKeys.inbox(clientId ?? '', params),
    queryFn: () => api.get<PagedResult<InboxItem>>(`/agency/social/clients/${clientId}/inbox`, { query: params }),
    enabled: !!clientId,
    placeholderData: keepPreviousData,
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['social', 'inbox'] });
  const patch = useMutation({
    mutationFn: ({ id, body }: { id: string; body: Record<string, unknown> }) => api.patch(`/agency/social/inbox/${id}`, body),
    onSuccess: () => void refresh(),
    onError: (e) => toast.error('Not updated', errorMessage(e)),
  });
  const sync = useMutation({
    mutationFn: () => api.post<{ configured: boolean; imported: number; message: string }>(`/agency/social/clients/${clientId}/inbox/sync`),
    onSuccess: (r) => (r.configured ? toast.success(r.message) : toast.info('Inbox sync not configured', r.message)),
  });

  const columns: DataTableColumn<InboxItem>[] = [
    {
      id: 'message',
      header: 'Message',
      primary: true,
      cell: (i) => (
        <span className="stack" style={{ gap: 2 }}>
          <span>
            <NetworkChip network={i.network} /> <strong>@{i.authorHandle}</strong> · {i.kind === 'DirectMessage' ? 'Direct message' : i.kind}
          </span>
          <span>{i.text}</span>
          {i.replies.map((r) => (
            <span key={r.id} className="sm-comment sm-muted">
              ↳ {r.byName}: {r.body} {r.sentViaApi ? '(sent via API)' : '(logged; sent by hand)'}
            </span>
          ))}
        </span>
      ),
    },
    { id: 'received', header: 'Received', cell: (i) => <DateTime value={i.receivedAt} format="relative" /> },
    { id: 'status', header: 'Status', cell: (i) => <Badge tone={i.status === 'Open' ? 'warning' : i.status === 'Replied' ? 'success' : 'neutral'}>{i.status}</Badge> },
    { id: 'assigned', header: 'Assigned', hideOnMobile: true, cell: (i) => i.assignedToName ?? '—' },
    { id: 'sentiment', header: 'Sentiment', hideOnMobile: true, cell: (i) => <SentimentBadge sentiment={i.sentiment} /> },
  ];
  const menu = (i: InboxItem): MenuEntry[] => [
    { id: 'reply', label: 'Reply…', onSelect: () => setReplying(i) },
    { id: 'close', label: 'Close', onSelect: () => patch.mutate({ id: i.id, body: { status: 'Closed', concurrencyStamp: i.concurrencyStamp } }) },
    { id: 'reopen', label: 'Reopen', onSelect: () => patch.mutate({ id: i.id, body: { status: 'Open', concurrencyStamp: i.concurrencyStamp } }) },
  ];

  return (
    <>
      <PageHeader
        title="Social inbox"
        description="Comments and direct messages. Without a connected inbox provider, log messages and replies by hand."
        actions={
          clientId && (
            <div className="cluster">
              <Button variant="secondary" leadingIcon={<RefreshCw />} onClick={() => sync.mutate()}>
                Sync
              </Button>
              <Button leadingIcon={<Plus />} onClick={() => setLogging(true)}>
                Log a message
              </Button>
            </div>
          )
        }
      />
      <div className="sm-toolbar">
        <ClientPicker value={clientId} onChange={setClientId} />
        <FormField label="Status">
          <Select
            value={status ?? ''}
            placeholder="All"
            onChange={(e) => {
              setStatus(e.target.value || undefined);
              setPage(1);
            }}
            options={['Open', 'Assigned', 'Replied', 'Closed'].map((s) => ({ value: s, label: s }))}
          />
        </FormField>
      </div>
      {!clientId ? (
        <EmptyState icon={<Inbox />} title="Choose a client" />
      ) : items.isError ? (
        <ErrorState error={items.error} />
      ) : (
        <>
          <DataTable
            caption="Inbox"
            columns={columns}
            rows={items.data?.items ?? []}
            getRowId={(i) => i.id}
            rowActions={menu}
            rowLabel={(i) => `Message from @${i.authorHandle}`}
            loading={items.isLoading}
            emptyState={<EmptyState compact icon={<Inbox />} headingLevel={3} title="Inbox zero" />}
          />
          {items.data && items.data.total > 25 && <Pagination page={page} pageSize={25} total={items.data.total} onPageChange={setPage} />}
        </>
      )}
      {replying && <ReplyDialog item={replying} onClose={() => setReplying(null)} onSaved={() => void refresh()} />}
      {logging && clientId && (
        <LogInboxDialog
          clientId={clientId}
          profiles={(profiles.data ?? []).map((p) => ({ value: p.id, label: `${NETWORK_LABELS[p.network]} @${p.handle}`, network: p.network }))}
          onClose={() => setLogging(false)}
          onSaved={() => void refresh()}
        />
      )}
    </>
  );
}

function ReplyDialog({ item, onClose, onSaved }: { item: InboxItem; onClose: () => void; onSaved: () => void }) {
  const [body, setBody] = useState('');
  const [error, setError] = useState<string | null>(null);
  const send = useMutation({
    mutationFn: (viaApi: boolean) => api.post(`/agency/social/inbox/${item.id}/replies`, { body, sendViaApi: viaApi }),
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
      title={`Reply to @${item.authorHandle}`}
      description={item.text}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button variant="secondary" disabled={!body.trim()} onClick={() => send.mutate(true)}>
            Send via API
          </Button>
          <Button disabled={!body.trim()} loading={send.isPending} onClick={() => send.mutate(false)}>
            Log reply sent by hand
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="warning">{error}</Alert>}
        <FormField label="Reply">
          <Textarea rows={4} value={body} onChange={(e) => setBody(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

function LogInboxDialog({
  clientId,
  profiles,
  onClose,
  onSaved,
}: {
  clientId: string;
  profiles: { value: string; label: string; network: SocialNetwork }[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [profileId, setProfileId] = useState(profiles[0]?.value ?? '');
  const [kind, setKind] = useState('Comment');
  const [author, setAuthor] = useState('');
  const [text, setText] = useState('');
  const [error, setError] = useState<string | null>(null);
  const profile = profiles.find((p) => p.value === profileId);
  const save = useMutation({
    mutationFn: () =>
      api.post(`/agency/social/clients/${clientId}/inbox`, {
        profileId: profileId || null,
        network: profile?.network ?? 'Instagram',
        kind,
        authorHandle: author,
        text,
      }),
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
      title="Log a comment or message"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={!author || !text} loading={save.isPending} onClick={() => save.mutate()}>
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Profile">
          <Select value={profileId} onChange={(e) => setProfileId(e.target.value)} options={profiles} />
        </FormField>
        <FormField label="Type">
          <Select
            value={kind}
            onChange={(e) => setKind(e.target.value)}
            options={[
              { value: 'Comment', label: 'Comment' },
              { value: 'DirectMessage', label: 'Direct message' },
              { value: 'Mention', label: 'Mention' },
            ]}
          />
        </FormField>
        <FormField label="From (handle)">
          <Input value={author} onChange={(e) => setAuthor(e.target.value)} />
        </FormField>
        <FormField label="Message">
          <Textarea rows={3} value={text} onChange={(e) => setText(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}

// ---------------------------------------------------------------- competitors

export function CompetitorsPage() {
  const [clientId, setClientId] = useClientParam();
  const queryClient = useQueryClient();
  const toast = useToast();
  const competitors = useQuery({
    queryKey: socialKeys.competitors(clientId ?? ''),
    queryFn: () => api.get<Competitor[]>(`/agency/social/clients/${clientId}/competitors`),
    enabled: !!clientId,
  });
  const [name, setName] = useState('');
  const [network, setNetwork] = useState<SocialNetwork>('Instagram');
  const [handle, setHandle] = useState('');
  const [snapshotFor, setSnapshotFor] = useState<Competitor | null>(null);
  const [editingCompetitor, setEditingCompetitor] = useState<Competitor | null>(null);
  const [historyFor, setHistoryFor] = useState<Competitor | null>(null);
  const [deletingCompetitor, setDeletingCompetitor] = useState<Competitor | null>(null);
  const refreshCompetitors = () => void queryClient.invalidateQueries({ queryKey: socialKeys.competitors(clientId ?? '') });
  const add = useMutation({
    mutationFn: () => api.post(`/agency/social/clients/${clientId}/competitors`, { name, network, handle }),
    onSuccess: () => {
      setName('');
      setHandle('');
      void queryClient.invalidateQueries({ queryKey: socialKeys.competitors(clientId ?? '') });
    },
    onError: (e) => toast.error('Not added', errorMessage(e)),
  });
  const list = competitors.data ?? [];
  const dates = [...new Set(list.flatMap((c) => c.snapshots.map((s) => s.date)))].sort();
  return (
    <>
      <PageHeader title="Competitor benchmarking" description="Follower and engagement snapshots of competitor profiles, entered manually or from exports." />
      <div className="sm-toolbar">
        <ClientPicker value={clientId} onChange={setClientId} />
      </div>
      {!clientId ? (
        <EmptyState icon={<Trophy />} title="Choose a client" />
      ) : (
        <div className="stack">
          {list.length > 0 && dates.length > 1 && (
            <LineChart
              title="Competitor followers"
              description="Followers over time per competitor (manual or imported snapshots)."
              labels={dates}
              series={list.slice(0, 3).map((c) => ({
                id: c.id,
                label: c.name,
                values: dates.map((d) => c.snapshots.find((s) => s.date === d)?.followers ?? 0),
              }))}
              valueFormatter={(v) => formatNumber(v)}
            />
          )}
          <DataTable
            caption="Competitors"
            columns={[
              { id: 'name', header: 'Competitor', primary: true, cell: (c: Competitor) => <span><NetworkChip network={c.network} /> {c.name} (@{c.handle})</span> },
              { id: 'followers', header: 'Followers', align: 'right', cell: (c: Competitor) => formatNumber(c.snapshots.at(-1)?.followers ?? 0) },
              { id: 'er', header: 'Engagement rate', align: 'right', cell: (c: Competitor) => percent(c.snapshots.at(-1)?.engagementRate) },
              { id: 'source', header: 'Source', cell: (c: Competitor) => (c.snapshots.at(-1) ? <SourceLabel label={c.snapshots.at(-1)!.sourceLabel} /> : '—') },
            ]}
            rows={list}
            getRowId={(c) => c.id}
            rowActions={(c) => [
              { id: 'snap', label: 'Record snapshot…', onSelect: () => setSnapshotFor(c) },
              { id: 'history', label: 'Snapshots…', disabled: c.snapshots.length === 0, onSelect: () => setHistoryFor(c) },
              { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditingCompetitor(c) },
              { id: 'delete', label: 'Stop tracking', icon: <Trash2 />, danger: true, onSelect: () => setDeletingCompetitor(c) },
            ]}
            rowLabel={(c) => c.name}
            loading={competitors.isLoading}
            emptyState={<EmptyState compact icon={<Trophy />} headingLevel={3} title="No competitors tracked" />}
          />
          <div className="cluster">
            <FormField label="Name">
              <Input value={name} onChange={(e) => setName(e.target.value)} />
            </FormField>
            <FormField label="Network">
              <Select value={network} onChange={(e) => setNetwork(e.target.value as SocialNetwork)} options={networkOptions} />
            </FormField>
            <FormField label="Handle">
              <Input value={handle} onChange={(e) => setHandle(e.target.value)} />
            </FormField>
            <Button leadingIcon={<Plus />} disabled={!name || !handle} onClick={() => add.mutate()}>
              Add competitor
            </Button>
          </div>
        </div>
      )}
      {snapshotFor && (
        <SnapshotDialog
          competitor={snapshotFor}
          onClose={() => setSnapshotFor(null)}
          onSaved={() => void queryClient.invalidateQueries({ queryKey: socialKeys.competitors(clientId ?? '') })}
        />
      )}
      {editingCompetitor && <CompetitorDialog competitor={editingCompetitor} onClose={() => setEditingCompetitor(null)} onSaved={refreshCompetitors} />}
      {historyFor && (
        <SnapshotHistoryDialog
          competitor={list.find((c) => c.id === historyFor.id) ?? historyFor}
          onClose={() => setHistoryFor(null)}
          onChanged={refreshCompetitors}
        />
      )}
      <ConfirmDialog
        open={deletingCompetitor !== null}
        onClose={() => setDeletingCompetitor(null)}
        tone="danger"
        title="Stop tracking this competitor?"
        description="The competitor and all of its snapshots are removed from benchmarking."
        confirmLabel="Stop tracking"
        onConfirm={async () => {
          if (!deletingCompetitor) return;
          await api.delete(`/agency/social/competitors/${deletingCompetitor.id}`);
          refreshCompetitors();
        }}
      />
    </>
  );
}

export function CompetitorDialog({ competitor, onClose, onSaved }: { competitor: Competitor; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ name: competitor.name, network: competitor.network, handle: competitor.handle, profileUrl: competitor.profileUrl ?? '' });
  const save = useMutation({
    mutationFn: () =>
      api.put(`/agency/social/competitors/${competitor.id}`, { ...form, profileUrl: form.profileUrl || null, concurrencyStamp: competitor.concurrencyStamp }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <Dialog
      open
      onClose={onClose}
      title="Edit competitor"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={save.isPending} disabled={!form.name || !form.handle} onClick={() => save.mutate()}>
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {save.isError && <Alert tone="danger">{errorMessage(save.error)}</Alert>}
        <FormField label="Name" required>
          <Input value={form.name} maxLength={200} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        </FormField>
        <FormField label="Network">
          <Select value={form.network} onChange={(e) => setForm({ ...form, network: e.target.value as SocialNetwork })} options={networkOptions} />
        </FormField>
        <FormField label="Handle" required>
          <Input value={form.handle} maxLength={150} onChange={(e) => setForm({ ...form, handle: e.target.value })} />
        </FormField>
        <FormField label="Profile URL" optional>
          <Input type="url" value={form.profileUrl} onChange={(e) => setForm({ ...form, profileUrl: e.target.value })} />
        </FormField>
      </div>
    </Dialog>
  );
}

function SnapshotHistoryDialog({ competitor, onClose, onChanged }: { competitor: Competitor; onClose: () => void; onChanged: () => void }) {
  const toast = useToast();
  const remove = useMutation({
    mutationFn: (id: string) => api.delete(`/agency/social/competitors/${competitor.id}/snapshots/${id}`),
    onSuccess: () => {
      toast.success('Snapshot deleted');
      onChanged();
    },
    onError: (e) => toast.error('Not deleted', errorMessage(e)),
  });
  return (
    <Dialog open onClose={onClose} title={`Snapshots — ${competitor.name}`}>
      {competitor.snapshots.length === 0 ? (
        <p className="sm-muted">No snapshots left.</p>
      ) : (
        <ul className="stack" style={{ listStyle: 'none', padding: 0, margin: 0 }}>
          {[...competitor.snapshots].reverse().map((s) => (
            <li key={s.id} className="cluster">
              <span>
                {s.date}: {formatNumber(s.followers)} followers{s.engagementRate != null ? ` · ${percent(s.engagementRate)}` : ''}
              </span>
              <SourceLabel label={s.sourceLabel} />
              <IconButton size="sm" label={`Delete snapshot of ${s.date}`} icon={<Trash2 />} onClick={() => remove.mutate(s.id)} />
            </li>
          ))}
        </ul>
      )}
    </Dialog>
  );
}

function SnapshotDialog({ competitor, onClose, onSaved }: { competitor: Competitor; onClose: () => void; onSaved: () => void }) {
  const [date, setDate] = useState(new Date().toISOString().slice(0, 10));
  const [followers, setFollowers] = useState('');
  const [er, setEr] = useState('');
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () =>
      api.put(`/agency/social/competitors/${competitor.id}/snapshots`, {
        date,
        followers: Number(followers),
        engagementRate: er ? Number(er) / 100 : null,
        source: 'Manual',
      }),
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
      title={`Snapshot — ${competitor.name}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={!followers} loading={save.isPending} onClick={() => save.mutate()}>
            Save
          </Button>
        </>
      }
    >
      <div className="stack">
        {error && <Alert tone="danger">{error}</Alert>}
        <FormField label="Date">
          <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </FormField>
        <FormField label="Followers">
          <Input type="number" min={0} value={followers} onChange={(e) => setFollowers(e.target.value)} />
        </FormField>
        <FormField label="Engagement rate (%)" optional>
          <Input type="number" min={0} max={100} step="0.01" value={er} onChange={(e) => setEr(e.target.value)} />
        </FormField>
      </div>
    </Dialog>
  );
}
