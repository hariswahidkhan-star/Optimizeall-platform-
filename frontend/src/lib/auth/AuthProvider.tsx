import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { api, expireSession, onSessionEvent, refreshSession, tokenStore } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type { AuthResponse, MessageResponse, RegisterRequest, SessionUser } from '@/lib/api/types';
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext';
import { announceSignOut, onSignOutElsewhere } from './crossTab';
import { getDeviceId } from './deviceId';
import { hasAnyPermission as hasAny, hasPermission as has } from './permissions';
import { IMPERSONATION_EXIT_PATH, loginPathAfterExpiry, SIGNED_OUT_PATH } from './sessionPaths';

/** Refresh this long before the access token expires. */
const REFRESH_LEAD_MS = 60_000;
/** Never schedule a refresh sooner than this (guards against clock skew and very short tokens). */
const MIN_REFRESH_DELAY_MS = 5_000;
/** Retry a proactive refresh that failed for a transient reason. */
const REFRESH_RETRY_MS = 30_000;

interface SessionState {
  status: AuthStatus;
  user: SessionUser | null;
  expiresAt: string | null;
  /** Set by a forced sign-out; cleared on sign-in and once /login is shown. */
  expired?: boolean;
  /** Set by a deliberate sign-out; cleared on sign-in and once /login is shown. */
  signedOut?: boolean;
}

/**
 * Owns the signed-in session. On mount it silently restores the session from the HttpOnly refresh cookie, keeps the
 * access token fresh ahead of expiry and redirects to /login when the session can no longer be renewed.
 * Must render inside the router (it navigates on expiry).
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<SessionState>({ status: 'loading', user: null, expiresAt: null });
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();

  const statusRef = useRef<AuthStatus>(state.status);
  statusRef.current = state.status;
  const locationRef = useRef(location);
  locationRef.current = location;
  const impersonatingRef = useRef(false);
  impersonatingRef.current = !!state.user?.impersonatedBy;

  const applySession = useCallback((session: AuthResponse) => {
    tokenStore.set(session.accessToken);
    setState({ status: 'authenticated', user: session.user, expiresAt: session.expiresAt });
  }, []);

  // Bootstrap: try to restore the session from the refresh cookie.
  useEffect(() => {
    let cancelled = false;
    refreshSession()
      .then((session) => {
        if (!cancelled) applySession(session);
      })
      .catch(() => {
        if (cancelled) return;
        tokenStore.clear();
        setState({ status: 'anonymous', user: null, expiresAt: null });
      });
    return () => {
      cancelled = true;
    };
  }, [applySession]);

  // React to refreshes/expiry performed by the API client.
  useEffect(
    () =>
      onSessionEvent((event) => {
        if (event.type === 'refreshed') {
          const endedImpersonation = impersonatingRef.current && !event.session.user.impersonatedBy;
          setState({ status: 'authenticated', user: event.session.user, expiresAt: event.session.expiresAt });
          if (endedImpersonation) {
            // The impersonation session ended (expired, or exited in another tab): back to the staff member's session.
            queryClient.clear();
            navigate(IMPERSONATION_EXIT_PATH, { replace: true });
          }
          return;
        }
        const wasSignedIn = statusRef.current === 'authenticated';
        // The flag reaches RequireAuth in the same render as the anonymous status, so its own redirect to /login
        // (which would otherwise supersede the navigation below) also carries expired=1.
        setState({ status: 'anonymous', user: null, expiresAt: null, expired: wasSignedIn });
        queryClient.clear();
        if (wasSignedIn) {
          const { pathname, search } = locationRef.current;
          navigate(loginPathAfterExpiry(pathname + search), { replace: true });
        }
      }),
    [navigate, queryClient],
  );

  // Another tab signed out: the shared refresh cookie is gone, so leave the portal now rather than on the next refresh.
  useEffect(
    () =>
      onSignOutElsewhere(() => {
        if (statusRef.current !== 'authenticated') return;
        tokenStore.clear();
        setState({ status: 'anonymous', user: null, expiresAt: null, signedOut: true });
        queryClient.clear();
        navigate(SIGNED_OUT_PATH, { replace: true });
      }),
    [navigate, queryClient],
  );

  // The expiry / signed-out notices are transient: once the sign-in page is shown, later redirects are ordinary ones.
  useEffect(() => {
    if ((state.expired || state.signedOut) && location.pathname === '/login')
      setState((prev) => ({ ...prev, expired: false, signedOut: false }));
  }, [state.expired, state.signedOut, location.pathname]);

  // Proactive refresh shortly before the access token expires.
  useEffect(() => {
    if (state.status !== 'authenticated' || !state.expiresAt) return;
    let timer: ReturnType<typeof setTimeout>;
    const schedule = (delay: number) => {
      timer = setTimeout(() => {
        refreshSession().catch((error: unknown) => {
          if (isApiError(error) && error.status === 401) expireSession();
          else schedule(REFRESH_RETRY_MS);
        });
      }, delay);
    };
    const expires = new Date(state.expiresAt).getTime();
    const sessionEnd = state.user?.impersonatedBy
      ? new Date(state.user.impersonatedBy.expiresAt).getTime()
      : null;
    // An impersonation token that already runs to the end of its session can't be renewed: refresh just after the
    // end instead, which hands back the staff member's own session.
    const delay =
      sessionEnd !== null && expires >= sessionEnd - 1_000
        ? sessionEnd - Date.now() + 1_000
        : expires - Date.now() - REFRESH_LEAD_MS;
    schedule(Math.max(delay, MIN_REFRESH_DELAY_MS));
    return () => clearTimeout(timer);
  }, [state.status, state.expiresAt, state.user?.impersonatedBy]);

  const login = useCallback(
    async (email: string, password: string) => {
      const session = await api.post<AuthResponse>('/auth/login', { email, password });
      queryClient.clear();
      applySession(session);
      return session.user;
    },
    [applySession, queryClient],
  );

  const startSession = useCallback(
    (session: AuthResponse) => {
      queryClient.clear();
      applySession(session);
      return session.user;
    },
    [applySession, queryClient],
  );

  const logout = useCallback(async () => {
    try {
      await api.post('/auth/logout');
    } catch {
      // The local session is discarded regardless; the server cookie expires on its own.
    }
    tokenStore.clear();
    announceSignOut();
    // Like `expired`, the flag reaches RequireAuth in the same render as the anonymous status, so the guard's own
    // redirect (which supersedes the navigation below) goes to /login?signedOut=1 too, not to /login?next=<page>.
    setState({ status: 'anonymous', user: null, expiresAt: null, signedOut: true });
    queryClient.clear();
    navigate(SIGNED_OUT_PATH, { replace: true });
  }, [navigate, queryClient]);

  const register = useCallback(async (request: Omit<RegisterRequest, 'deviceId'>) => {
    const response = await api.post<MessageResponse>('/auth/register', {
      ...request,
      deviceId: getDeviceId(),
    });
    return response.message;
  }, []);

  const startImpersonation = useCallback(
    async (userId: string, reason: string) => {
      const next = await api.post<AuthResponse>(`/admin/users/${encodeURIComponent(userId)}/impersonate`, {
        reason,
        confirm: true,
      });
      return startSession(next);
    },
    [startSession],
  );

  const exitImpersonation = useCallback(async () => {
    let next: AuthResponse;
    try {
      next = await api.post<AuthResponse>('/auth/impersonation/exit');
    } catch (error) {
      if (isApiError(error) && error.status === 401) {
        expireSession();
        return;
      }
      throw error;
    }
    startSession(next);
    navigate(IMPERSONATION_EXIT_PATH, { replace: true });
  }, [startSession, navigate]);

  const refreshUser = useCallback(async () => {
    if (!tokenStore.get()) return null;
    const user = await api.get<SessionUser>('/auth/me');
    setState((prev) => ({ ...prev, status: 'authenticated', user }));
    return user;
  }, []);

  const value = useMemo<AuthContextValue>(() => {
    const permissions = state.user?.permissions ?? [];
    return {
      status: state.status,
      user: state.user,
      permissions,
      expiresAt: state.expiresAt,
      sessionExpired: !!state.expired,
      signedOut: !!state.signedOut,
      hasPermission: (permission) => has(permissions, permission),
      hasAnyPermission: (required) => hasAny(permissions, required),
      login,
      startSession,
      logout,
      register,
      refreshUser,
      impersonation: state.user?.impersonatedBy ?? null,
      startImpersonation,
      exitImpersonation,
    };
  }, [state, login, startSession, logout, register, refreshUser, startImpersonation, exitImpersonation]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
