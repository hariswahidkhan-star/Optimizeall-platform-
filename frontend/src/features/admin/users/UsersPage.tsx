import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { UserPlus, Users } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody } from '@/components/ui/Card';
import { DataTable, type DataTableColumn } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { FilterBar } from '@/components/ui/FilterBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pagination } from '@/components/ui/Pagination';
import { countryName, countryOptions } from '@/features/auth/localeOptions';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { ROLES, TIERS, USER_STATUSES, type AdminUserListItem } from '../api/types';
import { AdminBadge, roleLabel } from '../shared/badges';
import { enumOptions, ExportCsvButton, QueryError, useCan } from '../shared/common';
import { useListParams } from '../shared/useListParams';
import { InviteStaffDialog } from './InviteStaffDialog';

const FILTER_KEYS = ['role', 'status', 'country', 'tier'] as const;

function flatCountries() {
  const all = countryOptions().find((g) => 'options' in g && g.label === 'All countries');
  return all && 'options' in all ? all.options : [];
}

export function UsersPage() {
  const list = useListParams(FILTER_KEYS);
  const canInvite = useCan(Permissions.RolesAssign);
  const [inviteOpen, setInviteOpen] = useState(false);

  const query = {
    search: list.search,
    ...list.filters,
    page: list.page,
    pageSize: list.pageSize,
    sort: list.sort,
    desc: list.sort ? list.desc : undefined,
  };
  const users = useQuery({
    queryKey: ['admin', 'users', query],
    queryFn: ({ signal }) => api.get<PagedResult<AdminUserListItem>>('/admin/users', { query, signal }),
    placeholderData: keepPreviousData,
  });

  const columns: DataTableColumn<AdminUserListItem>[] = [
    {
      id: 'displayName',
      header: 'Name',
      primary: true,
      sortable: true,
      cell: (u) => (
        <div className="admin-cell-stack">
          <Link className="ui-link" to={u.id}>
            {u.displayName}
          </Link>
          <span className="text-small text-muted">{u.email}</span>
        </div>
      ),
    },
    {
      id: 'roles',
      header: 'Roles',
      cell: (u) => (
        <span className="cluster admin-tight">
          {u.roles.map((r) => (
            <Badge key={r} size="sm" tone={r === 'Participant' ? 'neutral' : 'brand'}>
              {roleLabel(r)}
            </Badge>
          ))}
        </span>
      ),
    },
    { id: 'status', header: 'Status', cell: (u) => <AdminBadge kind="user" value={u.status} /> },
    { id: 'tier', header: 'Tier', cell: (u) => u.tier },
    { id: 'country', header: 'Country', cell: (u) => countryName(u.countryCode), hideOnMobile: true },
    {
      id: 'createdAt',
      header: 'Joined',
      sortable: true,
      cell: (u) => <DateTime value={u.createdAt} format="date" />,
      hideOnMobile: true,
    },
    {
      id: 'lastActiveAt',
      header: 'Last active',
      sortable: true,
      cell: (u) => (u.lastActiveAt ? <DateTime value={u.lastActiveAt} format="relative" /> : 'Never'),
    },
  ];

  return (
    <>
      <PageHeader
        title="Users"
        description="Find anyone on the platform, review their account and manage roles, tiers and suspensions."
        actions={
          canInvite && (
            <Button leadingIcon={<UserPlus />} onClick={() => setInviteOpen(true)}>
              Invite staff
            </Button>
          )
        }
      />
      <Card>
        <CardBody className="stack">
          <FilterBar
            search={list.search}
            onSearchChange={(search) => list.update({ search })}
            searchPlaceholder="Search name or email…"
            searchLabel="Search users"
            filters={[
              { id: 'role', label: 'Role', options: enumOptions(ROLES, roleLabel) },
              { id: 'status', label: 'Status', options: enumOptions(USER_STATUSES) },
              { id: 'country', label: 'Country', options: flatCountries() },
              { id: 'tier', label: 'Tier', options: enumOptions(TIERS) },
            ]}
            values={list.filters}
            onFilterChange={(id, value) => list.update({ [id]: value })}
            onReset={list.reset}
            actions={
              <ExportCsvButton
                path="/admin/users/export.csv"
                query={{ search: list.search, ...list.filters }}
                fileName="users.csv"
              />
            }
          />
          {users.isError ? (
            <QueryError error={users.error} onRetry={() => void users.refetch()} />
          ) : (
            <>
              <DataTable
                caption="Users"
                columns={columns}
                rows={users.data?.items ?? []}
                getRowId={(u) => u.id}
                loading={users.isPending}
                sort={list.sort ? { id: list.sort, desc: list.desc } : null}
                onSortChange={(sort) => list.update({ sort: sort.id, desc: sort.desc })}
                emptyState={
                  <EmptyState
                    icon={<Users />}
                    headingLevel={2}
                    title="No users match"
                    description="Try a different search or clear the filters."
                  />
                }
              />
              {users.data && (
                <Pagination
                  page={list.page}
                  pageSize={list.pageSize}
                  total={users.data.total}
                  onPageChange={(page) => list.update({ page }, false)}
                  onPageSizeChange={(pageSize) => list.update({ pageSize })}
                  label="Users pages"
                />
              )}
            </>
          )}
        </CardBody>
      </Card>
      {inviteOpen && <InviteStaffDialog open onClose={() => setInviteOpen(false)} />}
    </>
  );
}
