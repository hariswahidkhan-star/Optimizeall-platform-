import {
  accounts,
  arrangeTestUser,
  axeViolations,
  expect,
  expectNoHorizontalScroll,
  impersonationBanner,
  landing,
  modal,
  runId,
  test,
  watchErrors,
} from './support/platform';

/**
 * Phone layout (390×844, touch) of the new pages: the impersonation banner, Finance → Payments and Admin → Roles &
 * permissions — usable, no horizontal scroll and no axe (WCAG 2.1 A/AA) violations. Runs in the mobile-chromium project
 * only (see playwright.config.ts).
 */
test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

test('impersonation banner on a phone: visible, readable, Exit reachable', async ({ as }) => {
  const id = runId();
  const target = await arrangeTestUser(`E2E Phone ${id}`);
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);

  await admin.goto(`/admin/users/${target.id}`);
  await expect(admin.getByRole('heading', { level: 1, name: target.displayName })).toBeVisible();
  await expectNoHorizontalScroll(admin, 'the admin user page');
  await admin.getByRole('button', { name: 'Log in as' }).click();
  const confirm = modal(admin, `Log in as ${target.displayName}?`);
  await confirm.getByLabel(/Why do you need to view this account/).fill(`E2E phone check ${id}`);
  await confirm.getByLabel(`Type ${target.email} to confirm`).fill(target.email);
  await confirm.getByRole('button', { name: 'Log in as user' }).click();
  await expect(admin).toHaveURL(landing.participant);

  const banner = impersonationBanner(admin);
  for (const path of ['/app', '/app/earnings', '/app/profile']) {
    await admin.goto(path);
    await expect(admin.getByRole('heading', { level: 1 }).first()).toBeVisible();
    await expect(banner).toBeInViewport();
    await expect(banner).toContainText(`You are viewing as ${target.displayName}`);
    await expectNoHorizontalScroll(admin, `${path} while impersonating`);
  }
  expect(await axeViolations(admin), 'axe violations with the impersonation banner').toEqual([]);

  const exit = banner.getByRole('button', { name: 'Exit' });
  const box = (await exit.boundingBox())!;
  expect(box.height, 'Exit is a comfortable touch target').toBeGreaterThanOrEqual(24);
  await exit.tap();
  await expect(admin).toHaveURL(/\/admin\/users$/);
  await expect(banner).toHaveCount(0);
  errors.expectClean('impersonating on a phone');
});

test('payments hub on a phone: KPIs, list and a record drawer, no horizontal scroll', async ({ as }) => {
  const finance = await as(accounts.finance, /\/(finance|agency)(\/|$)/);
  const errors = watchErrors(finance);
  await finance.goto('/finance/payments');
  await expect(finance.getByRole('heading', { level: 1, name: 'Payments' })).toBeVisible();
  await expect(finance.getByRole('group', { name: 'Payment totals' })).toBeVisible();
  await expect(finance.getByRole('searchbox', { name: 'Search payments' })).toBeVisible();
  await expectNoHorizontalScroll(finance, 'the payments hub');
  expect(await axeViolations(finance), 'axe violations on the payments hub (phone)').toEqual([]);

  // Rows collapse into cards on a phone; open one record's details.
  const first = finance.getByRole('button', { name: /^(Incoming|Outgoing) / }).first();
  await expect(first).toBeVisible();
  await first.tap();
  const drawer = finance.getByRole('dialog').first();
  await expect(drawer).toBeVisible();
  await expect(drawer.getByText('Status', { exact: true })).toBeVisible();
  await expectNoHorizontalScroll(finance, 'a payment record drawer');
  expect(await axeViolations(finance), 'axe violations in the payment drawer (phone)').toEqual([]);
  await finance.keyboard.press('Escape');
  await expect(drawer).toBeHidden();
  errors.expectClean('the payments hub on a phone');
});

test('roles & permissions on a phone: lists and the role editor, no horizontal scroll', async ({ as }) => {
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/roles');
  await expect(admin.getByRole('heading', { level: 1, name: 'Roles & permissions' })).toBeVisible();
  await expect(admin.getByRole('heading', { name: 'Built-in roles' })).toBeVisible();
  await expectNoHorizontalScroll(admin, 'the roles page');
  expect(await axeViolations(admin), 'axe violations on the roles page (phone)').toEqual([]);

  await admin.getByRole('button', { name: 'New role' }).tap();
  const editor = modal(admin, 'New role');
  await expect(editor.getByLabel('Name')).toBeVisible();
  await editor.getByRole('searchbox', { name: 'Search permissions' }).fill('crm');
  await editor.getByRole('checkbox', { name: /^View CRM crm\.view/ }).check();
  await expect(editor.getByText('1 permission selected')).toBeVisible();
  await expectNoHorizontalScroll(admin, 'the role editor');
  expect(await axeViolations(admin), 'axe violations in the role editor (phone)').toEqual([]);
  await editor.getByRole('button', { name: 'Cancel' }).tap();
  await expect(editor).toBeHidden();
  errors.expectClean('the roles page on a phone');
});
