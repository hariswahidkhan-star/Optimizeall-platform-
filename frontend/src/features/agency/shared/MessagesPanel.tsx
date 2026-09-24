import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MessagesSquare, Paperclip } from 'lucide-react';
import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  Button,
  DateTime,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  Skeleton,
  Textarea,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { DeliveryFile, Thread, ThreadSummary } from './deliveryTypes';
import { FilePreview } from './deliveryUi';

interface Props {
  /** API base for the client's threads: `/agency/clients/{id}` or `/client/orgs/{id}`. */
  base: string;
  audience: 'staff' | 'client';
  /** Hide the composer (e.g. read-only users). */
  canWrite?: boolean;
}

const MAX_BYTES = 50 * 1024 * 1024;

function Composer({ base, onSent, threadId }: { base: string; threadId?: string; onSent: (thread: Thread) => void }) {
  const [subject, setSubject] = useState('');
  const [body, setBody] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const send = useMutation({
    mutationFn: async () => {
      const uploaded: DeliveryFile[] = [];
      for (const file of files) {
        const form = new FormData();
        form.append('file', file);
        uploaded.push(await api.upload<DeliveryFile>(`${base}/files`, form));
      }
      const payload = { body, attachmentFileIds: uploaded.map((f) => f.id) };
      return threadId
        ? api.post<Thread>(`${base}/threads/${threadId}/messages`, payload)
        : api.post<Thread>(`${base}/threads`, { ...payload, subject });
    },
    onSuccess: (thread) => {
      setBody('');
      setSubject('');
      setFiles([]);
      onSent(thread);
    },
  });
  return (
    <form
      className="dl-form"
      aria-label={threadId ? 'Reply' : 'New conversation'}
      onSubmit={(e) => {
        e.preventDefault();
        send.mutate();
      }}
    >
      {!threadId ? (
        <FormField label="Subject" required>
          <Input value={subject} onChange={(e) => setSubject(e.target.value)} required minLength={2} maxLength={200} />
        </FormField>
      ) : null}
      <FormField label={threadId ? 'Your reply' : 'Message'} required>
        <Textarea value={body} onChange={(e) => setBody(e.target.value)} required rows={3} maxLength={10000} />
      </FormField>
      <FormField label="Attachments" optional hint="PNG, JPEG, WebP, PDF or MP4, up to 50 MB each.">
        <Input
          type="file"
          multiple
          accept="image/png,image/jpeg,image/webp,application/pdf,video/mp4"
          onChange={(e) => setFiles([...(e.target.files ?? [])].filter((f) => f.size <= MAX_BYTES).slice(0, 10))}
        />
      </FormField>
      {send.error ? <Alert tone="danger">{errorMessage(send.error)}</Alert> : null}
      <div className="dl-row">
        <Button type="submit" loading={send.isPending} disabled={!body.trim() || (!threadId && subject.trim().length < 2)}>
          {threadId ? 'Send reply' : 'Start conversation'}
        </Button>
      </div>
    </form>
  );
}

/** Staff rename a conversation (the subject both sides see). */
function RenameThread({ base, thread, onRenamed }: { base: string; thread: Thread; onRenamed: (t: Thread) => void }) {
  const [editing, setEditing] = useState(false);
  const [subject, setSubject] = useState(thread.subject);
  const rename = useMutation({
    mutationFn: () => api.put<Thread>(`${base}/threads/${thread.id}`, { subject }),
    onSuccess: (t) => {
      onRenamed(t);
      setEditing(false);
    },
  });
  if (!editing)
    return (
      <Button
        size="sm"
        variant="ghost"
        onClick={() => {
          setSubject(thread.subject);
          setEditing(true);
        }}
      >
        Rename<span className="visually-hidden"> conversation {thread.subject}</span>
      </Button>
    );
  return (
    <form
      className="dl-toolbar"
      onSubmit={(e) => {
        e.preventDefault();
        rename.mutate();
      }}
    >
      {rename.error ? <Alert tone="danger">{errorMessage(rename.error)}</Alert> : null}
      <FormField label="Subject">
        <Input value={subject} minLength={2} maxLength={200} onChange={(e) => setSubject(e.target.value)} />
      </FormField>
      <Button type="submit" size="sm" loading={rename.isPending} disabled={subject.trim().length < 2}>
        Save
      </Button>
      <Button size="sm" variant="ghost" onClick={() => setEditing(false)}>
        Cancel
      </Button>
    </form>
  );
}

/**
 * Client ↔ account-team conversations (separate from participant support tickets): thread list, messages with
 * attachments and read receipts, composer. The open thread id lives in `?thread=`.
 */
export function MessagesPanel({ base, audience, canWrite = true }: Props) {
  const qc = useQueryClient();
  const [params, setParams] = useSearchParams();
  const openId = params.get('thread');
  const threads = useQuery({
    queryKey: ['delivery', 'threads', base],
    queryFn: ({ signal }) => api.get<ThreadSummary[]>(`${base}/threads`, { signal }),
  });
  const thread = useQuery({
    queryKey: ['delivery', 'thread', base, openId],
    queryFn: ({ signal }) => api.get<Thread>(`${base}/threads/${openId}`, { signal }),
    enabled: Boolean(openId),
  });
  const open = (id: string | null) =>
    setParams(
      (p) => {
        const next = new URLSearchParams(p);
        if (id) next.set('thread', id);
        else next.delete('thread');
        return next;
      },
      { replace: true },
    );
  const onSent = (t: Thread) => {
    qc.setQueryData(['delivery', 'thread', base, t.id], t);
    void qc.invalidateQueries({ queryKey: ['delivery', 'threads', base] });
    open(t.id);
  };

  return (
    <div className="dl-two-col">
      <section aria-labelledby="threads-heading" className="dl-page">
        <h2 id="threads-heading">Conversations</h2>
        {threads.isPending ? (
          <Skeleton height={120} />
        ) : threads.isError ? (
          <ErrorState error={threads.error} compact />
        ) : threads.data.length === 0 ? (
          <EmptyState compact icon={<MessagesSquare aria-hidden="true" />} title="No conversations yet" />
        ) : (
          <ul className="dl-list" aria-label="Conversations">
            {threads.data.map((t) => (
              <li key={t.id} className="dl-list__item" aria-current={t.id === openId ? 'true' : undefined}>
                <span className="dl-list__main">
                  <button type="button" className="dl-card__title" onClick={() => open(t.id)}>
                    {t.subject}
                  </button>
                  <span className="dl-meta">
                    {t.lastAuthor ? <span>{t.lastAuthor}</span> : null}
                    <DateTime value={t.lastMessageAt} format="relative" />
                  </span>
                  {t.lastMessagePreview ? <span className="dl-muted">{t.lastMessagePreview}</span> : null}
                </span>
                {t.unreadCount > 0 ? <Badge tone="brand">{t.unreadCount} unread</Badge> : null}
              </li>
            ))}
          </ul>
        )}
        {canWrite && openId ? (
          <Button variant="secondary" onClick={() => open(null)}>
            New conversation
          </Button>
        ) : null}
      </section>
      <section aria-label={thread.data ? thread.data.subject : 'Conversation'} className="dl-page">
        {!openId ? (
          canWrite ? (
            <>
              <h2>New conversation</h2>
              <Composer base={base} onSent={onSent} />
            </>
          ) : (
            <p className="dl-muted">Choose a conversation.</p>
          )
        ) : thread.isPending ? (
          <Skeleton height={200} />
        ) : thread.isError ? (
          <ErrorState error={thread.error} />
        ) : (
          <>
            <div className="dl-row">
              <h2>{thread.data.subject}</h2>
              {audience === 'staff' && canWrite ? (
                <RenameThread
                  base={base}
                  thread={thread.data}
                  onRenamed={(t) => {
                    qc.setQueryData(['delivery', 'thread', base, t.id], t);
                    void qc.invalidateQueries({ queryKey: ['delivery', 'threads', base] });
                  }}
                />
              ) : null}
            </div>
            <ol className="dl-comments" aria-label="Messages, oldest first">
              {thread.data.messages.map((m) => (
                <li key={m.id} className="dl-comment">
                  <div className="dl-comment__head">
                    <strong>{m.author.displayName}</strong>
                    <span>{m.fromClient ? (audience === 'client' ? '' : '(client)') : audience === 'client' ? '(your agency team)' : ''}</span>
                    <DateTime value={m.createdAt} format="both" />
                  </div>
                  <p className="dl-report__body">{m.body}</p>
                  {m.attachments.map((f) => (
                    <div key={f.id} className="dl-row">
                      <Paperclip aria-hidden="true" size={16} />
                      <FilePreview file={f} url={audience === 'staff' ? f.staffUrl : f.clientUrl} alt={f.fileName} />
                    </div>
                  ))}
                  {m.readBy.length > 0 ? <p className="dl-kpi__source">Read by {m.readBy.join(', ')}</p> : null}
                </li>
              ))}
            </ol>
            {canWrite ? <Composer base={base} threadId={thread.data.id} onSent={onSent} /> : null}
          </>
        )}
      </section>
    </div>
  );
}
