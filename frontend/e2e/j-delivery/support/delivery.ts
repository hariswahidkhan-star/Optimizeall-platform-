import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type BrowserContext, type Page, test as base, expect } from '@playwright/test';
import { API_URL, ApiError, ApiSession } from '../../journeys/support/api';
import type { Credentials } from '../../journeys/support/fixtures';
import { makePng } from '../../journeys/support/png';
import { signIn } from '../../journeys/support/ui';
import { DEMO_PASSWORD, accounts as agencyAccounts } from '../../agency/support/agency';

export { API_URL, ApiError, ApiSession, latestMail, mailLink } from '../../journeys/support/api';
export { makePng } from '../../journeys/support/png';
export { clients, isoDate, landing, modal, toast, watchErrors } from '../../agency/support/agency';

/**
 * Client-delivery journey helpers (E2E_SUITE=j-delivery, Demo seed). The account manager onboards a brand-new client
 * in 01-onboarding.spec.ts; every later spec builds on that client, its invited users and its project, which the specs
 * hand on through the run state file (.state/j-delivery.json). Every record carries the run id, so reruns against a
 * kept database never collide.
 */
const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

export const accounts = {
  ...agencyAccounts,
  /** Strategist (Daniel Okafor): projects.manage, deliverables.submit, time.track, reports.manage. */
  strategist: demo('strategist@demo.optimizeall.app', 'Daniel Okafor'),
  /** Content writer (Priya Nair): delivery staff without projects.manage. */
  content: demo('content@demo.optimizeall.app', 'Priya Nair'),
  /** Designer (Lucas Moreau): delivery staff without projects.manage. */
  designerStaff: demo('designer@demo.optimizeall.app', 'Lucas Moreau'),
  /** Nimbus Fitness Owner (another tenant than the journey's client). */
  nimbusOwner: demo('owner@nimbus.demo.optimizeall.app', 'Nimbus Owner'),
} as const;

/** Display names of the seeded staff, as the pickers show them. */
export const staffNames = {
  am: 'Amira Haddad',
  admin: 'Nadia Rahman',
  strategist: 'Daniel Okafor',
  content: 'Priya Nair',
  designer: 'Lucas Moreau',
} as const;

/** The four client duties, in the order the journey invites them. */
export const DUTIES = ['Owner', 'Approver', 'Billing', 'Viewer'] as const;
export type Duty = (typeof DUTIES)[number];

/** Password every invited client user chooses through the emailed set-password link. */
export const CLIENT_PASSWORD = 'Client#Delivery-2026!';

// ------------------------------------------------------------------ run state

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'j-delivery.json');

export interface ClientUserState extends Credentials {
  id: string;
}

export interface DeliveryState {
  runId: string;
  client?: { id: string; name: string };
  users?: Partial<Record<Duty, ClientUserState>>;
  project?: { id: string; name: string };
  /** A client-visible task and an internal (not client-visible) task of the project. */
  sharedTask?: { id: string; title: string };
  internalTask?: { id: string; title: string; attachmentFileId?: string };
  deliverable?: { id: string; title: string };
}

export function writeState(state: DeliveryState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

export function state(): DeliveryState {
  return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as DeliveryState;
}

/** Merges `patch` into the run state (specs run serially, in file order). */
export function saveState(patch: Partial<DeliveryState>) {
  const current = existsSync(STATE_FILE) ? state() : ({ runId: Date.now().toString(36) } as DeliveryState);
  writeState({ ...current, ...patch });
}

/** A value an earlier spec of the journey stored; fails with a clear message when that spec did not run. */
export function need<K extends keyof DeliveryState>(key: K): NonNullable<DeliveryState[K]> {
  const value = state()[key];
  if (value === undefined || value === null)
    throw new Error(`The run state has no "${key}": run the j-delivery specs in order (01-onboarding first).`);
  return value as NonNullable<DeliveryState[K]>;
}

export function clientUser(duty: Duty): ClientUserState {
  const user = need('users')[duty];
  if (!user) throw new Error(`No invited ${duty} in the run state: 01-onboarding.spec.ts must run first.`);
  return user;
}

export const runId = () => state().runId;

// ------------------------------------------------------------------ fixtures

export interface DeliveryFixtures {
  /** A fresh browser context (own session cookie) signed in as `user`; closed when the test ends. */
  as: (user: Credentials, landingUrl: RegExp) => Promise<Page>;
  /** A fresh anonymous browser context; closed when the test ends. */
  anonymous: () => Promise<Page>;
}

export const test = base.extend<DeliveryFixtures & { contexts: BrowserContext[] }>({
  contexts: async ({ browser: _browser }, provide) => {
    const contexts: BrowserContext[] = [];
    await provide(contexts);
    for (const context of contexts) await context.close();
  },
  as: async ({ browser, contexts }, provide) => {
    await provide(async (user, landingUrl) => {
      const context = await browser.newContext();
      contexts.push(context);
      const page = await context.newPage();
      await signIn(page, user, landingUrl);
      return page;
    });
  },
  anonymous: async ({ browser, contexts }, provide) => {
    await provide(async () => {
      const context = await browser.newContext();
      contexts.push(context);
      return context.newPage();
    });
  },
});

export { expect };

// ------------------------------------------------------------------ API helpers (arrange + authorization checks)

/** HTTP status of an API call made through ApiSession (200 when it succeeded). */
export async function statusOf(call: Promise<unknown>): Promise<number> {
  try {
    await call;
    return 200;
  } catch (error) {
    if (error instanceof ApiError) return error.status;
    throw error;
  }
}

/** The error code of a failed API call (fails the test when the call succeeded). */
export async function errorOf(call: Promise<unknown>): Promise<{ status: number; code: string | undefined }> {
  try {
    await call;
  } catch (error) {
    if (error instanceof ApiError) return { status: error.status, code: error.code };
    throw error;
  }
  throw new Error('Expected the API call to fail, but it succeeded');
}

export function login(user: Credentials) {
  return ApiSession.login(user.email, user.password);
}

/** Raw authenticated request (for verbs and bodies ApiSession does not cover); returns status and parsed body. */
export async function raw(
  session: ApiSession,
  method: string,
  path: string,
  init: { json?: unknown; form?: FormData } = {},
): Promise<{ status: number; body: unknown }> {
  const headers: Record<string, string> = {
    'X-Requested-With': 'fetch',
    Authorization: `Bearer ${session.token}`,
  };
  let body: BodyInit | undefined;
  if (init.form) body = init.form;
  else if (init.json !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(init.json);
  }
  const res = await fetch(`${API_URL}/api/v1${path}`, { method, headers, body });
  const text = await res.text();
  let parsed: unknown = text;
  try {
    parsed = text ? JSON.parse(text) : undefined;
  } catch {
    // not JSON (a file)
  }
  return { status: res.status, body: parsed };
}

// ------------------------------------------------------------------ files

/** A real PNG for `setInputFiles` (distinct per seed). */
export function png(seed: number, name = `delivery-${seed}.png`) {
  return { name, mimeType: 'image/png', buffer: makePng(seed, 64, 64) };
}

/** A minimal, valid one-page PDF. */
export function pdf(name = 'brief.pdf', text = 'Optimize All e2e') {
  const body = `%PDF-1.4
1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj
2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj
3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 100]/Contents 4 0 R>>endobj
4 0 obj<</Length ${text.length + 30}>>stream
BT /F1 12 Tf 10 50 Td (${text}) Tj ET
endstream endobj
trailer<</Root 1 0 R>>
%%EOF
`;
  return { name, mimeType: 'application/pdf', buffer: Buffer.from(body, 'latin1') };
}

/** A text file disguised as a PNG (name and MIME type lie; the content is checked). */
export function fakePng(name = 'not-really.png') {
  return { name, mimeType: 'image/png', buffer: Buffer.from('this is plain text, not an image\n'.repeat(20)) };
}

/** A PNG header followed by padding, `bytes` long in total (for the 50 MB limit). */
export function sizedPng(bytes: number, name = 'huge.png') {
  const buffer = Buffer.alloc(bytes);
  makePng(1, 8, 8).copy(buffer);
  return { name, mimeType: 'image/png', buffer };
}

export const MAX_UPLOAD_BYTES = 50 * 1024 * 1024;
