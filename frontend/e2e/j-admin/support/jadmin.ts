import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type Page, expect } from '@playwright/test';
import { API_URL, ApiError, ApiSession } from '../../journeys/support/api';
import { accounts } from '../../platform/support/platform';

export { ApiError, ApiSession, API_URL };
export {
  DEMO_ADMIN_NAME,
  accounts,
  expect,
  impersonationBanner,
  landing,
  linkNames,
  modal,
  portalNav,
  signOut,
  test,
  toast,
  watchErrors,
} from '../../platform/support/platform';

/**
 * Helpers of the platform-administration journey suite (E2E_SUITE=j-admin): users, custom roles, test accounts and
 * "log in as", audit, settings, content, jobs and global search, against the Demo seed with the non-production test
 * sign-in on (scripts/e2e-journeys.sh). Every record a journey creates carries the run id so reruns against a kept
 * database never collide.
 */

// ------------------------------------------------------------------ run state (written by global-setup.ts)

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'j-admin.json');

export interface JAdminState {
  runId: string;
}

export function writeState(state: JAdminState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

let cached: JAdminState | undefined;
/** Short unique suffix of this run, for names of records the journeys create. */
export function runId(): string {
  cached ??= JSON.parse(readFileSync(STATE_FILE, 'utf8')) as JAdminState;
  return cached.runId;
}

// ------------------------------------------------------------------ API helpers

/** HTTP status of an API promise: 2xx → 200, otherwise the error's status. */
export const statusOf = (p: Promise<unknown>) =>
  p.then(
    () => 200,
    (e: { status?: number }) => e.status ?? 0,
  );

/** Error code of a refused API call (undefined when it succeeded). */
export const codeOf = (p: Promise<unknown>) =>
  p.then(
    () => undefined,
    (e: unknown) => (e instanceof ApiError ? `${e.status} ${e.code ?? ''}`.trim() : String(e)),
  );

export async function adminApi(): Promise<ApiSession> {
  return ApiSession.login(accounts.admin.email, accounts.admin.password);
}

/**
 * Raw request with an explicit bearer token (e.g. one the browser holds while impersonating). Returns status and
 * parsed body; never throws on HTTP errors.
 */
export async function rawApi(
  method: string,
  path: string,
  token: string | null,
  body?: unknown,
): Promise<{ status: number; body: { code?: string } & Record<string, unknown> }> {
  const headers: Record<string, string> = { 'X-Requested-With': 'fetch' };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(`${API_URL}/api/v1${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json: Record<string, unknown> = {};
  try {
    json = text ? (JSON.parse(text) as Record<string, unknown>) : {};
  } catch {
    json = { text };
  }
  return { status: res.status, body: json };
}

/**
 * Tracks the bearer token the page's app sends to the API (the SPA keeps it in memory only), so a journey can call the
 * API "as the browser" — e.g. prove that an impersonation token is refused for a blocked action.
 */
export function trackBearer(page: Page) {
  let token: string | null = null;
  page.on('request', (req) => {
    const auth = req.headers().authorization;
    if (auth?.startsWith('Bearer ') && new URL(req.url()).pathname.startsWith('/api/v1/'))
      token = auth.slice(7);
  });
  return {
    /** The latest token the page sent (fails the test when the page has not called the API yet). */
    current(): string {
      expect(token, 'the page has not sent an authenticated API request yet').not.toBeNull();
      return token!;
    },
  };
}

export interface CreatedTestUser {
  id: string;
  email: string;
  displayName: string;
  password: string;
  roles: string[];
}

/** Arranges a test user through the API as the demo admin (the UI flow is covered by 03-test-accounts). */
export async function arrangeTestUser(
  displayName: string,
  roles: string[] = ['Participant'],
): Promise<CreatedTestUser> {
  const admin = await adminApi();
  return admin.post<CreatedTestUser>('/admin/test-users', { roles, displayName });
}

export interface CustomRoleDto {
  id: string;
  name: string;
  permissions: string[];
  userCount: number;
  concurrencyStamp: string;
}

/** Arranges a custom role (as the demo admin) and optionally assigns it to users. */
export async function arrangeRole(
  name: string,
  permissions: string[],
  assignTo: string[] = [],
): Promise<CustomRoleDto> {
  const admin = await adminApi();
  const saved = await admin.post<{ role: CustomRoleDto }>('/admin/roles', { name, permissions });
  for (const userId of assignTo) await admin.put(`/admin/roles/${saved.role.id}/users/${userId}`);
  return saved.role;
}

/** The "You don’t have access to this page" heading the router renders on a 403. */
export const FORBIDDEN = 'You don’t have access to this page';
