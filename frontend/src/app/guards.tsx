import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { FullPageLoader } from '@/components/FullPageLoader';
import { useAuth } from '@/lib/auth/useAuth';
import { defaultLandingPath } from './portals';
import { safeNextPath } from './redirects';

export { RequirePermission } from './RequirePermission';

/** Renders children only for signed-in users; others go to /login?next=<current path>. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  const location = useLocation();
  if (status === 'loading') return <FullPageLoader />;
  if (status === 'anonymous') {
    const next = encodeURIComponent(location.pathname + location.search + location.hash);
    return <Navigate to={`/login?next=${next}`} replace />;
  }
  return <>{children}</>;
}

/** For sign-in/registration pages: signed-in users are sent on to their portal (or a permitted `next`). */
export function RedirectIfAuthenticated({ children }: { children: ReactNode }) {
  const { status, user } = useAuth();
  const location = useLocation();
  if (status === 'loading') return <FullPageLoader />;
  if (status === 'authenticated' && user) {
    const next = safeNextPath(new URLSearchParams(location.search).get('next'));
    return <Navigate to={defaultLandingPath(user.permissions, next)} replace />;
  }
  return <>{children}</>;
}
