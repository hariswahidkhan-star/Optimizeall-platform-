import { vi } from 'vitest';
import type { AuthResponse, SessionUser } from '@/lib/api/types';

export interface MockRequest {
  method: string;
  path: string;
  headers: Record<string, string>;
  body: unknown;
}

type Handler = (req: MockRequest) => Response | Promise<Response>;

export function json(status: number, body?: unknown, headers: Record<string, string> = {}): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json', ...headers },
  });
}

export function problem(
  status: number,
  code: string,
  title: string,
  extra: Record<string, unknown> = {},
): Response {
  return json(status, { status, title, code, traceId: 'trace-123', ...extra });
}

/**
 * Installs a fetch stub. Routes are "METHOD /path" (path without /api/v1 and query string). Unmatched requests
 * answer 404. Returns the recorded calls.
 */
export function mockFetch(routes: Record<string, Handler>) {
  const calls: MockRequest[] = [];
  const fn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost');
    const path = url.pathname.replace(/^\/api\/v1/, '');
    const method = (init?.method ?? 'GET').toUpperCase();
    const headers = (init?.headers ?? {}) as Record<string, string>;
    let body: unknown = init?.body;
    if (typeof body === 'string') {
      try {
        body = JSON.parse(body);
      } catch {
        // keep raw
      }
    }
    const req = { method, path, headers, body };
    calls.push(req);
    const handler = routes[`${method} ${path}`];
    if (!handler) return problem(404, 'http_404', 'Not found');
    return handler(req);
  });
  vi.stubGlobal('fetch', fn);
  return { calls, fn };
}

export function makeUser(overrides: Partial<SessionUser> = {}): SessionUser {
  return {
    id: 'u1',
    email: 'ada@example.com',
    displayName: 'Ada Lovelace',
    emailVerified: true,
    countryCode: 'GB',
    languageCode: 'en',
    timeZone: 'Europe/London',
    status: 'Active',
    roles: ['Participant'],
    permissions: ['participant.portal'],
    ...overrides,
  };
}

export function session(user: SessionUser = makeUser(), token = 'access-1'): AuthResponse {
  return { accessToken: token, expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(), user };
}
