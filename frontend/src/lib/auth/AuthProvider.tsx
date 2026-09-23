import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { api, expireSession, onSessionEvent, refreshSession, tokenStore } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type { AuthResponse, MessageResponse, RegisterRequest, SessionUser } from '@/lib/api/types';
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext';
import { getDeviceId } from './deviceId';
import { hasAnyPermission as hasAny, hasPermission as has } from './permissions';

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
          setState({ status: 'authenticated', user: event.session.user, expiresAt: event.session.expiresAt });
          return;
        }
        const wasSignedIn = statusRef.current === 'authenticated';
        setState({ status: 'anonymous', user: null, expiresAt: null });
        queryClient.clear();
        if (wasSignedIn) {
          const { pathname, search } = locationRef.current;
          const next = encodeURIComponent(pathname + search);
          navigate(`/login?expired=1&next=${next}`, { replace: true });
        }
      }),
    [navigate, queryClient],
  );

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
    const delay = new Date(state.expiresAt).getTime() - Date.now() - REFRESH_LEAD_MS;
    schedule(Math.max(delay, MIN_REFRESH_DELAY_MS));
    return () => clearTimeout(timer);
  }, [state.status, state.expiresAt]);

  const login = useCallback(
    async (email: string, password: string) => {
      const session = await api.post<AuthResponse>('/auth/login', { email, password });
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
    setState({ status: 'anonymous', user: null, expiresAt: null });
    queryClient.clear();
    navigate('/login?signedOut=1', { replace: true });
  }, [navigate, queryClient]);

  const register = useCallback(async (request: Omit<RegisterRequest, 'deviceId'>) => {
    const response = await api.post<MessageResponse>('/auth/register', {
      ...request,
      deviceId: getDeviceId(),
    });
    return response.message;
  }, []);

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
      hasPermission: (permission) => has(permissions, permission),
      hasAnyPermission: (required) => hasAny(permissions, required),
      login,
      logout,
      register,
      refreshUser,
    };
  }, [state, login, logout, register, refreshUser]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
