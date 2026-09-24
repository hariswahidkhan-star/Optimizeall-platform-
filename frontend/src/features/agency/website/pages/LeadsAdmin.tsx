import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CalendarClock, Download, Inbox, Mail, Newspaper, Users } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  Alert,
  Badge,
  BarChart,
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  ConfirmDialog,
  DataTable,
  Dialog,
  ErrorState,
  FilterBar,
  FormField,
  KeyValueList,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  Stat,
  Switch,
  Textarea,
  useToast,
} from '@/components/ui';
import type { Tone } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import { Permissions } from '@/lib/auth/permissions';
import { formatDate, formatDateTime } from '@/lib/format/dates';
import { isExternalHref } from '@/lib/safeHref';
import {
  type AvailabilityWindow,
  type Booking,
  type BookingSettings,
  type Inquiry,
  type InquiryStatus,
  type InquirySummary,
  type InquiryType,
  type Overview,
  type Paged,
  type Subscriber,
  W,
} from '../api';
import { ListEditor, SelectField, TextField } from '../shared/fields';
import '../website.css';

const INQUIRY_STATUSES: InquiryStatus[] = ['New', 'InProgress', 'Qualified', 'Converted', 'Closed', 'Spam'];
const INQUIRY_TYPES: InquiryType[] = ['Contact', 'Audit', 'Quote', 'Consultation'];
const STATUS_TONE: Record<InquiryStatus, Tone> = { New: 'info', InProgress: 'warning', Qualified: 'brand', Converted: 'success', Closed: 'neutral', Spam: 'danger' };
const human = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2');

interface StaffPerson {
  id: string;
  displayName: string;
  email: string;
}

/** Active agency staff for the assignee picker (needs clients.view; otherwise only "me" is offered). */
function useStaff() {
  const { hasPermission } = useAuth();
  return useQuery({
    queryKey: ['agency', 'staff'],
    enabled: hasPermission(Permissions.ClientsView),
    staleTime: 5 * 60_000,
    queryFn: ({ signal }) => api.get<StaffPerson[]>('/agency/staff', { signal }),
  });
}

export function InquiryStatusBadge({ status }: { status: InquiryStatus }) {
  return <Badge tone={STATUS_TONE[status]}>{human(status)}</Badge>;
}

// ---------------------------------------------------------------- Overview

/** Website overview: inquiries KPI and shortcuts. */
export function WebsiteOverviewPage() {
  const { data, isLoading, error, refetch } = useQuery({ queryKey: ['agency', 'website', 'overview'], queryFn: () => api.get<Overview>(`${W}/overview`) });
  const { hasPermission } = useAuth();
  const change = data ? data.inquiriesLast30Days - data.inquiriesPrevious30Days : 0;
  return (
    <div className="cms-page">
      <PageHeader title="Website" description="Leads, consultations, subscribers and content on the public agency website." />
      {error ? (
        <ErrorState error={error} onRetry={() => void refetch()} />
      ) : (
        <>
          <div className="cms-kpis">
            <Stat
              label="Inquiries (30 days)"
              value={data?.inquiriesLast30Days ?? '—'}
              measurement="Count"
              loading={isLoading}
              icon={<Inbox />}
              delta={data && data.inquiriesPrevious30Days > 0 ? { value: change / data.inquiriesPrevious30Days, label: 'vs. previous 30 days' } : undefined}
            />
            <Stat label="New, unhandled" value={data?.newInquiries ?? '—'} measurement="Count" loading={isLoading} icon={<Mail />} />
            <Stat label="Upcoming consultations" value={data?.upcomingConsultations ?? '—'} measurement="Count" loading={isLoading} icon={<CalendarClock />} />
            <Stat label="Newsletter subscribers" value={data?.confirmedSubscribers ?? '—'} measurement="Count" loading={isLoading} icon={<Users />} hint={data ? `${data.pendingSubscribers} awaiting confirmation` : undefined} />
            <Stat label="Posts in review" value={data?.postsInReview ?? '—'} measurement="Count" loading={isLoading} icon={<Newspaper />} hint={data ? `${data.publishedPosts} published` : undefined} />
            <Stat label="New job applications" value={data?.newApplications ?? '—'} measurement="Count" loading={isLoading} />
          </div>
          {data && (
            <div className="cms-grid-2">
              <Card>
                <CardHeader title="Inquiries per day" description="Last 30 days, spam excluded." />
                <CardBody>
                  <BarChart title="Inquiries per day" description="Website inquiries per day over the last 30 days" data={data.inquiriesByDay.map((d) => ({ label: d.key.slice(5), value: d.count }))} valueLabel="Inquiries" />
                </CardBody>
              </Card>
              <Card>
                <CardHeader title="By type and source" />
                <CardBody>
                  <KeyValueList items={data.inquiriesByType.map((t) => ({ label: human(t.key), value: t.count }))} layout="inline" />
                  <h3 className="public-footer__heading" style={{ marginTop: 'var(--space-4)' }}>
                    Top sources (utm_source)
                  </h3>
                  <KeyValueList items={data.inquiriesBySource.map((t) => ({ label: t.key, value: t.count }))} layout="inline" />
                </CardBody>
              </Card>
            </div>
          )}
          <div className="cms-toolbar">
            {(hasPermission(Permissions.SiteManage) || hasPermission(Permissions.CrmView)) && <ButtonLink to="../inquiries" relative="path">Open inquiries inbox</ButtonLink>}
            {hasPermission(Permissions.SiteManage) && (
              <ButtonLink to="../bookings" relative="path" variant="secondary">
                Consultations
              </ButtonLink>
            )}
            <a className="ui-button ui-button--ghost ui-button--md" href="/" target="_blank" rel="noopener noreferrer">
              View website<span className="visually-hidden"> (opens in a new tab)</span>
            </a>
          </div>
        </>
      )}
      {isLoading && <Skeleton height={200} />}
    </div>
  );
}

// ---------------------------------------------------------------- Inquiries

export function InquiriesPage() {
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState(params.get('search') ?? '');
  const page = Number(params.get('page') ?? 1) || 1;
  const type = params.get('type') ?? undefined;
  const status = params.get('status') ?? undefined;
  const assignedTo = params.get('assignedTo') ?? undefined;
  const staff = useStaff();
  const staffName = (id: string | null) => (id ? (staff.data?.find((p) => p.id === id)?.displayName ?? 'Assigned') : '—');
  const query = useQuery({
    queryKey: ['agency', 'website', 'inquiries', type, status, assignedTo, search, page],
    queryFn: () => api.get<Paged<InquirySummary>>(`${W}/inquiries`, { query: { type, status, assignedTo, search, page, pageSize: 25 } }),
    placeholderData: (previous) => previous,
  });
  const update = (key: string, value: string | undefined) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    if (key !== 'page') next.delete('page');
    setParams(next);
  };
  return (
    <div className="cms-page">
      <PageHeader
        title="Inquiries"
        description="Every contact, audit, quote and consultation request from the website. New inquiries also notify staff and create CRM leads."
        actions={
          <Button variant="secondary" leadingIcon={<Download />} onClick={() => void api.download(`${W}/inquiries/export.csv`, 'website-inquiries.csv', { query: { type, status } })}>
            Export CSV
          </Button>
        }
      />
      <FilterBar
        search={search}
        onSearchChange={(v) => {
          setSearch(v);
          update('search', v || undefined);
        }}
        searchLabel="Search name, email or company"
        filters={[
          { id: 'type', label: 'Type', options: INQUIRY_TYPES.map((t) => ({ value: t, label: t })) },
          { id: 'status', label: 'Status', options: INQUIRY_STATUSES.map((s) => ({ value: s, label: human(s) })) },
          {
            id: 'assignedTo',
            label: 'Assigned to',
            options: [
              { value: 'me', label: 'Me' },
              { value: 'unassigned', label: 'Unassigned' },
              ...(staff.data ?? []).map((p) => ({ value: p.id, label: p.displayName })),
            ],
          },
        ]}
        values={{ type, status, assignedTo }}
        onFilterChange={(id, v) => update(id, v)}
        onReset={() => {
          setSearch('');
          setParams({});
        }}
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <>
          <DataTable
            caption="Website inquiries"
            rows={query.data?.items ?? []}
            loading={query.isLoading}
            getRowId={(r) => r.id}
            rowLabel={(r) => `${r.name} (${r.reference})`}
            columns={[
              { id: 'name', header: 'From', primary: true, cell: (r) => <Link to={r.id}>{r.name}{r.company ? ` — ${r.company}` : ''}</Link> },
              { id: 'type', header: 'Type', cell: (r) => r.type },
              { id: 'status', header: 'Status', cell: (r) => <InquiryStatusBadge status={r.status} /> },
              { id: 'services', header: 'Services', cell: (r) => r.serviceSlugs.join(', ') || '—', hideOnMobile: true },
              { id: 'assignee', header: 'Assigned to', cell: (r) => staffName(r.assignedToUserId), hideOnMobile: true },
              { id: 'source', header: 'Source', cell: (r) => r.utmSource ?? 'direct', hideOnMobile: true },
              { id: 'received', header: 'Received', cell: (r) => formatDateTime(r.createdAt), hideOnMobile: true },
            ]}
            emptyState={<p className="text-muted">No inquiries match these filters.</p>}
          />
          {query.data && query.data.total > query.data.pageSize && (
            <Pagination page={query.data.page} pageSize={query.data.pageSize} total={query.data.total} onPageChange={(p) => update('page', String(p))} />
          )}
        </>
      )}
    </div>
  );
}

export function InquiryDetailPage() {
  const { inquiryId = '' } = useParams();
  const toast = useToast();
  const client = useQueryClient();
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.SiteManage);
  const { user } = useAuth();
  const navigate = useNavigate();
  const staff = useStaff();
  const query = useQuery({ queryKey: ['agency', 'website', 'inquiry', inquiryId], queryFn: () => api.get<Inquiry>(`${W}/inquiries/${inquiryId}`) });
  const [status, setStatus] = useState<InquiryStatus | null>(null);
  const [notes, setNotes] = useState<string | null>(null);
  const [assignee, setAssignee] = useState<string | null | undefined>(undefined);
  const [deleting, setDeleting] = useState(false);
  const save = useMutation({
    mutationFn: (override?: { status: InquiryStatus }) =>
      api.put<Inquiry>(`${W}/inquiries/${inquiryId}`, {
        status: override?.status ?? status ?? query.data!.status,
        staffNotes: notes ?? query.data!.staffNotes,
        assignedToUserId: assignee === undefined ? query.data!.assignedToUserId : assignee,
        concurrencyStamp: query.data!.concurrencyStamp,
      }),
    onSuccess: async (saved) => {
      toast.success('Inquiry updated');
      setStatus(null);
      setNotes(null);
      setAssignee(undefined);
      client.setQueryData(['agency', 'website', 'inquiry', inquiryId], saved);
      await client.invalidateQueries({ queryKey: ['agency', 'website', 'inquiries'] });
    },
    onError: (e) =>
      toast.error(
        "Couldn't update the inquiry",
        isApiError(e) && e.code === 'concurrency.conflict' ? 'Someone else updated this inquiry. Reload the page to see their changes.' : errorMessage(e),
      ),
  });
  if (query.isLoading) return <Skeleton height={300} />;
  if (query.isError) return <ErrorState error={query.error} />;
  const i = query.data!;
  const currentAssignee = assignee === undefined ? i.assignedToUserId : assignee;
  const people = staff.data ?? [];
  const assigneeOptions = [
    { value: '', label: 'Unassigned' },
    ...(user && !people.some((p) => p.id === user.id) ? [{ value: user.id, label: `${user.displayName} (me)` }] : []),
    ...people.map((p) => ({ value: p.id, label: p.id === user?.id ? `${p.displayName} (me)` : p.displayName })),
    ...(currentAssignee && currentAssignee !== user?.id && !people.some((p) => p.id === currentAssignee) ? [{ value: currentAssignee, label: 'Current assignee' }] : []),
  ];
  const dirty = status !== null || notes !== null || assignee !== undefined;
  return (
    <div className="cms-page">
      <PageHeader
        title={`${i.name}${i.company ? ` — ${i.company}` : ''}`}
        eyebrow={`${i.type} inquiry · ${i.reference}`}
        breadcrumbs={[{ label: 'Inquiries', to: '..' }, { label: i.reference }]}
        meta={<InquiryStatusBadge status={i.status} />}
        actions={
          canEdit ? (
            <Button variant="ghost" onClick={() => setDeleting(true)}>
              Delete inquiry
            </Button>
          ) : undefined
        }
      />
      <ConfirmDialog
        open={deleting}
        onClose={() => setDeleting(false)}
        title={`Delete inquiry ${i.reference}?`}
        description="Use this for spam or a data-erasure request. The inquiry and its details are removed permanently; a linked consultation keeps its own record. The CRM lead, if any, is not affected."
        confirmLabel="Delete inquiry"
        tone="danger"
        onConfirm={async () => {
          await api.delete(`${W}/inquiries/${inquiryId}`);
          toast.success('Inquiry deleted');
          await client.invalidateQueries({ queryKey: ['agency', 'website', 'inquiries'] });
          navigate('..', { relative: 'path' });
        }}
      />
      <div className="cms-grid-2">
        <Card>
          <CardHeader title="Contact" />
          <CardBody>
            <KeyValueList
              items={[
                { label: 'Email', value: <a href={`mailto:${i.email}`}>{i.email}</a> },
                { label: 'Phone', value: i.phone ?? '—' },
                { label: 'Company', value: i.company ?? '—' },
                {
                  label: 'Website',
                  value: i.website && isExternalHref(i.website) ? (
                    <a href={i.website} target="_blank" rel="noopener noreferrer">
                      {i.website}
                    </a>
                  ) : (
                    i.website ?? '—'
                  ),
                },
                { label: 'Received', value: formatDateTime(i.createdAt) },
                { label: 'Consent', value: `${i.consentVersion} at ${formatDateTime(i.consentAt)}` },
              ]}
            />
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Request" />
          <CardBody>
            <KeyValueList
              items={[
                { label: 'Services', value: i.serviceSlugs.join(', ') || '—' },
                { label: 'Budget', value: i.budgetRange ?? '—' },
                { label: 'Timeline', value: i.timeline ?? '—' },
                ...Object.entries(i.details).map(([k, v]) => ({ label: human(k.charAt(0).toUpperCase() + k.slice(1)), value: v })),
                ...(i.bookingId ? [{ label: 'Consultation', value: <Link to="../../bookings">View bookings</Link> }] : []),
              ]}
            />
            {i.message && <p style={{ whiteSpace: 'pre-wrap', marginTop: 'var(--space-4)' }}>{i.message}</p>}
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Attribution" />
          <CardBody>
            <KeyValueList
              items={[
                { label: 'utm_source', value: i.utmSource ?? '—' },
                { label: 'utm_medium', value: i.utmMedium ?? '—' },
                { label: 'utm_campaign', value: i.utmCampaign ?? '—' },
                { label: 'utm_term', value: i.utmTerm ?? '—' },
                { label: 'utm_content', value: i.utmContent ?? '—' },
                { label: 'Referrer', value: i.referrer ?? '—' },
                { label: 'Landing page', value: i.landingPath ?? '—' },
              ]}
            />
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Handling" />
          <CardBody>
            {canEdit ? (
              <div className="cms-form">
                <FormField label="Status">
                  <Select value={status ?? i.status} onChange={(e) => setStatus(e.target.value as InquiryStatus)} options={INQUIRY_STATUSES.map((s) => ({ value: s, label: human(s) }))} />
                </FormField>
                <FormField label="Assigned to">
                  <Select value={currentAssignee ?? ''} onChange={(e) => setAssignee(e.target.value || null)} options={assigneeOptions} />
                </FormField>
                <FormField label="Internal notes" optional>
                  <Textarea rows={4} value={notes ?? i.staffNotes ?? ''} onChange={(e) => setNotes(e.target.value)} maxLength={4000} />
                </FormField>
                <div className="cms-toolbar">
                  <Button onClick={() => save.mutate(undefined)} loading={save.isPending} disabled={!dirty}>
                    Save
                  </Button>
                  {i.status !== 'Closed' && (
                    <Button
                      variant="secondary"
                      disabled={save.isPending}
                      onClick={() => save.mutate({ status: 'Closed' })}
                    >
                      Mark closed
                    </Button>
                  )}
                </div>
              </div>
            ) : (
              <Alert tone="info">You can view inquiries; changing their status needs the site.manage permission.</Alert>
            )}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------- Bookings

const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

function AvailabilityCard() {
  const toast = useToast();
  const client = useQueryClient();
  const query = useQuery({ queryKey: ['agency', 'website', 'booking-settings'], queryFn: () => api.get<BookingSettings>(`${W}/bookings/settings`) });
  const [draft, setDraft] = useState<BookingSettings | null>(null);
  const [blackout, setBlackout] = useState({ date: '', reason: '' });
  const current = draft ?? query.data;
  const save = useMutation({
    mutationFn: () => api.put<BookingSettings>(`${W}/bookings/settings`, current),
    onSuccess: (saved) => {
      toast.success('Availability saved');
      setDraft(null);
      client.setQueryData(['agency', 'website', 'booking-settings'], saved);
    },
    onError: (e) => toast.error("Couldn't save availability", errorMessage(e)),
  });
  const addBlackout = useMutation({
    mutationFn: () => api.post(`${W}/bookings/blackouts`, { date: blackout.date, reason: blackout.reason || null }),
    onSuccess: async () => {
      setBlackout({ date: '', reason: '' });
      await client.invalidateQueries({ queryKey: ['agency', 'website', 'booking-settings'] });
    },
    onError: (e) => toast.error("Couldn't block the day", errorMessage(e)),
  });
  const removeBlackout = useMutation({
    mutationFn: (id: string) => api.delete(`${W}/bookings/blackouts/${id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: ['agency', 'website', 'booking-settings'] }),
  });
  if (query.isError) return <ErrorState error={query.error} />;
  if (!current) return <Skeleton height={200} />;
  const set = <K extends keyof BookingSettings>(k: K, v: BookingSettings[K]) => setDraft({ ...current, [k]: v });
  return (
    <Card>
      <CardHeader title="Availability" description="Weekly windows in the agency's time zone; visitors see slots in their own time zone." />
      <CardBody>
        <div className="cms-form">
          <Switch label="Online booking enabled" checked={current.isEnabled} onCheckedChange={(v) => set('isEnabled', v)} />
          <div className="cms-grid-2">
            <TextField label="Time zone" required value={current.timeZone} onChange={(v) => set('timeZone', v)} hint="IANA name, e.g. Europe/London" />
            <SelectField label="Slot length" required value={String(current.slotMinutes)} onChange={(v) => set('slotMinutes', Number(v))} options={[15, 20, 30, 45, 60, 90].map((m) => ({ value: String(m), label: `${m} minutes` }))} />
            <TextField label="Minimum notice (hours)" type="number" value={String(current.minNoticeHours)} onChange={(v) => set('minNoticeHours', Number(v) || 0)} />
            <TextField label="Book up to (days ahead)" type="number" value={String(current.maxDaysAhead)} onChange={(v) => set('maxDaysAhead', Number(v) || 1)} />
          </div>
          <ListEditor<AvailabilityWindow>
            legend="Weekly availability"
            items={current.weeklyAvailability}
            onChange={(v) => set('weeklyAvailability', v)}
            empty={{ day: 'Monday', start: '09:00', end: '17:00' }}
            addLabel="Add window"
            render={(w, up) => (
              <div className="cms-grid-2">
                <SelectField label="Day" value={w.day} onChange={(v) => up({ ...w, day: v })} options={DAYS.map((d) => ({ value: d, label: d }))} />
                <div className="cms-grid-2">
                  <TextField label="From" type="time" value={w.start} onChange={(v) => up({ ...w, start: v })} />
                  <TextField label="To" type="time" value={w.end} onChange={(v) => up({ ...w, end: v })} />
                </div>
              </div>
            )}
          />
          <div>
            <Button onClick={() => save.mutate()} loading={save.isPending} disabled={!draft}>
              Save availability
            </Button>
          </div>
          <fieldset className="cms-fieldset">
            <legend>Blackout days</legend>
            <ul className="cms-list">
              {current.blackouts.map((b) => (
                <li key={b.id} className="cms-list__item">
                  <span className="cms-list__body">
                    {formatDate(b.date)} {b.reason && <span className="text-muted">— {b.reason}</span>}
                  </span>
                  <Button size="sm" variant="ghost" onClick={() => removeBlackout.mutate(b.id)}>
                    Remove<span className="visually-hidden"> blackout on {b.date}</span>
                  </Button>
                </li>
              ))}
            </ul>
            <div className="cms-grid-2">
              <TextField label="Date" type="date" value={blackout.date} onChange={(v) => setBlackout((x) => ({ ...x, date: v }))} />
              <TextField label="Reason" value={blackout.reason} onChange={(v) => setBlackout((x) => ({ ...x, reason: v }))} />
            </div>
            <div>
              <Button size="sm" variant="secondary" disabled={!blackout.date} onClick={() => addBlackout.mutate()}>
                Block day
              </Button>
            </div>
          </fieldset>
        </div>
      </CardBody>
    </Card>
  );
}

function RescheduleDialog({ booking, onClose }: { booking: Booking | null; onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const slots = useQuery({
    queryKey: ['agency', 'website', 'reschedule-slots'],
    queryFn: () => api.get<{ slots: string[]; timeZone: string }>(`${W}/bookings/slots`, { query: { days: 30 } }),
    enabled: !!booking,
  });
  const [slot, setSlot] = useState('');
  const [notify, setNotify] = useState(true);
  const save = useMutation({
    mutationFn: () => api.post(`${W}/bookings/${booking!.id}/reschedule`, { slotStart: slot, notifyVisitor: notify, concurrencyStamp: booking!.concurrencyStamp }),
    onSuccess: async () => {
      toast.success('Consultation rescheduled');
      onClose();
      await client.invalidateQueries({ queryKey: ['agency', 'website', 'bookings'] });
    },
    onError: (e) => toast.error("Couldn't reschedule", errorMessage(e)),
  });
  return (
    <Dialog
      open={!!booking}
      onClose={onClose}
      title="Reschedule consultation"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={!slot} loading={save.isPending} onClick={() => save.mutate()}>
            Reschedule
          </Button>
        </>
      }
    >
      <div className="cms-form">
        <FormField label="New time" hint="Free slots from the published availability, shown in your time zone.">
          <Select value={slot} onChange={(e) => setSlot(e.target.value)} placeholder="Choose a slot" options={(slots.data?.slots ?? []).map((s) => ({ value: s, label: formatDateTime(s) }))} />
        </FormField>
        <Switch label="Email the visitor" checked={notify} onCheckedChange={setNotify} />
      </div>
    </Dialog>
  );
}

export function BookingsPage() {
  const toast = useToast();
  const client = useQueryClient();
  const [upcoming, setUpcoming] = useState(true);
  const from = upcoming ? new Date().toISOString() : undefined;
  const query = useQuery({
    queryKey: ['agency', 'website', 'bookings', upcoming],
    queryFn: () => api.get<Paged<Booking>>(`${W}/bookings`, { query: { from, pageSize: 100 } }),
  });
  const [cancelling, setCancelling] = useState<Booking | null>(null);
  const [rescheduling, setRescheduling] = useState<Booking | null>(null);
  const setStatus = useMutation({
    mutationFn: ({ b, status }: { b: Booking; status: 'Completed' | 'NoShow' }) => api.post(`${W}/bookings/${b.id}/status`, { status, concurrencyStamp: b.concurrencyStamp }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['agency', 'website', 'bookings'] }),
    onError: (e) => toast.error("Couldn't update the booking", errorMessage(e)),
  });
  return (
    <div className="cms-page">
      <PageHeader title="Consultations" description="Free strategy calls booked on the website. Double booking is prevented by the server." />
      <AvailabilityCard />
      <Card>
        <CardHeader title={upcoming ? 'Upcoming consultations' : 'All consultations'} actions={<Switch label="Upcoming only" checked={upcoming} onCheckedChange={setUpcoming} />} />
        <CardBody>
          {query.isError ? (
            <ErrorState error={query.error} />
          ) : (
            <DataTable
              caption="Consultations"
              rows={query.data?.items ?? []}
              loading={query.isLoading}
              getRowId={(r) => r.id}
              rowLabel={(r) => `${r.name} ${formatDateTime(r.slotStart)}`}
              columns={[
                { id: 'when', header: 'When', primary: true, cell: (r) => formatDateTime(r.slotStart) },
                { id: 'who', header: 'Who', cell: (r) => `${r.name}${r.company ? ` — ${r.company}` : ''}` },
                { id: 'email', header: 'Email', cell: (r) => <a href={`mailto:${r.email}`}>{r.email}</a>, hideOnMobile: true },
                { id: 'status', header: 'Status', cell: (r) => <Badge tone={r.status === 'Confirmed' ? 'success' : r.status === 'Cancelled' ? 'danger' : 'neutral'}>{human(r.status)}</Badge> },
                {
                  id: 'actions',
                  header: <span className="visually-hidden">Actions</span>,
                  align: 'right',
                  cell: (r) =>
                    r.status === 'Confirmed' ? (
                      <div className="cms-row-actions">
                        <Button size="sm" variant="secondary" onClick={() => setRescheduling(r)}>
                          Reschedule
                        </Button>
                        <Button size="sm" variant="ghost" onClick={() => setCancelling(r)}>
                          Cancel
                        </Button>
                        <Button size="sm" variant="ghost" onClick={() => setStatus.mutate({ b: r, status: 'Completed' })}>
                          Completed
                        </Button>
                        <Button size="sm" variant="ghost" onClick={() => setStatus.mutate({ b: r, status: 'NoShow' })}>
                          No-show
                        </Button>
                      </div>
                    ) : null,
                },
              ]}
              emptyState={<p className="text-muted">No consultations booked.</p>}
            />
          )}
        </CardBody>
      </Card>
      <ConfirmDialog
        open={!!cancelling}
        onClose={() => setCancelling(null)}
        title="Cancel this consultation?"
        description="The visitor is emailed with your reason and the slot becomes free again."
        requireReason
        reasonLabel="Reason (sent to the visitor)"
        tone="danger"
        confirmLabel="Cancel consultation"
        onConfirm={async ({ reason }) => {
          await api.post(`${W}/bookings/${cancelling!.id}/cancel`, { reason, notifyVisitor: true, concurrencyStamp: cancelling!.concurrencyStamp });
          toast.success('Consultation cancelled');
          setCancelling(null);
          await client.invalidateQueries({ queryKey: ['agency', 'website', 'bookings'] });
        }}
      />
      <RescheduleDialog booking={rescheduling} onClose={() => setRescheduling(null)} />
    </div>
  );
}

// ---------------------------------------------------------------- Newsletter

export function SubscribersPage() {
  const toast = useToast();
  const client = useQueryClient();
  const [erasing, setErasing] = useState<Subscriber | null>(null);
  const unsubscribe = useMutation({
    mutationFn: (s: Subscriber) => api.post<Subscriber>(`${W}/newsletter/subscribers/${s.id}/unsubscribe`),
    onSuccess: async (s) => {
      toast.success('Unsubscribed', `${s.email} won't receive the newsletter any more.`);
      await client.invalidateQueries({ queryKey: ['agency', 'website', 'subscribers'] });
    },
    onError: (e) => toast.error("Couldn't unsubscribe", errorMessage(e)),
  });
  const [status, setStatus] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const query = useQuery({
    queryKey: ['agency', 'website', 'subscribers', status, search, page],
    queryFn: () => api.get<Paged<Subscriber>>(`${W}/newsletter/subscribers`, { query: { status, search, page, pageSize: 50 } }),
    placeholderData: (previous) => previous,
  });
  return (
    <div className="cms-page">
      <PageHeader
        title="Newsletter subscribers"
        description="Double opt-in: only Confirmed subscribers may be emailed. Consent version, time and source are kept for every signup."
        actions={
          <Button variant="secondary" leadingIcon={<Download />} onClick={() => void api.download(`${W}/newsletter/subscribers/export.csv`, 'newsletter-subscribers.csv', { query: { status } })}>
            Export CSV
          </Button>
        }
      />
      <FilterBar
        search={search}
        onSearchChange={(v) => {
          setSearch(v);
          setPage(1);
        }}
        searchLabel="Search email"
        filters={[{ id: 'status', label: 'Status', options: ['Pending', 'Confirmed', 'Unsubscribed'].map((s) => ({ value: s, label: s })) }]}
        values={{ status }}
        onFilterChange={(_, v) => {
          setStatus(v);
          setPage(1);
        }}
        onReset={() => {
          setStatus(undefined);
          setSearch('');
        }}
      />
      {query.isError ? (
        <ErrorState error={query.error} />
      ) : (
        <>
          <DataTable
            caption="Subscribers"
            rows={query.data?.items ?? []}
            loading={query.isLoading}
            getRowId={(r) => r.id}
            rowLabel={(r) => r.email}
            columns={[
              { id: 'email', header: 'Email', primary: true, cell: (r) => r.email },
              { id: 'status', header: 'Status', cell: (r) => <Badge tone={r.status === 'Confirmed' ? 'success' : r.status === 'Pending' ? 'warning' : 'neutral'}>{r.status}</Badge> },
              { id: 'source', header: 'Source', cell: (r) => r.source ?? '—', hideOnMobile: true },
              { id: 'consent', header: 'Consent', cell: (r) => `${r.consentVersion} · ${formatDate(r.consentAt)}`, hideOnMobile: true },
            ]}
            rowActions={(r) => [
              ...(r.status !== 'Unsubscribed'
                ? [{ id: 'unsubscribe', label: 'Unsubscribe', onSelect: () => unsubscribe.mutate(r) }]
                : []),
              { id: 'erase', label: 'Erase subscriber…', danger: true, onSelect: () => setErasing(r) },
            ]}
            emptyState={<p className="text-muted">No subscribers yet.</p>}
          />
          {query.data && query.data.total > query.data.pageSize && <Pagination page={page} pageSize={query.data.pageSize} total={query.data.total} onPageChange={setPage} />}
        </>
      )}
      <ConfirmDialog
        open={!!erasing}
        onClose={() => setErasing(null)}
        title={`Erase ${erasing?.email ?? ''}?`}
        description="Removes the address and its consent record permanently (for a data-erasure request). To just stop emails, unsubscribe instead."
        confirmLabel="Erase subscriber"
        tone="danger"
        onConfirm={async () => {
          await api.delete(`${W}/newsletter/subscribers/${erasing!.id}`);
          toast.success('Subscriber erased');
          setErasing(null);
          await client.invalidateQueries({ queryKey: ['agency', 'website', 'subscribers'] });
        }}
      />
    </div>
  );
}
