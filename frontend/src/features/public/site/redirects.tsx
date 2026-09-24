import { useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { safeNextPath } from '@/app/redirects';
import { Skeleton } from '@/components/ui';
import { api } from '@/lib/api/client';

/** `GET /public/redirects?path=` — where a moved public address lives now (docs/WEBSITE.md "Redirects"). */
export interface RedirectLookup {
  location: string;
  statusCode: number;
}

/**
 * First path segments the app itself owns (portals, sign-in, short links): never redirected, so never looked up.
 * Mirrors RedirectPaths.IsProtected in the API.
 */
const APP_SEGMENTS = new Set([
  'app', 'agency', 'client', 'admin', 'finance', 'review', 'manage', 'api', 'auth', 'login', 'register', 'check-email',
  'verify-email', 'forgot-password', 'reset-password', 'join', 'c', 't', 'e', 'p', 'i', 'email', 'f', 'assets', 'design-system',
]);

/** Whether a not-found public address may have moved (anything outside the app's own areas). */
export function isRedirectCandidate(pathname: string): boolean {
  const first = pathname.replace(/^\/+/, '').split('/')[0]?.toLowerCase() ?? '';
  return first.length > 0 && !APP_SEGMENTS.has(first);
}

/**
 * Looks up the current address. The web server answers moved addresses with a real 301 before the app loads; this covers
 * client-side navigation (links inside the app, history) to an address that has since moved.
 */
function useMovedTo(enabled: boolean): { pending: boolean; to: string | null } {
  const location = useLocation();
  const target = location.pathname + location.search;
  const lookup = useQuery({
    queryKey: ['public', 'redirect', target],
    queryFn: () => api.get<RedirectLookup>('/public/redirects', { query: { path: target } }),
    enabled: enabled && isRedirectCandidate(location.pathname),
    staleTime: 5 * 60_000,
    retry: false,
  });
  const to = lookup.data ? safeNextPath(lookup.data.location) : null;
  // Never "redirect" to the address we are on (a stale answer must not loop).
  return { pending: lookup.isLoading, to: to && to !== target ? to : null };
}

/** Renders `children` (the not-found state) unless the address has moved, in which case it navigates there (replacing history). */
export function MovedOrNotFound({ children }: { children: ReactNode }) {
  const { pending, to } = useMovedTo(true);
  if (to) return <Navigate to={to} replace />;
  if (pending)
    return (
      <div className="container site-loading" aria-busy="true">
        <Skeleton height={48} width="60%" />
      </div>
    );
  return <>{children}</>;
}

/** Navigates to the new address when `when` holds and the current address has moved; renders nothing otherwise. */
export function RedirectIfMoved({ when }: { when: boolean }) {
  const { to } = useMovedTo(when);
  return to ? <Navigate to={to} replace /> : null;
}
