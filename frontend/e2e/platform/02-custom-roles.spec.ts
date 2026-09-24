import {
  ApiSession,
  accounts,
  expect,
  test,
  arrangeTestUser,
  axeViolations,
  landing,
  linkNames,
  modal,
  portalNav,
  runId,
  toast,
  watchErrors,
} from './support/platform';

/**
 * Custom roles (dynamic RBAC):
 *   admin → Roles & permissions → New role with only crm.view + crm.manage → assigns it to a new user from the user's
 *   page → that user signs in, lands in the agency portal and sees only the CRM navigation, and can work in the CRM →
 *   admin removes the role → the user's next request is refused: after a refresh the CRM page shows 403, without
 *   signing in again (permissions are resolved per request).
 */
const FORBIDDEN = 'You don’t have access to this page';

const statusOf = (p: Promise<unknown>) =>
  p.then(
    () => 200,
    (e: { status?: number }) => e.status ?? 0,
  );

test('a custom CRM-only role grants exactly the CRM, and removing it applies on the next request', async ({
  as,
}) => {
  const id = runId();
  const roleName = `CRM only ${id}`;
  // A brand-new account (participant test user) that gets its staff access from the custom role alone.
  const member = await arrangeTestUser(`E2E CRM member ${id}`);

  // ---------------------------------------------------------------- admin creates the role
  const admin = await as(accounts.admin, landing.admin);
  const adminErrors = watchErrors(admin);
  await admin
    .getByRole('navigation', { name: 'Admin navigation' })
    .getByRole('link', { name: 'Roles & permissions' })
    .click();
  await expect(admin.getByRole('heading', { level: 1, name: 'Roles & permissions' })).toBeVisible();
  await expect(admin.getByRole('table', { name: 'Built-in roles' })).toBeVisible();
  expect(await axeViolations(admin), 'axe violations on the roles page').toEqual([]);

  await admin.getByRole('button', { name: 'New role' }).click();
  const editor = modal(admin, 'New role');
  await editor.getByLabel('Name').fill(roleName);
  await editor
    .getByLabel('Description (optional)')
    .fill('Sales CRM only: contacts, companies, deals and CRM tasks.');
  await editor.getByRole('searchbox', { name: 'Search permissions' }).fill('crm');
  await editor.getByRole('checkbox', { name: /^View CRM crm\.view/ }).check();
  await editor.getByRole('checkbox', { name: /^Manage CRM crm\.manage/ }).check();
  await expect(editor.getByText('2 permissions selected')).toBeVisible();
  expect(await axeViolations(admin), 'axe violations in the role editor').toEqual([]);
  await editor.getByRole('button', { name: 'Create role' }).click();
  await expect(toast(admin, 'Role created')).toBeVisible();
  await expect(editor).toBeHidden();
  const roleRow = admin
    .getByRole('table', { name: 'Custom roles' })
    .getByRole('row')
    .filter({ hasText: roleName });
  await expect(roleRow).toBeVisible();
  await expect(roleRow.getByRole('cell').nth(1)).toHaveText('2');

  // ---------------------------------------------------------------- assign it from the user's page
  await admin.goto(`/admin/users/${member.id}`);
  await expect(admin.getByRole('heading', { level: 1, name: member.displayName })).toBeVisible();
  const section = admin.getByRole('region', { name: 'Custom roles' });
  const assignment = section.getByRole('checkbox', { name: roleName });
  // Controlled checkbox: it flips once the API has assigned the role.
  await assignment.click();
  await expect(toast(admin, 'Role assigned')).toBeVisible();
  await expect(assignment).toBeChecked();
  await expect(assignment).toBeEnabled();
  adminErrors.expectClean('creating and assigning the role');

  // ---------------------------------------------------------------- the member lands in the agency portal, CRM only
  const user = await as(
    { email: member.email, password: member.password, displayName: member.displayName },
    landing.agency,
  );
  const userErrors = watchErrors(user);
  const nav = portalNav(user, 'Agency navigation');
  await expect(nav.getByRole('link', { name: 'Sales CRM' })).toBeVisible();
  // Only CRM areas: the CRM itself plus the website's lead inbox, which crm.view opens by design (inquiries are leads).
  expect(await linkNames(nav)).toEqual([
    'Home',
    'Sales CRM',
    'Pipeline',
    'My tasks',
    'Website',
    'Website inquiries',
  ]);
  // The agency home renders for a CRM-only member without calling APIs it may not use.
  await user.goto('/agency');
  await expect(
    user.getByRole('heading', { level: 1, name: /^Good (morning|afternoon|evening), / }),
  ).toBeVisible();
  await user.goto('/agency/website/inquiries');
  await expect(user.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(user.getByRole('heading', { level: 1, name: FORBIDDEN })).toHaveCount(0);

  await nav.getByRole('link', { name: 'Sales CRM' }).click();
  await expect(user.getByRole('heading', { level: 1, name: 'Sales CRM' })).toBeVisible();
  await user.goto('/agency/crm/contacts');
  await expect(user.getByRole('heading', { level: 1, name: 'Contacts' })).toBeVisible();
  // crm.manage: they can create records.
  await expect(user.getByRole('button', { name: 'New contact' })).toBeEnabled();
  // Outside the CRM the agency answers 403.
  await user.goto('/agency/billing/invoices');
  await expect(user.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
  await user.goto('/agency/crm/contacts');
  await expect(user.getByRole('heading', { level: 1, name: 'Contacts' })).toBeVisible();
  userErrors.expectClean('the CRM-only member');

  // An API session opened while the member still has the role.
  const api = await ApiSession.login(member.email, member.password);
  expect(await statusOf(api.get('/agency/crm/contacts'))).toBe(200);

  // ---------------------------------------------------------------- admin removes the role
  await assignment.click();
  await expect(toast(admin, 'Role removed')).toBeVisible();
  await expect(assignment).not.toBeChecked();

  // The member's existing session token no longer opens the CRM API (resolved per request, no re-login)…
  expect(await statusOf(api.get('/agency/crm/contacts'))).toBe(403);

  // …and after a refresh the page answers 403 instead of the CRM.
  await user.reload();
  await expect(user.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
  await expect(user.getByRole('heading', { level: 1, name: 'Contacts' })).toHaveCount(0);
  await expect(user.getByRole('navigation', { name: 'Agency navigation' })).toHaveCount(0);
  userErrors.expectClean('the member after the role was removed');

  // The role stays defined with nobody on it.
  await admin.goto('/admin/roles');
  await expect(roleRow.getByRole('cell').nth(2)).toHaveText('0');
  adminErrors.expectClean('removing the role');
});
