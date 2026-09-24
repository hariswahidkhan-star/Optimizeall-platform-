import { type Page, expect, test } from '@playwright/test';
import {
  ApiSession,
  accounts,
  impersonationBanner,
  landing,
  modal,
  raw,
  runId,
  signedIn,
  signOut,
} from './support/auth';

/**
 * "Log in as" (impersonation) from the security side: who may start it and whom it may target, the banner across
 * reloads, sensitive writes refused with the impersonation error while reads work, the token dying with the session
 * (Exit, sign-out), and the non-production test sign-in refusing real accounts.
 */
interface TestUser {
  id: string;
  email: string;
  password: string;
  displayName: string;
}

const DEMO_ADMIN_NAME = 'Nadia Rahman';
const FORBIDDEN_WHILE_IMPERSONATING = 'auth.impersonation_forbidden_action';

test.describe.serial('impersonation', () => {
  let admin: ApiSession;
  let target: TestUser;
  let page: Page;
  let token = '';

  test.beforeAll(async ({ browser }) => {
    admin = await ApiSession.login(accounts.admin.email, accounts.admin.password);
    target = await admin.post<TestUser>('/admin/test-users', {
      roles: ['Participant'],
      displayName: `Auth Viewed ${runId()}`,
    });
    page = (await signedIn(browser, accounts.admin, landing.admin)).page;
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  test('who may start it: not without users.impersonate, never on an admin or oneself, always with a reason', async () => {
    for (const who of [
      accounts.manager,
      accounts.finance,
      accounts.reviewer,
      accounts.am,
      accounts.participant,
    ]) {
      const s = await ApiSession.login(who.email, who.password);
      const res = await raw('POST', `/admin/users/${target.id}/impersonate`, {
        token: s.token,
        body: { reason: 'Trying to look around', confirm: true },
      });
      expect(res.status, who.email).toBe(403);
    }
    const adminMe = await admin.get<{ id: string }>('/auth/me');
    const self = await raw('POST', `/admin/users/${adminMe.id}/impersonate`, {
      token: admin.token,
      body: { reason: 'Myself, why not', confirm: true },
    });
    expect(self.status).toBe(403);
    expect(self.json).toMatchObject({ code: 'admin.impersonation_self' });

    const noReason = await raw('POST', `/admin/users/${target.id}/impersonate`, {
      token: admin.token,
      body: { reason: '', confirm: true },
    });
    expect(noReason.status).toBe(400);
    const unconfirmed = await raw('POST', `/admin/users/${target.id}/impersonate`, {
      token: admin.token,
      body: { reason: 'Support ticket 42', confirm: false },
    });
    expect(unconfirmed.status).toBe(400);

    // Another admin (the bootstrap admin) is a protected target.
    const bootstrap = await ApiSession.login(
      process.env.E2E_ADMIN_EMAIL ?? 'e2e-admin@optimizeall.test',
      process.env.E2E_ADMIN_PASSWORD ?? 'E2e-Admin#Journey-2026',
    );
    const otherAdmin = await raw('POST', `/admin/users/${bootstrap.user.id}/impersonate`, {
      token: admin.token,
      body: { reason: 'Looking at another admin', confirm: true },
    });
    expect(otherAdmin.status).toBe(403);
    expect(otherAdmin.json).toMatchObject({ code: 'admin.impersonation_target_forbidden' });
  });

  test('the banner stays across reloads; reads work, sensitive writes are refused', async () => {
    await page.goto('/admin/users');
    await page.getByRole('searchbox', { name: 'Search users' }).fill(target.email);
    await page.getByRole('button', { name: `Log in as ${target.displayName}` }).click();
    const confirm = modal(page, `Log in as ${target.displayName}?`);
    await confirm.getByLabel(/Why do you need to view this account/).fill(`E2E auth suite ${runId()}`);
    await confirm.getByLabel(`Type ${target.email} to confirm`).fill(target.email);
    const started = page.waitForResponse(
      (r) => r.url().endsWith('/impersonate') && r.request().method() === 'POST',
    );
    await confirm.getByRole('button', { name: 'Log in as user' }).click();
    token = ((await (await started).json()) as { accessToken: string }).accessToken;
    await expect(page).toHaveURL(landing.participant);
    const banner = impersonationBanner(page);
    await expect(banner).toContainText(`You are viewing as ${target.displayName}`);
    await expect(banner).toContainText(`signed in as ${DEMO_ADMIN_NAME}`);
    await page.reload();
    await expect(banner).toBeVisible();

    // Reads as the user work…
    expect((await raw('GET', '/me/profile', { token })).status).toBe(200);
    const me = await raw('GET', '/auth/me', { token });
    expect(me.json).toMatchObject({ id: target.id, impersonatedBy: { displayName: DEMO_ADMIN_NAME } });
    // …writes to credentials, identity/contact details and further impersonation are refused.
    const refused: [string, string, unknown][] = [
      [
        'POST',
        '/auth/change-password',
        { currentPassword: target.password, newPassword: 'Hijacked#Pass-2026' },
      ],
      ['PUT', '/me/profile', { displayName: 'Changed by staff' }],
    ];
    for (const [method, path, body] of refused) {
      const res = await raw(method, path, { token, body });
      expect(res.status, `${method} ${path}`).toBe(403);
      expect(res.json, `${method} ${path}`).toMatchObject({ code: FORBIDDEN_WHILE_IMPERSONATING });
    }
    // Starting another impersonation from inside one is refused as well (the participant lacks the permission anyway).
    expect(
      (
        await raw('POST', `/admin/users/${target.id}/impersonate`, {
          token,
          body: { reason: 'Nested session', confirm: true },
        })
      ).status,
    ).toBe(403);
    // The target's password still works: nothing changed.
    await ApiSession.login(target.email, target.password);

    // The UI shows the same refusal on the security page.
    await page.goto('/app/profile/security');
    const form = page.getByRole('form', { name: 'Change password' });
    await form.getByLabel('Current password').fill(target.password);
    await form.getByLabel('New password', { exact: true }).fill('Hijacked#Pass-2026');
    await form.getByLabel('Confirm new password').fill('Hijacked#Pass-2026');
    await form.getByRole('button', { name: 'Change password' }).click();
    await expect(form.getByRole('alert')).toContainText(
      'not available while you are viewing as another user',
    );
  });

  test('Exit returns to the admin; the impersonation token dies with the session', async () => {
    await impersonationBanner(page).getByRole('button', { name: 'Exit' }).click();
    await expect(page).toHaveURL(/\/admin\/users$/);
    await expect(impersonationBanner(page)).toHaveCount(0);
    expect((await raw('GET', '/me/profile', { token })).status).toBe(401);
    // A reload stays the admin (the ended session is not resumed).
    await page.reload();
    await expect(page).toHaveURL(/\/admin\/users$/);
    await expect(page.getByRole('button', { name: `Account menu for ${DEMO_ADMIN_NAME}` })).toBeVisible();
  });

  test('signing out while viewing as someone ends the impersonation and the admin session', async () => {
    await page.goto('/admin/users');
    await page.getByRole('searchbox', { name: 'Search users' }).fill(target.email);
    await page.getByRole('button', { name: `Log in as ${target.displayName}` }).click();
    const confirm = modal(page, `Log in as ${target.displayName}?`);
    await confirm.getByLabel(/Why do you need to view this account/).fill(`E2E auth sign-out ${runId()}`);
    await confirm.getByLabel(`Type ${target.email} to confirm`).fill(target.email);
    const started = page.waitForResponse(
      (r) => r.url().endsWith('/impersonate') && r.request().method() === 'POST',
    );
    await confirm.getByRole('button', { name: 'Log in as user' }).click();
    token = ((await (await started).json()) as { accessToken: string }).accessToken;
    await expect(impersonationBanner(page)).toBeVisible();

    await signOut(page, target.displayName);
    expect((await raw('GET', '/me/profile', { token })).status).toBe(401);
    await page.goto('/admin/users');
    await expect(page).toHaveURL(/\/login/);
  });

  test('the test sign-in only works for test and demo accounts', async () => {
    const list = (await raw('GET', '/dev/test-accounts')).json as unknown as { id: string; email: string }[];
    expect(list.some((a) => a.id === target.id)).toBe(true);
    // A real (non-test, non-demo) account is never offered and cannot be signed into without its password.
    const bootstrap = await ApiSession.login(
      process.env.E2E_ADMIN_EMAIL ?? 'e2e-admin@optimizeall.test',
      process.env.E2E_ADMIN_PASSWORD ?? 'E2e-Admin#Journey-2026',
    );
    expect(list.some((a) => a.id === bootstrap.user.id)).toBe(false);
    const refused = await raw('POST', '/dev/test-login', {
      body: { userId: bootstrap.user.id },
      headers: { 'X-Requested-With': 'fetch' },
    });
    expect(refused.status).toBe(404);
    expect(refused.setCookies).toEqual([]);
  });
});
