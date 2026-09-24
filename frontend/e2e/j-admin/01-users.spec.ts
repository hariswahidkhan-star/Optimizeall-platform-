import {
  DEMO_ADMIN_NAME,
  accounts,
  arrangeTestUser,
  expect,
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
 * Users: search and filter the directory (filters survive a reload), open a user's detail page, suspend with a reason
 * (the person is signed out on their very next request and can't sign in), reactivate, change built-in roles (their
 * sessions are revoked; the next sign-in lands in the new portal), and the user's status history and the audit log
 * record every change with its reason and before/after values.
 */
test.describe.configure({ mode: 'serial' });

test('the admin searches and filters the user directory', async ({ as }) => {
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin
    .getByRole('navigation', { name: 'Admin navigation' })
    .getByRole('link', { name: 'Users' })
    .click();
  await expect(admin.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible();
  const table = admin.getByRole('table', { name: 'Users' });

  // Search by name or email.
  await admin.getByRole('searchbox', { name: 'Search users' }).fill('sara.participant@demo');
  await expect(admin).toHaveURL(/search=sara\.participant/);
  await expect(table.getByRole('link', { name: 'Sara Khan' })).toBeVisible();
  await expect(table.getByRole('row')).toHaveCount(2); // header + Sara
  await admin.getByRole('button', { name: 'Reset filters' }).click();
  await expect(admin.getByRole('searchbox', { name: 'Search users' })).toHaveValue('');

  // Filter: suspended accounts only.
  await admin.getByLabel('Status', { exact: true }).selectOption('Suspended');
  await expect(admin).toHaveURL(/status=Suspended/);
  await expect(table.getByText('suspended.participant@demo.optimizeall.app')).toBeVisible();
  for (const status of await table.locator('tbody tr').getByText(/^(Active|Suspended|Deactivated)$/).allInnerTexts())
    expect(status).toBe('Suspended');

  // Filters are in the URL: a reload keeps them.
  await admin.reload();
  await expect(admin.getByLabel('Status', { exact: true })).toHaveValue('Suspended');
  await expect(table.getByText('suspended.participant@demo.optimizeall.app')).toBeVisible();
  await admin.getByRole('button', { name: 'Remove filter Status: Suspended' }).click();

  // Filter by role: finance staff only.
  await admin.getByLabel('Role', { exact: true }).selectOption('Finance');
  await expect(table.getByText('finance1@demo.optimizeall.app')).toBeVisible();
  await expect(table.getByText('finance2@demo.optimizeall.app')).toBeVisible();
  await expect(table.getByText('sara.participant@demo.optimizeall.app')).toHaveCount(0);

  // A search that matches nobody shows the empty state.
  await admin.getByRole('searchbox', { name: 'Search users' }).fill(`nobody-${runId()}@nowhere.test`);
  await expect(admin.getByRole('heading', { name: 'No users match' })).toBeVisible();
  errors.expectClean('searching and filtering users');
});

test('suspending signs the person out on their next request; reactivating lets them back in', async ({
  as,
}) => {
  const id = runId();
  const person = await arrangeTestUser(`E2E Suspendee ${id}`);
  const reason = `E2E ${id}: chargeback investigation on this account`;

  const user = await as(person, landing.participant);
  const bearer = trackBearer(user);
  await user.goto('/app/campaigns');
  await expect(user.getByRole('heading', { level: 1 }).first()).toBeVisible();
  const token = bearer.current();
  expect((await rawApi('GET', '/auth/me', token)).status).toBe(200);

  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/users');
  await admin.getByRole('searchbox', { name: 'Search users' }).fill(person.email);
  await admin.getByRole('link', { name: person.displayName }).click();
  await expect(admin.getByRole('heading', { level: 1, name: person.displayName })).toBeVisible();
  await expect(admin.getByText(person.email).first()).toBeVisible();
  await expect(admin.getByRole('region', { name: 'Profile' })).toBeVisible();

  // The confirmation needs a reason of at least 3 characters and the email typed out.
  await admin.getByRole('button', { name: 'Suspend', exact: true }).click();
  const dialog = modal(admin, `Suspend ${person.displayName}?`);
  const confirm = dialog.getByRole('button', { name: 'Suspend account' });
  await expect(dialog.getByText('Sessions are revoked immediately')).toBeVisible();
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel(`Type ${person.email} to confirm`).fill(person.email);
  await dialog.getByLabel('Reason').fill('no');
  await confirm.click();
  await expect(dialog.getByText('Enter a reason (at least 3 characters).')).toBeVisible();
  await dialog.getByLabel('Reason').fill(reason);
  await confirm.click();
  await expect(dialog).toBeHidden();
  await expect(toast(admin, 'Account suspended')).toBeVisible();
  const alert = admin.getByRole('alert').filter({ hasText: 'This account is suspended' });
  await expect(alert).toContainText(`Reason: ${reason}`);
  await expect(admin.getByRole('button', { name: 'Reactivate', exact: true })).toBeVisible();

  // The person's token no longer works, and their next in-app navigation lands on the sign-in page.
  expect((await rawApi('GET', '/auth/me', token)).status).toBe(401);
  await user.getByRole('link', { name: 'Earnings' }).first().click();
  await expect(user).toHaveURL(/\/login\?expired=1/);
  await expect(user.getByText('Your session has expired')).toBeVisible();
  // They can't sign back in while suspended.
  await user.getByLabel('Email', { exact: true }).fill(person.email);
  await user.getByLabel('Password', { exact: true }).fill(person.password);
  await user.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(user.getByRole('alert')).toContainText(/suspended/i);
  await expect(user).toHaveURL(/\/login/);

  // Reactivate (reason required) → the person signs in again.
  await admin.getByRole('button', { name: 'Reactivate', exact: true }).click();
  const back = modal(admin, `Reactivate ${person.displayName}?`);
  await back.getByLabel('Reason').fill(`E2E ${id}: investigation closed`);
  await back.getByRole('button', { name: 'Reactivate account' }).click();
  await expect(back).toBeHidden();
  await expect(toast(admin, 'Account reactivated')).toBeVisible();
  await expect(admin.getByRole('button', { name: 'Suspend', exact: true })).toBeVisible();
  await user.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(user).toHaveURL(landing.participant);

  // Status history on the user's page lists both changes with their reasons and who made them.
  await admin.reload();
  const history = admin.getByRole('region', { name: 'Status history' });
  await expect(history).toContainText('Suspended');
  await expect(history).toContainText(reason);
  await expect(history).toContainText('Reactivated');
  await expect(history).toContainText(`by ${DEMO_ADMIN_NAME}`);
  errors.expectClean('suspending and reactivating a user');
});

test('changing built-in roles revokes the person’s sessions; they land in the new portal', async ({ as }) => {
  const id = runId();
  const person = await arrangeTestUser(`E2E Promotee ${id}`);
  const reason = `E2E ${id}: joins the review team`;

  const user = await as(person, landing.participant);
  const bearer = trackBearer(user);
  await user.goto('/app/earnings');
  await expect(user.getByRole('heading', { level: 1 }).first()).toBeVisible();
  const token = bearer.current();

  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/users');
  await admin.getByRole('searchbox', { name: 'Search users' }).fill(person.email);
  await admin.getByRole('link', { name: person.displayName }).click();

  await admin.getByRole('button', { name: 'Change roles' }).click();
  const dialog = modal(admin, `Change roles for ${person.displayName}`);
  // Saving without a change is refused in the dialog.
  await dialog.getByLabel('Reason').fill(reason);
  await dialog.getByRole('button', { name: 'Save roles' }).click();
  await expect(dialog.getByRole('alert')).toContainText('Select a different set of roles to save a change.');
  // No role at all is refused too.
  await dialog.getByRole('checkbox', { name: 'Participant' }).uncheck();
  await dialog.getByRole('button', { name: 'Save roles' }).click();
  await expect(dialog.getByText('Choose at least one role.').first()).toBeVisible();
  await dialog.getByRole('checkbox', { name: 'Reviewer' }).check();
  await dialog.getByRole('button', { name: 'Save roles' }).click();
  await expect(dialog).toBeHidden();
  await expect(toast(admin, 'Roles updated')).toBeVisible();
  await expect(admin.getByRole('main').getByText('Reviewer', { exact: true }).first()).toBeVisible();

  // Sessions are revoked: the old token is refused and the next navigation signs them out.
  expect((await rawApi('GET', '/auth/me', token)).status).toBe(401);
  await user.getByRole('link', { name: 'Campaigns' }).first().click();
  await expect(user).toHaveURL(/\/login\?expired=1/);
  await user.getByLabel('Email', { exact: true }).fill(person.email);
  await user.getByLabel('Password', { exact: true }).fill(person.password);
  await user.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(user).toHaveURL(/\/review(\/|$)/);

  // The audit log has the change with the before/after role lists and the reason.
  await admin.goto('/admin/audit');
  const filters = admin.getByRole('search', { name: 'Audit log filters' });
  await filters.getByLabel('Action').fill('admin.user_roles_changed');
  await filters.getByRole('button', { name: 'Apply filters' }).click();
  const entry = admin
    .getByRole('region', { name: 'Audit entries' })
    .getByRole('listitem')
    .filter({ hasText: reason });
  await expect(entry).toHaveCount(1);
  await expect(entry).toContainText(DEMO_ADMIN_NAME);
  await entry.getByRole('button', { name: /admin\.user_roles_changed/ }).click();
  await expect(entry.getByText('Before')).toBeVisible();
  await expect(entry).toContainText('"Participant"');
  await expect(entry).toContainText('"Reviewer"');
  errors.expectClean('changing built-in roles');
});
