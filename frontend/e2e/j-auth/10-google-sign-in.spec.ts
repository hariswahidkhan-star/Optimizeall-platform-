import { expect, test } from '@playwright/test';
import { raw } from './support/auth';

/**
 * Sign in with Google while it is not configured (the e2e API runs without Google credentials and there is no fake
 * provider): the API reports it disabled and every Google endpoint answers 404, the sign-in page offers no Google
 * button, and the callback page handles every way Google can come back — cancelled, missing code/state, or a
 * code/state the API refuses — with an explanation and a way back, never a crash or a half-signed-in state.
 */
test('the API reports Google disabled and its endpoints answer 404', async () => {
  expect((await raw('GET', '/auth/providers')).json).toEqual({ google: { enabled: false } });
  const csrf = { 'X-Requested-With': 'fetch' };
  for (const [method, path, body] of [
    ['POST', '/auth/google/start', { returnTo: '/app' }],
    ['POST', '/auth/google/callback', { code: 'code', state: 'state' }],
    ['POST', '/auth/google/complete', { ticket: 'ticket', acceptTerms: true, countryCode: 'GB' }],
  ] as const) {
    const res = await raw(method, path, { body, headers: csrf });
    expect(res.status, path).toBe(404);
    expect(
      res.setCookies.filter((c) => c.startsWith('oa_')),
      path,
    ).toEqual([]);
  }
});

test('the sign-in page offers no Google button', async ({ page }) => {
  await page.goto('/login');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: /Google/ })).toHaveCount(0);
});

test('the callback page explains a cancelled or broken Google return', async ({ page }) => {
  const cases: [string, RegExp][] = [
    ['?error=access_denied&state=x', /You cancelled signing in with Google/],
    ['?state=only-state', /Google didn’t complete the sign-in/],
    ['?code=only-code', /Google didn’t complete the sign-in/],
    ['', /Google didn’t complete the sign-in/],
  ];
  for (const [query, message] of cases) {
    await page.goto(`/auth/google/callback${query}`);
    const alert = page.getByRole('alert');
    await expect(alert, query).toContainText('Google sign-in didn’t complete');
    await expect(alert, query).toContainText(message);
  }
  // A forged code/state reaches the API, which refuses it (Google is off here): an error, still signed out.
  const answered = page.waitForResponse((r) => r.url().endsWith('/api/v1/auth/google/callback'));
  await page.goto('/auth/google/callback?code=forged&state=forged');
  expect((await answered).status()).toBe(404);
  await expect(page.getByRole('alert')).toContainText('Google sign-in didn’t complete');
  expect((await page.context().cookies()).filter((c) => c.name === 'oa_refresh')).toEqual([]);
  await page.goto('/app');
  await expect(page).toHaveURL(/\/login\?next=%2Fapp$/);
});
