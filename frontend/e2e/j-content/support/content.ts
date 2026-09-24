import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type Browser, type Page, expect } from '@playwright/test';
import { API_URL, ApiError, ApiSession } from '../../journeys/support/api';
import type { Credentials } from '../../journeys/support/fixtures';
import { makePng } from '../../journeys/support/png';
import { modal, signIn } from '../../journeys/support/ui';

export { API_URL, ApiError, ApiSession, makePng, modal, signIn };
export { toast, watchErrors } from '../../agency/support/agency';

/**
 * Site content, SEO and landing-page builder journey (E2E_SUITE=j-content). Runs against Baseline + Demo: the actors
 * are the seeded demo staff (admin, the content writer with blog.write, the designer with forms.manage, the account
 * manager with none of the content permissions) plus a "website editor" created by global-setup.ts whose site.manage
 * and blog.publish come from a custom role only. Every record a spec creates carries the run id.
 */
export const DEMO_PASSWORD = 'Demo#2026!pass';

const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

export const accounts = {
  /** Every staff permission (site.manage, blog.publish, content.manage, users.impersonate…); lands in /admin. */
  admin: demo('admin@demo.optimizeall.app', 'Demo Admin'),
  /** Content creator: blog.write only on the website (drafts and submits posts, never publishes). */
  writer: demo('content@demo.optimizeall.app', 'Priya Nair'),
  /** Designer: forms.manage (landing pages and forms), no site.manage. */
  designer: demo('designer@demo.optimizeall.app', 'Lucas Moreau'),
  /** Account manager: CRM, clients, billing view… but no site.manage, blog.* or forms.manage. */
  am: demo('am@demo.optimizeall.app', 'Account Manager'),
  /** Nimbus Fitness client user (client portal only). */
  nimbusApprover: demo('approver@nimbus.demo.optimizeall.app', 'Nimbus Approver'),
} as const;

export const clients = {
  nimbus: { slug: 'nimbus-fitness', name: 'Nimbus Fitness' },
  aurora: { slug: 'aurora-skincare', name: 'Aurora Skincare' },
} as const;

export const landing = {
  admin: /\/admin(\/|$)/,
  agency: /\/agency(\/|$)/,
  client: /\/client(\/|$)/,
};

// ------------------------------------------------------------------ run state (written by global-setup.ts)

export interface Editor extends Credentials {
  id: string;
}

export interface State {
  runId: string;
  /** A test account with the ContentCreator role plus the custom "Website editor" role (site.manage, blog.publish). */
  editor: Editor;
  roleId: string;
}

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'state.json');
const SHARED_FILE = join(dirname(STATE_FILE), 'shared.json');

export function writeState(s: State) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(s, null, 2));
  writeFileSync(SHARED_FILE, '{}');
}

let cached: State | undefined;
export function state(): State {
  cached ??= JSON.parse(readFileSync(STATE_FILE, 'utf8')) as State;
  return cached;
}

export const runId = () => state().runId;

/** Values later specs need from earlier ones (the suite runs serially, in file order). */
export function remember(key: string, value: unknown) {
  const all = JSON.parse(readFileSync(SHARED_FILE, 'utf8')) as Record<string, unknown>;
  all[key] = value;
  writeFileSync(SHARED_FILE, JSON.stringify(all, null, 2));
}
export function recall<T>(key: string): T {
  const all = JSON.parse(readFileSync(SHARED_FILE, 'utf8')) as Record<string, unknown>;
  if (!(key in all)) throw new Error(`"${key}" was not remembered by an earlier spec`);
  return all[key] as T;
}

// ------------------------------------------------------------------ API helpers

const sessions = new Map<string, Promise<ApiSession>>();
/** A cached API session per user. */
export function api(user: Credentials): Promise<ApiSession> {
  let s = sessions.get(user.email);
  if (!s) {
    s = ApiSession.login(user.email, user.password);
    sessions.set(user.email, s);
  }
  return s;
}

/** Calls the API and returns the ApiError it fails with (the test fails if the call succeeds). */
export async function refused(call: Promise<unknown>): Promise<ApiError> {
  try {
    await call;
  } catch (error) {
    if (error instanceof ApiError) return error;
    throw error;
  }
  throw new Error('Expected the API to refuse the request, but it succeeded');
}

/** A raw anonymous GET against the API origin (status + body), for public reads, sitemap and robots. */
export async function anonGet(path: string): Promise<{ status: number; text: string; json: () => unknown }> {
  const res = await fetch(`${API_URL}${path}`, { headers: { 'X-Requested-With': 'fetch' } });
  const text = await res.text();
  return { status: res.status, text, json: () => JSON.parse(text) as unknown };
}

/** An API call with an explicit bearer token (e.g. an impersonation token). */
export async function withToken<T>(token: string, method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${API_URL}/api/v1${path}`, {
    method,
    headers: {
      'X-Requested-With': 'fetch',
      Authorization: `Bearer ${token}`,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  const json = text ? (JSON.parse(text) as { code?: string }) : undefined;
  if (!res.ok) throw new ApiError(res.status, json?.code, json ?? text, `${method} ${path}`);
  return json as T;
}

/** Locations listed in the public sitemap (paths only). */
export async function sitemapPaths(): Promise<string[]> {
  const res = await anonGet('/api/v1/public/sitemap.xml');
  expect(res.status).toBe(200);
  return [...res.text.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => new URL(m[1]!).pathname);
}

/** An uploadable PNG (400×400 by default) as a multipart form with a `file` part. */
export function imageForm(seed: number, width = 400, height = 400, name = `image-${seed}.png`): FormData {
  const form = new FormData();
  form.append('file', new Blob([makePng(seed, width, height)], { type: 'image/png' }), name);
  return form;
}

// ------------------------------------------------------------------ browser helpers

/** A fresh context (own session cookie) signed in as `user`, landing on `landingUrl`. */
export async function actor(browser: Browser, user: Credentials, landingUrl: RegExp): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, user, landingUrl);
  return page;
}

/** A fresh anonymous visitor (no session, empty caches). */
export async function visitor(browser: Browser): Promise<Page> {
  const context = await browser.newContext();
  return context.newPage();
}

/** The value of a `<meta name|property=…>` tag in the document head (null when absent). */
export function meta(page: Page, key: string) {
  return page.evaluate((k) => {
    const el = document.head.querySelector(`meta[name="${k}"], meta[property="${k}"]`);
    return el?.getAttribute('content') ?? null;
  }, key);
}

/** The canonical link of the document (null when absent). */
export function canonical(page: Page) {
  return page.evaluate(
    () => document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]')?.href ?? null,
  );
}

/** Parsed JSON-LD objects written into the document head. */
export function jsonLd(page: Page) {
  return page.evaluate(() =>
    Array.from(document.head.querySelectorAll('script[type="application/ld+json"]')).map(
      (s) => JSON.parse(s.textContent ?? 'null') as Record<string, unknown>,
    ),
  );
}

/** The public site's "page not found" state. */
export function notFound(page: Page) {
  return page.getByRole('heading', { level: 1, name: 'We couldn’t find that page' });
}

/** Opens `path` in a fresh anonymous context and returns the page. */
export async function openPublic(browser: Browser, path: string): Promise<Page> {
  const page = await visitor(browser);
  await page.goto(path);
  return page;
}

/** yyyy-MM-ddTHH:mm in the browser's local time, for datetime-local inputs, `minutes` from now. */
export function localDateTime(minutesFromNow: number): string {
  const d = new Date(Date.now() + minutesFromNow * 60_000);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
