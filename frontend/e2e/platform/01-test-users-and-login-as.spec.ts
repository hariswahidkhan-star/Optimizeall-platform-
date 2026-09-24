import {
  DEMO_ADMIN_NAME,
  accounts,
  expect,
  test,
  axeViolations,
  impersonationBanner,
  landing,
  modal,
  runId,
  signOut,
  watchErrors,
} from './support/platform';

/**
 * Test users and "log in as":
 *   admin → Users → Create test user (participant; the password is shown once) → sign out → the sign-in page's
 *   "Test accounts" panel signs in as the new test user with one click (and the shown password works too) → admin
 *   → Log in as the test user with a written reason → the banner is on every page → changing the password is refused
 *   with the impersonation error → Exit returns to Admin → Users → the audit log reads "Admin as User".
 */
test.describe.configure({ mode: 'serial' });

test('admin creates a test user, signs in as it from the test accounts panel, then logs in as it (audited)', async ({
  as,
  anonymous,
}) => {
  const id = runId();
  const testName = `E2E Tester ${id}`;
  const reason = `E2E support ticket ${id}: checking the participant home`;

  // ---------------------------------------------------------------- admin creates a test user
  const admin = await as(accounts.admin, landing.admin);
  const adminErrors = watchErrors(admin);
  await admin
    .getByRole('navigation', { name: 'Admin navigation' })
    .getByRole('link', { name: 'Users' })
    .click();
  await expect(admin.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible();
  await admin.getByRole('button', { name: 'Create test user' }).click();
  const create = modal(admin, 'Create a test user');
  await create.getByLabel('Name').fill(testName);
  await expect(create.getByRole('checkbox', { name: 'Participant' })).toBeChecked();
  expect(await axeViolations(admin), 'axe violations in the create test user dialog').toEqual([]);
  await create.getByRole('button', { name: 'Create test user' }).click();

  const created = modal(admin, 'Test user created');
  await expect(created.getByText('Copy the password now')).toBeVisible();
  const email = (await created.locator('dd code').first().textContent())!.trim();
  const password = (await created.getByTestId('test-user-password').textContent())!.trim();
  expect(email).toMatch(/^test\+e2e-tester-[\w-]+@test\.optimizeall\.app$/);
  expect(password.length).toBeGreaterThanOrEqual(16);
  await created.getByRole('button', { name: 'Done' }).click();
  await expect(created).toBeHidden();

  // The list labels it TEST.
  await admin.getByRole('searchbox', { name: 'Search users' }).fill(email);
  const row = admin.getByRole('row').filter({ hasText: testName });
  await expect(row).toBeVisible();
  await expect(row.getByText('TEST', { exact: true })).toBeVisible();
  adminErrors.expectClean('creating a test user');
  await signOut(admin, DEMO_ADMIN_NAME);

  // ---------------------------------------------------------------- one-click sign-in from the "Test accounts" panel
  const panel = admin.getByRole('region', { name: 'Test accounts' });
  await expect(panel.getByText('Not production')).toBeVisible();
  await panel.getByRole('button', { name: `Sign in as ${testName} (Participant), ${email}` }).click();
  await expect(admin).toHaveURL(landing.participant);
  await expect(admin.getByRole('button', { name: `Account menu for ${testName}` })).toBeVisible();
  await expect(impersonationBanner(admin)).toHaveCount(0);
  await signOut(admin, testName);

  // The password shown once is the real one.
  await admin.getByLabel('Email', { exact: true }).fill(email);
  await admin.getByLabel('Password', { exact: true }).fill(password);
  await admin.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(admin).toHaveURL(landing.participant);
  await signOut(admin, testName);
  adminErrors.expectClean('signing in as the test user');

  // ---------------------------------------------------------------- admin logs in as the test user
  const support = await as(accounts.admin, landing.admin);
  const errors = watchErrors(support);
  await support.goto('/admin/users');
  await support.getByRole('searchbox', { name: 'Search users' }).fill(email);
  await support.getByRole('button', { name: `Log in as ${testName}` }).click();
  const confirm = modal(support, `Log in as ${testName}?`);
  await expect(confirm.getByText('Some actions are blocked while you view as someone else')).toBeVisible();
  const go = confirm.getByRole('button', { name: 'Log in as user' });
  await confirm.getByLabel(/Why do you need to view this account/).fill(reason);
  await expect(go).toBeDisabled(); // the email must be typed out first
  await confirm.getByLabel(`Type ${email} to confirm`).fill(email);
  await go.click();
  await expect(support).toHaveURL(landing.participant);

  const banner = impersonationBanner(support);
  await expect(banner).toContainText(`You are viewing as ${testName} (participant)`);
  await expect(banner).toContainText('TEST');
  await expect(banner).toContainText(`signed in as ${DEMO_ADMIN_NAME}`);

  // The banner stays on every page, including after a full reload.
  for (const path of ['/app/campaigns', '/app/earnings', '/app/profile', '/app/profile/security']) {
    await support.goto(path);
    await expect(support.getByRole('heading', { level: 1 }).first()).toBeVisible();
    await expect(banner, `banner on ${path}`).toBeVisible();
    await expect(banner.getByRole('button', { name: 'Exit' })).toBeVisible();
  }

  // ---------------------------------------------------------------- a blocked action: change password
  errors.ignore(/HTTP 403 POST .*\/api\/v1\/auth\/change-password$/);
  const form = support.getByRole('form', { name: 'Change password' });
  await form.getByLabel('Current password').fill(password);
  await form.getByLabel('New password', { exact: true }).fill(`E2e-New#${id}-Passw0rd`);
  await form.getByLabel('Confirm new password').fill(`E2e-New#${id}-Passw0rd`);
  await form.getByRole('button', { name: 'Change password' }).click();
  await expect(form.getByRole('alert')).toContainText(
    'This action is not available while you are viewing as another user. Exit the impersonation session first.',
  );
  await expect(support).toHaveURL(/\/app\/profile\/security$/); // still signed in, nothing changed

  // ---------------------------------------------------------------- Exit → back to Admin → Users as the admin
  await banner.getByRole('button', { name: 'Exit' }).click();
  await expect(support).toHaveURL(/\/admin\/users$/);
  await expect(support.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible();
  await expect(banner).toHaveCount(0);
  await expect(support.getByRole('button', { name: `Account menu for ${DEMO_ADMIN_NAME}` })).toBeVisible();

  // ---------------------------------------------------------------- the audit log: "Admin as User"
  await support
    .getByRole('navigation', { name: 'Admin navigation' })
    .getByRole('link', { name: 'Audit log' })
    .click();
  await expect(support.getByRole('heading', { level: 1, name: 'Audit log' })).toBeVisible();
  const filters = support.getByRole('search', { name: 'Audit log filters' });
  await filters.getByLabel('Action').fill('impersonation.');
  await filters.getByRole('button', { name: 'Apply filters' }).click();
  const entries = support.getByRole('listitem').filter({ hasText: `${DEMO_ADMIN_NAME} as ${testName}` });
  await expect(entries.first()).toBeVisible();
  await expect(entries.filter({ hasText: 'impersonation.request' }).first()).toBeVisible();
  errors.expectClean('the login-as journey');

  // The same person can sign in normally again: nothing changed on the test user's account.
  const page = await anonymous();
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(landing.participant);
});
