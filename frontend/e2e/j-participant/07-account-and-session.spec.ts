import { type Page, type Request, expect, test } from '@playwright/test';
import { ApiSession } from '../journeys/support/api';
import { signIn } from '../journeys/support/ui';
import { as } from './support/staff';
import { state, writeState } from './support/state';

/**
 * Participant lifecycle, part 7 — the session: access tokens the API rejects mid-flow are refreshed silently (one
 * refresh for a burst of requests), a session that cannot be refreshed any more sends the participant to sign in and
 * back to where they were (nothing half-sent is saved), the password change (wrong current password, mismatch, same
 * password; success signs out everywhere and invalidates other sessions), and signing out and in again.
 */
test.describe.serial('account and session', () => {
  let page: Page;
  const s = () => state();
  const NEW_PASSWORD = 'Lifecycle-Changed#2026?';

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
    await signIn(page, s().pat, /\/app$/);
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  /** Makes the API reject the access token of every /api/v1/me request until `until()` says stop. */
  async function rejectAccessTokens(until: () => boolean) {
    await page.route('**/api/v1/me/**', async (route) => {
      if (until()) return route.fallback();
      return route.fallback({ headers: { ...route.request().headers(), authorization: 'Bearer expired.token.value' } });
    });
  }

  test('an access token the API rejects is refreshed silently — once for a burst of requests', async () => {
    await page.goto('/app');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    const refreshes: Request[] = [];
    const failures: string[] = [];
    page.on('request', (r) => {
      if (r.url().endsWith('/api/v1/auth/refresh')) refreshes.push(r);
    });
    page.on('response', (r) => {
      if (r.url().includes('/api/v1/') && r.status() >= 400 && r.status() !== 401) failures.push(`${r.status()} ${r.url()}`);
    });
    await rejectAccessTokens(() => refreshes.length > 0);

    // Client-side navigation keeps the in-memory token: the earnings page fires several requests at once, all rejected.
    await page.getByRole('navigation').getByRole('link', { name: 'Earnings' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'Earnings' })).toBeVisible();
    await expect(page.getByRole('group', { name: /^Paid\b/ })).toContainText('$16.00');
    await expect(page).toHaveURL(/\/app\/earnings$/);
    expect(refreshes).toHaveLength(1);
    expect((await refreshes[0]!.response())!.status()).toBe(200);
    expect(failures).toEqual([]);
    await page.unroute('**/api/v1/me/**');
  });

  test('a session that can no longer be refreshed returns to sign-in and back; nothing half-sent is saved', async () => {
    const api = await as(s().pat);
    const ticketsBefore = (await api.get<{ total: number }>('/me/support/tickets')).total;

    await page.goto('/app/support/new');
    await page.getByLabel('Category').selectOption('Account');
    await page.getByLabel('Subject').fill('Written while my session ended');
    await page.getByLabel('How can we help?').fill('This ticket must not be created twice or half.');
    // The refresh cookie is gone (signed out elsewhere / expired) and the access token is rejected.
    await page.context().clearCookies();
    await rejectAccessTokens(() => false);
    await page.getByRole('button', { name: 'Send ticket' }).click();

    await expect(page).toHaveURL(/\/login\?expired=1&next=%2Fapp%2Fsupport%2Fnew/);
    await expect(page.getByRole('status').filter({ hasText: 'Your session has expired' })).toBeVisible();
    await page.unroute('**/api/v1/me/**');
    await page.getByLabel('Email', { exact: true }).fill(s().pat.email);
    await page.getByLabel('Password', { exact: true }).fill(s().pat.password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page).toHaveURL(/\/app\/support\/new$/);
    await expect(page.getByRole('heading', { level: 1, name: 'New support ticket' })).toBeVisible();
    expect((await api.get<{ total: number }>('/me/support/tickets')).total).toBe(ticketsBefore);
  });

  test('change password: wrong current password, mismatch and reuse are refused', async () => {
    await page.goto('/app/profile/security');
    const current = page.getByLabel('Current password', { exact: true });
    const next = page.getByLabel('New password', { exact: true });
    const confirm = page.getByLabel('Confirm new password', { exact: true });

    await current.fill(s().pat.password);
    await next.fill(s().pat.password);
    await confirm.fill(s().pat.password);
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(next).toHaveAccessibleDescription(/different from your current one/);

    await next.fill(NEW_PASSWORD);
    await confirm.fill(`${NEW_PASSWORD}x`);
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(confirm).toHaveAccessibleDescription(/don’t match/);
    await expect(confirm).toBeFocused();

    await current.fill('Not-The-Password#1');
    await confirm.fill(NEW_PASSWORD);
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(current).toHaveAttribute('aria-invalid', 'true');
    // Still signed in with the old password.
    await ApiSession.login(s().pat.email, s().pat.password);
  });

  test('changes the password: signed out everywhere, old sessions and the old password stop working', async () => {
    const otherSession = await ApiSession.login(s().pat.email, s().pat.password);
    await otherSession.get('/me/profile');

    await page.getByLabel('Current password', { exact: true }).fill(s().pat.password);
    await page.getByLabel('New password', { exact: true }).fill(NEW_PASSWORD);
    await page.getByLabel('Confirm new password', { exact: true }).fill(NEW_PASSWORD);
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(page).toHaveURL(/\/login/);

    const old = s().pat.password;
    const updated = state();
    updated.pat.password = NEW_PASSWORD;
    writeState(updated);

    await expect(otherSession.get('/me/profile')).rejects.toMatchObject({ status: 401 });
    await expect(ApiSession.login(s().pat.email, old)).rejects.toMatchObject({ status: 401 });

    await page.goto('/app/earnings');
    await expect(page).toHaveURL(/\/login/);
  });

  test('signs in with the new password, signs out and in again', async () => {
    await signIn(page, s().pat, /\/app$/);
    await page.getByRole('button', { name: `Account menu for ${s().pat.displayName}` }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    await expect(page).toHaveURL(/\/login\?signedOut=1$/);
    await expect(page.getByText('You’ve been signed out.')).toBeVisible();

    // The refresh cookie was revoked: going back into the portal needs a new sign-in.
    await page.goto('/app/submissions');
    await expect(page).toHaveURL(/\/login/);
    await page.getByLabel('Email', { exact: true }).fill(s().pat.email);
    await page.getByLabel('Password', { exact: true }).fill(s().pat.password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page).toHaveURL(/\/app\/submissions$/);
    await expect(page.getByRole('heading', { level: 1, name: 'My submissions' })).toBeVisible();
  });
});
