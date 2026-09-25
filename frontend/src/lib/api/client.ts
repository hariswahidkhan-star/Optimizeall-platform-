import { ApiError, networkError, parseErrorResponse } from './errors';
import type { AuthResponse } from './types';

/** API base path. Production serves the SPA and the API from the same origin (nginx proxies /api). */
export const API_BASE = '/api/v1';

/**
 * Auth endpoints that must never trigger the refresh-and-retry flow: they either establish a session themselves
 * or are expected to answer 401 for bad credentials.
 */
const NO_REFRESH_PATHS = [
  '/auth/login',
  '/auth/register',
  '/auth/refresh',
  '/auth/logout',
  '/auth/verify-email',
  '/auth/resend-verification',
  '/auth/forgot-password',
  '/auth/reset-password',
];

// ---------- Access token (memory only — never persisted) ----------

let accessToken: string | null = null;

export const tokenStore = {
  get: (): string | null => accessToken,
  set: (token: string | null): void => {
    accessToken = token;
  },
  clear: (): void => {
    accessToken = null;
  },
};

// ---------- Session events ----------

export type SessionEvent = { type: 'refreshed'; session: AuthResponse } | { type: 'session-expired' };
type SessionListener = (event: SessionEvent) => void;

const listeners = new Set<SessionListener>();

/** Subscribe to session changes made by the client (token refreshed, session expired). Returns an unsubscribe fn. */
export function onSessionEvent(listener: SessionListener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function emit(event: SessionEvent): void {
  for (const listener of [...listeners]) listener(event);
}

/** Clears the in-memory token and notifies listeners that the session is gone. */
export function expireSession(): void {
  tokenStore.clear();
  emit({ type: 'session-expired' });
}

// ---------- Refresh (single flight) ----------

let refreshInFlight: Promise<AuthResponse> | null = null;

/** The API's "lost a rotation race, retry" answer to POST /auth/refresh (401, the session is intact). */
export const REFRESH_RACE_CODE = 'auth.refresh_race';
const REFRESH_RACE_RETRY_MS = 250;

/**
 * Exchanges the HttpOnly refresh cookie for a new access token. Concurrent callers share one request, so a burst of
 * 401s rotates the refresh token exactly once. Resolves with the new session; rejects with ApiError on failure
 * (the caller decides whether that means "expired").
 */
export function refreshSession(): Promise<AuthResponse> {
  if (!refreshInFlight) {
    refreshInFlight = (async () => {
      try {
        let session: AuthResponse;
        try {
          session = await send<AuthResponse>('POST', '/auth/refresh', { skipRefresh: true });
        } catch (error) {
          // Another tab (or a response this page never received) rotated the cookie a moment ago; the server kept the
          // session and asks for a retry, which carries the cookie the browser now holds.
          if (!(error instanceof ApiError) || error.code !== REFRESH_RACE_CODE) throw error;
          await new Promise((resolve) => setTimeout(resolve, REFRESH_RACE_RETRY_MS));
          session = await send<AuthResponse>('POST', '/auth/refresh', { skipRefresh: true });
        }
        tokenStore.set(session.accessToken);
        emit({ type: 'refreshed', session });
        return session;
      } finally {
        refreshInFlight = null;
      }
    })();
  }
  return refreshInFlight;
}

// ---------- Requests ----------

export interface RequestOptions {
  /** Extra headers. */
  headers?: Record<string, string>;
  /** Query string parameters; `undefined`, `null` and '' values are skipped. */
  query?: QueryParams;
  signal?: AbortSignal;
  /** Skip the 401 → refresh → retry flow (used for auth endpoints). */
  skipRefresh?: boolean;
}

export type QueryValue = string | number | boolean | null | undefined;
export type QueryParams = Record<string, QueryValue | QueryValue[]>;

interface SendOptions extends RequestOptions {
  body?: unknown;
  /** Return the raw Response (for downloads) instead of parsing it. */
  raw?: boolean;
}

/** Builds `?a=1&b=2` from params, skipping empty values. Arrays repeat the key. */
export function buildQueryString(params?: QueryParams): string {
  if (!params) return '';
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    const values = Array.isArray(value) ? value : [value];
    for (const v of values) {
      if (v === undefined || v === null || v === '') continue;
      search.append(key, String(v));
    }
  }
  const qs = search.toString();
  return qs ? `?${qs}` : '';
}

function resolveUrl(path: string, query?: QueryParams): string {
  const url = /^https?:\/\//.test(path) || path.startsWith(`${API_BASE}/`) ? path : `${API_BASE}${path}`;
  return url + buildQueryString(query);
}

function isRefreshExempt(path: string): boolean {
  const bare = path.startsWith(API_BASE) ? path.slice(API_BASE.length) : path;
  return NO_REFRESH_PATHS.some((p) => bare === p || bare.startsWith(`${p}?`));
}

async function doFetch(
  method: string,
  path: string,
  options: SendOptions,
  token: string | null,
): Promise<Response> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    'X-Requested-With': 'fetch',
    ...options.headers,
  };
  let body: BodyInit | undefined;
  if (options.body instanceof FormData) {
    body = options.body;
  } else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(options.body);
  }
  if (token) headers.Authorization = `Bearer ${token}`;

  try {
    return await fetch(resolveUrl(path, options.query), {
      method,
      headers,
      body,
      credentials: 'include',
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw networkError();
  }
}

async function parseBody<T>(response: Response): Promise<T> {
  if (response.status === 204 || response.status === 205) return undefined as T;
  const text = await response.text();
  if (!text) return undefined as T;
  const type = response.headers.get('content-type') ?? '';
  if (type.includes('json')) return JSON.parse(text) as T;
  return text as T;
}

async function send<T>(method: string, path: string, options: SendOptions = {}): Promise<T> {
  const tokenUsed = tokenStore.get();
  let response = await doFetch(method, path, options, tokenUsed);

  if (response.status === 401 && !options.skipRefresh && !isRefreshExempt(path)) {
    const current = tokenStore.get();
    if (current && current !== tokenUsed) {
      // Another request already refreshed while this one was in flight — just retry with the new token.
      response = await doFetch(method, path, options, current);
    } else {
      let session: AuthResponse;
      try {
        session = await refreshSession();
      } catch {
        expireSession();
        throw new ApiError({
          status: 401,
          code: 'auth.session_expired',
          title: 'Your session has expired. Please sign in again.',
        });
      }
      response = await doFetch(method, path, options, session.accessToken);
    }
  }

  if (!response.ok) throw await parseErrorResponse(response);
  if (options.raw) return response as T;
  return parseBody<T>(response);
}

/** Extracts the filename from a Content-Disposition header (RFC 6266, incl. `filename*=UTF-8''...`). */
export function filenameFromContentDisposition(header: string | null): string | null {
  if (!header) return null;
  const star = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header);
  if (star?.[1]) {
    try {
      return decodeURIComponent(star[1].trim().replace(/^"|"$/g, ''));
    } catch {
      return star[1].trim();
    }
  }
  const plain = /filename\s*=\s*("?)([^";]+)\1/.exec(header);
  return plain?.[2]?.trim() || null;
}

export const api = {
  get: <T>(path: string, options?: RequestOptions) => send<T>('GET', path, options),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    send<T>('POST', path, { ...options, body }),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    send<T>('PUT', path, { ...options, body }),
  patch: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    send<T>('PATCH', path, { ...options, body }),
  delete: <T = void>(path: string, body?: unknown, options?: RequestOptions) =>
    send<T>('DELETE', path, { ...options, body }),

  /** Multipart upload (browser sets the boundary header). */
  upload: <T>(path: string, form: FormData, options?: RequestOptions) =>
    send<T>('POST', path, { ...options, body: form }),

  /** Multipart replace (PUT), e.g. editing a record that carries a file. */
  uploadPut: <T>(path: string, form: FormData, options?: RequestOptions) =>
    send<T>('PUT', path, { ...options, body: form }),

  /** Fetches a binary resource with the current session (e.g. a private screenshot) as a Blob. */
  blob: async (path: string, options?: RequestOptions): Promise<Blob> => {
    const response = await send<Response>('GET', path, {
      ...options,
      raw: true,
      headers: { Accept: '*/*', ...options?.headers },
    });
    return response.blob();
  },

  /**
   * Downloads a file (e.g. CSV export) with the current session and saves it via a temporary object URL.
   * The filename comes from Content-Disposition when present.
   */
  download: async (path: string, fallbackFileName: string, options?: RequestOptions): Promise<void> => {
    const response = await send<Response>('GET', path, {
      ...options,
      raw: true,
      headers: { Accept: '*/*', ...options?.headers },
    });
    const blob = await response.blob();
    const fileName =
      filenameFromContentDisposition(response.headers.get('content-disposition')) ?? fallbackFileName;
    const url = URL.createObjectURL(blob);
    try {
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = fileName;
      anchor.rel = 'noopener';
      anchor.style.display = 'none';
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
    } finally {
      setTimeout(() => URL.revokeObjectURL(url), 0);
    }
  },
};

export type Api = typeof api;
