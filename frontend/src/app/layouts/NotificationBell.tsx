import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Bell, CheckCheck } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@/components/ui/Button';
import { DateTime } from '@/components/ui/DateTime';
import { Drawer } from '@/components/ui/Drawer';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import { api } from '@/lib/api/client';
import { isInternalHref } from '@/lib/safeHref';

interface InboxItem {
  id: string;
  title: string;
  body: string;
  linkUrl: string | null;
  createdAt: string;
  isRead: boolean;
}

const KEY = ['me', 'inbox'] as const;

/**
 * In-app notifications for the staff and client portals (participants have their own Notifications page): new website
 * inquiries, assigned leads, proposal views and acceptances, payment reports… A top-bar button with the unread count
 * opens the latest notifications; following one marks it read.
 */
export function NotificationBell() {
  const [open, setOpen] = useState(false);
  const client = useQueryClient();
  const unread = useQuery({
    queryKey: [...KEY, 'unread'],
    queryFn: () => api.get<{ count: number }>('/me/notifications/unread-count'),
    refetchInterval: 60_000,
  });
  const list = useQuery({
    queryKey: [...KEY, 'list'],
    queryFn: () => api.get<{ items: InboxItem[] }>('/me/notifications', { query: { page: 1, pageSize: 20 } }),
    enabled: open,
  });
  const refresh = () => client.invalidateQueries({ queryKey: KEY });
  const markRead = useMutation({
    mutationFn: (id: string | 'all') =>
      id === 'all' ? api.post('/me/notifications/read-all') : api.post(`/me/notifications/${id}/read`),
    onSettled: () => void refresh(),
  });
  const count = unread.data?.count ?? 0;
  const label = count > 0 ? `Notifications (${count} unread)` : 'Notifications';

  return (
    <>
      <button
        type="button"
        className="portal-bell"
        aria-label={label}
        title={label}
        aria-haspopup="dialog"
        onClick={() => {
          setOpen(true);
          void refresh();
        }}
      >
        <Bell aria-hidden="true" />
        {count > 0 && (
          <span className="portal-bell__count" aria-hidden="true">
            {count > 99 ? '99+' : count}
          </span>
        )}
      </button>
      <Drawer open={open} onClose={() => setOpen(false)} title="Notifications" side="right">
        <div className="portal-inbox">
          <Button
            size="sm"
            variant="secondary"
            leadingIcon={<CheckCheck />}
            disabled={count === 0}
            loading={markRead.isPending && markRead.variables === 'all'}
            onClick={() => markRead.mutate('all')}
          >
            Mark all as read
          </Button>
          {list.isPending ? (
            <Skeleton height={160} />
          ) : (list.data?.items.length ?? 0) === 0 ? (
            <EmptyState compact headingLevel={3} icon={<Bell />} title="No notifications yet" />
          ) : (
            <ul className="portal-inbox__list" aria-label="Latest notifications">
              {list.data!.items.map((n) => (
                <li key={n.id} className="portal-inbox__item" data-unread={!n.isRead}>
                  <span className="portal-inbox__title">
                    {isInternalHref(n.linkUrl) ? (
                      <Link
                        to={n.linkUrl}
                        className="ui-link"
                        onClick={() => {
                          if (!n.isRead) markRead.mutate(n.id);
                          setOpen(false);
                        }}
                      >
                        {n.title}
                      </Link>
                    ) : (
                      n.title
                    )}
                    {!n.isRead && <span className="visually-hidden"> (unread)</span>}
                  </span>
                  <span className="text-small">{n.body}</span>
                  <span className="text-small text-muted">
                    <DateTime value={n.createdAt} format="relative" />
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </Drawer>
    </>
  );
}
