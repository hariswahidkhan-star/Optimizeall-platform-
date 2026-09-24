import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Locator, Page } from '@playwright/test';
import type { Credentials } from '../../journeys/support/fixtures';
import { API_URL } from '../../journeys/support/api';
import { DEMO_PASSWORD } from '../../agency/support/agency';

export { ApiError, ApiSession, API_URL, latestMail, mailLink, publicApi } from '../../journeys/support/api';
export { modal, signIn } from '../../journeys/support/ui';
export { toast, watchErrors } from '../../agency/support/agency';
export { expect, test } from '../../platform/support/platform';

/**
 * Finance money-journey helpers (E2E_SUITE=j-finance). The suite runs against the Demo seed like the platform suite
 * (scripts/e2e-journeys.sh seeds Baseline + Demo) and adds its own participants in global-setup.ts, so every amount the
 * journey asserts is known exactly: the Demo participants' earnings land in the same batches, but the assertions only
 * ever look at the journey's own participants (or at sums the API itself reports).
 */
const demo = (email: string, displayName: string): Credentials => ({ email, password: DEMO_PASSWORD, displayName });

export const accounts = {
  /** Finance 1 (Fatima Al-Mansoori): prepares batches, creates adjustments, records payments. */
  finance1: demo('finance1@demo.optimizeall.app', 'Fatima Al-Mansoori'),
  /** Finance 2 (James Whitfield): the second pair of eyes (approves credits, reverses/refunds). */
  finance2: demo('finance2@demo.optimizeall.app', 'James Whitfield'),
  /** Admin (Nadia Rahman): finalizes when both finance users are conflicted; impersonates. */
  admin: demo('admin@demo.optimizeall.app', 'Nadia Rahman'),
  /** Nimbus Fitness client user with the Billing duty ("I've paid"). */
  nimbusBilling: demo('billing@nimbus.demo.optimizeall.app', 'Nimbus Billing'),
} as const;

export const FINANCE_LANDING = /\/finance(\/|$)/;
export const ADMIN_LANDING = /\/admin(\/|$)/;
export const CLIENT_LANDING = /\/client(\/|$)/;
export const PARTICIPANT_LANDING = /\/app(\/|$)/;

// ------------------------------------------------------------------ run state (written by global-setup.ts)

export interface Participant extends Credentials {
  id: string;
}

export interface FinanceState {
  runId: string;
  /** Journey participants (registered, verified), keyed by role in the journey. */
  participants: {
    /** Paid in the first batch (USD + PKR + AED credits and a debit). PayPal. */
    ana: Participant;
    /** Below the minimum in the first batch (carried over), exactly at the minimum in the second. Bank transfer. */
    ben: Participant;
    /** On a payout hold during the first batch; released before the second. PayPal. */
    cat: Participant;
    /** Paid in the first batch, returned by the bank → re-queued into the second. PayPal. */
    dan: Participant;
    /** No payout details on file: a Held item. */
    eve: Participant;
  };
  /** A test account (Participant role) with a credit: never paid (TestAccount exclusion). */
  tess: Participant;
  clientIds: Record<'nimbus' | 'karachi' | 'aurora' | 'wanderly', string>;
}

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'j-finance.json');
/** Values one spec hands to the next (batch ids, item ids…), merged into the state file. */
export const SHARED_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'j-finance-shared.json');

export function writeState(state: FinanceState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
  writeFileSync(SHARED_FILE, '{}');
}

let cached: FinanceState | undefined;
export function state(): FinanceState {
  cached ??= JSON.parse(readFileSync(STATE_FILE, 'utf8')) as FinanceState;
  return cached;
}

export function shared<T = Record<string, unknown>>(): T {
  return JSON.parse(readFileSync(SHARED_FILE, 'utf8')) as T;
}

export function share(values: Record<string, unknown>) {
  writeFileSync(SHARED_FILE, JSON.stringify({ ...shared(), ...values }, null, 2));
}

// ------------------------------------------------------------------ raw API (status codes and headers)

export interface RawResponse<T = unknown> {
  status: number;
  body: T;
  headers: Headers;
}

/** An API call that never throws: the status and parsed body, for asserting refusals exactly. */
export async function raw<T = Record<string, unknown>>(
  token: string | null,
  method: string,
  path: string,
  body?: unknown,
): Promise<RawResponse<T>> {
  const headers: Record<string, string> = { 'X-Requested-With': 'fetch' };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(`${API_URL}/api/v1${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let parsed: unknown = text;
  try {
    parsed = text ? JSON.parse(text) : undefined;
  } catch {
    /* CSV or plain text */
  }
  return { status: res.status, body: parsed as T, headers: res.headers };
}

// ------------------------------------------------------------------ money

/** Exact rounding to a currency's minor units, half away from zero (the server's Money.Round). */
export function round(amount: number, digits = 2): number {
  const f = 10 ** digits;
  const scaled = Math.abs(amount) * f;
  // Guard against binary noise (e.g. 1.005 * 100 = 100.49999…): round the scaled value to 6 places first.
  const r = Math.round(Number(scaled.toFixed(6)));
  return (Math.sign(amount) * r) / f;
}

/** "$1,234.56" / "PKR 1,234.00" as the app's Money component renders it (en-US). */
export function money(amount: number, currency = 'USD'): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(amount);
}

/** Sum of decimal strings/numbers as integer minor units (no float drift), back to a number. */
export function sum(values: (number | string)[], digits = 2): number {
  const f = 10 ** digits;
  return values.reduce<number>((acc, v) => acc + Math.round(Number(v) * f), 0) / f;
}

// ------------------------------------------------------------------ UI helpers

/** A table (by caption) row that contains `text`. */
export function row(page: Page | Locator, table: string, text: string | RegExp): Locator {
  return page.getByRole('table', { name: table }).getByRole('row').filter({ hasText: text });
}

/** Fills a FormDialog and ticks its confirmation checkbox when it has one. */
export async function confirmBox(dialog: Locator, label: string | RegExp) {
  await dialog.getByRole('checkbox', { name: label }).check();
}

/** Picks a participant in the finance UserPicker by pasting the user id (works with and without users.view). */
export async function pickUser(dialog: Locator, userId: string) {
  await dialog.getByLabel(/^(Or paste the user id|User id)/).fill(userId);
}

/** `yyyy-mm-ddThh:mm` for a datetime-local input, in UTC (the suite runs its browsers in UTC). */
export function localMinute(date = new Date()): string {
  return date.toISOString().slice(0, 16);
}
