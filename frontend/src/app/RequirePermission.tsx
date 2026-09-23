import type { ReactNode } from 'react';
import { ForbiddenPage } from '@/features/public/ForbiddenPage';
import { meetsRequirement, type PermissionRequirement } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';

/**
 * Renders a friendly 403 page unless the signed-in user meets the permission requirement.
 * Kept free of portal imports so feature route files can use it without an import cycle.
 */
export function RequirePermission({
  children,
  ...requirement
}: PermissionRequirement & { children: ReactNode }) {
  const { permissions } = useAuth();
  if (!meetsRequirement(permissions, requirement)) return <ForbiddenPage />;
  return <>{children}</>;
}
