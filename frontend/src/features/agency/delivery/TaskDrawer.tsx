import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Eye, EyeOff, X } from 'lucide-react';
import { useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DateTime,
  Drawer,
  ErrorState,
  FormField,
  IconButton,
  Input,
  Select,
  Skeleton,
  Switch,
  Textarea,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import {
  TASK_PRIORITIES,
  TASK_STATUSES,
  type DeliveryFile,
  type TaskDetail,
  type TaskPriority,
  type TaskStatus,
} from '../shared/deliveryTypes';
import { FilePreview, TaskStatusBadge, taskStatusLabel } from '../shared/deliveryUi';
import { dk, useStaff } from './api';

function Editor({ detail, projectId }: { detail: TaskDetail; projectId: string }) {
  const qc = useQueryClient();
  const toast = useToast();
  const staff = useStaff();
  const t = detail.task;
  const [form, setForm] = useState({
    title: t.title,
    description: detail.description ?? '',
    status: t.status,
    priority: t.priority,
    dueDate: t.dueDate ?? '',
    estimateHours: t.estimateHours?.toString() ?? '',
    clientVisible: t.clientVisible,
    assigneeUserIds: t.assignees.map((a) => a.id),
  });
  const save = useMutation({
    mutationFn: () =>
      api.put<TaskDetail>(`/agency/tasks/${t.id}`, {
        ...form,
        dueDate: form.dueDate || null,
        estimateHours: form.estimateHours ? Number(form.estimateHours) : null,
        labels: t.labels,
        milestoneId: t.milestoneId,
        blockedByTaskIds: detail.blockedBy.map((b) => b.id),
        concurrencyStamp: t.concurrencyStamp,
      }),
    onSuccess: (d) => {
      qc.setQueryData(dk.task(t.id), d);
      void qc.invalidateQueries({ queryKey: dk.tasks(projectId) });
      toast.success('Task saved');
    },
  });
  const toggleAssignee = (id: string, on: boolean) =>
    setForm((f) => ({ ...f, assigneeUserIds: on ? [...f.assigneeUserIds, id] : f.assigneeUserIds.filter((x) => x !== id) }));
  return (
    <form
      className="dl-form"
      aria-label="Task details"
      onSubmit={(e) => {
        e.preventDefault();
        save.mutate();
      }}
    >
      {save.error ? <Alert tone="danger">{errorMessage(save.error)}</Alert> : null}
      <FormField label="Title" required>
        <Input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} required minLength={2} maxLength={300} />
      </FormField>
      <div className="dl-form__row">
        <FormField label="Status">
          <Select value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as TaskStatus })} options={TASK_STATUSES.map((s) => ({ value: s, label: taskStatusLabel(s) }))} />
        </FormField>
        <FormField label="Priority">
          <Select value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value as TaskPriority })} options={TASK_PRIORITIES.map((p) => ({ value: p, label: p }))} />
        </FormField>
        <FormField label="Due date">
          <Input type="date" value={form.dueDate} onChange={(e) => setForm({ ...form, dueDate: e.target.value })} />
        </FormField>
        <FormField label="Estimate (hours)">
          <Input type="number" min={0} step="0.25" value={form.estimateHours} onChange={(e) => setForm({ ...form, estimateHours: e.target.value })} />
        </FormField>
      </div>
      <FormField label="Description" hint="Markdown supported.">
        <Textarea rows={5} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
      </FormField>
      <fieldset className="dl-form">
        <legend>Assignees</legend>
        <div className="dl-form__row">
          {(staff.data ?? []).map((s) => (
            <Checkbox key={s.id} label={s.displayName} checked={form.assigneeUserIds.includes(s.id)} onChange={(e) => toggleAssignee(s.id, e.target.checked)} />
          ))}
        </div>
      </fieldset>
      <Switch
        checked={form.clientVisible}
        onCheckedChange={(v) => setForm({ ...form, clientVisible: v })}
        label="Visible to the client"
        description="Title, status and due date show in the client portal's project view."
      />
      <div className="dl-row">
        <Button type="submit" loading={save.isPending}>
          Save task
        </Button>
      </div>
    </form>
  );
}

function Checklist({ detail }: { detail: TaskDetail }) {
  const qc = useQueryClient();
  const [text, setText] = useState('');
  const onSaved = (d: TaskDetail) => qc.setQueryData(dk.task(detail.task.id), d);
  const toggle = useMutation({
    mutationFn: ({ id, text: t, isDone }: { id: string; text: string; isDone: boolean }) =>
      api.put<TaskDetail>(`/agency/tasks/${detail.task.id}/checklist/${id}`, { text: t, isDone }),
    onSuccess: onSaved,
  });
  const add = useMutation({
    mutationFn: () => api.post<TaskDetail>(`/agency/tasks/${detail.task.id}/checklist`, { text }),
    onSuccess: (d) => {
      onSaved(d);
      setText('');
    },
  });
  return (
    <section aria-labelledby="checklist-heading" className="dl-form">
      <h3 id="checklist-heading">Checklist</h3>
      <ul className="dl-checklist">
        {detail.checklist.map((c) => (
          <li key={c.id}>
            <Checkbox label={c.text} checked={c.isDone} onChange={(e) => toggle.mutate({ id: c.id, text: c.text, isDone: e.target.checked })} />
          </li>
        ))}
      </ul>
      <form
        className="dl-toolbar"
        onSubmit={(e) => {
          e.preventDefault();
          if (text.trim()) add.mutate();
        }}
      >
        <FormField label="New checklist item">
          <Input value={text} onChange={(e) => setText(e.target.value)} maxLength={500} />
        </FormField>
        <Button type="submit" variant="secondary" disabled={!text.trim()} loading={add.isPending}>
          Add
        </Button>
      </form>
    </section>
  );
}

/** A comment: its author can edit or delete it; a project manager can delete any comment. */
function CommentItem({ detail, comment }: { detail: TaskDetail; comment: TaskDetail['comments'][number] }) {
  const qc = useQueryClient();
  const { user, hasPermission } = useAuth();
  const mine = user?.id === comment.author.id;
  const canDelete = mine || hasPermission(Permissions.ProjectsManage);
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState(comment.body);
  const [deleting, setDeleting] = useState(false);
  const url = `/agency/tasks/${detail.task.id}/comments/${comment.id}`;
  const edit = useMutation({
    mutationFn: () => api.put<TaskDetail>(url, { body: text }),
    onSuccess: (d) => {
      qc.setQueryData(dk.task(detail.task.id), d);
      setEditing(false);
    },
  });
  return (
    <li className="dl-comment">
      <div className="dl-comment__head">
        <strong>{comment.author.displayName}</strong>
        <DateTime value={comment.createdAt} format="relative" />
        {comment.editedAt ? <span>(edited)</span> : null}
        {comment.mentions.length > 0 ? <span>mentioned {comment.mentions.map((m) => `@${m.displayName}`).join(', ')}</span> : null}
      </div>
      {editing ? (
        <form
          className="dl-form"
          onSubmit={(e) => {
            e.preventDefault();
            edit.mutate();
          }}
        >
          {edit.error ? <Alert tone="danger">{errorMessage(edit.error)}</Alert> : null}
          <FormField label="Edit comment" hint="Mentioned teammates are not notified again.">
            <Textarea rows={3} value={text} maxLength={10000} onChange={(e) => setText(e.target.value)} />
          </FormField>
          <div className="dl-row">
            <Button type="submit" size="sm" disabled={!text.trim()} loading={edit.isPending}>
              Save
            </Button>
            <Button
              size="sm"
              variant="ghost"
              onClick={() => {
                setText(comment.body);
                setEditing(false);
              }}
            >
              Cancel
            </Button>
          </div>
        </form>
      ) : (
        <p className="dl-report__body">{comment.body}</p>
      )}
      {!editing && (mine || canDelete) ? (
        <div className="dl-row">
          {mine ? (
            <Button size="sm" variant="ghost" onClick={() => setEditing(true)}>
              Edit<span className="visually-hidden"> comment by {comment.author.displayName}</span>
            </Button>
          ) : null}
          {canDelete ? (
            <Button size="sm" variant="ghost" onClick={() => setDeleting(true)}>
              Delete<span className="visually-hidden"> comment by {comment.author.displayName}</span>
            </Button>
          ) : null}
        </div>
      ) : null}
      <ConfirmDialog
        open={deleting}
        onClose={() => setDeleting(false)}
        tone="danger"
        title="Delete this comment?"
        description="The comment is removed for everyone. The deletion is recorded in the audit log."
        confirmLabel="Delete"
        onConfirm={async () => {
          const d = await api.delete<TaskDetail>(url);
          qc.setQueryData(dk.task(detail.task.id), d);
        }}
      />
    </li>
  );
}

function Comments({ detail }: { detail: TaskDetail }) {
  const qc = useQueryClient();
  const staff = useStaff();
  const [body, setBody] = useState('');
  const [mentions, setMentions] = useState<string[]>([]);
  const post = useMutation({
    mutationFn: () => api.post<TaskDetail>(`/agency/tasks/${detail.task.id}/comments`, { body, mentionUserIds: mentions }),
    onSuccess: (d) => {
      qc.setQueryData(dk.task(detail.task.id), d);
      setBody('');
      setMentions([]);
    },
  });
  const mentionOptions = (staff.data ?? []).filter((s) => !mentions.includes(s.id));
  return (
    <section aria-labelledby="comments-heading" className="dl-form">
      <h3 id="comments-heading">Comments</h3>
      <ol className="dl-comments" aria-label="Comments, oldest first">
        {detail.comments.map((c) => (
          <CommentItem key={c.id} detail={detail} comment={c} />
        ))}
      </ol>
      <form
        className="dl-form"
        onSubmit={(e) => {
          e.preventDefault();
          post.mutate();
        }}
      >
        {post.error ? <Alert tone="danger">{errorMessage(post.error)}</Alert> : null}
        <FormField label="Add a comment" hint="Mention teammates to notify them.">
          <Textarea rows={3} value={body} onChange={(e) => setBody(e.target.value)} maxLength={10000} />
        </FormField>
        <div className="dl-toolbar">
          <FormField label="Mention">
            <Select
              value=""
              onChange={(e) => {
                const id = e.target.value;
                if (!id) return;
                setMentions((m) => [...m, id]);
                const person = staff.data?.find((s) => s.id === id);
                if (person && !body.includes(`@${person.displayName}`)) setBody((b) => `${b}${b && !b.endsWith(' ') ? ' ' : ''}@${person.displayName} `);
              }}
              placeholder="Choose a teammate…"
              options={mentionOptions.map((s) => ({ value: s.id, label: s.displayName }))}
            />
          </FormField>
          {mentions.map((id) => {
            const person = staff.data?.find((s) => s.id === id);
            return (
              <Badge key={id} tone="brand">
                @{person?.displayName ?? 'someone'}{' '}
                <IconButton size="sm" variant="ghost" label={`Remove mention of ${person?.displayName ?? 'teammate'}`} icon={<X />} onClick={() => setMentions((m) => m.filter((x) => x !== id))} />
              </Badge>
            );
          })}
        </div>
        <div className="dl-row">
          <Button type="submit" disabled={!body.trim()} loading={post.isPending}>
            Comment
          </Button>
        </div>
      </form>
    </section>
  );
}

function Attachments({ detail, clientId }: { detail: TaskDetail; clientId: string }) {
  const qc = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const upload = useMutation({
    mutationFn: async () => {
      const form = new FormData();
      form.append('file', file!);
      const stored = await api.upload<DeliveryFile>(`/agency/clients/${clientId}/files`, form);
      return api.post<TaskDetail>(`/agency/tasks/${detail.task.id}/attachments`, { fileId: stored.id });
    },
    onSuccess: (d) => {
      qc.setQueryData(dk.task(detail.task.id), d);
      setFile(null);
    },
  });
  return (
    <section aria-labelledby="attachments-heading" className="dl-form">
      <h3 id="attachments-heading">Attachments</h3>
      {detail.attachments.map((a) => (
        <FilePreview key={a.id} file={a.file} url={a.file.staffUrl} alt={a.file.fileName} />
      ))}
      <form
        className="dl-toolbar"
        onSubmit={(e) => {
          e.preventDefault();
          if (file) upload.mutate();
        }}
      >
        <FormField label="Attach a file" hint="PNG, JPEG, WebP, PDF or MP4, up to 50 MB.">
          <input type="file" accept="image/png,image/jpeg,image/webp,application/pdf,video/mp4" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        </FormField>
        <Button type="submit" variant="secondary" disabled={!file} loading={upload.isPending}>
          Upload
        </Button>
      </form>
      {upload.error ? <Alert tone="danger">{errorMessage(upload.error)}</Alert> : null}
    </section>
  );
}

/** Task details in a side drawer: fields, checklist, comments with @mentions, watchers, dependencies, attachments. */
export function TaskDrawer({ taskId, projectId, clientId, onClose }: { taskId: string; projectId: string; clientId: string; onClose: () => void }) {
  const qc = useQueryClient();
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.DeliverablesSubmit);
  const detail = useQuery({
    queryKey: dk.task(taskId),
    queryFn: ({ signal }) => api.get<TaskDetail>(`/agency/tasks/${taskId}`, { signal }),
  });
  const watch = useMutation({
    mutationFn: (on: boolean) => (on ? api.post<TaskDetail>(`/agency/tasks/${taskId}/watch`) : api.delete<TaskDetail>(`/agency/tasks/${taskId}/watch`)),
    onSuccess: (d) => qc.setQueryData(dk.task(taskId), d),
  });
  return (
    <Drawer open onClose={onClose} title={detail.data?.task.title ?? 'Task'}>
      {detail.isPending ? (
        <Skeleton height={300} />
      ) : detail.isError ? (
        <ErrorState error={detail.error} />
      ) : (
        <div className="dl-page">
          <div className="dl-row">
            <TaskStatusBadge status={detail.data.task.status} />
            {detail.data.task.isOverdue ? <Badge tone="danger">Overdue</Badge> : null}
            <span className="dl-meta">{detail.data.hoursLogged}h logged</span>
            <Button
              size="sm"
              variant="ghost"
              leadingIcon={detail.data.iWatch ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
              onClick={() => watch.mutate(!detail.data.iWatch)}
            >
              {detail.data.iWatch ? 'Stop watching' : 'Watch'}
            </Button>
          </div>
          {detail.data.blockedBy.length > 0 ? (
            <Alert tone="warning" title="Blocked by">
              <ul>
                {detail.data.blockedBy.map((b) => (
                  <li key={b.id}>
                    {b.title} — {taskStatusLabel(b.status)}
                  </li>
                ))}
              </ul>
            </Alert>
          ) : null}
          {canEdit ? <Editor key={detail.data.task.concurrencyStamp} detail={detail.data} projectId={projectId} /> : <p className="dl-report__body">{detail.data.description}</p>}
          <Checklist detail={detail.data} />
          <Comments detail={detail.data} />
          {canEdit ? <Attachments detail={detail.data} clientId={clientId} /> : null}
          <p className="dl-meta">
            Watchers: {detail.data.watchers.map((w) => w.displayName).join(', ') || 'none'}
          </p>
        </div>
      )}
    </Drawer>
  );
}
