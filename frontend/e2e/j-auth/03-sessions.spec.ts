import { type BrowserContext, type Page, type Request, expect, test } from '@playwright/test';
import type { Credentials } from '../journeys/support/fixtures';
import {
  apiLogin,
  browserRefreshCookie,
  landing,
  raw,
  refreshCookie,
  refreshWith,
  registerVerified,
  signIn,
  signOut,
  watchErrors,
  webStorageDump,
} from './support/auth';

/**
 * Sessions: where the tokens live (refresh cookie HttpOnly + SameSite=Strict on the auth path only, access token in
 * memory, nothing in Web Storage), the CSRF header on refresh/logout, rotation on every refresh with reuse detection,
 * a reload that aborts the refresh response (the rotated cookie never reaches the browser), several tabs refreshing at
 * once, an expired session that returns to the original page after signing in again, and signing out in one tab.
 */
test.describe.serial('sessions and refresh tokens', () => {
  let sam: Credentials;
  let context: BrowserContext;
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    sam = await registerVerified('sam', 'Sam Session');
    context = await browser.newContext();
    page = await context.newPage();
  });
  test.afterAll(async () => {
    await context.close();
  });

  const refreshes = (p: Page) => {
    const seen: Request[] = [];
    p.on('request', (r) => {
      if (r.url().endsWith('/api/v1/auth/refresh')) seen.push(r);
    });
    return seen;
  };

  test('the refresh token is an HttpOnly, SameSite=Strict cookie scoped to /api/v1/auth; nothing in Web Storage', async () => {
    const login = page.waitForResponse((r) => r.url().endsWith('/api/v1/auth/login'));
    await signIn(page, sam, landing.participant);
    const body = (await (await login).json()) as { accessToken: string };
    const cookie = await browserRefreshCookie(context);
    expect(cookie).toMatchObject({ httpOnly: true, sameSite: 'Strict', path: '/api/v1/auth' });
    // Development runs on plain http (Security:SecureCookies=false); production defaults to Secure.
    expect(cookie!.secure).toBe(false);
    expect(cookie!.expires).toBeGreaterThan(Date.now() / 1000 + 13 * 86_400);

    // Only the auth endpoints ever receive the cookie; the SPA itself cannot read it or the access token.
    const storage = await webStorageDump(page);
    expect(storage).not.toContain(body.accessToken);
    expect(storage).not.toContain(cookie!.value);
    expect(storage).not.toMatch(/eyJ[\w-]+\.[\w-]+\.[\w-]+/); // no JWT anywhere
    expect(storage).not.toContain('oa_refresh');
    const indexedDbNames = await page.evaluate(async () => (await indexedDB.databases()).map((d) => d.name));
    expect(indexedDbNames).toEqual([]);

    const apiCall = page.waitForRequest((r) => r.url().includes('/api/v1/me/'));
    await page.getByRole('navigation').getByRole('link', { name: 'Earnings' }).first().click();
    const headers = await (await apiCall).allHeaders();
    expect(headers.cookie ?? '').not.toContain('oa_refresh');
    expect(headers.authorization).toMatch(/^Bearer /);
  });

  test('refresh and logout require the X-Requested-With header (CSRF)', async () => {
    const { cookie } = await apiLogin(sam);
    const noHeader = await raw('POST', '/auth/refresh', { headers: { Cookie: cookie } });
    expect(noHeader.status).toBe(403);
    expect(noHeader.json).toMatchObject({ code: 'auth.csrf' });
    const logout = await raw('POST', '/auth/logout', { headers: { Cookie: cookie } });
    expect(logout.status).toBe(403);
    // The session survived both.
    expect((await refreshWith(cookie)).status).toBe(200);

    // A cross-site form post (another site's page) carries neither the header nor, with SameSite=Strict, the cookie.
    const cookieBefore = (await browserRefreshCookie(context))!.value;
    // The app's own port under another host name (127.0.0.1 vs localhost) is another site.
    const app = new URL(test.info().project.use.baseURL!);
    const attackerOrigin = `http://127.0.0.1:${app.port}`;
    const logoutUrl = new URL('/api/v1/auth/logout', app).href;
    const attacker = await context.newPage();
    await attacker.goto(`${attackerOrigin}/`);
    const answer = attacker.waitForResponse((r) => r.url().includes('/api/v1/auth/logout'));
    await attacker.evaluate((action) => {
      const form = document.createElement('form');
      form.method = 'POST';
      form.action = action;
      document.body.appendChild(form);
      form.submit();
    }, logoutUrl);
    expect((await answer).status()).toBe(403);
    await attacker.waitForURL(/\/api\/v1\/auth\/logout$/, { waitUntil: 'load' });
    await expect(attacker.locator('body')).toContainText('auth.csrf');
    await attacker.close();
    expect((await browserRefreshCookie(context))!.value).toBe(cookieBefore);
    await page.reload();
    await expect(page).toHaveURL(/\/app\/earnings$/);
  });

  test('every refresh rotates the cookie; replaying an old one later revokes the whole session', async () => {
    test.setTimeout(150_000);
    const before = (await browserRefreshCookie(context))!.value;
    await page.reload();
    await expect(page.getByRole('heading', { level: 1, name: 'Earnings' })).toBeVisible();
    const after = (await browserRefreshCookie(context))!.value;
    expect(after).not.toBe(before);

    // Within 30 s of the rotation (and before the browser used its new cookie again) the old cookie would still get a
    // sibling — a lost response. After that it is reuse: likely theft.
    await page.waitForTimeout(35_000);
    const replay = await refreshWith(`oa_refresh=${before}`);
    expect(replay.status).toBe(401);
    expect(replay.json).toMatchObject({ code: 'auth.session_expired' });
    // The legitimate browser's session died with the family.
    expect((await refreshWith(`oa_refresh=${after}`)).status).toBe(401);
    await page.reload();
    await expect(page).toHaveURL(/\/login\?next=%2Fapp%2Fearnings/);
  });

  test('a reload that aborts the refresh response keeps the user signed in', async () => {
    await signIn(page, sam, landing.participant);
    await page.goto('/app/earnings');
    await expect(page.getByRole('heading', { level: 1, name: 'Earnings' })).toBeVisible();
    const cookieBefore = (await browserRefreshCookie(context))!.value;

    // The server rotates, but the response (and its Set-Cookie) never reaches the browser.
    let intercepted = false;
    let aborted = 0;
    await page.route('**/api/v1/auth/refresh', async (route) => {
      if (intercepted) return route.fallback();
      intercepted = true;
      // Forward the browser's request (its cookie) to the API, which rotates; then drop the response.
      const forwarded = await refreshWith(`oa_refresh=${cookieBefore}`);
      expect(forwarded.status).toBe(200);
      // (If the browser already gave up on the request, it never saw the response either.)
      await route.abort('connectionreset').catch(() => undefined);
      aborted++;
    });
    await page.reload();
    await expect.poll(() => aborted).toBe(1);
    expect((await browserRefreshCookie(context))!.value).toBe(cookieBefore);
    await page.unroute('**/api/v1/auth/refresh');

    // The next load presents the stale cookie again: still signed in, on the same page.
    const seen = refreshes(page);
    await page.reload();
    await expect(page.getByRole('heading', { level: 1, name: 'Earnings' })).toBeVisible();
    await expect(page).toHaveURL(/\/app\/earnings$/);
    expect((await seen[0]!.response())!.status()).toBe(200);
    expect((await browserRefreshCookie(context))!.value).not.toBe(cookieBefore);
    // And the session keeps going normally afterwards.
    await page.reload();
    await expect(page).toHaveURL(/\/app\/earnings$/);
  });

  test('several tabs reloading at once all stay signed in', async () => {
    const tabs = [page, await context.newPage(), await context.newPage()];
    const errors = tabs.map((t) => watchErrors(t));
    await Promise.all(tabs.map((t) => t.goto('/app/earnings')));
    await Promise.all(tabs.map((t) => t.reload()));
    for (const t of tabs) {
      await expect(t.getByRole('heading', { level: 1, name: 'Earnings' })).toBeVisible();
      await expect(t).toHaveURL(/\/app\/earnings$/);
    }
    errors.forEach((e, i) => e.expectClean(`tab ${i + 1}`));
    await tabs[1]!.close();
    await tabs[2]!.close();
  });

  test('an expired session goes to sign-in and comes back to the page, with its query string', async () => {
    await page.goto('/app/campaigns?search=zzz-no-match');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    // The access token is rejected and the refresh cookie is gone (expired / signed out elsewhere).
    await context.clearCookies();
    await page.route('**/api/v1/me/**', (route) =>
      route.fallback({
        headers: { ...route.request().headers(), authorization: 'Bearer expired.token.value' },
      }),
    );
    await page.getByRole('navigation').getByRole('link', { name: 'Earnings' }).first().click();
    await expect(page).toHaveURL(/\/login\?expired=1&next=%2Fapp%2Fearnings/);
    await expect(page.getByRole('status').filter({ hasText: 'Your session has expired' })).toBeVisible();
    await page.unroute('**/api/v1/me/**');

    // A deep link with a query string survives the round trip through sign-in.
    await page.goto('/app/campaigns?search=zzz-no-match');
    await expect(page).toHaveURL(/\/login\?next=%2Fapp%2Fcampaigns%3Fsearch%3Dzzz-no-match$/);
    await page.getByLabel('Email', { exact: true }).fill(sam.email);
    await page.getByLabel('Password', { exact: true }).fill(sam.password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page).toHaveURL(/\/app\/campaigns\?search=zzz-no-match$/);
  });

  test('signing out in one tab signs out the other tabs too', async () => {
    const other = await context.newPage();
    await other.goto('/app/earnings');
    await expect(other.getByRole('heading', { level: 1, name: 'Earnings' })).toBeVisible();

    await signOut(page, sam.displayName);
    await expect(page.getByText('You’ve been signed out.')).toBeVisible();
    expect(await browserRefreshCookie(context)).toBeUndefined();

    // The other tab leaves the portal without waiting for its access token to expire…
    await expect(other).toHaveURL(/\/login\?signedOut=1$/);
    // …and opening the portal again asks for a sign-in.
    await other.goto('/app/earnings');
    await expect(other).toHaveURL(/\/login\?next=%2Fapp%2Fearnings$/);
    await other.close();
  });

  test('the access token of a signed-out session is refused by the API', async () => {
    const session = await apiLogin(sam);
    expect((await raw('GET', '/auth/me', { token: session.token })).status).toBe(200);
    const logout = await raw('POST', '/auth/logout', {
      headers: { Cookie: session.cookie, 'X-Requested-With': 'fetch' },
    });
    expect(logout.status).toBe(204);
    expect(logout.setCookies.join(';')).toMatch(/oa_refresh=;/);
    expect((await refreshWith(session.cookie)).status).toBe(401);
    expect((await raw('GET', '/auth/me', { token: session.token })).status).toBe(401);
    expect((await raw('GET', '/me/profile', { token: session.token })).status).toBe(401);

    // Other sessions of the same user are untouched by that sign-out.
    const second = await apiLogin(sam);
    expect((await raw('GET', '/auth/me', { token: second.token })).status).toBe(200);
    expect(refreshCookie(await refreshWith(second.cookie))).toBeTruthy();
  });
});
