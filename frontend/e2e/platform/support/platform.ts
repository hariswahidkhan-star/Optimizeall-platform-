import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type BrowserContext, type Locator, type Page, test as base, expect } from '@playwright/test';
import type { Credentials } from '../../journeys/support/fixtures';
import { ApiSession } from '../../journeys/support/api';
import { signIn } from '../../journeys/support/ui';
import { DEMO_PASSWORD, accounts as agencyAccounts } from '../../agency/support/agency';

export {
  API_URL,
  ApiSession,
  axeViolations,
  clients,
  expectNoHorizontalScroll,
  isoDate,
  landing,
  modal,
  pathOf,
  signIn,
  toast,
  watchErrors,
} from '../../agency/support/agency';

/**
 * Platform-suite helpers (test users and login-as, custom roles, the payments hub, editing across areas, Google sign-in
 * off). Runs against the Demo seed like the agency suite (scripts/e2e-journeys.sh with E2E_SUITE=platform), with the
 * non-production test sign-in enabled and Google sign-in not configured. Every record a journey creates carries the run
 * id so reruns against a kept database never collide.
 */
const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

export const accounts = {
  ...agencyAccounts,
  /** Second finance user (the other pair of eyes). */
  finance2: demo('finance2@demo.optimizeall.app', 'Finance 2'),
  /** Nimbus Fitness client user with the Billing duty (sees invoices, reports payments). */
  nimbusBilling: demo('billing@nimbus.demo.optimizeall.app', 'Nimbus Billing'),
  /** SEO specialist (seo.manage). */
  seo: demo('seo@demo.optimizeall.app', 'SEO'),
} as const;

/** Display name of the demo admin (admin@demo.optimizeall.app), as the audit log and banner show it. */
export const DEMO_ADMIN_NAME = 'Nadia Rahman';

// ------------------------------------------------------------------ run state (written by global-setup.ts)

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'platform.json');

export interface PlatformState {
  runId: string;
}

export function writeState(state: PlatformState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

let cached: PlatformState | undefined;
/** Short unique suffix of this run, for names of records the journeys create. */
export function runId(): string {
  cached ??= JSON.parse(readFileSync(STATE_FILE, 'utf8')) as PlatformState;
  return cached.runId;
}

// ------------------------------------------------------------------ fixtures

export interface PlatformFixtures {
  /**
   * A fresh browser context (own session cookie) signed in as `user`, landing on `landingUrl`. Every context a test
   * opens is closed when the test ends, so no page keeps polling the API into the next journey.
   */
  as: (user: Credentials, landingUrl: RegExp) => Promise<Page>;
  /** A fresh, anonymous browser context (a visitor or a sign-in page), closed when the test ends. */
  anonymous: () => Promise<Page>;
}

export const test = base.extend<PlatformFixtures & { contexts: BrowserContext[] }>({
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

// ------------------------------------------------------------------ UI helpers

/** The persistent "You are viewing as …" banner shown on every page while impersonating. */
export function impersonationBanner(page: Page) {
  return page.getByRole('region', { name: 'Impersonation' });
}

/** Signs out through the account menu of any portal and waits for the sign-in page. */
export async function signOut(page: Page, displayName: string) {
  await page.getByRole('button', { name: `Account menu for ${displayName}` }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login(\?|$)/);
}

/** The portal's side navigation (desktop). */
export function portalNav(page: Page, name: string) {
  return page.getByRole('navigation', { name });
}

/** Link names of a navigation landmark, in order. */
export async function linkNames(nav: Locator): Promise<string[]> {
  return (await nav.getByRole('link').allInnerTexts()).map((t) => t.trim()).filter(Boolean);
}

export interface CreatedTestUser {
  id: string;
  email: string;
  displayName: string;
  password: string;
}

/**
 * Arranges a test user through the API (POST /admin/test-users, as the demo admin). The UI flow itself is covered by
 * 01-test-users-and-login-as.spec.ts; other journeys only need an account to act on.
 */
export async function arrangeTestUser(
  displayName: string,
  roles: string[] = ['Participant'],
): Promise<CreatedTestUser> {
  const admin = await ApiSession.login(accounts.admin.email, accounts.admin.password);
  return admin.post<CreatedTestUser>('/admin/test-users', { roles, displayName });
}
