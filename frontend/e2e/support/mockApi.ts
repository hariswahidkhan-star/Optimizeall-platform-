import type { Page, Route } from '@playwright/test';

export interface MockUser {
  id: string;
  email: string;
  displayName: string;
  emailVerified: boolean;
  countryCode: string;
  languageCode: string;
  timeZone: string;
  status: string;
  roles: string[];
  permissions: string[];
}

export const participant: MockUser = {
  id: 'u-participant',
  email: 'ada@example.com',
  displayName: 'Ada Lovelace',
  emailVerified: true,
  countryCode: 'GB',
  languageCode: 'en',
  timeZone: 'Europe/London',
  status: 'Active',
  roles: ['Participant'],
  permissions: ['participant.portal'],
};

type Handler = (route: Route) => Promise<void> | void;

export function problem(status: number, code: string, title: string, extra: Record<string, unknown> = {}) {
  return {
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ status, title, code, traceId: 'e2e-trace', ...extra }),
  };
}

export function authResponse(user: MockUser) {
  return {
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      accessToken: 'e2e-token',
      expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
      user,
    }),
  };
}

/**
 * Mocks the API for a page. `handlers` keys are "METHOD /path" relative to /api/v1. Anything unmatched answers
 * 404 so no request ever reaches a real backend. By default the visitor is anonymous (refresh → 401).
 */
export async function mockApi(page: Page, handlers: Record<string, Handler> = {}) {
  const calls: { method: string; path: string; body: unknown }[] = [];
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace(/^\/api\/v1/, '');
    const key = `${request.method()} ${path}`;
    let body: unknown = null;
    try {
      body = request.postDataJSON();
    } catch {
      body = request.postData();
    }
    calls.push({ method: request.method(), path, body });
    const handler =
      handlers[key] ??
      (key === 'POST /auth/refresh'
        ? (r: Route) => r.fulfill(problem(401, 'auth.session_expired', 'Your session has expired.'))
        : (r: Route) => r.fulfill(problem(404, 'http_404', 'Not found')));
    await handler(route);
  });
  return calls;
}

/** True when the page scrolls horizontally. */
export async function hasHorizontalScroll(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
}
