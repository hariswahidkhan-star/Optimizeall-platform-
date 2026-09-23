import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { LifeBuoy } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Card, CardBody } from '@/components/ui/Card';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { FilterBar } from '@/components/ui/FilterBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { humanize } from '@/lib/format/text';
import { TICKET_CATEGORIES, TICKET_PRIORITIES, TICKET_STATUSES, type StaffTicketSummary } from '../api/types';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { statusMeta } from '@/components/ui/statusMap';
import { AdminBadge } from '../shared/badges';
import { enumOptions, QueryError } from '../shared/common';
import { useListParams } from '../shared/useListParams';
import { useSupportStaff } from './useSupportStaff';

const FILTER_KEYS = ['status', 'priority', 'category', 'assignedTo'] as const;

export function TicketsPage() {
  const list = useListParams(FILTER_KEYS);
  const staff = useSupportStaff();

  const query = { search: list.search, ...list.filters, page: list.page, pageSize: list.pageSize };
  const tickets = useQuery({
    queryKey: ['admin', 'tickets', query],
    queryFn: ({ signal }) =>
      api.get<PagedResult<StaffTicketSummary>>('/admin/support/tickets', { query, signal }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader
        title="Support tickets"
        description="Participant questions and disputes. Newest activity first."
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={list.search}
            onSearchChange={(search) => list.update({ search })}
            searchPlaceholder="Subject, reference or requester email…"
            searchLabel="Search tickets"
            filters={[
              {
                id: 'status',
                label: 'Status',
                options: enumOptions(TICKET_STATUSES, (v) => statusMeta('ticket', v).label),
              },
              { id: 'priority', label: 'Priority', options: enumOptions(TICKET_PRIORITIES) },
              { id: 'category', label: 'Category', options: enumOptions(TICKET_CATEGORIES) },
              {
                id: 'assignedTo',
                label: 'Assigned to',
                allLabel: 'Anyone',
                options: [
                  { value: 'me', label: 'Me' },
                  { value: 'unassigned', label: 'Unassigned' },
                  ...(staff.data ?? []).map((s) => ({ value: s.id, label: s.displayName })),
                ],
              },
            ]}
            values={list.filters}
            onFilterChange={(id, value) => list.update({ [id]: value })}
            onReset={list.reset}
          />
          {tickets.isError ? (
            <QueryError error={tickets.error} onRetry={() => void tickets.refetch()} />
          ) : (
            <>
              <DataTable
                caption="Support tickets"
                rows={tickets.data?.items ?? []}
                loading={tickets.isPending}
                getRowId={(t) => t.id}
                columns={[
                  {
                    id: 'subject',
                    header: 'Ticket',
                    primary: true,
                    cell: (t) => (
                      <div className="admin-cell-stack">
                        <Link className="ui-link" to={t.id}>
                          {t.subject}
                        </Link>
                        <span className="text-small text-muted">
                          <code>{t.reference}</code> · {humanize(t.category)}
                        </span>
                      </div>
                    ),
                  },
                  {
                    id: 'requester',
                    header: 'Requester',
                    cell: (t) => (
                      <div className="admin-cell-stack">
                        <span>{t.requester.displayName}</span>
                        <span className="text-small text-muted">{t.requester.email}</span>
                      </div>
                    ),
                  },
                  {
                    id: 'status',
                    header: 'Status',
                    cell: (t) => <StatusBadge kind="ticket" status={t.status} />,
                  },
                  {
                    id: 'priority',
                    header: 'Priority',
                    cell: (t) => <AdminBadge kind="priority" value={t.priority} />,
                  },
                  {
                    id: 'assignedTo',
                    header: 'Assignee',
                    cell: (t) => t.assignedTo?.displayName ?? <span className="text-muted">Unassigned</span>,
                  },
                  {
                    id: 'updatedAt',
                    header: 'Last activity',
                    cell: (t) => <DateTime value={t.updatedAt} format="relative" />,
                  },
                ]}
                emptyState={
                  <EmptyState
                    icon={<LifeBuoy />}
                    headingLevel={2}
                    title="No tickets match"
                    description="Try clearing the filters."
                  />
                }
              />
              {tickets.data && (
                <Pagination
                  page={list.page}
                  pageSize={list.pageSize}
                  total={tickets.data.total}
                  onPageChange={(page) => list.update({ page }, false)}
                  onPageSizeChange={(pageSize) => list.update({ pageSize })}
                  label="Ticket pages"
                />
              )}
            </>
          )}
        </CardBody>
      </Card>
    </>
  );
}
