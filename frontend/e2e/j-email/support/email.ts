import { createHmac } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type Browser, type BrowserContextOptions, type Page, expect } from '@playwright/test';
import { API_URL, ApiSession } from '../../journeys/support/api';
import type { Credentials } from '../../journeys/support/fixtures';
import { DEMO_PASSWORD, accounts as agencyAccounts } from '../../agency/support/agency';

export { API_URL, ApiError, ApiSession, latestMail, mailLink } from '../../journeys/support/api';
export { landing, modal, toast, watchErrors } from '../../agency/support/agency';

/**
 * Email-marketing journey helpers (E2E_SUITE=j-email, Baseline + Demo seed, file-mode email + dev mailbox, background
 * jobs off). global-setup.ts onboards a brand-new client ("Lumen <run id>") whose email workspace starts empty, invites
 * its owner, and writes the run state; the specs build the workspace up (settings, sender, lists, contacts, templates,
 * campaigns) and hand ids on through the state file. Outbound mail never leaves the machine: the API writes .eml files
 * to $E2E_MAIL_DIR, which the helpers below read.
 */
const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

export const accounts = {
  ...agencyAccounts,
  /** Content writer (Priya Nair): email.manage but not email.send. */
  content: demo('content@demo.optimizeall.app', 'Priya Nair'),
  /** Designer (Lucas Moreau): no email permission at all. */
  designerStaff: demo('designer@demo.optimizeall.app', 'Lucas Moreau'),
  /** Nimbus Fitness Owner (another tenant than the journey's client). */
  nimbusOwner: demo('owner@nimbus.demo.optimizeall.app', 'Nimbus Owner'),
} as const;

export const CLIENT_PASSWORD = 'Client#Email-2026!';

// ------------------------------------------------------------------ run state

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'j-email.json');

export interface EmailState {
  runId: string;
  client: { id: string; name: string };
  /** The client's invited owner (client portal, approves campaigns). */
  owner: Credentials & { id: string };
  /** Filled in by the specs. */
  memo: Record<string, string>;
}

export function writeState(s: EmailState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(s, null, 2));
}

export function state(): EmailState {
  return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as EmailState;
}

export function remember(key: string, value: string) {
  const s = state();
  s.memo[key] = value;
  writeState(s);
}

export function recall(key: string): string {
  const value = state().memo[key];
  if (!value) throw new Error(`Nothing remembered under "${key}" — did an earlier spec file fail?`);
  return value;
}

/** A unique address for this run (lower case: the API normalizes addresses). */
export function address(local: string): string {
  return `${local}.${state().runId}@e2e-mail.optimizeall.test`.toLowerCase();
}

// ------------------------------------------------------------------ API

const sessions = new Map<string, ApiSession>();
/** A cached API session for `user` (bearer token). */
export async function as(user: Credentials): Promise<ApiSession> {
  const key = `${user.email}\n${user.password}`;
  let s = sessions.get(key);
  if (!s) {
    s = await ApiSession.login(user.email, user.password);
    sessions.set(key, s);
  }
  return s;
}

export interface RawResponse<T = unknown> {
  status: number;
  body: T;
  headers: Headers;
}

/** Any request, returning status + body instead of throwing (for negative cases). */
export async function raw<T = unknown>(
  method: string,
  path: string,
  options: {
    token?: string;
    body?: unknown;
    text?: string;
    headers?: Record<string, string>;
    redirect?: RequestRedirect;
  } = {},
): Promise<RawResponse<T>> {
  const headers: Record<string, string> = { 'X-Requested-With': 'fetch', ...(options.headers ?? {}) };
  if (options.token) headers.Authorization = `Bearer ${options.token}`;
  let payload: BodyInit | undefined;
  if (options.text !== undefined) payload = options.text;
  else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    payload = JSON.stringify(options.body);
  }
  const url = path.startsWith('http')
    ? path
    : `${API_URL}${path.startsWith('/api/') || path.startsWith('/e/') ? '' : '/api/v1'}${path}`;
  const res = await fetch(url, { method, headers, body: payload, redirect: options.redirect ?? 'manual' });
  const text = await res.text();
  let body: unknown = text;
  try {
    body = text ? JSON.parse(text) : undefined;
  } catch {
    /* not JSON */
  }
  return { status: res.status, body: body as T, headers: res.headers };
}

/** `raw` with the bearer token of `user`. */
export async function call<T = unknown>(
  user: Credentials,
  method: string,
  path: string,
  body?: unknown,
): Promise<RawResponse<T>> {
  return raw<T>(method, path, { token: (await as(user)).token, body });
}

/** The error code of a problem response. */
export function codeOf(res: RawResponse): string | undefined {
  return (res.body as { code?: string } | undefined)?.code;
}

/** Runs a background job now (Jobs:Enabled is false in the e2e harness), as the platform admin. */
export async function runJob(name: string) {
  const res = await call<{ status: string; summary: string | null; error: string | null }>(
    accounts.admin,
    'POST',
    `/admin/jobs/${name}/run`,
  );
  expect(res.status, `${name}: ${JSON.stringify(res.body)}`).toBe(200);
  expect(res.body.error, `${name}: ${res.body.summary}`).toBeNull();
  return res.body;
}

export const runSendJob = () => runJob('CampaignSendJob');

export interface CampaignReport {
  recipients: number;
  sent: number;
  pending: number;
  failed: number;
  skipped: number;
  cancelled: number;
  delivered: number;
  deliveredIsEstimated: boolean;
  hardBounces: number;
  softBounces: number;
  uniqueOpens: number;
  totalOpens: number;
  machineOpens: number;
  uniqueClicks: number;
  totalClicks: number;
  unsubscribes: number;
  complaints: number;
  links: { url: string; uniqueClicks: number; totalClicks: number }[];
}

export async function report(campaignId: string): Promise<CampaignReport> {
  const res = await call<CampaignReport>(accounts.am, 'GET', `/agency/email/campaigns/${campaignId}/report`);
  expect(res.status).toBe(200);
  return res.body;
}

export interface Suppression {
  id: string;
  value: string;
  reason: string;
  source: string;
}

export async function suppressions(search: string): Promise<Suppression[]> {
  const res = await call<{ items: Suppression[] }>(
    accounts.am,
    'GET',
    `/agency/email/suppressions?clientAccountId=${state().client.id}&search=${encodeURIComponent(search)}&pageSize=100`,
  );
  expect(res.status).toBe(200);
  return res.body.items;
}

export async function subscriberByEmail(email: string) {
  const res = await call<{ items: { id: string; email: string; status: string; emailConsent: string }[] }>(
    accounts.am,
    'GET',
    `/agency/email/subscribers?clientAccountId=${state().client.id}&search=${encodeURIComponent(email)}&pageSize=10`,
  );
  expect(res.status).toBe(200);
  return res.body.items.find((s) => s.email?.toLowerCase() === email.toLowerCase());
}

// ------------------------------------------------------------------ mail (file-mode pickup directory)

const MAIL_DIR = process.env.E2E_MAIL_DIR;

export interface Mail {
  file: string;
  to: string;
  subject: string;
  headers: Record<string, string>;
  text: string;
  html: string;
}

function decodeWords(value: string): string {
  return value.replace(
    /=\?([^?]+)\?([BbQq])\?([^?]*)\?=/g,
    (_m, _charset: string, enc: string, text: string) =>
      enc.toUpperCase() === 'B'
        ? Buffer.from(text, 'base64').toString('utf8')
        : Buffer.from(
            text
              .replace(/_/g, ' ')
              .replace(/=([0-9A-Fa-f]{2})/g, (_x, h: string) => String.fromCharCode(parseInt(h, 16))),
            'latin1',
          ).toString('utf8'),
  );
}

function parseHeaders(block: string): Record<string, string> {
  const headers: Record<string, string> = {};
  for (const line of block.replace(/\r?\n[ \t]+/g, ' ').split(/\r?\n/)) {
    const i = line.indexOf(':');
    if (i <= 0) continue;
    headers[line.slice(0, i).trim().toLowerCase()] = decodeWords(line.slice(i + 1).trim()).replace(
      /\?=\s+=\?/g,
      '?==?',
    );
  }
  return headers;
}

function decodeBody(body: string, encoding: string | undefined): string {
  const enc = (encoding ?? '').toLowerCase();
  if (enc === 'base64') return Buffer.from(body.replace(/\s+/g, ''), 'base64').toString('utf8');
  if (enc === 'quoted-printable')
    return Buffer.from(
      body
        .replace(/=\r?\n/g, '')
        .replace(/=([0-9A-Fa-f]{2})/g, (_x, h: string) => String.fromCharCode(parseInt(h, 16))),
      'latin1',
    ).toString('utf8');
  return body;
}

/** Collects the text/plain and text/html parts of a MIME entity (recursively through multiparts). */
function parts(headers: Record<string, string>, body: string, into: { text: string; html: string }) {
  const type = headers['content-type'] ?? 'text/plain';
  const boundary = /boundary="?([^";]+)"?/i.exec(type)?.[1];
  if (/^multipart\//i.test(type) && boundary) {
    for (const chunk of body.split(`--${boundary}`).slice(1)) {
      if (chunk.startsWith('--')) break;
      const split = chunk.search(/\r?\n\r?\n/);
      if (split < 0) continue;
      parts(parseHeaders(chunk.slice(0, split)), chunk.slice(split).replace(/^\r?\n\r?\n/, ''), into);
    }
    return;
  }
  const decoded = decodeBody(body, headers['content-transfer-encoding']);
  if (/^text\/html/i.test(type)) into.html += decoded;
  else if (/^text\/plain/i.test(type)) into.text += decoded;
}

function parseMail(file: string): Mail {
  const rawMail = readFileSync(join(MAIL_DIR!, file), 'utf8');
  const split = rawMail.search(/\r?\n\r?\n/);
  const headers = parseHeaders(rawMail.slice(0, split));
  const body = { text: '', html: '' };
  parts(headers, rawMail.slice(split).replace(/^\r?\n\r?\n/, ''), body);
  return { file, to: (headers.to ?? '').toLowerCase(), subject: headers.subject ?? '', headers, ...body };
}

const parsed = new Map<string, Mail>();
/** Every email written for `to`, oldest first. */
export function mailsTo(to: string): Mail[] {
  if (!MAIL_DIR || !existsSync(MAIL_DIR))
    throw new Error('E2E_MAIL_DIR is not set (run the suite through scripts/e2e-journeys.sh)');
  const wanted = to.toLowerCase();
  return readdirSync(MAIL_DIR)
    .filter((f) => f.endsWith('.eml'))
    .sort()
    .map((f) => {
      let m = parsed.get(f);
      if (!m) {
        m = parseMail(f);
        parsed.set(f, m);
      }
      return m;
    })
    .filter((m) => m.to.includes(wanted));
}

/** Emails to `to` whose subject matches. */
export function mailsWith(to: string, subject: RegExp | string): Mail[] {
  return mailsTo(to).filter((m) =>
    typeof subject === 'string' ? m.subject.includes(subject) : subject.test(m.subject),
  );
}

/** Waits for exactly one (or at least one) email to `to` matching `subject` and returns the latest. */
export async function waitForMail(to: string, subject: RegExp | string, timeoutMs = 20_000): Promise<Mail> {
  let found: Mail[] = [];
  await expect
    .poll(() => (found = mailsWith(to, subject)).length, {
      timeout: timeoutMs,
      message: `mail to ${to} matching ${subject}`,
    })
    .toBeGreaterThan(0);
  return found[found.length - 1];
}

/** Every http(s) URL in an email's HTML (attribute values, entity-decoded) and text. */
export function linksIn(mail: Mail): URL[] {
  const html = [...mail.html.matchAll(/(?:href|src)="([^"]+)"/g)].map((m) => m[1].replace(/&amp;/g, '&'));
  const text = mail.text.match(/https?:\/\/[^\s<>"]+/g) ?? [];
  return [...new Set([...html, ...text])].filter((l) => /^https?:\/\//.test(l)).map((l) => new URL(l));
}

/** The path of the first link starting with `prefix` (e.g. "/e/c/"); throws when there is none. */
export function linkPath(mail: Mail, prefix: string): string {
  const link = linksIn(mail).find((u) => u.pathname.startsWith(prefix));
  if (!link)
    throw new Error(
      `No ${prefix} link in "${mail.subject}" to ${mail.to}: ${linksIn(mail)
        .map((u) => u.pathname)
        .join(', ')}`,
    );
  return link.pathname;
}

/** The 6-digit sender verification code from the latest verification email to `to`. */
export async function verificationCode(to: string, after = 0): Promise<string> {
  let code = '';
  await expect
    .poll(
      () => {
        const mail = mailsWith(to, 'Verify your sender address').slice(after).pop();
        code = mail ? (/\b(\d{6})\b/.exec(mail.text)?.[1] ?? '') : '';
        return code;
      },
      { timeout: 20_000, message: `sender verification code for ${to}` },
    )
    .toMatch(/^\d{6}$/);
  return code;
}

// ------------------------------------------------------------------ user agents (tracking classifies them)

/** A desktop mail client: counted as a human open/click. */
export const HUMAN_UA =
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Thunderbird/128.3.0';
/** Apple Mail Privacy Protection prefetch: a machine open. */
export const APPLE_MPP_UA = 'Mozilla/5.0';

// ------------------------------------------------------------------ provider webhooks

/** A Mailgun webhook body signed with `signingKey` (HMAC-SHA256 over timestamp + token). */
export function mailgunEvent(
  signingKey: string,
  eventData: Record<string, unknown>,
  {
    timestamp = Math.floor(Date.now() / 1000),
    token = `tok${Math.random().toString(36).slice(2)}${Date.now()}`,
  } = {},
) {
  const ts = String(timestamp);
  const signature = createHmac('sha256', signingKey)
    .update(ts + token)
    .digest('hex');
  return { signature: { timestamp: ts, token, signature }, 'event-data': eventData };
}

// ------------------------------------------------------------------ browser

/**
 * A fresh page in its own context (own cookies). Reduced motion turns off the app's smooth scrolling: on a loaded
 * machine a smooth scroll can still be moving a button when Playwright clicks it, and the click then misses.
 */
export async function visitorPage(browser: Browser, options: BrowserContextOptions = {}): Promise<Page> {
  const context = await browser.newContext({ reducedMotion: 'reduce', ...options });
  return context.newPage();
}

/** A fresh context signed in as `user`, landing on `landingUrl`. */
export async function actor(browser: Browser, user: Credentials, landingUrl: RegExp): Promise<Page> {
  const page = await visitorPage(browser);
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill(user.email);
  await page.getByLabel('Password', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  // The admin's landing dashboard is heavy; on a loaded CI machine (MySQL) it can take longer than the default.
  await expect(page).toHaveURL(landingUrl, { timeout: 60_000 });
  return page;
}

/** Opens an email-marketing page of the journey's client workspace (the picker is remembered per page). */
export async function openEmail(page: Page, path: string) {
  await page.goto(`/agency/email${path}`);
  const picker = page.getByLabel('Workspace');
  await expect(picker).toBeVisible();
  const name = state().client.name;
  if ((await picker.locator('option:checked').textContent()) !== name)
    await picker.selectOption({ label: name });
  await expect(picker.locator('option:checked')).toHaveText(name);
}

/** A campaign design (block JSON) with a heading, a personalised paragraph, a tracked button and the footer. */
export function design(
  heading: string,
  link: string,
  body = '<p>Hi {{first_name|friend}}, here is something new.</p>',
) {
  return {
    blocks: [
      { type: 'header', title: heading },
      { type: 'text', html: body },
      { type: 'button', text: 'Read more', href: link },
      { type: 'footer' },
    ],
  };
}
