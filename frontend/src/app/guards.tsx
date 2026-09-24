import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { FullPageLoader } from '@/components/FullPageLoader';
import { loginPathAfterExpiry, SIGNED_OUT_PATH } from '@/lib/auth/sessionPaths';
import { useAuth } from '@/lib/auth/useAuth';
import { defaultLandingPath } from './portals';
import { safeNextPath } from './redirects';

export { RequirePermission } from './RequirePermission';

/**
 * Renders children only for signed-in users; others go to /login?next=<current path> (with `expired=1` after a
 * forced sign-out, so the sign-in page explains why; straight to /login?signedOut=1 after signing out).
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status, sessionExpired, signedOut } = useAuth();
  const location = useLocation();
  if (status === 'loading') return <FullPageLoader />;
  if (status === 'anonymous') {
    const current = location.pathname + location.search + location.hash;
    const to = sessionExpired
      ? loginPathAfterExpiry(current)
      : signedOut
        ? SIGNED_OUT_PATH
        : `/login?next=${encodeURIComponent(current)}`;
    return <Navigate to={to} replace />;
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
