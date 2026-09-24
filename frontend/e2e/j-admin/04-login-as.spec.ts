import {
  DEMO_ADMIN_NAME,
  accounts,
  adminApi,
  arrangeRole,
  arrangeTestUser,
  expect,
  impersonationBanner,
  landing,
  modal,
  rawApi,
  runId,
  test,
  toast,
  trackBearer,
  watchErrors,
} from './support/jadmin';

/**
 * "Log in as":
 *   admin → Log in as a participant test user who also holds a custom role (finance reads + role management), with a
 *   reason and the email typed out → the banner is on every page type (participant portal, finance portal, admin portal
 *   and the public website) and survives a reload → blocked actions answer 403 auth.impersonation_forbidden_action in
 *   the UI (payout destination) and through the browser's own token (password, payout destination, payout batch
 *   preparation, custom-role changes, linking Google) → Exit returns the admin; exiting twice is harmless → the audit
 *   log reads "Admin as User" for the session and its requests.
 *   Negatives: admins can't be impersonated (no button, 403 in the API), suspended accounts can't be (409), a suspended
 *   impersonator's session stops on the next request.
 */
test.describe.configure({ mode: 'serial' });

const BLOCKED =
  'This action is not available while you are viewing as another user. Exit the impersonation session first.';

test('log in as a user: banner on every page type, blocked actions, exit, audit', async ({ as }) => {
  const id = runId();
  const target = await arrangeTestUser(`E2E Viewed ${id}`);
  const roleName = `E2E finance readers ${id}`;
  await arrangeRole(
    roleName,
    ['ledger.view', 'payouts.view', 'payouts.prepare', 'users.view', 'roles.manage'],
    [target.id],
  );
  const reason = `E2E ticket ${id}: participant reports a missing payout`;

  const admin = await as(accounts.admin, landing.admin);
  const bearer = trackBearer(admin);
  const errors = watchErrors(admin);
  await admin.goto(`/admin/users/${target.id}`);
  await expect(admin.getByRole('heading', { level: 1, name: target.displayName })).toBeVisible();
  await admin.getByRole('button', { name: 'Log in as' }).click();
  const confirm = modal(admin, `Log in as ${target.displayName}?`);
  const go = confirm.getByRole('button', { name: 'Log in as user' });
  // The reason needs 5+ characters.
  await confirm.getByLabel(`Type ${target.email} to confirm`).fill(target.email);
  await confirm.getByLabel(/Why do you need to view this account/).fill('abcd');
  await go.click();
  await expect(confirm.getByText('Enter a reason (at least 5 characters).')).toBeVisible();
  await confirm.getByLabel(/Why do you need to view this account/).fill(reason);
  await go.click();
  // A participant with finance permissions lands in the finance portal.
  await expect(admin).toHaveURL(/\/finance(\/|$)/);

  const banner = impersonationBanner(admin);
  await expect(banner).toContainText(`You are viewing as ${target.displayName}`);
  await expect(banner).toContainText('TEST');
  await expect(banner).toContainText(`signed in as ${DEMO_ADMIN_NAME}`);

  // Every page type: finance portal, admin portal, participant portal, the public website — also after a reload.
  for (const path of [
    '/finance/ledger',
    '/admin/roles',
    '/app',
    '/app/profile/payout-details',
    '/',
    '/services',
  ]) {
    await admin.goto(path);
    await expect(admin.getByRole('heading', { level: 1 }).first()).toBeVisible();
    await expect(banner, `banner on ${path}`).toBeVisible();
    await expect(banner.getByRole('button', { name: 'Exit' })).toBeVisible();
  }
  await admin.reload();
  await expect(banner).toBeVisible();

  // Blocked in the UI: saving a payout destination.
  errors.ignore(/HTTP 403 (PUT|POST|DELETE) /);
  await admin.goto('/app/profile/payout-details');
  const form = admin.getByRole('form', { name: 'Payout details' });
  await form.getByRole('radio', { name: /PayPal/ }).check();
  await form.getByLabel('Account holder name').fill('Viewed Person');
  await form.getByLabel('PayPal email address').fill(`viewed-${id}@example.com`);
  await form.getByRole('button', { name: /Save/ }).click();
  await expect(toast(admin, 'Payout details not saved')).toContainText(BLOCKED);

  // Blocked through the browser's own (impersonation) token.
  const token = bearer.current();
  const me = await rawApi('GET', '/auth/me', token);
  expect(me.status).toBe(200);
  expect((me.body as { email?: string }).email).toBe(target.email);
  const blocked = [
    [
      'POST',
      '/auth/change-password',
      { currentPassword: target.password, newPassword: `E2e-${id}-New#Passw0rd` },
    ],
    [
      'PUT',
      '/me/payout-profile',
      { method: 'PayPal', accountHolderName: 'X Y', destination: 'x@example.com', preferredCurrency: 'USD' },
    ],
    ['POST', '/finance/payout-batches/prepare', {}],
    ['POST', '/admin/roles', { name: `Sneaky ${id}`, permissions: ['ledger.view'] }],
    ['POST', '/auth/external-logins/google/start', { returnTo: '/app/profile/security' }],
  ] as const;
  for (const [method, path, body] of blocked) {
    const res = await rawApi(method, path, token, body);
    expect(`${res.status} ${res.body.code}`, `${method} ${path}`).toBe(
      '403 auth.impersonation_forbidden_action',
    );
  }
  // Reads keep working (they see what the user sees).
  expect((await rawApi('GET', '/admin/roles', token)).status).toBe(200);

  // Exit: back to Admin → Users as the admin; the impersonation token is dead.
  await banner.getByRole('button', { name: 'Exit' }).click();
  await expect(admin).toHaveURL(/\/admin\/users$/);
  await expect(banner).toHaveCount(0);
  await expect(admin.getByRole('button', { name: `Account menu for ${DEMO_ADMIN_NAME}` })).toBeVisible();
  expect((await rawApi('GET', '/auth/me', token)).status).toBe(401);
  // Exiting twice is harmless: the second call answers with the admin's own session.
  // (Sent from the page with its cookies and the admin's current access token, as the app does.)
  await admin.getByRole('heading', { level: 1, name: 'Users' }).waitFor();
  await admin.reload();
  await expect(admin.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible();
  const adminToken = bearer.current();
  expect(adminToken).not.toBe(token);
  const again = await admin.evaluate(async (bearerToken) => {
    const res = await fetch('/api/v1/auth/impersonation/exit', {
      method: 'POST',
      headers: { 'X-Requested-With': 'fetch', Authorization: `Bearer ${bearerToken}` },
      credentials: 'include',
    });
    return { status: res.status, email: ((await res.json()) as { user?: { email?: string } }).user?.email };
  }, adminToken);
  expect(again).toEqual({ status: 200, email: accounts.admin.email });
  await admin.reload();
  await expect(admin.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible();
  await expect(banner).toHaveCount(0);

  // Audit: the session start with the reason, and requests recorded as "admin as user".
  await admin.goto('/admin/audit');
  const filters = admin.getByRole('search', { name: 'Audit log filters' });
  await filters.getByLabel('Action').fill('admin.impersonation_started');
  await filters.getByRole('button', { name: 'Apply filters' }).click();
  const entries = admin.getByRole('region', { name: 'Audit entries' }).getByRole('listitem');
  await expect(entries.filter({ hasText: reason })).toHaveCount(1);
  await filters.getByLabel('Action').fill('impersonation.request');
  await filters.getByRole('button', { name: 'Apply filters' }).click();
  await expect(
    entries.filter({ hasText: `${DEMO_ADMIN_NAME} as ${target.displayName}` }).first(),
  ).toBeVisible();
  await filters.getByLabel('Action').fill('admin.impersonation_ended');
  await filters.getByRole('button', { name: 'Apply filters' }).click();
  await expect(entries.first()).toBeVisible();
  errors.expectClean('the login-as journey');
});

test('who can’t be impersonated, and a suspended impersonator’s session stops', async ({ as }) => {
  const id = runId();
  const api = await adminApi();
  const otherAdmin = await arrangeTestUser(`E2E second admin ${id}`, ['Admin']);
  const suspendedTarget = await arrangeTestUser(`E2E suspended target ${id}`);
  await api.post(`/admin/users/${suspendedTarget.id}/suspend`, {
    reason: 'E2E: suspended target',
    confirm: true,
  });

  // Admins: no "Log in as" in the list or on the page, and the API refuses.
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/users');
  await admin.getByRole('searchbox', { name: 'Search users' }).fill(otherAdmin.email);
  await expect(admin.getByRole('link', { name: otherAdmin.displayName })).toBeVisible();
  await expect(admin.getByRole('button', { name: `Log in as ${otherAdmin.displayName}` })).toHaveCount(0);
  await admin.getByRole('link', { name: otherAdmin.displayName }).click();
  await expect(admin.getByRole('heading', { level: 1, name: otherAdmin.displayName })).toBeVisible();
  await expect(admin.getByRole('button', { name: 'Log in as' })).toHaveCount(0);
  const start = (userId: string) =>
    rawApi('POST', `/admin/users/${userId}/impersonate`, api.token, {
      reason: 'E2E negative',
      confirm: true,
    });
  expect((await start(otherAdmin.id)).body.code).toBe('admin.impersonation_target_forbidden');
  const suspended = await start(suspendedTarget.id);
  expect(`${suspended.status} ${suspended.body.code}`).toBe('409 admin.impersonation_target_inactive');
  expect((await start(api.user.id)).body.code).toBe('admin.impersonation_self');
  errors.expectClean('impersonation targets');

  // A second admin logs in as someone; suspending that admin ends the impersonation on its next request.
  const target = await arrangeTestUser(`E2E viewed by second admin ${id}`);
  const second = await as(otherAdmin, landing.admin);
  const secondBearer = trackBearer(second);
  await second.goto(`/admin/users/${target.id}`);
  await second.getByRole('button', { name: 'Log in as' }).click();
  const confirm = modal(second, `Log in as ${target.displayName}?`);
  await confirm.getByLabel(/Why do you need to view this account/).fill(`E2E ${id}: check the home page`);
  await confirm.getByLabel(`Type ${target.email} to confirm`).fill(target.email);
  await confirm.getByRole('button', { name: 'Log in as user' }).click();
  await expect(second).toHaveURL(landing.participant);
  await expect(impersonationBanner(second)).toBeVisible();
  const impersonationToken = secondBearer.current();
  expect((await rawApi('GET', '/auth/me', impersonationToken)).status).toBe(200);

  await api.post(`/admin/users/${otherAdmin.id}/suspend`, {
    reason: `E2E ${id}: suspend the impersonator`,
    confirm: true,
  });
  // The impersonation token is refused on its very next use, and the open page ends up on the sign-in page (its next
  // request — a background refresh or the navigation below — is refused and cannot be resumed).
  expect((await rawApi('GET', '/auth/me', impersonationToken)).status).toBe(401);
  await second.goto('/app/earnings');
  await expect(second).toHaveURL(/\/login/);
  await expect(impersonationBanner(second)).toHaveCount(0);
  // Nothing left to resume: a reload stays signed out.
  await second.goto('/app');
  await expect(second).toHaveURL(/\/login/);
});
