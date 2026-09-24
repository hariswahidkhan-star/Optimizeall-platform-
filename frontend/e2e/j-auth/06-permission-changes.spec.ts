import { expect, test } from '@playwright/test';
import {
  ApiSession,
  FORBIDDEN,
  accounts,
  apiLogin,
  landing,
  raw,
  refreshWith,
  runId,
  signedIn,
  statusOf,
} from './support/auth';

/**
 * Permission changes take effect without signing in again: a custom role granted to a signed-in user opens the page
 * and the API on the next request (PermissionVersion), editing the role's permissions or removing it closes them just
 * as fast, and a built-in role change ends the user's sessions (SecurityVersion). Only admins manage roles.
 */
interface TestUser {
  id: string;
  email: string;
  password: string;
  displayName: string;
}

test.describe.serial('permission changes', () => {
  let admin: ApiSession;
  let member: TestUser;
  let roleId: string;
  let stamp: string;

  test.beforeAll(async () => {
    admin = await ApiSession.login(accounts.admin.email, accounts.admin.password);
    member = await admin.post<TestUser>('/admin/test-users', {
      roles: ['Participant'],
      displayName: `Auth Member ${runId()}`,
    });
  });

  test('a custom role opens its page and API on the next request, then closes them again', async ({
    browser,
  }) => {
    const { context, page } = await signedIn(browser, member, landing.participant);
    const api = await ApiSession.login(member.email, member.password);

    await page.goto('/admin/audit');
    await expect(page.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
    expect(await statusOf(api.get('/admin/audit-logs'))).toBe(403);

    // Admin grants "audit.view" through a custom role.
    const created = await admin.post<{ role: { id: string; concurrencyStamp: string } }>('/admin/roles', {
      name: `Auditor ${runId()}`,
      description: 'Reads the audit log.',
      permissions: ['audit.view'],
    });
    roleId = created.role.id;
    stamp = created.role.concurrencyStamp;
    await admin.put(`/admin/roles/${roleId}/users/${member.id}`);

    // The same session (no new sign-in) may read the audit log now…
    expect(await statusOf(api.get('/admin/audit-logs'))).toBe(200);
    await page.reload();
    await expect(page.getByRole('heading', { level: 1, name: 'Audit log' })).toBeVisible();

    // …until the role changes: the permission is gone on the very next request.
    const updated = await admin.put<{ role: { concurrencyStamp: string } }>(`/admin/roles/${roleId}`, {
      name: `Auditor ${runId()}`,
      description: 'Now analytics only.',
      permissions: ['analytics.view'],
      concurrencyStamp: stamp,
    });
    stamp = updated.role.concurrencyStamp;
    expect(await statusOf(api.get('/admin/audit-logs'))).toBe(403);
    await page.reload();
    await expect(page.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();

    // A stale edit (old stamp) is refused rather than silently overwriting.
    const stale = await raw('PUT', `/admin/roles/${roleId}`, {
      token: admin.token,
      body: {
        name: `Auditor ${runId()}`,
        permissions: ['audit.view'],
        concurrencyStamp: created.role.concurrencyStamp,
      },
    });
    expect(stale.status).toBe(409);

    // Removing the role closes the remaining permission too.
    expect((await api.get<{ permissions: string[] }>('/auth/me')).permissions).toContain('analytics.view');
    const removed = await raw('DELETE', `/admin/roles/${roleId}/users/${member.id}`, { token: admin.token });
    expect(removed.status).toBe(200);
    const me = await api.get<{ permissions: string[]; customRoles: string[] }>('/auth/me');
    expect(me.permissions).not.toContain('analytics.view');
    expect(me.customRoles).toEqual([]);
    await context.close();
  });

  test('only admins manage roles; admin-only permissions cannot be handed out by anyone else', async () => {
    for (const who of [accounts.manager, accounts.finance, accounts.am, accounts.reviewer]) {
      const session = await ApiSession.login(who.email, who.password);
      const res = await raw('POST', '/admin/roles', {
        token: session.token,
        body: { name: `Escalate ${runId()}`, permissions: ['settings.manage'] },
      });
      expect(res.status, who.email).toBe(403);
      expect(
        (await raw('PUT', `/admin/roles/${roleId}/users/${member.id}`, { token: session.token })).status,
      ).toBe(403);
    }
    // The member cannot grant themselves anything either.
    const self = await ApiSession.login(member.email, member.password);
    expect(
      (await raw('PUT', `/admin/roles/${roleId}/users/${member.id}`, { token: self.token })).status,
    ).toBe(403);
    expect(
      (
        await raw('PUT', `/admin/users/${member.id}/roles`, {
          token: self.token,
          body: { roles: ['Admin'], reason: 'I want it', confirm: true },
        })
      ).status,
    ).toBe(403);
  });

  test('a built-in role change ends the user’s sessions; signing in again lands in the new portal', async ({
    browser,
  }) => {
    const { context, page } = await signedIn(browser, member, landing.participant);
    const session = await apiLogin(member);

    await admin.put(`/admin/users/${member.id}/roles`, {
      roles: ['Reviewer'],
      reason: 'E2E: moved to the review team',
      confirm: true,
    });

    // Old tokens and refresh cookies are dead at once.
    expect((await raw('GET', '/auth/me', { token: session.token })).status).toBe(401);
    expect((await refreshWith(session.cookie)).status).toBe(401);
    await page.goto('/app/earnings');
    await expect(page).toHaveURL(/\/login/);

    await page.getByLabel('Email', { exact: true }).fill(member.email);
    await page.getByLabel('Password', { exact: true }).fill(member.password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    // The `next` (/app/earnings) is no longer allowed for a reviewer: they land in their own portal instead.
    await expect(page).toHaveURL(landing.reviewer);
    await page.goto('/app/earnings');
    await expect(page.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
    await context.close();
  });
});
