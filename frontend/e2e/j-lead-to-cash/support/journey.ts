import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type Page, expect } from '@playwright/test';
import type { Credentials } from '../../journeys/support/fixtures';
import { API_URL, ApiError, ApiSession, latestMail, mailLink } from '../../journeys/support/api';
import { DEMO_PASSWORD, accounts as agencyAccounts } from '../../agency/support/agency';
import { accounts as platformAccounts } from '../../platform/support/platform';

export {
  axeViolations,
  clients,
  expect,
  isoDate,
  landing,
  modal,
  pathOf,
  test,
  toast,
  watchErrors,
} from '../../platform/support/platform';
export { API_URL, ApiError, ApiSession, latestMail, mailLink };

/**
 * Lead-to-cash journey helpers (E2E_SUITE=j-lead-to-cash). Runs against the Demo seed like the agency and platform
 * suites: an anonymous visitor becomes a lead on the public website, sales works it in the CRM and sends a proposal,
 * the client accepts it, and billing collects the first recurring invoice. Every record carries the run id so reruns
 * against a kept database never collide.
 */
const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

export const accounts = {
  ...agencyAccounts,
  ...platformAccounts,
  /** Sales rep (crm.manage, proposals.manage, billing.view — no contracts, no billing.manage). */
  sales: demo('sales@demo.optimizeall.app', 'Omar Farooq'),
  /** Nimbus Fitness client owner (another organisation than the one this journey creates). */
  nimbusOwner: demo('owner@nimbus.demo.optimizeall.app', 'Nimbus Owner'),
} as const;

export const FINANCE_LANDING = /\/(finance|agency)(\/|$)/;

// ------------------------------------------------------------------ run state (written by global-setup.ts)

export const STATE_FILE = join(
  dirname(fileURLToPath(import.meta.url)),
  '..',
  '.state',
  'j-lead-to-cash.json',
);

export interface JourneyState {
  runId: string;
  [key: string]: string;
}

export function writeState(state: JourneyState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

export function readState(): JourneyState {
  return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as JourneyState;
}

/** Records values later specs need (ids, links); the specs run serially in file order. */
export function remember(values: Record<string, string>) {
  writeState({ ...readState(), ...values });
}

/** A value an earlier spec remembered; fails clearly when that spec did not get that far. */
export function recall(key: string): string {
  const value = readState()[key];
  if (!value)
    throw new Error(`"${key}" was not recorded — an earlier lead-to-cash spec failed before recording it`);
  return value;
}

export function runId(): string {
  return readState().runId;
}

/** The identity of this run's prospect (the visitor who becomes a client). */
export function prospect() {
  const id = runId();
  return {
    name: `Lena Lead ${id}`,
    firstName: 'Lena',
    email: `lena.${id}@brightpeak-${id}.test`,
    company: `Brightpeak ${id}`,
    website: `brightpeak-${id}.test`,
    password: `Client#Pass-${id}-2026`,
  };
}

// ------------------------------------------------------------------ public forms

const API_BASE = `${API_URL}/api/v1`;

export interface FormToken {
  token: string;
  minFillSeconds: number;
}

/** A fresh public form token (GET /public/forms/token). */
export async function formToken(): Promise<FormToken & { issuedAt: number }> {
  const res = await fetch(`${API_BASE}/public/forms/token`);
  expect(res.ok, 'form token').toBe(true);
  return { ...((await res.json()) as FormToken), issuedAt: Date.now() };
}

/** Waits (polling, no fixed sleep) until the token is older than the server's minimum fill time. */
export async function waitMinFill(token: FormToken & { issuedAt: number }) {
  await expect
    .poll(() => Date.now() - token.issuedAt, { message: 'form token older than the minimum fill time' })
    .toBeGreaterThanOrEqual(token.minFillSeconds * 1000 + 250);
}

/** POSTs a public form as an anonymous visitor; returns status and body (never throws on 4xx). */
export async function postPublic(
  path: string,
  body: unknown,
): Promise<{ status: number; body: { code?: string } & Record<string, unknown> }> {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  return { status: res.status, body: text ? JSON.parse(text) : {} };
}

/** Waits until the page's own form token (fetched on mount) is older than the minimum fill time. */
export async function pageFormToken(page: Page, open: () => Promise<unknown>) {
  const tokenResponse = page.waitForResponse((r) => r.url().includes('/api/v1/public/forms/token') && r.ok());
  await open();
  const token = (await (await tokenResponse).json()) as FormToken;
  return { ...token, issuedAt: Date.now() };
}

// ------------------------------------------------------------------ API status helper

/** Resolves to the HTTP status (and error code) of an API call arranged through ApiSession. */
export async function statusOf(call: Promise<unknown>): Promise<{ status: number; code?: string }> {
  try {
    await call;
    return { status: 200 };
  } catch (error) {
    if (error instanceof ApiError) return { status: error.status, code: error.code };
    throw error;
  }
}

/** Signs in through the API (bearer token), for arranging data and asserting server-side refusals. */
export function apiAs(user: Credentials) {
  return ApiSession.login(user.email, user.password);
}
