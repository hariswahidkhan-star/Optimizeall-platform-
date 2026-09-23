import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CircleHelp, LifeBuoy, Plus, Send, X } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable, type DataTableColumn } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FilterBar } from '@/components/ui/FilterBar';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Select } from '@/components/ui/Select';
import { Textarea } from '@/components/ui/Textarea';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { humanize } from '@/lib/format/text';
import { qk, usePayouts, useSubmissions, useTicket, useTickets } from '../api/queries';
import type { CreateTicketRequest, Ticket, TicketCategory, TicketSummary } from '../api/types';
import { TicketStatusBadge } from '../components/Badges';
import { QueryState } from '../components/QueryState';
import { firstMessage, focusFirstError, mapFormErrors } from '../lib/formErrors';
import { ticketCategoryOptions, ticketStatusOptions } from '../lib/labels';
import '../participant.css';

const columns: DataTableColumn<TicketSummary>[] = [
  {
    id: 'subject',
    header: 'Subject',
    primary: true,
    cell: (t) => (
      <span className="stack" style={{ ['--stack-gap' as string]: '2px' }}>
        <Link to={`/app/support/${t.id}`} className="ui-link">
          {t.subject}
        </Link>
        <span className="text-small pp-muted">{t.reference}</span>
      </span>
    ),
  },
  { id: 'status', header: 'Status', cell: (t) => <TicketStatusBadge status={t.status} /> },
  { id: 'category', header: 'Category', cell: (t) => humanize(t.category) },
  {
    id: 'updated',
    header: 'Last activity',
    nowrap: true,
    cell: (t) => <DateTime value={t.updatedAt} format="relative" />,
  },
];

export function SupportPage() {
  const [params, setParams] = useSearchParams();
  const status = params.get('status') ?? undefined;
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1);
  const list = useTickets({ status, page });

  return (
    <div className="pp-page">
      <PageHeader
        title="Support"
        description="Questions about a submission, payout or your account? We’re here to help."
        actions={
          <div className="pp-actions">
            <Link to="/faq" className="ui-link">
              <CircleHelp aria-hidden="true" className="pp-inline-icon" />
              Read the FAQ
            </Link>
            <ButtonLink to="/app/support/new" leadingIcon={<Plus />}>
              New ticket
            </ButtonLink>
          </div>
        }
      />
      <FilterBar
        filters={[{ id: 'status', label: 'Status', options: ticketStatusOptions }]}
        values={{ status }}
        onFilterChange={(_id, value) => setParams(value ? { status: value } : {})}
      />
      {list.isError ? (
        <Card flat>
          <ErrorState
            error={list.error}
            title="Your tickets couldn’t be loaded"
            onRetry={() => void list.refetch()}
          />
        </Card>
      ) : (
        <>
          <DataTable
            caption="Your support tickets"
            columns={columns}
            rows={list.data?.items ?? []}
            getRowId={(t) => t.id}
            loading={list.isPending}
            emptyState={
              <EmptyState
                icon={<LifeBuoy />}
                title={status ? 'No tickets with this status' : 'No support tickets'}
                description="Check the FAQ first — most answers are there. Otherwise open a ticket and we’ll reply by email and here."
                action={
                  <ButtonLink to="/app/support/new" variant="secondary">
                    Open a ticket
                  </ButtonLink>
                }
              />
            }
          />
          {list.data && list.data.total > 20 && (
            <Pagination
              page={page}
              pageSize={20}
              total={list.data.total}
              onPageChange={(p) => setParams({ ...(status ? { status } : {}), page: String(p) })}
              label="Ticket pages"
            />
          )}
        </>
      )}
    </div>
  );
}

const TICKET_FIELDS = ['subject', 'category', 'body', 'submissionId', 'payoutItemId'] as const;
type TicketField = (typeof TICKET_FIELDS)[number];

export function NewTicketPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const toast = useToast();
  const client = useQueryClient();
  const presetSubmission = params.get('submission') ?? '';
  const presetPayout = params.get('payout') ?? '';
  const [subject, setSubject] = useState('');
  const [category, setCategory] = useState<string>(
    presetSubmission ? 'Submission' : presetPayout ? 'Payout' : '',
  );
  const [body, setBody] = useState('');
  const [submissionId, setSubmissionId] = useState(presetSubmission);
  const [payoutItemId, setPayoutItemId] = useState(presetPayout);
  const [clientErrors, setClientErrors] = useState<Partial<Record<TicketField, string>>>({});

  const submissions = useSubmissions({ page: 1, pageSize: 50 });
  const payouts = usePayouts(1);
  const submissionOptions = useMemo(
    () =>
      (submissions.data?.items ?? []).map((s) => ({
        value: s.id,
        label: `${s.campaign.title} · ${new Date(s.submittedAt).toLocaleDateString()}`,
      })),
    [submissions.data],
  );
  const payoutOptions = useMemo(
    () =>
      (payouts.data?.items ?? []).map((p) => ({
        value: p.itemId,
        label: `${p.batchReference} · ${p.amount} ${p.currency}`,
      })),
    [payouts.data],
  );

  const codeMap = {
    'support.invalid_submission': 'submissionId',
    'support.invalid_payout_item': 'payoutItemId',
  };
  const create = useMutation({
    mutationFn: (request: CreateTicketRequest) => api.post<Ticket>('/me/support/tickets', request),
    onSuccess: async (ticket) => {
      toast.success('Ticket opened', `Reference ${ticket.reference}`);
      client.setQueryData(qk.ticket(ticket.id), ticket);
      await client.invalidateQueries({ queryKey: qk.tickets });
      await client.invalidateQueries({ queryKey: qk.home });
      navigate(`/app/support/${ticket.id}`);
    },
    onError: (error) => {
      toast.error('Your ticket wasn’t sent', errorMessage(error));
      const mapped = mapFormErrors(error, TICKET_FIELDS, codeMap);
      if (mapped) focusFirstError('ticket', TICKET_FIELDS, mapped.fields);
    },
  });
  const server = create.isError ? mapFormErrors(create.error, TICKET_FIELDS, codeMap) : null;
  const errorFor = (f: TicketField) => clientErrors[f] ?? firstMessage(server?.fields[f]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (create.isPending) return;
    const found: Partial<Record<TicketField, string>> = {};
    if (subject.trim().length < 3) found.subject = 'Enter a subject of at least 3 characters.';
    if (!category) found.category = 'Choose a category.';
    if (body.trim().length < 10) found.body = 'Describe the problem in at least 10 characters.';
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstError('ticket', TICKET_FIELDS, found);
      return;
    }
    create.mutate({
      subject: subject.trim(),
      category: category as TicketCategory,
      body: body.trim(),
      submissionId: submissionId || null,
      payoutItemId: payoutItemId || null,
    });
  };

  return (
    <div className="pp-page">
      <PageHeader
        title="New support ticket"
        breadcrumbs={[{ label: 'Support', to: '/app/support' }, { label: 'New ticket' }]}
        description={
          <>
            Many questions are answered in the{' '}
            <Link to="/faq" className="ui-link">
              FAQ
            </Link>
            .
          </>
        }
      />
      <Card style={{ maxWidth: 760 }}>
        <CardBody>
          <form className="pp-form" noValidate onSubmit={onSubmit} aria-label="New support ticket">
            {server?.form && (
              <Alert tone="danger" role="alert">
                {server.form.title}
              </Alert>
            )}
            <div className="pp-form-grid">
              <FormField id="ticket-category" label="Category" required error={errorFor('category')}>
                <Select
                  value={category}
                  placeholder="Choose a category"
                  options={ticketCategoryOptions}
                  onChange={(e) => setCategory(e.target.value)}
                />
              </FormField>
              <FormField id="ticket-subject" label="Subject" required error={errorFor('subject')}>
                <Input value={subject} maxLength={200} onChange={(e) => setSubject(e.target.value)} />
              </FormField>
            </div>
            <FormField
              id="ticket-body"
              label="How can we help?"
              required
              error={errorFor('body')}
              hint="Include links, dates and what you expected to happen. Never share passwords or full bank details."
            >
              <Textarea value={body} rows={6} maxLength={5000} onChange={(e) => setBody(e.target.value)} />
            </FormField>
            <div className="pp-form-grid">
              <FormField
                id="ticket-submissionId"
                label="Related submission"
                optional
                error={errorFor('submissionId')}
              >
                <Select
                  value={submissionId}
                  placeholder="None"
                  options={submissionOptions}
                  onChange={(e) => setSubmissionId(e.target.value)}
                />
              </FormField>
              <FormField
                id="ticket-payoutItemId"
                label="Related payout"
                optional
                error={errorFor('payoutItemId')}
              >
                <Select
                  value={payoutItemId}
                  placeholder="None"
                  options={payoutOptions}
                  onChange={(e) => setPayoutItemId(e.target.value)}
                />
              </FormField>
            </div>
            <div className="pp-actions">
              <Button type="submit" loading={create.isPending} leadingIcon={<Send />}>
                Send ticket
              </Button>
              <ButtonLink to="/app/support" variant="ghost">
                Cancel
              </ButtonLink>
            </div>
          </form>
        </CardBody>
      </Card>
    </div>
  );
}

function TicketView({ ticket }: { ticket: Ticket }) {
  const toast = useToast();
  const client = useQueryClient();
  const [reply, setReply] = useState('');
  const [replyError, setReplyError] = useState<string | null>(null);
  const [closing, setClosing] = useState(false);

  const refresh = (updated: Ticket) => {
    client.setQueryData(qk.ticket(ticket.id), updated);
    void client.invalidateQueries({ queryKey: [...qk.tickets, 'list'] });
  };

  const send = useMutation({
    mutationFn: (text: string) =>
      api.post<Ticket>(`/me/support/tickets/${ticket.id}/messages`, { body: text }),
    onSuccess: (updated) => {
      refresh(updated);
      setReply('');
      toast.success('Reply sent');
    },
    onError: (error) => toast.error('Your reply wasn’t sent', errorMessage(error)),
  });

  const close = useMutation({
    mutationFn: () => api.post<Ticket>(`/me/support/tickets/${ticket.id}/close`),
    onSuccess: (updated) => {
      refresh(updated);
      toast.success('Ticket closed');
    },
  });

  const onReply = (event: FormEvent) => {
    event.preventDefault();
    if (send.isPending) return;
    if (!reply.trim()) {
      setReplyError('Write a message first.');
      document.getElementById('ticket-reply')?.focus();
      return;
    }
    setReplyError(null);
    send.mutate(reply.trim());
  };

  return (
    <div className="pp-page">
      <PageHeader
        title={ticket.subject}
        eyebrow={ticket.reference}
        breadcrumbs={[{ label: 'Support', to: '/app/support' }, { label: ticket.reference }]}
        meta={<TicketStatusBadge status={ticket.status} />}
        actions={
          ticket.status !== 'Closed' ? (
            <Button variant="secondary" leadingIcon={<X />} onClick={() => setClosing(true)}>
              Close ticket
            </Button>
          ) : undefined
        }
      />
      <div className="pp-two-col">
        <Card as="section" aria-labelledby="thread-title">
          <CardHeader titleId="thread-title" title="Conversation" />
          <CardBody className="stack">
            <ol className="pp-thread" aria-label="Messages">
              {ticket.messages.map((m) => (
                <li key={m.id} className="pp-message" data-from={m.fromStaff ? 'staff' : 'me'}>
                  <p className="pp-message__meta">
                    <strong>{m.fromStaff ? m.authorName : 'You'}</strong>
                    <DateTime value={m.createdAt} />
                  </p>
                  <p className="pp-prewrap">{m.body}</p>
                </li>
              ))}
            </ol>
            {ticket.canReply ? (
              <form className="pp-form" noValidate onSubmit={onReply} aria-label="Reply">
                <FormField
                  id="ticket-reply"
                  label="Your reply"
                  error={replyError ?? firstMessage(send.isError ? errorMessage(send.error) : null)}
                >
                  <Textarea
                    rows={4}
                    maxLength={5000}
                    value={reply}
                    onChange={(e) => setReply(e.target.value)}
                  />
                </FormField>
                <div>
                  <Button type="submit" loading={send.isPending} leadingIcon={<Send />}>
                    Send reply
                  </Button>
                </div>
              </form>
            ) : (
              <Alert tone="neutral">This ticket is closed. Open a new ticket if you need more help.</Alert>
            )}
          </CardBody>
        </Card>
        <Card as="section" aria-labelledby="ticket-details-title">
          <CardHeader titleId="ticket-details-title" title="Details" />
          <CardBody>
            <KeyValueList
              layout="inline"
              items={[
                { label: 'Status', value: <TicketStatusBadge status={ticket.status} /> },
                { label: 'Category', value: humanize(ticket.category) },
                { label: 'Opened', value: <DateTime value={ticket.createdAt} format="date" /> },
                { label: 'Last activity', value: <DateTime value={ticket.updatedAt} format="relative" /> },
                ...(ticket.submissionId
                  ? [
                      {
                        label: 'Submission',
                        value: (
                          <Link to={`/app/submissions/${ticket.submissionId}`} className="ui-link">
                            View
                          </Link>
                        ),
                      },
                    ]
                  : []),
                ...(ticket.payoutItemId
                  ? [
                      {
                        label: 'Payout',
                        value: (
                          <Link to={`/app/payouts/${ticket.payoutItemId}`} className="ui-link">
                            View
                          </Link>
                        ),
                      },
                    ]
                  : []),
              ]}
            />
          </CardBody>
        </Card>
      </div>
      <ConfirmDialog
        open={closing}
        onClose={() => setClosing(false)}
        title="Close this ticket?"
        description="Close it if your question is answered. You can still read it later."
        confirmLabel="Close ticket"
        onConfirm={async () => {
          await close.mutateAsync();
          setClosing(false);
        }}
      />
    </div>
  );
}

export function TicketDetailPage() {
  const { ticketId = '' } = useParams();
  const query = useTicket(ticketId);
  return (
    <QueryState query={query} errorTitle="This ticket isn’t available">
      {(ticket) => <TicketView ticket={ticket} />}
    </QueryState>
  );
}
