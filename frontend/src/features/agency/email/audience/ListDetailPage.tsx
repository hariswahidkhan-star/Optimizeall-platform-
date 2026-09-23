import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Download, Pencil, Upload, UserPlus, Users } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { LineChart } from '@/components/ui/Charts';
import { Checkbox } from '@/components/ui/Checkbox';
import { CopyField } from '@/components/ui/CopyField';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { Dialog } from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { FilterBar } from '@/components/ui/FilterBar';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { SkeletonText } from '@/components/ui/Skeleton';
import { Stat } from '@/components/ui/Stat';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { EMAIL_API, emailKeys, useList, useListHealth, useSubscribers } from '../api/queries';
import type { EmailList, SubscriberDetail } from '../api/types';
import { ConsentBadge, SubscriberStatusBadge, TierBadge } from '../shared/ui';
import { ImportDialog } from './ImportDialog';
import { ListFormDialog } from './ListsPage';

function AddContactDialog({ open, onClose, list }: { open: boolean; onClose: () => void; list: EmailList }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [attest, setAttest] = useState(false);
  const [source, setSource] = useState('');
  const save = useMutation({
    mutationFn: () =>
      api.post<SubscriberDetail>(`${EMAIL_API}/subscribers`, {
        clientAccountId: list.clientAccountId,
        email: email || null,
        phone: phone || null,
        firstName: firstName || null,
        lastName: lastName || null,
        attestEmailConsent: attest,
        consentSource: attest ? source : null,
        listIds: [list.id],
      }),
    onSuccess: (s) => {
      const pending = s.lists.find((l) => l.listId === list.id)?.status === 'Pending';
      toast.success('Contact added', pending ? 'A confirmation email was sent (double opt-in).' : undefined);
      void queryClient.invalidateQueries({ queryKey: emailKeys.all });
      onClose();
    },
  });
  const errors = isApiError(save.error) ? Object.values(save.error.errors ?? {}).flat() : [];
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Add a contact"
      description={list.doubleOptIn ? 'Without a consent attestation the contact gets a double opt-in email first.' : undefined}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="add-contact-form" loading={save.isPending}>
            Add contact
          </Button>
        </>
      }
    >
      <form id="add-contact-form" className="stack" onSubmit={submit}>
        <FormField label="Email">
          <Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
        </FormField>
        <FormField label="Phone" optional hint="International format, e.g. +14155550123">
          <Input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
        </FormField>
        <FormField label="First name" optional>
          <Input value={firstName} onChange={(e) => setFirstName(e.target.value)} />
        </FormField>
        <FormField label="Last name" optional>
          <Input value={lastName} onChange={(e) => setLastName(e.target.value)} />
        </FormField>
        <Checkbox checked={attest} onChange={(e) => setAttest(e.target.checked)} label="The contact gave consent to receive email marketing from this sender." />
        {attest && (
          <FormField label="Where and how was consent given?" required>
            <Input value={source} onChange={(e) => setSource(e.target.value)} maxLength={200} />
          </FormField>
        )}
        {save.isError && <Alert tone="danger">{errors.length ? errors.join(' ') : errorMessage(save.error)}</Alert>}
      </form>
    </Dialog>
  );
}

export function ListDetailPage() {
  const { id } = useParams();
  const list = useList(id);
  const health = useListHealth(id);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [importOpen, setImportOpen] = useState(false);
  const [addOpen, setAddOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const toast = useToast();
  const subscribers = useSubscribers(list.data?.clientAccountId ?? null, { listId: id, search: search || undefined, status, page, pageSize: 25 });

  if (list.isPending) return <SkeletonText lines={6} />;
  if (list.isError) return <ErrorState error={list.error} onRetry={() => void list.refetch()} />;
  const l = list.data;
  const h = health.data;

  return (
    <>
      <PageHeader
        title={l.name}
        description={l.description ?? undefined}
        breadcrumbs={[{ label: 'Audience', to: '/agency/email/lists' }, { label: l.name }]}
        actions={
          <>
            <Button variant="secondary" leadingIcon={<Pencil />} onClick={() => setEditOpen(true)}>
              Edit list
            </Button>
            <Button
              variant="secondary"
              leadingIcon={<Download />}
              onClick={() => api.download(`${EMAIL_API}/lists/${l.id}/export.csv`, 'list.csv').catch((e) => toast.error('Export failed', errorMessage(e)))}
            >
              Export CSV
            </Button>
            <Button variant="secondary" leadingIcon={<UserPlus />} onClick={() => setAddOpen(true)}>
              Add contact
            </Button>
            <Button leadingIcon={<Upload />} onClick={() => setImportOpen(true)}>
              Import CSV
            </Button>
          </>
        }
      />
      <div className="email-grid">
        <Stat label="Subscribed" value={formatNumber(l.subscribed)} measurement="Count" />
        <Stat label="Awaiting confirmation" value={formatNumber(l.pending)} measurement="Count" />
        <Stat label="Unsubscribed" value={formatNumber(l.unsubscribed)} measurement="Count" />
        <Stat label="Active (opened/clicked ≤30 days)" value={formatNumber(h?.active ?? 0)} measurement="Measured" loading={health.isPending} />
        <Stat label="Warm (31–90 days)" value={formatNumber(h?.warm ?? 0)} measurement="Measured" loading={health.isPending} />
        <Stat label="Cold (> 90 days)" value={formatNumber(h?.cold ?? 0)} measurement="Measured" loading={health.isPending} />
        <Stat
          label="Net growth (90 days)"
          value={formatNumber(h?.netGrowth ?? 0)}
          measurement="Count"
          loading={health.isPending}
          hint={h ? `${formatNumber(h.bounced)} bounced · ${formatNumber(h.complained)} complaints` : undefined}
        />
      </div>
      {h && h.growth.length > 1 && (
        <Card>
          <CardHeader title="List growth" />
          <CardBody>
            <LineChart
              title="Subscriptions and unsubscribes per day"
              description="Last 90 days."
              labels={h.growth.map((g) => g.day)}
              series={[
                { id: 'subscribed', label: 'Subscribed', values: h.growth.map((g) => g.subscribed) },
                { id: 'unsubscribed', label: 'Unsubscribed', values: h.growth.map((g) => g.unsubscribed) },
              ]}
            />
          </CardBody>
        </Card>
      )}
      <Card>
        <CardHeader title="Hosted sign-up form" description="Share this link or embed it; consent and IP (hashed) are recorded with the consent wording." />
        <CardBody>
          <CopyField label="Sign-up form URL" value={l.signupUrl} />
        </CardBody>
      </Card>
      <Card>
        <CardHeader title="Contacts" />
        <CardBody className="stack">
          <FilterBar
            search={search}
            onSearchChange={(v) => {
              setSearch(v);
              setPage(1);
            }}
            searchLabel="Search contacts"
            searchPlaceholder="Name, email or phone…"
            filters={[{ id: 'status', label: 'Status', options: ['Subscribed', 'Unsubscribed', 'Bounced', 'Complained', 'Cleaned'].map((s) => ({ value: s, label: s })) }]}
            values={{ status }}
            onFilterChange={(_, v) => {
              setStatus(v);
              setPage(1);
            }}
            onReset={() => {
              setSearch('');
              setStatus(undefined);
            }}
          />
          {subscribers.isError ? (
            <ErrorState error={subscribers.error} onRetry={() => void subscribers.refetch()} />
          ) : (
            <>
              <DataTable
                caption="Contacts on this list"
                rows={subscribers.data?.items ?? []}
                getRowId={(s) => s.id}
                loading={subscribers.isPending}
                columns={[
                  {
                    id: 'contact',
                    header: 'Contact',
                    primary: true,
                    cell: (s) => (
                      <span className="email-cell-stack">
                        <Link className="ui-link" to={`/agency/email/contacts/${s.id}`}>
                          {[s.firstName, s.lastName].filter(Boolean).join(' ') || s.email || s.phone}
                        </Link>
                        <span className="email-muted">{s.email ?? s.phone}</span>
                      </span>
                    ),
                  },
                  { id: 'status', header: 'Status', cell: (s) => <SubscriberStatusBadge status={s.status} /> },
                  { id: 'consent', header: 'Email consent', cell: (s) => <ConsentBadge status={s.emailConsent} /> },
                  { id: 'tier', header: 'Engagement', hideOnMobile: true, cell: (s) => <TierBadge tier={s.tier} /> },
                  { id: 'tags', header: 'Tags', hideOnMobile: true, cell: (s) => s.tags.join(', ') || '—' },
                  { id: 'added', header: 'Added', hideOnMobile: true, cell: (s) => <DateTime value={s.createdAt} format="date" /> },
                ]}
                emptyState={<EmptyState icon={<Users />} headingLevel={3} title="No contacts" description="Import a CSV or share the sign-up form." />}
              />
              {subscribers.data && subscribers.data.total > 25 && (
                <Pagination page={page} pageSize={25} total={subscribers.data.total} onPageChange={setPage} label="Contact pages" />
              )}
            </>
          )}
        </CardBody>
      </Card>
      {importOpen && <ImportDialog open onClose={() => setImportOpen(false)} listId={l.id} />}
      {addOpen && <AddContactDialog open onClose={() => setAddOpen(false)} list={l} />}
      {editOpen && <ListFormDialog open onClose={() => setEditOpen(false)} list={l} />}
    </>
  );
}
