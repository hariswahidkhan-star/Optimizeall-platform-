import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { EyeOff, MessageSquare, RefreshCw, Send, StickyNote } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import clsx from 'clsx';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { FormField } from '@/components/ui/FormField';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { PageHeader } from '@/components/ui/PageHeader';
import { RadioGroup } from '@/components/ui/RadioGroup';
import { Select } from '@/components/ui/Select';
import { SkeletonText } from '@/components/ui/Skeleton';
import { Textarea } from '@/components/ui/Textarea';
import { useToast } from '@/components/ui/toastContext';
import { countryName } from '@/features/auth/localeOptions';
import { api } from '@/lib/api/client';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { humanize } from '@/lib/format/text';
import { TICKET_CATEGORIES, TICKET_PRIORITIES, TICKET_STATUSES, type StaffMessage, type StaffTicket } from '../api/types';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { statusMeta } from '@/components/ui/statusMap';
import { AdminBadge } from '../shared/badges';
import { enumOptions, QueryError, useCan } from '../shared/common';
import { adminErrorMessage, isConflict, mapFieldErrors } from '../shared/errors';
import { useSupportStaff } from './useSupportStaff';

export const INTERNAL_LABEL = 'Internal — not visible to participant';

export function Message({ message }: { message: StaffMessage }) {
  const kind = message.isInternalNote ? 'internal' : message.fromStaff ? 'staff' : 'participant';
  return (
    <li className={clsx('admin-message', `admin-message--${kind}`)} data-kind={kind}>
      <div className="admin-message__head">
        <span className="admin-message__author">{message.authorName}</span>
        {message.isInternalNote ? (
          <Badge tone="warning" icon={<EyeOff />}>
            {INTERNAL_LABEL}
          </Badge>
        ) : message.fromStaff ? (
          <Badge tone="brand" size="sm">
            Staff reply
          </Badge>
        ) : (
          <Badge tone="neutral" size="sm">
            Participant
          </Badge>
        )}
        <DateTime value={message.createdAt} className="admin-message__time text-small" />
      </div>
      <p className="admin-message__body admin-prewrap">{message.body}</p>
    </li>
  );
}

function Composer({ ticket }: { ticket: StaffTicket }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<'reply' | 'internal'>('reply');
  const [body, setBody] = useState('');
  const [clientError, setClientError] = useState<string | null>(null);
  const internal = mode === 'internal';
  const closed = ticket.status === 'Closed';

  const send = useMutation({
    mutationFn: () =>
      api.post<StaffTicket>(`/admin/support/tickets/${ticket.id}/messages`, {
        body: body.trim(),
        isInternalNote: internal,
      }),
    onSuccess: (updated) => {
      queryClient.setQueryData(['admin', 'ticket', ticket.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['admin', 'tickets'] });
      setBody('');
      toast.success(
        internal ? 'Internal note added' : 'Reply sent',
        internal ? 'Only staff can see it.' : 'The participant has been notified.',
      );
    },
  });

  const server = mapFieldErrors(send.error, ['body']);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    const text = body.trim();
    if (text.length < 1) return setClientError('Write a message first.');
    if (text.length > 5000) return setClientError('Messages can be at most 5000 characters.');
    setClientError(null);
    send.mutate();
  };

  return (
    <form
      onSubmit={submit}
      noValidate
      className={clsx('stack admin-composer', internal && 'admin-composer--internal')}
    >
      <RadioGroup
        legend="Message type"
        orientation="horizontal"
        value={mode}
        onChange={(v) => setMode(v as 'reply' | 'internal')}
        options={[
          {
            value: 'reply',
            label: 'Reply to participant',
            description: 'Emailed and shown in their ticket.',
          },
          { value: 'internal', label: 'Internal note', description: 'Only staff can see it.' },
        ]}
      />
      {internal && (
        <Alert tone="warning" icon={<EyeOff />}>
          {INTERNAL_LABEL}. Notes don’t change the ticket status or notify anyone.
        </Alert>
      )}
      {closed && !internal && (
        <Alert tone="info">
          This ticket is closed. Reopen it (change the status) before replying, or add an internal note.
        </Alert>
      )}
      {send.isError && server.form && (
        <Alert tone="danger" role="alert">
          {adminErrorMessage(send.error)}
        </Alert>
      )}
      <FormField
        label={internal ? 'Internal note' : 'Reply'}
        error={clientError ?? server.fields.body}
        hint="Up to 5000 characters."
      >
        <Textarea rows={5} value={body} maxLength={5000} onChange={(e) => setBody(e.target.value)} />
      </FormField>
      <div>
        <Button
          type="submit"
          variant={internal ? 'secondary' : 'primary'}
          leadingIcon={internal ? <StickyNote /> : <Send />}
          loading={send.isPending}
          disabled={closed && !internal}
        >
          {internal ? 'Add internal note' : 'Send reply'}
        </Button>
      </div>
    </form>
  );
}

function UpdatePanel({
  ticket,
  onReload,
  reloading,
}: {
  ticket: StaffTicket;
  onReload: () => void;
  reloading: boolean;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const staff = useSupportStaff();
  const [status, setStatus] = useState(ticket.status);
  const [priority, setPriority] = useState(ticket.priority);
  const [assignee, setAssignee] = useState(ticket.assignedTo?.id ?? '');
  const [category, setCategory] = useState(ticket.category);

  useEffect(() => {
    setStatus(ticket.status);
    setPriority(ticket.priority);
    setAssignee(ticket.assignedTo?.id ?? '');
    setCategory(ticket.category);
  }, [ticket.concurrencyStamp, ticket.status, ticket.priority, ticket.assignedTo?.id, ticket.category]);

  const update = useMutation({
    mutationFn: () =>
      api.put<StaffTicket>(`/admin/support/tickets/${ticket.id}`, {
        status,
        priority,
        assignedToUserId: assignee || null,
        // Only sent when re-filed; the API keeps the category otherwise.
        ...(category !== ticket.category ? { category } : {}),
        concurrencyStamp: ticket.concurrencyStamp,
      }),
    onSuccess: (updated) => {
      queryClient.setQueryData(['admin', 'ticket', ticket.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['admin', 'tickets'] });
      toast.success(
        'Ticket updated',
        `${updated.reference} is ${statusMeta('ticket', updated.status).label.toLowerCase()}.`,
      );
    },
  });

  const conflict = isConflict(update.error);
  const server = mapFieldErrors(update.error, ['status', 'priority', 'assignedToUserId', 'category'], {
    'support.invalid_assignee': 'assignedToUserId',
  });
  const dirty =
    status !== ticket.status ||
    priority !== ticket.priority ||
    assignee !== (ticket.assignedTo?.id ?? '') ||
    category !== ticket.category;

  const assigneeOptions = [
    { value: '', label: 'Unassigned' },
    ...(user ? [{ value: user.id, label: `Me (${user.displayName})` }] : []),
    ...(staff.data ?? [])
      .filter((s) => s.id !== user?.id)
      .map((s) => ({ value: s.id, label: s.displayName })),
  ];
  if (ticket.assignedTo && !assigneeOptions.some((o) => o.value === ticket.assignedTo?.id))
    assigneeOptions.push({ value: ticket.assignedTo.id, label: ticket.assignedTo.displayName });

  return (
    <Card as="section" aria-labelledby="ticket-update">
      <CardHeader titleId="ticket-update" title="Status, category & assignment" headingLevel={2} />
      <CardBody>
        <form
          className="stack"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            update.mutate();
          }}
        >
          {conflict && (
            <Alert
              tone="warning"
              role="alert"
              title="This ticket changed while you were viewing it"
              actions={
                <Button
                  size="sm"
                  variant="secondary"
                  leadingIcon={<RefreshCw />}
                  loading={reloading}
                  onClick={() => {
                    update.reset();
                    onReload();
                  }}
                >
                  Reload ticket
                </Button>
              }
            >
              Someone else updated it. Reload to see the latest status and messages, then apply your change
              again.
            </Alert>
          )}
          {!conflict && server.form && (
            <Alert tone="danger" role="alert">
              {adminErrorMessage(update.error)}
            </Alert>
          )}
          <FormField label="Status" error={server.fields.status}>
            <Select
              value={status}
              options={enumOptions(TICKET_STATUSES, (v) => statusMeta('ticket', v).label)}
              onChange={(e) => setStatus(e.target.value)}
            />
          </FormField>
          <FormField label="Priority" error={server.fields.priority}>
            <Select
              value={priority}
              options={enumOptions(TICKET_PRIORITIES)}
              onChange={(e) => setPriority(e.target.value)}
            />
          </FormField>
          <FormField label="Category" error={server.fields.category}>
            <Select value={category} options={enumOptions(TICKET_CATEGORIES)} onChange={(e) => setCategory(e.target.value)} />
          </FormField>
          <FormField
            label="Assignee"
            error={server.fields.assignedToUserId}
            hint="Only active staff with support access."
          >
            <Select
              value={assignee}
              options={assigneeOptions}
              onChange={(e) => setAssignee(e.target.value)}
            />
          </FormField>
          <div>
            <Button type="submit" loading={update.isPending} disabled={!dirty || conflict}>
              Update ticket
            </Button>
          </div>
        </form>
      </CardBody>
    </Card>
  );
}

export function TicketDetailPage() {
  const { ticketId = '' } = useParams();
  const canViewUsers = useCan(Permissions.UsersView);
  const ticket = useQuery({
    queryKey: ['admin', 'ticket', ticketId],
    queryFn: ({ signal }) => api.get<StaffTicket>(`/admin/support/tickets/${ticketId}`, { signal }),
  });

  const breadcrumbs = [
    { label: 'Support tickets', to: '/admin/support' },
    { label: ticket.data?.reference ?? 'Ticket' },
  ];

  if (ticket.isPending)
    return (
      <>
        <PageHeader title="Ticket" breadcrumbs={breadcrumbs} />
        <Card>
          <CardBody>
            <SkeletonText lines={6} />
          </CardBody>
        </Card>
      </>
    );
  if (ticket.isError)
    return (
      <>
        <PageHeader title="Ticket" breadcrumbs={breadcrumbs} />
        <QueryError error={ticket.error} onRetry={() => void ticket.refetch()} />
      </>
    );

  const t = ticket.data;
  const r = t.requester;
  const internalCount = t.messages.filter((m) => m.isInternalNote).length;

  return (
    <>
      <PageHeader
        eyebrow={t.reference}
        title={t.subject}
        breadcrumbs={breadcrumbs}
        meta={
          <>
            <StatusBadge kind="ticket" status={t.status} />
            <AdminBadge kind="priority" value={t.priority} />
            <Badge tone="neutral">{humanize(t.category)}</Badge>
          </>
        }
      />
      <div className="admin-ticket">
        <div className="stack admin-ticket__main">
          <Card as="section" aria-labelledby="ticket-thread">
            <CardHeader
              titleId="ticket-thread"
              title="Conversation"
              headingLevel={2}
              description={
                internalCount > 0
                  ? `${t.messages.length} messages, including ${internalCount} internal note${internalCount === 1 ? '' : 's'} the participant can’t see.`
                  : `${t.messages.length} messages.`
              }
            />
            <CardBody>
              {t.messages.length === 0 ? (
                <p className="text-muted">
                  <MessageSquare aria-hidden="true" /> No messages yet.
                </p>
              ) : (
                <ol className="admin-thread" aria-label="Messages, oldest first">
                  {t.messages.map((m) => (
                    <Message key={m.id} message={m} />
                  ))}
                </ol>
              )}
            </CardBody>
          </Card>
          <Card as="section" aria-labelledby="ticket-compose">
            <CardHeader titleId="ticket-compose" title="Respond" headingLevel={2} />
            <CardBody>
              <Composer ticket={t} />
            </CardBody>
          </Card>
        </div>
        <div className="stack admin-ticket__side">
          <Card as="section" aria-labelledby="ticket-requester">
            <CardHeader titleId="ticket-requester" title="Requester" headingLevel={2} />
            <CardBody className="stack">
              <div className="admin-cell-stack">
                {canViewUsers ? (
                  <Link className="ui-link" to={`/admin/users/${r.id}`}>
                    <strong>{r.displayName}</strong>
                  </Link>
                ) : (
                  <strong>{r.displayName}</strong>
                )}
                <span className="text-small text-muted">{r.email}</span>
              </div>
              <KeyValueList
                layout="inline"
                items={[
                  { label: 'Status', value: <AdminBadge kind="user" value={r.status} /> },
                  { label: 'Tier', value: r.tier },
                  { label: 'Country', value: countryName(r.countryCode) },
                  { label: 'Member since', value: <DateTime value={r.createdAt} format="date" /> },
                  { label: 'Open tickets', value: r.openTicketCount },
                  { label: 'All tickets', value: r.totalTicketCount },
                ]}
              />
            </CardBody>
          </Card>
          <UpdatePanel ticket={t} onReload={() => void ticket.refetch()} reloading={ticket.isFetching} />
          <Card as="section" aria-labelledby="ticket-facts">
            <CardHeader titleId="ticket-facts" title="Details" headingLevel={2} />
            <CardBody>
              <KeyValueList
                layout="inline"
                items={[
                  { label: 'Opened', value: <DateTime value={t.createdAt} /> },
                  { label: 'Last activity', value: <DateTime value={t.updatedAt} format="relative" /> },
                  { label: 'Resolved', value: t.resolvedAt ? <DateTime value={t.resolvedAt} /> : '—' },
                  { label: 'Submission', value: t.submissionId ? <code>{t.submissionId}</code> : '—' },
                  { label: 'Payout item', value: t.payoutItemId ? <code>{t.payoutItemId}</code> : '—' },
                ]}
              />
            </CardBody>
          </Card>
        </div>
      </div>
    </>
  );
}
