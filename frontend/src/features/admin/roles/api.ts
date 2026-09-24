import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { IsoDateTime } from '@/lib/api/types';

/** Mirrors backend `Modules/Admin/Roles/RoleDtos.cs`. */
export interface BuiltInRole {
  name: string;
  label: string;
  permissions: string[];
  userCount: number;
}

export interface CustomRole {
  id: string;
  name: string;
  description: string | null;
  permissions: string[];
  isSystem: boolean;
  userCount: number;
  createdAt: IsoDateTime;
  createdByUserId: string | null;
  createdByName: string | null;
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
  /** The caller may edit, delete, assign and unassign this role. */
  canManage: boolean;
}

export interface RolesOverview {
  builtIn: BuiltInRole[];
  custom: CustomRole[];
}

export interface RoleHolder {
  userId: string;
  displayName: string;
  email: string;
  status: string;
  assignedAt: IsoDateTime;
}

export interface CustomRoleDetail {
  role: CustomRole;
  holders: RoleHolder[];
}

export interface PermissionInfo {
  key: string;
  label: string;
  description: string;
  sensitive: boolean;
  /** Only users with the built-in Admin role can grant it. */
  adminOnly: boolean;
  /** The caller may put it into a role. */
  granted: boolean;
}

export interface PermissionArea {
  area: string;
  permissions: PermissionInfo[];
}

export interface PermissionCatalog {
  areas: PermissionArea[];
  callerIsAdmin: boolean;
}

export interface UserCustomRoles {
  userId: string;
  roles: { id: string; name: string; assignedAt: IsoDateTime }[];
  effectivePermissions: string[];
}

export interface SaveRoleInput {
  name: string;
  description: string | null;
  permissions: string[];
  concurrencyStamp?: string;
}

export const CLIENT_PORTAL = 'client.portal';
export const PORTAL_MARKERS = ['client.portal', 'participant.portal'];

/** Mirrors the server guardrail: client.portal can't be combined with staff permissions. */
export function mixesClientAndStaff(permissions: readonly string[]): boolean {
  return permissions.includes(CLIENT_PORTAL) && permissions.some((p) => !PORTAL_MARKERS.includes(p));
}

export const roleKeys = {
  all: ['admin', 'roles'] as const,
  overview: ['admin', 'roles', 'overview'] as const,
  catalog: ['admin', 'roles', 'catalog'] as const,
  detail: (id: string) => ['admin', 'roles', 'detail', id] as const,
  user: (userId: string) => ['admin', 'roles', 'user', userId] as const,
};

export function useRolesOverview(enabled = true) {
  return useQuery({
    queryKey: roleKeys.overview,
    enabled,
    queryFn: ({ signal }) => api.get<RolesOverview>('/admin/roles', { signal }),
  });
}

export function usePermissionCatalog(enabled = true) {
  return useQuery({
    queryKey: roleKeys.catalog,
    enabled,
    staleTime: 5 * 60_000,
    queryFn: ({ signal }) => api.get<PermissionCatalog>('/admin/roles/catalog', { signal }),
  });
}

export function useRoleDetail(id: string | null) {
  return useQuery({
    queryKey: roleKeys.detail(id ?? ''),
    enabled: !!id,
    queryFn: ({ signal }) => api.get<CustomRoleDetail>(`/admin/roles/${id}`, { signal }),
  });
}

export function useUserCustomRoles(userId: string, enabled = true) {
  return useQuery({
    queryKey: roleKeys.user(userId),
    enabled,
    queryFn: ({ signal }) => api.get<UserCustomRoles>(`/admin/roles/users/${userId}`, { signal }),
  });
}

/** Invalidates everything that shows roles or a user's roles after a change. */
function useInvalidateRoles() {
  const client = useQueryClient();
  return () =>
    Promise.all([
      client.invalidateQueries({ queryKey: roleKeys.all }),
      client.invalidateQueries({ queryKey: ['admin', 'user'] }),
      client.invalidateQueries({ queryKey: ['admin', 'users'] }),
    ]);
}

export function useSaveRole() {
  const invalidate = useInvalidateRoles();
  return useMutation({
    mutationFn: ({ id, input }: { id?: string; input: SaveRoleInput }) =>
      id
        ? api.put<CustomRoleDetail>(`/admin/roles/${id}`, input)
        : api.post<CustomRoleDetail>('/admin/roles', input),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteRole() {
  const invalidate = useInvalidateRoles();
  return useMutation({
    mutationFn: ({ id, confirm, reassignTo }: { id: string; confirm: boolean; reassignTo?: string | null }) =>
      api.delete(`/admin/roles/${id}`, undefined, {
        query: { confirm: confirm || undefined, reassignTo: reassignTo || undefined },
      }),
    onSuccess: () => invalidate(),
  });
}

export function useToggleAssignment(userId: string) {
  const invalidate = useInvalidateRoles();
  return useMutation({
    mutationFn: ({ roleId, assign }: { roleId: string; assign: boolean }) =>
      assign
        ? api.put<UserCustomRoles>(`/admin/roles/${roleId}/users/${userId}`)
        : api.delete<UserCustomRoles>(`/admin/roles/${roleId}/users/${userId}`),
    onSuccess: () => invalidate(),
  });
}
