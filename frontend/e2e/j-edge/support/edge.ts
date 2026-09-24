import type { Page } from '@playwright/test';
import { ApiSession, accounts, landing, signIn } from '../../agency/support/agency';

export { ApiSession, accounts, landing, signIn, watchErrors } from '../../agency/support/agency';

/**
 * Helpers of the cross-cutting edge-case suite (E2E_SUITE=j-edge): time zones at the edges of the offset range,
 * calendar dates, currencies with 0/2/3 minor units and search input with wildcard and non-Latin characters, in a real
 * browser against the Demo seed (scripts/e2e-journeys.sh). Every record a spec creates carries a run id so reruns
 * against a kept database never collide.
 */
export const runId = () => `${Date.now().toString(36)}${Math.floor(Math.random() * 1296).toString(36)}`;

/** Demo staff session for arranging data through the API. */
export async function staff(who: 'admin' | 'am' | 'finance'): Promise<ApiSession> {
  const user = accounts[who];
  return ApiSession.login(user.email, user.password);
}

/** Signs the demo admin in (every staff permission) and opens `path`. */
export async function adminAt(page: Page, path: string) {
  await signIn(page, accounts.admin, landing.admin);
  await page.goto(path);
}

/** "Mar 14, 2031" — how the app renders a calendar date in en-US, whatever the zone. */
export function calendarLabel(isoDate: string): string {
  const [y, m, d] = isoDate.split('-').map(Number);
  return new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeZone: 'UTC' }).format(
    new Date(Date.UTC(y!, m! - 1, d!)),
  );
}

/** yyyy-MM-dd `days` from today (UTC). */
export function isoInDays(days: number): string {
  return new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);
}
