import { type BrowserContext, type Page, expect, test } from '@playwright/test';
import type { Credentials } from '../journeys/support/fixtures';
import {
  accounts,
  apiLogin,
  emailFor,
  failSignIn,
  landing,
  linkIn,
  mailsTo,
  raw,
  refreshWith,
  registerVerified,
  signIn,
  subjectsTo,
  watchErrors,
} from './support/auth';

/**
 * Sign-in, lockout and password reset: unknown emails and wrong passwords look the same, five failures lock the
 * account (even the right password is refused, without revealing the lock), suspended accounts are told so, the
 * forgot-password flow reveals nothing, reset links are single-use, a reset unlocks the account and revokes every
 * session, and an older reset link dies with the reset.
 */
test.describe.serial('sign-in, lockout and password reset', () => {
  let page: Page;
  let lena: Credentials;
  let olga: Credentials;
  /** Sessions Lena opened before the lockout (an API session and a second browser). */
  let lenaApi: { token: string; cookie: string };
  let lenaOther: BrowserContext;
  const NEW_PASSWORD = 'Unlocked#Again-2026';
  const field = (label: string) => page.getByLabel(label, { exact: true });

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
    lena = await registerVerified('lena', 'Lena Lockout');
    olga = await registerVerified('olga', 'Olga Older-Link');
    // Olga asks for a reset link now; the last test asks again (after the 2-minute resend throttle) and uses the
    // newer link, after which this one must be dead.
    expect((await raw('POST', '/auth/forgot-password', { body: { email: olga.email } })).status).toBe(202);
    await expect.poll(() => subjectsTo(olga.email).length).toBe(2);
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  test('an unknown email and a wrong password get the same answer', async () => {
    const unknown = await raw('POST', '/auth/login', {
      body: { email: emailFor('ghost'), password: 'Whatever#2026x' },
    });
    const wrong = await raw('POST', '/auth/login', {
      body: { email: lena.email, password: 'Whatever#2026x' },
    });
    expect(unknown.status).toBe(401);
    expect(wrong.status).toBe(401);
    expect({ ...unknown.json, traceId: undefined }).toEqual({ ...wrong.json, traceId: undefined });

    const a = await failSignIn(page, emailFor('ghost'), 'Whatever#2026x');
    const unknownText = await a.textContent();
    await expect(field('Password')).toHaveValue('');
    const b = await failSignIn(page, lena.email, 'Whatever#2026x');
    expect(await b.textContent()).toBe(unknownText);
    expect(unknownText).toContain('Sign-in failed');
  });

  test('the email is matched without regard to case or surrounding spaces', async () => {
    await page.goto('/login');
    await field('Email').fill(`  ${lena.email.toUpperCase()} `);
    await field('Password').fill(lena.password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page).toHaveURL(landing.participant);
    // A signed-in visitor to /login goes straight back to the portal.
    await page.goto('/login');
    await expect(page).toHaveURL(landing.participant);
    await page.context().clearCookies();
  });

  test('five wrong passwords lock the account: the right one is then refused the same way', async ({
    browser,
  }) => {
    // Sessions opened before the lockout (they must survive it, and die with the reset later).
    const api = await apiLogin(lena);
    lenaApi = api;
    lenaOther = await browser.newContext();
    const otherPage = await lenaOther.newPage();
    await signIn(otherPage, lena, landing.participant);

    await page.goto('/login');
    for (let i = 1; i <= 5; i++) {
      const alert = await failSignIn(page, lena.email, `Wrong#Guess-${i}x`);
      await expect(alert).toContainText('The email or password is incorrect');
    }
    const locked = await failSignIn(page, lena.email, lena.password);
    await expect(locked).toContainText('Sign-in failed');
    await expect(locked).toContainText('sign-in is paused for 15 minutes');
    const lockedApi = await raw('POST', '/auth/login', {
      body: { email: lena.email, password: lena.password },
    });
    expect(lockedApi.status).toBe(401);
    expect(lockedApi.json).toMatchObject({ code: 'auth.invalid_credentials' });

    // The lock is per account: others sign in normally.
    await apiLogin(olga);
    // Existing sessions are not ended by a lockout (an attacker guessing must not sign the owner out).
    expect((await raw('GET', '/auth/me', { token: api.token })).status).toBe(200);
    await otherPage.goto('/app/profile');
    await expect(otherPage.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(otherPage).toHaveURL(/\/app\/profile$/);
  });

  test('suspended accounts are told so, but only with the right password', async () => {
    const wrong = await failSignIn(page, accounts.suspended.email, 'Not-The-Password#1');
    await expect(wrong).toContainText('Sign-in failed');
    const right = await failSignIn(page, accounts.suspended.email, accounts.suspended.password);
    await expect(right).toContainText('This account can’t sign in');
    await expect(right).toContainText('suspended');
    await expect(page).toHaveURL(/\/login$/);
  });

  test('forgot password reveals nothing about which emails exist', async () => {
    const errors = watchErrors(page);
    for (const email of [emailFor('ghost'), lena.email]) {
      await page.goto('/forgot-password');
      await field('Email').fill(email);
      await page.getByRole('button', { name: 'Send reset link' }).click();
      await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
      await expect(page.getByText('If an account exists for that email')).toBeVisible();
    }
    expect(mailsTo(emailFor('ghost'))).toEqual([]);
    await expect.poll(() => subjectsTo(lena.email).filter((s) => /reset/i.test(s))).toHaveLength(1);
    errors.expectClean('forgot password');
  });

  test('the reset link: policy and mismatch first, then the change unlocks and signs out everywhere', async () => {
    const api = lenaApi;
    const other = lenaOther;
    const link = linkIn(lena.email, /reset/i, '/reset-password');
    await page.goto(`${link.pathname}${link.search}`);
    await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();

    await field('New password').fill('password1234');
    await field('Confirm new password').fill('password1234');
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(field('New password')).toHaveAccessibleDescription(/too common/);

    await field('New password').fill(NEW_PASSWORD);
    await field('Confirm new password').fill(`${NEW_PASSWORD}!`);
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(field('Confirm new password')).toHaveAccessibleDescription(/don’t match/);

    await field('Confirm new password').fill(NEW_PASSWORD);
    // A double click sends the single-use link once: no "expired link" error after a successful change.
    const resets: string[] = [];
    page.on('request', (r) => {
      if (r.url().endsWith('/api/v1/auth/reset-password')) resets.push(r.method());
    });
    await page.getByRole('button', { name: 'Change password' }).dblclick();
    await expect(page).toHaveURL(/\/login\?reset=1$/);
    await expect(page.getByRole('status').filter({ hasText: 'Password changed' })).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0);
    expect(resets).toEqual(['POST']);

    // Every session opened before is gone: the access token, the refresh cookie and the other browser.
    expect((await raw('GET', '/auth/me', { token: api.token })).status).toBe(401);
    expect((await refreshWith(api.cookie)).status).toBe(401);
    const otherPage = other.pages()[0]!;
    await otherPage.goto('/app/earnings');
    await expect(otherPage).toHaveURL(/\/login/);
    await other.close();

    // The reset lifted the lockout; the old password is dead.
    expect(
      (await raw('POST', '/auth/login', { body: { email: lena.email, password: lena.password } })).status,
    ).toBe(401);
    lena = { ...lena, password: NEW_PASSWORD };
    await signIn(page, lena, landing.participant);
    await page.context().clearCookies();
  });

  test('a used reset link cannot be used again', async () => {
    const link = linkIn(lena.email, /reset/i, '/reset-password');
    await page.goto(`${link.pathname}${link.search}`);
    await field('New password').fill('Second#Reset-2026x');
    await field('Confirm new password').fill('Second#Reset-2026x');
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(page.getByRole('heading', { name: 'This reset link has expired' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Request a new link' })).toBeVisible();
    await apiLogin(lena);

    await page.goto('/reset-password');
    await expect(page.getByRole('heading', { name: 'This link is incomplete' })).toBeVisible();
  });

  test('resetting with the newest link kills an older one still in the mailbox', async () => {
    test.setTimeout(240_000);
    const older = linkIn(olga.email, /reset/i, '/reset-password');
    // Reset links are throttled to one per two minutes per account: ask until the newer one arrives.
    await expect
      .poll(
        async () => {
          await raw('POST', '/auth/forgot-password', { body: { email: olga.email } });
          return mailsTo(olga.email).filter((m) => /reset/i.test(m.subject)).length;
        },
        { timeout: 180_000, intervals: [10_000] },
      )
      .toBe(2);
    const newer = linkIn(olga.email, /reset/i, '/reset-password');
    expect(newer.searchParams.get('token')).not.toBe(older.searchParams.get('token'));

    await page.goto(`${newer.pathname}${newer.search}`);
    await field('New password').fill('Newest#Link-2026x');
    await field('Confirm new password').fill('Newest#Link-2026x');
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(page).toHaveURL(/\/login\?reset=1$/);

    await page.goto(`${older.pathname}${older.search}`);
    await field('New password').fill('Older#Link-2026xy');
    await field('Confirm new password').fill('Older#Link-2026xy');
    await page.getByRole('button', { name: 'Change password' }).click();
    await expect(page.getByRole('heading', { name: 'This reset link has expired' })).toBeVisible();
    await apiLogin({ ...olga, password: 'Newest#Link-2026x' });
  });
});
