import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import type { AdminUserListItem } from '../api/types';

export interface StaffOption {
  id: string;
  displayName: string;
  email: string;
}

/**
 * Active staff who can be assigned tickets (holders of `support.manage`, built-in or custom role). Needs `users.view`;
 * without it the list is empty and only "me" / "unassigned" are offered.
 */
export function useSupportStaff() {
  const { hasPermission } = useAuth();
  const enabled = hasPermission(Permissions.UsersView);
  return useQuery({
    queryKey: ['admin', 'support', 'staff'],
    enabled,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      // Everyone holding support.manage, through a built-in or a custom role (the API's permission directory).
      const page = await api.get<PagedResult<AdminUserListItem>>('/admin/users', {
        query: { permission: Permissions.SupportManage, status: 'Active', pageSize: 200, sort: 'displayName' },
        signal,
      });
      return page.items
        .map((u) => ({ id: u.id, displayName: u.displayName, email: u.email }))
        .sort((a, b) => a.displayName.localeCompare(b.displayName));
    },
  });
}
