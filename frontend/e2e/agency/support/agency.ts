import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type Browser, type Page, expect } from '@playwright/test';
import type { Credentials } from '../../journeys/support/fixtures';
import { signIn } from '../../journeys/support/ui';

export { API_URL, ApiSession, latestMail, mailLink } from '../../journeys/support/api';
export { axeViolations, expectNoHorizontalScroll, modal, signIn } from '../../journeys/support/ui';

/**
 * Agency-suite helpers. The suite runs against the Demo seed (scripts/e2e-journeys.sh with E2E_SUITE=agency), so the
 * actors are the seeded demo accounts (backend Modules/Clients/DeliveryDemoData.cs, Modules/Seed/DemoSeeder.cs); every
 * record a journey creates carries the run id so reruns against a kept database never collide.
 */
export const DEMO_PASSWORD = 'Demo#2026!pass';

const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

export const accounts = {
  /** Admin (every staff permission; lands in /admin). */
  admin: demo('admin@demo.optimizeall.app', 'Demo Admin'),
  /** Account manager: CRM, proposals, clients, projects, deliverables, email, social (no publish), billing view. */
  am: demo('am@demo.optimizeall.app', 'Account Manager'),
  /** Finance: billing manage (issue invoices). */
  finance: demo('finance1@demo.optimizeall.app', 'Finance'),
  /** Social media manager: social manage + publish (schedules approved posts). */
  social: demo('social@demo.optimizeall.app', 'Social'),
  /** Designer: forms.manage (landing pages and forms). */
  designer: demo('designer@demo.optimizeall.app', 'Designer'),
  /** Nimbus Fitness client user with the Approver duty. */
  nimbusApprover: demo('approver@nimbus.demo.optimizeall.app', 'Nimbus Approver'),
  /** Aurora Skincare client owner (a different organisation). */
  auroraOwner: demo('owner@aurora.demo.optimizeall.app', 'Aurora Owner'),
  /** A creator-program participant. */
  participant: demo('sara.participant@demo.optimizeall.app', 'Sara'),
} as const;

export const clients = {
  nimbus: { slug: 'nimbus-fitness', name: 'Nimbus Fitness' },
  aurora: { slug: 'aurora-skincare', name: 'Aurora Skincare' },
  wanderly: { slug: 'wanderly-travel', name: 'Wanderly Travel' },
} as const;

/** Landing URL patterns per actor. */
export const landing = {
  admin: /\/admin(\/|$)/,
  agency: /\/agency(\/|$)/,
  client: /\/client(\/|$)/,
  participant: /\/app(\/|$)/,
};

// ------------------------------------------------------------------ run state (written by global-setup.ts)

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'agency.json');

export interface AgencyState {
  runId: string;
}

export function writeState(state: AgencyState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

let cached: AgencyState | undefined;
/** Short unique suffix of this run, for names of records the journeys create. */
export function runId(): string {
  cached ??= JSON.parse(readFileSync(STATE_FILE, 'utf8')) as AgencyState;
  return cached.runId;
}

// ------------------------------------------------------------------ browser helpers

/** A fresh context (own session cookie) signed in as `user`, landing on `landingUrl`. */
export async function actor(browser: Browser, user: Credentials, landingUrl: RegExp): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, user, landingUrl);
  return page;
}

/**
 * Collects console errors, uncaught page errors and failed API responses. The anonymous session probe
 * (POST /api/v1/auth/refresh → 401) is the app's normal "not signed in" answer and is ignored, together with the
 * browser's "Failed to load resource" console line it produces.
 */
export function watchErrors(page: Page) {
  const problems: string[] = [];
  const expected401 = (url: string) => /\/api\/v1\/auth\/refresh$/.test(new URL(url).pathname);
  let pending401 = 0;
  page.on('response', (res) => {
    if (res.status() < 400) return;
    if (res.status() === 401 && expected401(res.url())) {
      pending401++;
      return;
    }
    problems.push(`HTTP ${res.status()} ${res.request().method()} ${res.url()}`);
  });
  page.on('console', (msg) => {
    if (msg.type() !== 'error') return;
    if (
      /Failed to load resource: the server responded with a status of 401/.test(msg.text()) &&
      pending401 > 0
    ) {
      pending401--;
      return;
    }
    if (/Failed to load resource/.test(msg.text())) return; // already reported by the response listener
    // Playwright's trace recorder injects its snapshot script into every frame; Chromium refuses it in the app's
    // sandboxed srcdoc previews (email/social previews use sandbox="") and logs this. It never happens without tracing.
    if (/^Blocked script execution in 'about:srcdoc'/.test(msg.text())) return;
    problems.push(`console: ${msg.text()}`);
  });
  page.on('pageerror', (err) => problems.push(`pageerror: ${err.message}`));
  const ignored: RegExp[] = [];
  return {
    problems,
    /** Ignores problems matching `pattern` (e.g. a 404 the journey provokes on purpose). */
    ignore(pattern: RegExp) {
      ignored.push(pattern);
    },
    expectClean(where: string) {
      expect(
        problems.filter((p) => !ignored.some((re) => re.test(p))),
        `errors on ${where}`,
      ).toEqual([]);
    },
  };
}

/** The path (+ query) of an absolute share link, so it opens against the app under test whatever its base URL. */
export function pathOf(link: string): string {
  const u = new URL(link);
  return `${u.pathname}${u.search}`;
}

/** A toast in the app's "Notifications" live region containing `text`. */
export function toast(page: Page, text: string | RegExp) {
  return page
    .getByRole('region', { name: 'Notifications' })
    .getByRole('listitem')
    .filter({ hasText: text })
    .first();
}

/** yyyy-mm-dd `days` from today (UTC), for date inputs. */
export function isoDate(days: number): string {
  return new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);
}
