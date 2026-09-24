import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CalendarDays, ChevronLeft, ChevronRight, MoveRight, Plus } from 'lucide-react';
import { useMemo, useState, type DragEvent, type KeyboardEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Button,
  ButtonLink,
  Card,
  CardBody,
  CardHeader,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  IconButton,
  Input,
  PageHeader,
  Skeleton,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { NETWORK_LABELS, socialKeys, type CalendarResponse, type Post, type PostSummary } from './api';
import { ClientPicker, NetworkChip, PostStatusBadge, fromLocalInput, toLocalInput, useClientParam } from './shared';
import './social.css';

type View = 'month' | 'week' | 'list';

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

function mondayOf(d: Date): Date {
  const day = startOfDay(d);
  const offset = (day.getDay() + 6) % 7;
  return new Date(day.getFullYear(), day.getMonth(), day.getDate() - offset);
}

function addDays(d: Date, n: number): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate() + n, d.getHours(), d.getMinutes());
}

function dayKey(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Keeps the time of day, moves to the target day. */
export function moveToDay(iso: string, target: Date): string {
  const current = new Date(iso);
  return new Date(target.getFullYear(), target.getMonth(), target.getDate(), current.getHours(), current.getMinutes()).toISOString();
}

export function CalendarPage() {
  const [clientId, setClientId] = useClientParam();
  const [view, setView] = useState<View>('month');
  const [anchor, setAnchor] = useState(() => startOfDay(new Date()));
  const [dragging, setDragging] = useState<PostSummary | null>(null);
  const [dropTarget, setDropTarget] = useState<string | null>(null);
  const [moving, setMoving] = useState<PostSummary | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const toast = useToast();
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const range = useMemo(() => {
    if (view === 'week') {
      const from = mondayOf(anchor);
      return { from, to: addDays(from, 7), days: Array.from({ length: 7 }, (_, i) => addDays(from, i)) };
    }
    const first = new Date(anchor.getFullYear(), anchor.getMonth(), 1);
    const from = mondayOf(first);
    return { from, to: addDays(from, 42), days: Array.from({ length: 42 }, (_, i) => addDays(from, i)) };
  }, [anchor, view]);

  const params = { clientId, from: range.from.toISOString(), to: range.to.toISOString() };
  const query = useQuery({
    queryKey: socialKeys.calendar(params),
    queryFn: () => api.get<CalendarResponse>('/agency/social/calendar', { query: params }),
    placeholderData: keepPreviousData,
  });

  const byDay = useMemo(() => {
    const map = new Map<string, PostSummary[]>();
    for (const p of query.data?.posts ?? []) {
      if (!p.scheduledAt) continue;
      const key = dayKey(new Date(p.scheduledAt));
      map.set(key, [...(map.get(key) ?? []), p]);
    }
    return map;
  }, [query.data]);

  const awareness = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const d of query.data?.awarenessDays ?? []) map.set(d.date, [...(map.get(d.date) ?? []), d.name]);
    return map;
  }, [query.data]);

  const reschedule = useMutation({
    mutationFn: ({ post, at }: { post: PostSummary; at: string }) =>
      api.post<Post>(`/agency/social/posts/${post.id}/reschedule`, { scheduledAt: at, concurrencyStamp: post.concurrencyStamp }),
    onSuccess: (updated) => {
      setAnnouncement(`Moved “${updated.title}” to ${new Date(updated.scheduledAt ?? '').toLocaleString()}.`);
      toast.success('Post moved');
      void queryClient.invalidateQueries({ queryKey: socialKeys.all });
    },
    onError: (error) => toast.error('Could not move the post', errorMessage(error)),
  });

  const canMove = (p: PostSummary) => p.status !== 'Publishing' && p.status !== 'Published';

  const onDrop = (e: DragEvent, day: Date) => {
    e.preventDefault();
    setDropTarget(null);
    const post = dragging;
    setDragging(null);
    if (!post?.scheduledAt || !canMove(post)) return;
    if (dayKey(new Date(post.scheduledAt)) === dayKey(day)) return;
    reschedule.mutate({ post, at: moveToDay(post.scheduledAt, day) });
  };

  /** Keyboard alternative to drag: Alt+←/→ moves a day, Alt+↑/↓ a week. */
  const onChipKey = (e: KeyboardEvent, post: PostSummary) => {
    if (!e.altKey || !post.scheduledAt || !canMove(post)) return;
    const delta = ({ ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 } as Record<string, number>)[e.key];
    if (!delta) return;
    e.preventDefault();
    reschedule.mutate({ post, at: addDays(new Date(post.scheduledAt), delta).toISOString() });
  };

  const chip = (p: PostSummary) => (
    <div key={p.id} className="stack" style={{ gap: 2 }}>
      <button
        type="button"
        className={`sm-chip sm-chip--${p.status}${dragging?.id === p.id ? ' sm-chip--moving' : ''}`}
        draggable={canMove(p)}
        onDragStart={(e) => {
          setDragging(p);
          e.dataTransfer?.setData?.('text/plain', p.id);
        }}
        onDragEnd={() => setDragging(null)}
        onKeyDown={(e) => onChipKey(e, p)}
        onClick={() => navigate(`/agency/social/posts/${p.id}`)}
        aria-label={`${p.title}, ${p.status}, ${p.scheduledAt ? new Date(p.scheduledAt).toLocaleString() : 'unscheduled'}${
          canMove(p) ? '. Press Alt and an arrow key to move it.' : ''
        }`}
        aria-describedby="sm-calendar-help"
      >
        <span className="sm-chip__title">
          {p.scheduledAt && new Date(p.scheduledAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} {p.title}
        </span>
        <span className="sm-network-list">
          {p.networks.map((n) => (
            <NetworkChip key={n} network={n} />
          ))}
        </span>
        {!clientId && <span className="sm-muted">{p.clientName}</span>}
        <PostStatusBadge status={p.status} />
      </button>
      {canMove(p) && (
        <IconButton size="sm" variant="ghost" label={`Move ${p.title} to another date`} icon={<MoveRight />} onClick={() => setMoving(p)} />
      )}
    </div>
  );

  const today = dayKey(new Date());
  const listColumns: DataTableColumn<PostSummary>[] = [
    {
      id: 'when',
      header: 'When',
      primary: true,
      cell: (p) => (p.scheduledAt ? <DateTime value={p.scheduledAt} format="datetime" /> : '—'),
    },
    {
      id: 'title',
      header: 'Post',
      cell: (p) => (
        <Link className="ui-link" to={`/agency/social/posts/${p.id}`}>
          {p.title}
        </Link>
      ),
    },
    { id: 'client', header: 'Client', cell: (p) => p.clientName, hideOnMobile: true },
    {
      id: 'networks',
      header: 'Networks',
      cell: (p) => (
        <span className="sm-network-list">
          {p.networks.map((n) => (
            <NetworkChip key={n} network={n} />
          ))}
        </span>
      ),
    },
    { id: 'status', header: 'Status', cell: (p) => <PostStatusBadge status={p.status} /> },
  ];

  const title =
    view === 'week'
      ? `Week of ${range.from.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })}`
      : anchor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });

  return (
    <>
      <PageHeader
        title="Content calendar"
        description="Plan, review and reschedule posts across clients. Drag a post to another day, or focus it and press Alt with an arrow key."
        actions={
          <ButtonLink to={`/agency/social/compose${clientId ? `?client=${clientId}` : ''}`} leadingIcon={<Plus />}>
            New post
          </ButtonLink>
        }
      />
      <div className="sm-toolbar">
        <ClientPicker value={clientId} onChange={setClientId} allowAll />
        <div className="cluster" role="group" aria-label="Calendar view">
          {(['month', 'week', 'list'] as View[]).map((v) => (
            <Button key={v} size="sm" variant={view === v ? 'primary' : 'secondary'} aria-pressed={view === v} onClick={() => setView(v)}>
              {v[0]!.toUpperCase() + v.slice(1)}
            </Button>
          ))}
        </div>
        <div className="cluster" role="group" aria-label="Navigate">
          <IconButton
            label={view === 'week' ? 'Previous week' : 'Previous month'}
            icon={<ChevronLeft />}
            variant="secondary"
            onClick={() => setAnchor((a) => (view === 'week' ? addDays(a, -7) : new Date(a.getFullYear(), a.getMonth() - 1, 1)))}
          />
          <Button size="sm" variant="secondary" onClick={() => setAnchor(startOfDay(new Date()))}>
            Today
          </Button>
          <IconButton
            label={view === 'week' ? 'Next week' : 'Next month'}
            icon={<ChevronRight />}
            variant="secondary"
            onClick={() => setAnchor((a) => (view === 'week' ? addDays(a, 7) : new Date(a.getFullYear(), a.getMonth() + 1, 1)))}
          />
        </div>
      </div>
      <p id="sm-calendar-help" className="visually-hidden">
        Alt plus left or right arrow moves the post by one day; Alt plus up or down arrow moves it by one week.
      </p>
      <div aria-live="polite" className="visually-hidden">
        {announcement}
      </div>

      <div className="sm-grid-2" style={{ gridTemplateColumns: 'minmax(0, 1fr)' }}>
        <section aria-labelledby="sm-calendar-title" className="stack">
          <h2 id="sm-calendar-title" className="sm-h2">
            {title}
          </h2>
          {query.isError ? (
            <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          ) : query.isLoading ? (
            <Skeleton height="24rem" />
          ) : view === 'list' ? (
            <DataTable
              caption="Scheduled and planned posts"
              columns={listColumns}
              rows={query.data?.posts ?? []}
              getRowId={(p) => p.id}
              emptyState={<EmptyState icon={<CalendarDays />} title="Nothing planned in this period" headingLevel={3} />}
            />
          ) : (
            <div className="sm-calendar" role="grid" aria-labelledby="sm-calendar-title">
              <div role="row" style={{ display: 'contents' }}>
                {WEEKDAYS.map((d) => (
                  <div key={d} role="columnheader" className="sm-calendar__head">
                    {d}
                  </div>
                ))}
              </div>
              {Array.from({ length: range.days.length / 7 }, (_, w) => (
                <div role="row" key={w} style={{ display: 'contents' }}>
                  {range.days.slice(w * 7, w * 7 + 7).map((day) => {
                    const key = dayKey(day);
                    const posts = byDay.get(key) ?? [];
                    const outside = view === 'month' && day.getMonth() !== anchor.getMonth();
                    return (
                      <div
                        key={key}
                        role="gridcell"
                        tabIndex={-1}
                        aria-label={day.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' })}
                        data-date={key}
                        className={`sm-day${outside ? ' sm-day--outside' : ''}${key === today ? ' sm-day--today' : ''}${
                          dropTarget === key ? ' sm-day--drop' : ''
                        }`}
                        onDragOver={(e) => {
                          if (!dragging) return;
                          e.preventDefault();
                          setDropTarget(key);
                        }}
                        onDragLeave={() => setDropTarget((t) => (t === key ? null : t))}
                        onDrop={(e) => onDrop(e, day)}
                      >
                        <span className="sm-day__number" aria-hidden="true">
                          {day.getDate()}
                        </span>
                        {(awareness.get(key) ?? []).map((name) => (
                          <span key={name} className="sm-awareness">
                            ★ {name}
                          </span>
                        ))}
                        {posts.map(chip)}
                      </div>
                    );
                  })}
                </div>
              ))}
            </div>
          )}
        </section>

        <Card as="section" aria-labelledby="sm-best-times">
          <CardHeader title="Recommended posting times" titleId="sm-best-times" />
          <CardBody className="stack">
            <ul className="sm-issues" style={{ paddingLeft: 0, listStyle: 'none' }}>
              {(query.data?.bestTimes ?? []).map((b) => (
                <li key={b.network}>
                  <strong>{NETWORK_LABELS[b.network]}:</strong> {b.times.join(', ')} (client's local time)
                </li>
              ))}
            </ul>
            <p className="sm-muted">{query.data?.bestTimes[0]?.source}</p>
            {(query.data?.awarenessDays.length ?? 0) > 0 && (
              <>
                <h3 className="sm-h3">Holidays & awareness days</h3>
                <ul className="sm-issues">
                  {query.data!.awarenessDays.map((d) => (
                    <li key={`${d.date}-${d.name}`}>
                      <DateTime value={d.date} format="date" /> — {d.name}{' '}
                      <a className="ui-link" href={d.sourceUrl} target="_blank" rel="noreferrer noopener">
                        source
                      </a>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </CardBody>
        </Card>
      </div>

      <MoveDialog
        post={moving}
        onClose={() => setMoving(null)}
        onMove={(at) => {
          if (moving) reschedule.mutate({ post: moving, at });
          setMoving(null);
        }}
      />
    </>
  );
}

function MoveDialog({ post, onClose, onMove }: { post: PostSummary | null; onClose: () => void; onMove: (iso: string) => void }) {
  const [value, setValue] = useState('');
  const [error, setError] = useState<string | null>(null);
  const open = !!post;
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Move post"
      description={post ? `Choose the new date and time for “${post.title}”.` : undefined}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={() => {
              const iso = fromLocalInput(value || toLocalInput(post?.scheduledAt));
              if (!iso) {
                setError('Enter a date and time.');
                return;
              }
              setError(null);
              onMove(iso);
              setValue('');
            }}
          >
            Move
          </Button>
        </>
      }
    >
      <FormField label="New date and time" error={error}>
        <Input type="datetime-local" value={value || toLocalInput(post?.scheduledAt)} onChange={(e) => setValue(e.target.value)} />
      </FormField>
    </Dialog>
  );
}

