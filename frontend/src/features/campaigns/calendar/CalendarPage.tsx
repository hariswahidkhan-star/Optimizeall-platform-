import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CalendarDays, ChevronLeft, ChevronRight, Plus } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  ConfirmDialog,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Select,
  Skeleton,
  Textarea,
  useToast,
  type Tone,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import { browserTimeZone, formatTime, toZonedDateKey } from '@/lib/format/dates';
import { browserLocale } from '@/lib/format/locale';
import { useIsMobile } from '@/lib/hooks/useMediaQuery';
import { qk, useCampaignOptions, useTemplateOptions } from '../api/queries';
import {
  CALENDAR_STATUSES,
  type CalendarEntry,
  type CalendarEntryInput,
  type CalendarEntryStatus,
  type CalendarResponse,
  type SocialPlatform,
} from '../api/types';
import { fieldError, fieldErrorsFrom, type FieldErrorMap } from '../shared/formErrors';
import { platformOptions } from '../shared/labels';
import { isoToZonedInput, zonedInputToIso } from '../shared/zonedTime';
import '../campaigns.css';

const STATUS_TONES: Record<CalendarEntryStatus, Tone> = {
  Planned: 'neutral',
  Scheduled: 'info',
  Published: 'success',
  Cancelled: 'danger',
};

const CODE_FIELDS: Record<string, string> = {
  'calendar.campaign_not_found': 'campaignId',
  'calendar.template_not_found': 'templateId',
  'calendar.template_archived': 'templateId',
};

interface Month {
  year: number;
  month: number; // 0-11
}

function monthOf(date: Date, timeZone: string): Month {
  const [y, m] = toZonedDateKey(date, timeZone).split('-').map(Number) as [number, number];
  return { year: y, month: m - 1 };
}

function shift(m: Month, delta: number): Month {
  const d = new Date(Date.UTC(m.year, m.month + delta, 1));
  return { year: d.getUTCFullYear(), month: d.getUTCMonth() };
}

const pad = (n: number) => String(n).padStart(2, '0');

/** The 6×7 grid of date keys covering a month (weeks start on Monday). */
function monthGrid(m: Month): string[] {
  const first = new Date(Date.UTC(m.year, m.month, 1));
  const offset = (first.getUTCDay() + 6) % 7;
  return Array.from({ length: 42 }, (_, i) => {
    const d = new Date(Date.UTC(m.year, m.month, 1 - offset + i));
    return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
  });
}

export function CalendarPage() {
  const { user } = useAuth();
  const timeZone = user?.timeZone || browserTimeZone();
  const isMobile = useIsMobile();
  const queryClient = useQueryClient();
  const [month, setMonth] = useState<Month>(() => monthOf(new Date(), timeZone));
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [editing, setEditing] = useState<CalendarEntry | { scheduledFor: string } | null>(null);
  const campaigns = useCampaignOptions();

  const grid = useMemo(() => monthGrid(month), [month]);
  // Query a little wider than the grid so zone offsets never drop an entry.
  const from = new Date(`${grid[0]}T00:00:00Z`).getTime() - 24 * 3600_000;
  const to = new Date(`${grid[grid.length - 1]}T23:59:59Z`).getTime() + 24 * 3600_000;
  const params = {
    from: new Date(from).toISOString(),
    to: new Date(to).toISOString(),
    campaignId: filters.campaign,
    platform: filters.platform,
    status: filters.status,
  };
  const query = useQuery({
    queryKey: qk.calendar(params),
    queryFn: () => api.get<CalendarResponse>('/marketing/calendar', { query: params }),
    placeholderData: keepPreviousData,
  });

  const byDay = useMemo(() => {
    const map = new Map<string, CalendarEntry[]>();
    for (const entry of query.data?.items ?? []) {
      const key = toZonedDateKey(entry.scheduledFor, timeZone);
      map.set(key, [...(map.get(key) ?? []), entry]);
    }
    return map;
  }, [query.data, timeZone]);

  const monthPrefix = `${month.year}-${pad(month.month + 1)}`;
  const monthLabel = new Intl.DateTimeFormat(browserLocale(), {
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(month.year, month.month, 1)));
  const today = toZonedDateKey(new Date(), timeZone);
  const weekdays = Array.from({ length: 7 }, (_, i) =>
    new Intl.DateTimeFormat(browserLocale(), { weekday: 'short', timeZone: 'UTC' }).format(
      new Date(Date.UTC(2024, 0, 1 + i)),
    ),
  );
  const monthDays = grid.filter((d) => d.startsWith(monthPrefix));
  const newAt = (day: string) => ({
    scheduledFor: zonedInputToIso(`${day}T10:00`, timeZone) ?? new Date().toISOString(),
  });

  return (
    <>
      <PageHeader
        title="Content calendar"
        description={`What goes live when, across campaigns. Times are shown in ${timeZone}.`}
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setEditing(newAt(today))}>
            New entry
          </Button>
        }
      />
      <div className="stack">
        <FilterBar
          filters={[
            {
              id: 'campaign',
              label: 'Campaign',
              options: (campaigns.data?.items ?? []).map((c) => ({ value: c.id, label: c.title })),
            },
            { id: 'platform', label: 'Platform', options: platformOptions },
            {
              id: 'status',
              label: 'Status',
              options: CALENDAR_STATUSES.map((s) => ({ value: s, label: s })),
            },
          ]}
          values={filters}
          onFilterChange={(id, value) => setFilters((f) => ({ ...f, [id]: value }))}
          onReset={() => setFilters({})}
        />
        <div className="cluster mg-space-between">
          <h2 className="mg-h2" aria-live="polite">
            {monthLabel}
          </h2>
          <div className="cluster mg-cluster-sm">
            <IconButton
              label="Previous month"
              icon={<ChevronLeft />}
              variant="secondary"
              onClick={() => setMonth((m) => shift(m, -1))}
            />
            <Button variant="secondary" size="sm" onClick={() => setMonth(monthOf(new Date(), timeZone))}>
              Today
            </Button>
            <IconButton
              label="Next month"
              icon={<ChevronRight />}
              variant="secondary"
              onClick={() => setMonth((m) => shift(m, 1))}
            />
          </div>
        </div>

        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : query.isLoading ? (
          <Skeleton height={420} />
        ) : isMobile ? (
          <MobileList days={monthDays} byDay={byDay} timeZone={timeZone} onOpen={setEditing} />
        ) : (
          <div className="mg-cal" role="table" aria-label={`Calendar for ${monthLabel}`}>
            <div className="mg-cal__row" role="row">
              {weekdays.map((d) => (
                <div key={d} role="columnheader" className="mg-cal__head">
                  {d}
                </div>
              ))}
            </div>
            {Array.from({ length: 6 }, (_, week) => (
              <div className="mg-cal__row" role="row" key={week}>
                {grid.slice(week * 7, week * 7 + 7).map((day) => {
                  const entries = byDay.get(day) ?? [];
                  const inMonth = day.startsWith(monthPrefix);
                  const dayNumber = Number(day.slice(8));
                  return (
                    <div
                      key={day}
                      role="cell"
                      className="mg-cal__cell"
                      data-outside={!inMonth || undefined}
                      data-today={day === today || undefined}
                      aria-label={`${day}${entries.length ? `, ${entries.length} entries` : ''}`}
                    >
                      <div className="mg-cal__day">
                        <span aria-hidden="true">{dayNumber}</span>
                        <IconButton
                          size="sm"
                          variant="ghost"
                          label={`Add entry on ${day}`}
                          icon={<Plus />}
                          className="mg-cal__add"
                          onClick={() => setEditing(newAt(day))}
                        />
                      </div>
                      <ul className="mg-cal__entries">
                        {entries.map((e) => (
                          <li key={e.id}>
                            <button
                              type="button"
                              className="mg-cal__entry"
                              data-status={e.status}
                              onClick={() => setEditing(e)}
                            >
                              <span className="mg-cal__time">{formatTime(e.scheduledFor, { timeZone })}</span>
                              <span className="mg-cal__title">{e.title}</span>
                              <span className="visually-hidden">, {e.status}</span>
                            </button>
                          </li>
                        ))}
                      </ul>
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
        )}
      </div>
      {editing && (
        <EntryDialog
          entry={'id' in editing ? editing : null}
          defaultScheduledFor={editing.scheduledFor}
          timeZone={timeZone}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void queryClient.invalidateQueries({ queryKey: ['manage', 'calendar'] });
          }}
        />
      )}
    </>
  );
}

function MobileList({
  days,
  byDay,
  timeZone,
  onOpen,
}: {
  days: string[];
  byDay: Map<string, CalendarEntry[]>;
  timeZone: string;
  onOpen: (entry: CalendarEntry) => void;
}) {
  const withEntries = days.filter((d) => (byDay.get(d) ?? []).length > 0);
  if (withEntries.length === 0) {
    return <EmptyState icon={<CalendarDays />} headingLevel={3} title="Nothing scheduled this month" />;
  }
  return (
    <ol className="mg-cal-list" aria-label="Scheduled entries">
      {withEntries.map((day) => (
        <li key={day}>
          <h3 className="mg-cal-list__day">
            <DateTime value={`${day}T12:00:00Z`} format="date" timeZone="UTC" />
          </h3>
          <ul className="stack mg-stack-sm">
            {(byDay.get(day) ?? []).map((e) => (
              <li key={e.id}>
                <button type="button" className="mg-cal-list__entry" onClick={() => onOpen(e)}>
                  <span className="mg-strong">{e.title}</span>
                  <span className="cluster mg-cluster-sm text-small">
                    <DateTime value={e.scheduledFor} timeZone={timeZone} />
                    <Badge size="sm" tone={STATUS_TONES[e.status]}>
                      {e.status}
                    </Badge>
                    {e.platform && <Badge size="sm">{e.platform}</Badge>}
                    {e.campaignTitle && <span className="text-muted">{e.campaignTitle}</span>}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </li>
      ))}
    </ol>
  );
}

function EntryDialog({
  entry,
  defaultScheduledFor,
  timeZone,
  onClose,
  onSaved,
}: {
  entry: CalendarEntry | null;
  defaultScheduledFor: string;
  timeZone: string;
  onClose: () => void;
  onSaved: () => void;
}) {
  const toast = useToast();
  const campaigns = useCampaignOptions();
  const templates = useTemplateOptions();
  const [form, setForm] = useState({
    title: entry?.title ?? '',
    campaignId: entry?.campaignId ?? '',
    templateId: entry?.templateId ?? '',
    platform: entry?.platform ?? '',
    scheduledFor: isoToZonedInput(entry?.scheduledFor ?? defaultScheduledFor, timeZone),
    notes: entry?.notes ?? '',
    status: (entry?.status ?? 'Planned') as CalendarEntryStatus,
  });
  const [errors, setErrors] = useState<FieldErrorMap>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));

  const save = useMutation({
    mutationFn: (body: CalendarEntryInput) =>
      entry
        ? api.put<CalendarEntry>(`/marketing/calendar/${entry.id}`, body)
        : api.post<CalendarEntry>('/marketing/calendar', body),
    onSuccess: () => {
      toast.success(entry ? 'Entry saved' : 'Entry added');
      onSaved();
    },
    onError: (err) => {
      const mapped = fieldErrorsFrom(err, CODE_FIELDS);
      setErrors(mapped);
      setFormError(Object.keys(mapped).length ? null : errorMessage(err));
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const scheduledFor = zonedInputToIso(form.scheduledFor, timeZone);
    const local: FieldErrorMap = {};
    if (form.title.trim().length < 2) local.title = ['Enter a title (at least 2 characters).'];
    if (!scheduledFor) local.scheduledfor = ['Choose a date and time.'];
    setErrors(local);
    if (Object.keys(local).length || !scheduledFor) return;
    save.mutate({
      title: form.title.trim(),
      campaignId: form.campaignId || null,
      templateId: form.templateId || null,
      platform: (form.platform || null) as SocialPlatform | null,
      scheduledFor,
      notes: form.notes.trim() || null,
      status: form.status,
    });
  };

  return (
    <>
      <Dialog
        open={!confirmDelete}
        onClose={onClose}
        title={entry ? 'Edit calendar entry' : 'New calendar entry'}
        dismissible={!save.isPending}
        footer={
          <>
            {entry && (
              <Button variant="danger" className="mg-push-left" onClick={() => setConfirmDelete(true)}>
                Delete
              </Button>
            )}
            <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
              Cancel
            </Button>
            <Button type="submit" form="calendar-form" loading={save.isPending}>
              {entry ? 'Save entry' : 'Add entry'}
            </Button>
          </>
        }
      >
        <form id="calendar-form" className="stack" onSubmit={submit} noValidate>
          {formError && (
            <Alert tone="danger" role="alert">
              {formError}
            </Alert>
          )}
          <FormField label="Title" required error={fieldError(errors, 'title')}>
            <Input value={form.title} maxLength={200} onChange={(e) => set({ title: e.target.value })} />
          </FormField>
          <div className="mg-grid mg-grid--2">
            <FormField
              label="Scheduled for"
              required
              hint={`In ${timeZone}`}
              error={fieldError(errors, 'scheduledFor')}
            >
              <Input
                type="datetime-local"
                value={form.scheduledFor}
                onChange={(e) => set({ scheduledFor: e.target.value })}
              />
            </FormField>
            <FormField label="Status" error={fieldError(errors, 'status')}>
              <Select
                value={form.status}
                options={CALENDAR_STATUSES.map((s) => ({ value: s, label: s }))}
                onChange={(e) => set({ status: e.target.value as CalendarEntryStatus })}
              />
            </FormField>
            <FormField label="Campaign" optional error={fieldError(errors, 'campaignId')}>
              <Select
                value={form.campaignId}
                options={[
                  { value: '', label: 'No campaign' },
                  ...(campaigns.data?.items ?? []).map((c) => ({ value: c.id, label: c.title })),
                ]}
                onChange={(e) => set({ campaignId: e.target.value })}
              />
            </FormField>
            <FormField label="Template" optional error={fieldError(errors, 'templateId')}>
              <Select
                value={form.templateId}
                options={[
                  { value: '', label: 'No template' },
                  ...(templates.data?.items ?? [])
                    .filter((t) => !t.isArchived || t.id === form.templateId)
                    .map((t) => ({ value: t.id, label: t.name })),
                ]}
                onChange={(e) => set({ templateId: e.target.value })}
              />
            </FormField>
            <FormField label="Platform" optional error={fieldError(errors, 'platform')}>
              <Select
                value={form.platform}
                options={[{ value: '', label: 'Any platform' }, ...platformOptions]}
                onChange={(e) => set({ platform: e.target.value })}
              />
            </FormField>
          </div>
          <FormField label="Notes" optional error={fieldError(errors, 'notes')}>
            <Textarea
              value={form.notes}
              rows={3}
              maxLength={2000}
              onChange={(e) => set({ notes: e.target.value })}
            />
          </FormField>
        </form>
      </Dialog>
      <ConfirmDialog
        open={confirmDelete}
        onClose={() => setConfirmDelete(false)}
        tone="danger"
        title="Delete this calendar entry?"
        confirmLabel="Delete entry"
        onConfirm={async () => {
          if (!entry) return;
          await api.delete(`/marketing/calendar/${entry.id}`);
          toast.success('Entry deleted');
          onSaved();
        }}
      />
    </>
  );
}
