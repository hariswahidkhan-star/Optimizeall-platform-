import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { Bell, CheckCheck } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { pluralize } from '@/lib/format/text';
import { qk, useNotifications, useUnreadCount } from '../api/queries';
import type { NotificationItem, PagedResult, ParticipantHome } from '../api/types';
import { QueryState } from '../components/QueryState';
import '../participant.css';
import { isInternalHref } from '@/lib/safeHref';

const PAGE_SIZE = 20;

type Snapshot = [readonly unknown[], unknown][];

/** Optimistically marks notifications read in every cached list and the unread counters. */
function applyRead(client: QueryClient, id: string | 'all'): Snapshot {
  const snapshot: Snapshot = [
    ...client.getQueriesData({ queryKey: qk.notifications }),
    [qk.home, client.getQueryData(qk.home)],
  ];
  const now = new Date().toISOString();
  let changed = 0;
  client.setQueriesData<PagedResult<NotificationItem>>(
    { queryKey: [...qk.notifications, 'list'] },
    (page) => {
      if (!page) return page;
      return {
        ...page,
        items: page.items.map((n) => {
          if ((id === 'all' || n.id === id) && !n.isRead) {
            changed += 1;
            return { ...n, isRead: true, readAt: now };
          }
          return n;
        }),
      };
    },
  );
  const decrement = (count: number) => (id === 'all' ? 0 : Math.max(0, count - (changed > 0 ? 1 : 0)));
  client.setQueryData<{ count: number }>(qk.unreadCount, (c) => (c ? { count: decrement(c.count) } : c));
  client.setQueryData<ParticipantHome>(qk.home, (h) =>
    h ? { ...h, unreadNotificationCount: decrement(h.unreadNotificationCount) } : h,
  );
  return snapshot;
}

function useMarkRead() {
  const client = useQueryClient();
  const toast = useToast();
  return useMutation({
    mutationFn: (id: string | 'all') =>
      id === 'all' ? api.post('/me/notifications/read-all') : api.post(`/me/notifications/${id}/read`),
    onMutate: async (id) => {
      await client.cancelQueries({ queryKey: qk.notifications });
      return { snapshot: applyRead(client, id) };
    },
    onError: (error, _id, context) => {
      context?.snapshot.forEach(([key, data]) => client.setQueryData(key, data));
      toast.error('Couldn’t mark as read', errorMessage(error));
    },
    onSuccess: (_data, id) => {
      if (id === 'all') toast.success('All notifications marked as read');
    },
    onSettled: () => {
      void client.invalidateQueries({ queryKey: qk.notifications });
      void client.invalidateQueries({ queryKey: qk.home });
    },
  });
}

/** Marks a read notification unread again, so it stays on the "Unread" list as a reminder. */
function useMarkUnread() {
  const client = useQueryClient();
  const toast = useToast();
  return useMutation({
    mutationFn: (id: string) => api.post(`/me/notifications/${id}/unread`),
    onError: (error) => toast.error('Couldn’t mark as unread', errorMessage(error)),
    onSettled: () => {
      void client.invalidateQueries({ queryKey: qk.notifications });
      void client.invalidateQueries({ queryKey: qk.unreadCount });
      void client.invalidateQueries({ queryKey: qk.home });
    },
  });
}

function NotificationRow({
  n,
  onRead,
  onUnread,
}: {
  n: NotificationItem;
  onRead: (id: string) => void;
  onUnread: (id: string) => void;
}) {
  const link = n.linkUrl;
  return (
    <li className="pp-list__item pp-notification" data-unread={!n.isRead}>
      <div className="pp-list__main">
        <span className="pp-list__title">
          {isInternalHref(link) ? (
            <Link to={link} className="ui-link" onClick={() => !n.isRead && onRead(n.id)}>
              {n.title}
            </Link>
          ) : (
            n.title
          )}
          {!n.isRead && <span className="visually-hidden"> (unread)</span>}
        </span>
        <span className="text-small pp-prewrap">{n.body}</span>
        <span className="pp-list__meta">
          <DateTime value={n.createdAt} format="relative" />
        </span>
      </div>
      {!n.isRead && (
        <Button
          size="sm"
          variant="ghost"
          onClick={() => onRead(n.id)}
          aria-label={`Mark “${n.title}” as read`}
        >
          Mark read
        </Button>
      )}
      {n.isRead && (
        <Button size="sm" variant="ghost" onClick={() => onUnread(n.id)} aria-label={`Mark “${n.title}” as unread`}>
          Mark unread
        </Button>
      )}
    </li>
  );
}

export function NotificationsPage() {
  const [params, setParams] = useSearchParams();
  const unreadOnly = params.get('unread') === '1';
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1);
  const list = useNotifications({ unreadOnly, page, pageSize: PAGE_SIZE });
  const unread = useUnreadCount();
  const markRead = useMarkRead();
  const markUnread = useMarkUnread();
  const unreadCount = unread.data?.count ?? 0;

  return (
    <div className="pp-page">
      <PageHeader
        title="Notifications"
        description="Updates about your submissions, earnings, payouts and account."
        actions={
          <div className="pp-actions">
            <Link to="/app/profile/notification-preferences" className="ui-link">
              Preferences
            </Link>
            <Button
              variant="secondary"
              leadingIcon={<CheckCheck />}
              disabled={unreadCount === 0}
              loading={markRead.isPending && markRead.variables === 'all'}
              onClick={() => markRead.mutate('all')}
            >
              Mark all as read
            </Button>
          </div>
        }
      />

      <div className="pp-actions" role="group" aria-label="Show">
        <Button
          variant={unreadOnly ? 'ghost' : 'secondary'}
          size="sm"
          aria-pressed={!unreadOnly}
          onClick={() => setParams({})}
        >
          All
        </Button>
        <Button
          variant={unreadOnly ? 'secondary' : 'ghost'}
          size="sm"
          aria-pressed={unreadOnly}
          onClick={() => setParams({ unread: '1' })}
        >
          Unread{unreadCount > 0 ? ` (${unreadCount})` : ''}
        </Button>
      </div>
      <p className="visually-hidden" aria-live="polite">
        {unread.isSuccess ? `${pluralize(unreadCount, 'unread notification')}` : ''}
      </p>

      <QueryState
        query={list}
        errorTitle="Notifications couldn’t be loaded"
        loading={<Skeleton height={240} />}
      >
        {(data) =>
          data.items.length === 0 ? (
            <Card flat>
              <EmptyState
                icon={<Bell />}
                title={unreadOnly ? 'You’re all caught up' : 'No notifications yet'}
                description={
                  unreadOnly
                    ? 'There are no unread notifications.'
                    : 'We’ll let you know when something happens.'
                }
              />
            </Card>
          ) : (
            <>
              <Card>
                <ul className="pp-list" aria-label="Notifications">
                  {data.items.map((n) => (
                    <NotificationRow key={n.id} n={n} onRead={(id) => markRead.mutate(id)} onUnread={(id) => markUnread.mutate(id)} />
                  ))}
                </ul>
              </Card>
              {data.total > PAGE_SIZE && (
                <Pagination
                  page={page}
                  pageSize={PAGE_SIZE}
                  total={data.total}
                  onPageChange={(p) =>
                    setParams(unreadOnly ? { unread: '1', page: String(p) } : { page: String(p) })
                  }
                  label="Notification pages"
                />
              )}
            </>
          )
        }
      </QueryState>
    </div>
  );
}
