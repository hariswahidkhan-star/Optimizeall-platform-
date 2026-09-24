import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type Browser, type BrowserContext, type Page, expect } from '@playwright/test';
import { API_URL, mailLink, publicApi } from '../../journeys/support/api';
import type { Credentials } from '../../journeys/support/fixtures';
import { signIn } from '../../journeys/support/ui';
import { accounts as agencyAccounts, landing as agencyLanding } from '../../agency/support/agency';

export { API_URL, ApiError, ApiSession, publicApi } from '../../journeys/support/api';
export { linkIn, mailsTo, subjectsTo } from '../../j-participant/support/mail';
export { modal, signIn } from '../../journeys/support/ui';
export { watchErrors } from '../../agency/support/agency';
export { impersonationBanner, signOut } from '../../platform/support/platform';

/**
 * Authentication & security suite (E2E_SUITE=j-auth): runs against the Demo seed (scripts/e2e-journeys.sh) with the
 * dev mailbox, the non-production test sign-in on and Google sign-in not configured. Accounts it changes (passwords,
 * lockouts, sessions) are always fresh ones it registers itself; the seeded demo accounts are only signed in with.
 */
export const DEMO_PASSWORD = 'Demo#2026!pass';

const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

/** One seeded account per portal. */
export const accounts = {
  ...agencyAccounts,
  reviewer: demo('reviewer1@demo.optimizeall.app', 'Reviewer 1'),
  manager: demo('manager@demo.optimizeall.app', 'Campaign Manager'),
  suspended: demo('suspended.participant@demo.optimizeall.app', 'Suspended'),
} as const;

export const landing = {
  ...agencyLanding,
  reviewer: /\/review(\/|$)/,
  manager: /\/manage(\/|$)/,
  finance: /\/finance(\/|$)/,
};

export const FORBIDDEN = 'You don’t have access to this page';

// ------------------------------------------------------------------ run state (written by global-setup.ts)

const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'j-auth.json');

export interface AuthState {
  runId: string;
  memo: Record<string, string>;
}

export function writeState(state: AuthState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

export function state(): AuthState {
  return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as AuthState;
}

export const runId = () => state().runId;

/** A unique address for this run (`key` keeps them readable in the mail directory). */
export const emailFor = (key: string) => `${key}.${runId()}@auth.e2e.optimizeall.test`;

export const PASSWORD = 'Auth-Journey#2026!';

/** Registers and verifies a participant through the API (dev mailbox). Returns its credentials. */
export async function registerVerified(key: string, displayName = `Auth ${key}`): Promise<Credentials> {
  const user = { email: emailFor(key), password: PASSWORD, displayName };
  await registerUnverified(user);
  const verify = await mailLink(user.email, '/verify-email', /verify/i);
  await publicApi.post('/auth/verify-email', { token: verify.searchParams.get('token') });
  return user;
}

export async function registerUnverified(user: Credentials) {
  await publicApi.post('/auth/register', {
    email: user.email,
    password: user.password,
    displayName: user.displayName,
    countryCode: 'GB',
    languageCode: 'en',
    timeZone: 'UTC',
    acceptTerms: true,
  });
}

// ------------------------------------------------------------------ raw HTTP (headers, cookies, status codes)

export interface RawResponse {
  status: number;
  headers: Headers;
  json: Record<string, unknown> | undefined;
  /** Every Set-Cookie header of the response. */
  setCookies: string[];
}

/** A request straight to the API (not through the browser), with full control over headers. */
export async function raw(
  method: string,
  path: string,
  {
    token,
    body,
    headers = {},
    base = API_URL,
  }: { token?: string; body?: unknown; headers?: Record<string, string>; base?: string } = {},
): Promise<RawResponse> {
  const h: Record<string, string> = { ...headers };
  if (token) h.Authorization = `Bearer ${token}`;
  if (body !== undefined) h['Content-Type'] = 'application/json';
  const res = await fetch(`${base}/api/v1${path}`, {
    method,
    headers: h,
    body: body === undefined ? undefined : JSON.stringify(body),
    redirect: 'manual',
  });
  const text = await res.text();
  let json: Record<string, unknown> | undefined;
  try {
    json = text ? (JSON.parse(text) as Record<string, unknown>) : undefined;
  } catch {
    json = undefined;
  }
  return { status: res.status, headers: res.headers, json, setCookies: res.headers.getSetCookie() };
}

/** The `oa_refresh=<value>` pair of a response's Set-Cookie headers (throws when absent). */
export function refreshCookie(res: RawResponse): string {
  const cookie = res.setCookies.find((c) => c.startsWith('oa_refresh='));
  if (!cookie) throw new Error(`No oa_refresh cookie in ${JSON.stringify(res.setCookies)}`);
  return cookie.split(';')[0]!;
}

/** Signs in through the API and returns the access token plus the refresh cookie pair. */
export async function apiLogin(user: Credentials) {
  const res = await raw('POST', '/auth/login', { body: { email: user.email, password: user.password } });
  expect(res.status, `login ${user.email}: ${JSON.stringify(res.json)}`).toBe(200);
  return { token: res.json!.accessToken as string, cookie: refreshCookie(res), res };
}

/** POST /auth/refresh with a given cookie pair (as a browser would, with the CSRF header). */
export const refreshWith = (cookie: string) =>
  raw('POST', '/auth/refresh', { headers: { Cookie: cookie, 'X-Requested-With': 'fetch' } });

/** Status of an API call made through an ApiSession (200 when it succeeds). */
export const statusOf = (p: Promise<unknown>) =>
  p.then(
    () => 200,
    (e: { status?: number }) => e.status ?? 0,
  );

// ------------------------------------------------------------------ browser helpers

/** A fresh context (own cookie jar) signed in through the UI. */
export async function signedIn(browser: Browser, user: Credentials, landingUrl: RegExp) {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, user, landingUrl);
  return { context, page };
}

/** The refresh cookie the browser holds for the app, if any. */
export async function browserRefreshCookie(context: BrowserContext) {
  return (await context.cookies()).find((c) => c.name === 'oa_refresh');
}

/** Everything the page keeps in Web Storage, as one string (to search for tokens). */
export async function webStorageDump(page: Page): Promise<string> {
  return page.evaluate(() => {
    const dump = (s: Storage) =>
      Array.from({ length: s.length }, (_, i) => `${s.key(i)}=${s.getItem(s.key(i)!)}`);
    return [...dump(localStorage), ...dump(sessionStorage), `cookie=${document.cookie}`].join('\n');
  });
}

/** Signs in through the login form, expecting it to be refused, and returns the alert. */
export async function failSignIn(page: Page, email: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  const alert = page.getByRole('alert').first();
  await expect(alert).toBeVisible();
  return alert;
}
