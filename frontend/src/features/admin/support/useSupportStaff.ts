import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import type { AdminUserListItem } from '../api/types';

/** Roles that hold `support.manage` (see docs/SECURITY.md) — the only valid ticket assignees. */
const SUPPORT_ROLES = ['Reviewer', 'Admin'] as const;

export interface StaffOption {
  id: string;
  displayName: string;
  email: string;
}

/**
 * Active staff who can be assigned tickets. Needs `users.view` (which every support.manage role has); without it
 * the list is empty and only "me" / "unassigned" are offered.
 */
export function useSupportStaff() {
  const { hasPermission } = useAuth();
  const enabled = hasPermission(Permissions.UsersView);
  return useQuery({
    queryKey: ['admin', 'support', 'staff'],
    enabled,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      const pages = await Promise.all(
        SUPPORT_ROLES.map((role) =>
          api.get<PagedResult<AdminUserListItem>>('/admin/users', {
            query: { role, status: 'Active', pageSize: 200, sort: 'displayName' },
            signal,
          }),
        ),
      );
      const byId = new Map<string, StaffOption>();
      for (const page of pages)
        for (const u of page.items) byId.set(u.id, { id: u.id, displayName: u.displayName, email: u.email });
      return [...byId.values()].sort((a, b) => a.displayName.localeCompare(b.displayName));
    },
  });
}
