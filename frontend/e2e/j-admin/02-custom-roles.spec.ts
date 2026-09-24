import {
  FORBIDDEN,
  accounts,
  adminApi,
  arrangeRole,
  arrangeTestUser,
  codeOf,
  expect,
  landing,
  linkNames,
  modal,
  portalNav,
  runId,
  test,
  toast,
  watchErrors,
  type CustomRoleDto,
} from './support/jadmin';
import { ApiSession } from './support/jadmin';

/**
 * Custom roles:
 *   1. the admin builds a role from every permission area with "Select all in …" (client portal excluded: mixing it with
 *      staff permissions is refused in the editor and by the API), then deletes it;
 *   2. several custom roles, each assigned to a fresh participant test user, land that person in the right portal with
 *      exactly the navigation the role grants;
 *   3. client/staff mixing is refused when assigning too (409 roles.client_staff_conflict, shown on the user page);
 *   4. a delegated role manager (roles.manage through a custom role) manages what they hold but cannot escalate: no
 *      permission they lack, no admin-only permission, no editing/assigning the role that made them a manager, no
 *      built-in role changes, no settings.
 */
test.describe.configure({ mode: 'serial' });

test('a role built from every permission area; client portal can’t be mixed with staff permissions', async ({
  as,
}) => {
  const id = runId();
  const roleName = `Everything staff ${id}`;
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/roles');
  await admin.getByRole('button', { name: 'New role' }).click();
  const editor = modal(admin, 'New role');
  await editor.getByLabel('Name').fill(roleName);

  const api = await adminApi();
  const catalog = await api.get<{ areas: { area: string; permissions: { key: string }[] }[] }>(
    '/admin/roles/catalog',
  );
  const staffAreas = catalog.areas.filter((a) => a.area !== 'Client portal');
  expect(staffAreas.length).toBeGreaterThanOrEqual(11);
  for (const area of staffAreas)
    await editor.getByRole('checkbox', { name: `Select all in ${area.area}` }).check();
  const expected = staffAreas.flatMap((a) => a.permissions.map((p) => p.key)).sort();
  await expect(editor.getByText(`${expected.length} permissions selected`)).toBeVisible();

  // The client portal is for client users only: nobody on staff holds it, so it can't be granted from here.
  const clientPortal = editor.getByRole('checkbox', { name: /^Client portal client\.portal/ });
  await expect(clientPortal).toBeDisabled();
  await expect(clientPortal).not.toBeChecked();
  await expect(
    editor.getByRole('group', { name: /^Client portal/ }).getByText('You can’t grant this'),
  ).toBeVisible();
  // The API refuses a client/staff mix on its own (400 before the grant check).
  expect(
    await codeOf(api.post('/admin/roles', { name: `Mixed ${id}`, permissions: ['client.portal', 'crm.view'] })),
  ).toBe('400 roles.client_portal_mixed');

  await editor.getByRole('button', { name: 'Create role' }).click();
  await expect(toast(admin, 'Role created')).toBeVisible();
  const row = admin.getByRole('table', { name: 'Custom roles' }).getByRole('row').filter({ hasText: roleName });
  await expect(row.getByRole('cell').nth(1)).toHaveText(String(expected.length));
  const saved = (await api.get<{ custom: CustomRoleDto[] }>('/admin/roles')).custom.find((r) => r.name === roleName)!;
  expect([...saved.permissions].sort()).toEqual(expected);

  // Nobody has it: deleting needs no typed confirmation.
  await row.getByRole('button', { name: `Delete ${roleName}` }).click();
  const del = modal(admin, `Delete ${roleName}?`);
  await expect(del.getByText('Nobody has this role.')).toBeVisible();
  await del.getByRole('button', { name: 'Delete role' }).click();
  await expect(toast(admin, 'Role deleted')).toBeVisible();
  await expect(row).toHaveCount(0);
  errors.expectClean('building a role from every area');
});

/** A custom role, the portal its holder lands in, and the exact navigation they get. */
const PORTAL_CASES = [
  {
    key: 'finance',
    permissions: ['ledger.view', 'payouts.view'],
    landing: /\/finance(\/|$)/,
    nav: 'Finance navigation',
    links: ['Overview', 'Payments', 'Payout batches', 'Ledger', 'Exchange rates', 'Payout schedule'],
    forbidden: '/finance/holds',
  },
  {
    key: 'support',
    permissions: ['support.manage', 'users.view'],
    landing: /\/admin(\/|$)/,
    nav: 'Admin navigation',
    links: ['Overview', 'Users', 'Support tickets'],
    forbidden: '/admin/settings',
  },
  {
    key: 'content',
    permissions: ['content.manage'],
    landing: /\/admin(\/|$)/,
    nav: 'Admin navigation',
    links: ['Overview', 'Content'],
    forbidden: '/admin/users',
  },
  {
    key: 'review',
    permissions: ['submissions.review', 'appeals.resolve'],
    landing: /\/review(\/|$)/,
    nav: 'Reviewer navigation',
    links: ['Overview', 'Queue', 'Live checks', 'Appeals'],
    forbidden: '/review/social-verification',
  },
] as const;

for (const c of PORTAL_CASES) {
  test(`a custom ${c.key} role lands its holder in the right portal with exactly its navigation`, async ({
    as,
  }) => {
    const id = runId();
    const roleName = `E2E ${c.key} ${id}`;
    const person = await arrangeTestUser(`E2E ${c.key} holder ${id}`);

    // The admin creates the role through the editor (search + tick each permission) and assigns it from the user page.
    const admin = await as(accounts.admin, landing.admin);
    const errors = watchErrors(admin);
    await admin.goto('/admin/roles');
    await admin.getByRole('button', { name: 'New role' }).click();
    const editor = modal(admin, 'New role');
    await editor.getByLabel('Name').fill(roleName);
    for (const key of c.permissions) {
      await editor.getByRole('searchbox', { name: 'Search permissions' }).fill(key);
      await editor.getByRole('checkbox', { name: new RegExp(` ${key.replace('.', '\\.')}`) }).check();
    }
    await expect(editor.getByText(`${c.permissions.length} permission`)).toBeVisible();
    await editor.getByRole('button', { name: 'Create role' }).click();
    await expect(toast(admin, 'Role created')).toBeVisible();

    await admin.goto(`/admin/users/${person.id}`);
    const assignment = admin.getByRole('region', { name: 'Custom roles' }).getByRole('checkbox', { name: roleName });
    await assignment.click();
    await expect(toast(admin, 'Role assigned')).toBeVisible();
    await expect(assignment).toBeChecked();
    // The header lists it next to the built-in roles.
    await expect(admin.getByRole('main').getByText(roleName).first()).toBeVisible();
    errors.expectClean(`assigning the ${c.key} role`);

    const user = await as(person, c.landing);
    const userErrors = watchErrors(user);
    const nav = portalNav(user, c.nav);
    await expect(nav.getByRole('link').first()).toBeVisible();
    expect(await linkNames(nav)).toEqual([...c.links]);
    // Every nav link opens (no 403 page), and a page outside the role answers 403.
    for (const link of c.links) {
      await nav.getByRole('link', { name: link, exact: true }).click();
      await expect(user.getByRole('heading', { level: 1 }).first()).toBeVisible();
      await expect(user.getByRole('heading', { level: 1, name: FORBIDDEN }), `${link} opens`).toHaveCount(0);
    }
    await user.goto(c.forbidden);
    await expect(user.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
    userErrors.expectClean(`the ${c.key} role holder`);
  });
}

test('client users and staff never mix: staff custom roles and built-in staff roles are refused', async ({
  as,
}) => {
  const id = runId();
  const staffRole = await arrangeRole(`E2E CRM viewers ${id}`, ['crm.view']);
  const client = await arrangeTestUser(`E2E client user ${id}`, ['Client']);

  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  errors.ignore(/HTTP 409 PUT .*\/api\/v1\/admin\/roles\/.*\/users\//);
  await admin.goto(`/admin/users/${client.id}`);
  const section = admin.getByRole('region', { name: 'Custom roles' });
  const box = section.getByRole('checkbox', { name: staffRole.name });
  await box.click();
  await expect(section.getByRole('alert')).toContainText(/client portal/i);
  await expect(box).not.toBeChecked();
  await expect(box).toBeEnabled();

  // Built-in roles follow the same rule: a client user can't also be made a reviewer.
  errors.ignore(/HTTP 409 PUT .*\/api\/v1\/admin\/users\/.*\/roles$/);
  await admin.getByRole('button', { name: 'Change roles' }).click();
  const dialog = modal(admin, `Change roles for ${client.displayName}`);
  await dialog.getByRole('checkbox', { name: 'Reviewer' }).check();
  await dialog.getByLabel('Reason').fill(`E2E ${id}: try to mix`);
  await dialog.getByRole('button', { name: 'Save roles' }).click();
  await expect(dialog.getByRole('alert')).toContainText(/client portal/i);
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  const api = await adminApi();
  expect(
    await codeOf(
      api.put(`/admin/users/${client.id}/roles`, { roles: ['Client', 'Reviewer'], reason: 'E2E mix', confirm: true }),
    ),
  ).toBe('409 roles.client_staff_conflict');
  errors.expectClean('refusing a client/staff mix');
});

test('a delegated role manager manages what they hold but can’t escalate', async ({ as }) => {
  const id = runId();
  const delegate = await arrangeTestUser(`E2E role delegate ${id}`);
  const managerRole = await arrangeRole(
    `E2E role managers ${id}`,
    ['roles.manage', 'users.view', 'crm.view'],
    [delegate.id],
  );
  const colleague = await arrangeTestUser(`E2E delegate colleague ${id}`);
  const adminOnlyRole = await arrangeRole(`E2E settings admins ${id}`, ['settings.manage']);

  const page = await as(delegate, /\/(admin|agency)(\/|$)/);
  const errors = watchErrors(page);
  await page.goto('/admin/roles');
  await expect(page.getByRole('heading', { level: 1, name: 'Roles & permissions' })).toBeVisible();

  // The role that made them a manager (and any role holding permissions they lack) is view-only.
  const table = page.getByRole('table', { name: 'Custom roles' });
  await expect(table.getByRole('button', { name: `View ${managerRole.name}` })).toBeVisible();
  await expect(table.getByRole('button', { name: `Edit ${managerRole.name}` })).toHaveCount(0);
  await expect(table.getByRole('button', { name: `View ${adminOnlyRole.name}` })).toBeVisible();

  // In the editor, what they don't hold and the admin-only permissions are disabled.
  await page.getByRole('button', { name: 'New role' }).click();
  const editor = modal(page, 'New role');
  await expect(editor.getByText('You can only grant permissions you hold yourself.')).toBeVisible();
  const search = editor.getByRole('searchbox', { name: 'Search permissions' });
  await search.fill('crm');
  await expect(editor.getByRole('checkbox', { name: /^View CRM crm\.view/ })).toBeEnabled();
  await expect(editor.getByRole('checkbox', { name: /^Manage CRM crm\.manage/ })).toBeDisabled();
  await search.fill('roles.manage');
  await expect(editor.getByRole('checkbox', { name: /roles\.manage/ })).toBeDisabled();
  await search.fill('settings.manage');
  await expect(editor.getByRole('checkbox', { name: /settings\.manage/ })).toBeDisabled();

  // They can create a role from what they hold, and assign it.
  const ownRole = `E2E CRM readers ${id}`;
  await editor.getByLabel('Name').fill(ownRole);
  await search.fill('crm.view');
  await editor.getByRole('checkbox', { name: /^View CRM crm\.view/ }).check();
  await editor.getByRole('button', { name: 'Create role' }).click();
  await expect(toast(page, 'Role created')).toBeVisible();
  await page.goto(`/admin/users/${colleague.id}`);
  const section = page.getByRole('region', { name: 'Custom roles' });
  await section.getByRole('checkbox', { name: ownRole }).click();
  await expect(toast(page, 'Role assigned')).toBeVisible();
  // The manager role can't be handed on from the page: its checkbox is disabled.
  await expect(section.getByRole('checkbox', { name: managerRole.name })).toBeDisabled();
  // No built-in role changes, no suspensions: those buttons aren't offered.
  await expect(page.getByRole('button', { name: 'Change roles' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Suspend', exact: true })).toHaveCount(0);
  // Settings stay closed.
  await page.goto('/admin/settings');
  await expect(page.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
  errors.expectClean('the delegated role manager in the UI');

  // The API enforces every guardrail on its own.
  const api = await ApiSession.login(delegate.email, delegate.password);
  const roles = (await api.get<{ custom: CustomRoleDto[] }>('/admin/roles')).custom;
  const mgr = roles.find((r) => r.id === managerRole.id)!;
  expect(await codeOf(api.post('/admin/roles', { name: `Escalate A ${id}`, permissions: ['crm.manage'] }))).toBe(
    '403 roles.cannot_grant_unheld',
  );
  expect(await codeOf(api.post('/admin/roles', { name: `Escalate B ${id}`, permissions: ['roles.manage'] }))).toBe(
    '403 roles.admin_only_permission',
  );
  expect(
    await codeOf(api.post('/admin/roles', { name: `Escalate C ${id}`, permissions: ['users.impersonate'] })),
  ).toBe('403 roles.admin_only_permission');
  expect(
    await codeOf(
      api.put(`/admin/roles/${mgr.id}`, {
        name: mgr.name,
        permissions: [...mgr.permissions, 'settings.manage'],
        concurrencyStamp: mgr.concurrencyStamp,
      }),
    ),
  ).toMatch(/^403 roles\.(admin_only_permission|cannot_grant_unheld)$/);
  expect(await codeOf(api.put(`/admin/roles/${mgr.id}/users/${colleague.id}`))).toMatch(/^403 /);
  expect(await codeOf(api.put(`/admin/roles/${adminOnlyRole.id}/users/${delegate.id}`))).toMatch(/^403 /);
  expect(
    await codeOf(api.put(`/admin/users/${colleague.id}/roles`, { roles: ['Admin'], reason: 'escalate', confirm: true })),
  ).toMatch(/^403 /);
  expect(
    await codeOf(api.put(`/admin/users/${delegate.id}/roles`, { roles: ['Admin'], reason: 'escalate', confirm: true })),
  ).toMatch(/^403 /);
  expect(await codeOf(api.put('/admin/settings/eligibility.minFollowers', { value: 1, reason: 'escalate', confirm: true }))).toMatch(/^403 /);
  expect(await codeOf(api.post(`/admin/users/${colleague.id}/suspend`, { reason: 'escalate', confirm: true }))).toMatch(/^403 /);
  expect(await codeOf(api.delete(`/admin/roles/${adminOnlyRole.id}`))).toMatch(/^403 /);
  // Nothing changed for them: still exactly the permissions of their roles.
  const me = await api.get<{ permissions: string[] }>('/auth/me');
  expect(me.permissions).not.toContain('settings.manage');
  expect(me.permissions).not.toContain('crm.manage');
});
