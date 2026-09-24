import {
  DEMO_ADMIN_NAME,
  accounts,
  expect,
  landing,
  modal,
  runId,
  signOut,
  test,
  watchErrors,
} from './support/jadmin';

/**
 * Test accounts: the admin creates a test user of each kind of role through Users → Create test user (a client user
 * joins a client organization), each is labelled TEST in the directory, and the sign-in page's "Test accounts" panel
 * signs in as each one with a single click, landing in that role's portal.
 */
test.describe.configure({ mode: 'serial' });

const ROLES = [
  { role: 'Participant', label: 'Participant', landing: /\/app(\/|$)/ },
  { role: 'Reviewer', label: 'Reviewer', landing: /\/review(\/|$)/ },
  { role: 'CampaignManager', label: 'Campaign manager', landing: /\/manage(\/|$)/ },
  { role: 'Finance', label: 'Finance', landing: /\/finance(\/|$)/ },
  { role: 'AccountManager', label: 'Account manager', landing: /\/agency(\/|$)/ },
  { role: 'Admin', label: 'Admin', landing: /\/admin(\/|$)/ },
  { role: 'Client', label: 'Client', landing: /\/client(\/|$)/ },
] as const;

test('a test user of each role signs in from the test accounts panel into the right portal', async ({
  as,
  anonymous,
}) => {
  const id = runId();
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  const created: { name: string; email: string; role: (typeof ROLES)[number] }[] = [];

  for (const r of ROLES) {
    const name = `E2E ${r.role} tester ${id}`;
    await admin.goto('/admin/users');
    await admin.getByRole('button', { name: 'Create test user' }).click();
    const dialog = modal(admin, 'Create a test user');
    await dialog.getByLabel('Name').fill(name);
    if (r.role !== 'Participant') {
      await dialog.getByRole('checkbox', { name: 'Participant' }).uncheck();
      await dialog.getByRole('checkbox', { name: r.label, exact: true }).check();
    }
    if (r.role === 'Client') {
      const org = dialog.getByLabel('Client organization');
      await expect(org.locator('option', { hasText: 'Nimbus Fitness' })).toHaveCount(1);
      await org.selectOption({ label: 'Nimbus Fitness' });
      await dialog.getByLabel('Client role').selectOption('Approver');
    }
    await dialog.getByRole('button', { name: 'Create test user' }).click();
    const done = modal(admin, 'Test user created');
    const email = (await done.locator('dd code').first().textContent())!.trim();
    await expect(done.getByText(r.label, { exact: true })).toBeVisible();
    await done.getByRole('button', { name: 'Done' }).click();
    created.push({ name, email, role: r });
  }

  // The directory's "Test accounts" filter lists them, labelled TEST.
  await admin.goto('/admin/users');
  await admin.getByLabel('Account type').selectOption('true');
  await admin.getByRole('searchbox', { name: 'Search users' }).fill(id);
  const table = admin.getByRole('table', { name: 'Users' });
  for (const c of created) {
    const row = table.getByRole('row').filter({ hasText: c.name });
    await expect(row).toBeVisible();
    await expect(row.getByText('TEST', { exact: true })).toBeVisible();
  }
  errors.expectClean('creating test users of every role');
  await signOut(admin, DEMO_ADMIN_NAME);

  // One click on the sign-in page per account.
  const page = await anonymous();
  const pageErrors = watchErrors(page);
  for (const c of created) {
    await page.goto('/login');
    const panel = page.getByRole('region', { name: 'Test accounts' });
    await expect(panel.getByText('Not production')).toBeVisible();
    await panel
      .getByRole('button', {
        name: new RegExp(
          `^Sign in as ${c.name} \\(.*${c.role.role === 'CampaignManager' ? 'Campaign Manager' : c.role.role === 'AccountManager' ? 'Account Manager' : c.role.role}.*\\), `,
        ),
      })
      .click();
    await expect(page, `${c.role.role} lands in its portal`).toHaveURL(c.role.landing);
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
    await signOut(page, c.name);
  }
  pageErrors.expectClean('signing in as each test user');
});
