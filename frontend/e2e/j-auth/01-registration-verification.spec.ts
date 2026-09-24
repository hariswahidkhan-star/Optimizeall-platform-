import { type Page, expect, test } from '@playwright/test';
import {
  API_URL,
  PASSWORD,
  apiLogin,
  emailFor,
  linkIn,
  mailsTo,
  publicApi,
  raw,
  signIn,
  subjectsTo,
  watchErrors,
} from './support/auth';

/**
 * Registration and email verification: the password policy on the client and (bypassing the client) on the server,
 * boundaries, duplicate emails that reveal nothing, the "check your email" page, an unverified account that may sign
 * in but is asked to verify, and verification links — valid, reused, tampered, missing.
 */
test.describe.serial('registration and email verification', () => {
  let page: Page;
  const field = (label: string) => page.getByLabel(label, { exact: true });
  const user = () => ({ email: emailFor('reg'), password: PASSWORD, displayName: 'Rae Registrant' });

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  test('the password policy is enforced on the form, before anything is sent', async () => {
    const registrations: string[] = [];
    page.on('request', (r) => {
      if (r.url().endsWith('/api/v1/auth/register')) registrations.push(r.url());
    });
    await page.goto('/register');
    await field('Email').fill(user().email);
    await field('Display name').fill(user().displayName);
    await field('Country').selectOption('GB');
    await page.getByRole('checkbox', { name: /accept the participant rules/i }).check();

    const cases: [string, RegExp][] = [
      ['Short#1', /at least 10 characters/],
      ['password123', /too common/],
      ['aaaaaaaaaaaa', /less repetitive/],
      [`${user().email.split('@')[0]}#2026`, /Don’t include your email address/],
    ];
    for (const [password, message] of cases) {
      await field('Password').fill(password);
      await page.getByRole('button', { name: 'Create account' }).click();
      await expect(field('Password'), password).toHaveAccessibleDescription(message);
      await expect(field('Password')).toHaveAttribute('aria-invalid', 'true');
    }
    expect(registrations).toEqual([]);
    expect(mailsTo(user().email)).toEqual([]);
  });

  test('the API enforces the same rules when the form is bypassed', async () => {
    const base = {
      email: emailFor('reg-api'),
      displayName: 'Api Bypass',
      countryCode: 'GB',
      languageCode: 'en',
      timeZone: 'UTC',
      acceptTerms: true,
    };
    const weak = await raw('POST', '/auth/register', { body: { ...base, password: 'qwertyuiop' } });
    expect(weak.status).toBe(400);
    expect(weak.json).toMatchObject({ code: 'auth.weak_password' });
    expect(JSON.stringify(weak.json)).toContain('too common');

    const short = await raw('POST', '/auth/register', { body: { ...base, password: 'Ab#1' } });
    expect(short.status).toBe(400);

    const noTerms = await raw('POST', '/auth/register', {
      body: { ...base, password: PASSWORD, acceptTerms: false },
    });
    expect(noTerms.status).toBe(400);
    expect(noTerms.json).toMatchObject({ code: 'auth.terms_required' });

    const badZone = await raw('POST', '/auth/register', {
      body: { ...base, password: PASSWORD, timeZone: 'Mars/Olympus_Mons' },
    });
    expect(badZone.status).toBe(400);
    expect(badZone.json).toMatchObject({ code: 'auth.invalid_timezone' });

    const noName = await raw('POST', '/auth/register', {
      body: { ...base, password: PASSWORD, displayName: 'x' },
    });
    expect(noName.status).toBe(400);
    // None of them created an account.
    expect(mailsTo(base.email)).toEqual([]);
  });

  test('registers through the form and lands on "Check your email"', async () => {
    const errors = watchErrors(page);
    await page.goto('/register');
    await field('Email').fill(`  ${user().email.toUpperCase()}  `);
    await field('Password').fill(user().password);
    await field('Display name').fill(user().displayName);
    await field('Country').selectOption('GB');
    await field('Time zone').selectOption('UTC');
    await page.getByRole('checkbox', { name: /accept the participant rules/i }).check();
    await page.getByRole('button', { name: 'Create account' }).click();
    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await expect.poll(() => subjectsTo(user().email)).toEqual([expect.stringMatching(/verify/i)]);
    errors.expectClean('registration');
  });

  test('a duplicate registration answers exactly like a new one and only notifies the owner', async () => {
    const again = await raw('POST', '/auth/register', {
      body: {
        email: user().email,
        password: 'Different#Pass-2026',
        displayName: 'Someone Else',
        countryCode: 'US',
        languageCode: 'en',
        timeZone: 'UTC',
        acceptTerms: true,
      },
    });
    const fresh = await raw('POST', '/auth/register', {
      body: {
        email: emailFor('reg-fresh'),
        password: PASSWORD,
        displayName: 'Fresh Person',
        countryCode: 'US',
        languageCode: 'en',
        timeZone: 'UTC',
        acceptTerms: true,
      },
    });
    expect(again.status).toBe(202);
    expect(again.json).toEqual(fresh.json);
    await expect
      .poll(() => subjectsTo(user().email))
      .toEqual([expect.stringMatching(/verify/i), 'Someone tried to register with your email']);
    const otherPassword = await raw('POST', '/auth/login', {
      body: { email: user().email, password: 'Different#Pass-2026' },
    });
    expect(otherPassword.status).toBe(401);
  });

  test('an unverified account may sign in but is asked to verify first', async () => {
    await signIn(page, user(), /\/app$/);
    await expect(page.getByRole('heading', { name: /verify/i }).first()).toBeVisible();
    await expect(
      page.getByText(`We sent a verification link to ${user().email.toLowerCase()}`),
    ).toBeVisible();
    const session = await apiLogin(user());
    expect((session.res.json!.user as { emailVerified: boolean }).emailVerified).toBe(false);

    // "Resend" right after registering is throttled: the UI confirms, but no second email floods the inbox.
    const before = mailsTo(user().email).length;
    await page.getByRole('button', { name: 'Resend verification email' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Verification email sent.' })).toBeAttached();
    expect(mailsTo(user().email)).toHaveLength(before);

    // Until the email is verified, the account cannot take part: a submission is refused with the reason.
    const campaigns = (await raw('GET', '/campaigns?pageSize=1', { token: session.token })).json as {
      items: { id: string }[];
    };
    const handle = `unverified${Date.now()}`;
    const account = await raw('POST', '/me/social-accounts', {
      token: session.token,
      body: {
        platform: 'Instagram',
        handle,
        profileUrl: `https://www.instagram.com/${handle}`,
        accountCreatedAt: new Date(Date.now() - 400 * 86_400_000).toISOString(),
        followerCount: 900,
      },
    });
    expect(account.status).toBe(201);
    const form = new FormData();
    form.append('campaignId', campaigns.items[0]!.id);
    form.append('socialAccountId', account.json!.id as string);
    form.append('platform', 'Instagram');
    form.append('postUrl', `https://www.instagram.com/p/Unverified${Date.now()}/`);
    form.append('postedAt', new Date(Date.now() - 60_000).toISOString());
    form.append('captionText', 'Not verified yet');
    const submitted = await fetch(`${API_URL}/api/v1/me/submissions`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${session.token}` },
      body: form,
    });
    expect(submitted.status).toBe(409);
    expect(await submitted.text()).toContain('account.email_unverified');
  });

  test('a tampered or missing verification token is refused and verifies nothing', async () => {
    const link = linkIn(user().email, /verify/i, '/verify-email');
    const token = link.searchParams.get('token')!;
    await page.goto(`/verify-email?token=${encodeURIComponent(`${token.slice(0, -2)}xx`)}`);
    await expect(page.getByRole('heading', { name: 'This link has expired' })).toBeVisible();
    await expect(page.getByRole('form', { name: 'Resend verification email' })).toBeVisible();

    await page.goto('/verify-email');
    await expect(page.getByRole('heading', { name: 'This link is incomplete' })).toBeVisible();

    const session = await apiLogin(user());
    expect((session.res.json!.user as { emailVerified: boolean }).emailVerified).toBe(false);
  });

  test('the mailbox link verifies the email once; the same link again reads "expired"', async () => {
    const link = linkIn(user().email, /verify/i, '/verify-email');
    await page.goto(`${link.pathname}${link.search}`);
    await expect(page.getByRole('heading', { name: 'Your email is verified' })).toBeVisible();
    await page.getByRole('link', { name: 'Continue to your dashboard' }).click();
    await expect(page).toHaveURL(/\/app$/);
    await expect(page.getByRole('button', { name: 'Resend verification email' })).toHaveCount(0);

    await page.goto(`${link.pathname}${link.search}`);
    await expect(page.getByRole('heading', { name: 'This link has expired' })).toBeVisible();
    await expect(
      publicApi.post('/auth/verify-email', { token: link.searchParams.get('token') }),
    ).rejects.toMatchObject({
      status: 400,
      code: 'auth.invalid_token',
    });
    const session = await apiLogin(user());
    expect((session.res.json!.user as { emailVerified: boolean }).emailVerified).toBe(true);
  });

  test('a verified account asking for a new link gets nothing (and learns nothing)', async () => {
    const before = mailsTo(user().email).length;
    const res = await raw('POST', '/auth/resend-verification', { body: { email: user().email } });
    const unknown = await raw('POST', '/auth/resend-verification', { body: { email: emailFor('nobody') } });
    expect(res.status).toBe(202);
    expect(res.json).toEqual(unknown.json);
    expect(mailsTo(user().email)).toHaveLength(before);
  });
});
