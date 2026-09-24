import {
  FORBIDDEN,
  accounts,
  adminApi,
  arrangeRole,
  arrangeTestUser,
  codeOf,
  expect,
  landing,
  modal,
  runId,
  statusOf,
  test,
  toast,
  watchErrors,
} from './support/jadmin';
import { ApiSession } from './support/jadmin';

/**
 * Jobs, notification deliveries, global search and the negative paths:
 *   Jobs & notifications → Run now (NotificationDispatchJob) → the run is in the history and the suspension email staged
 *   for a user shows as a delivery; Ctrl+K search finds users for an admin but never for a role without users.view;
 *   a user without roles.manage gets 403 in the UI and the API; a stale custom-role edit answers 409; a double-submitted
 *   role create makes one role; boundary values of names and reasons are refused.
 */
test.describe.configure({ mode: 'serial' });

test('run a job now; the notification deliveries it sends are listed', async ({ as }) => {
  const id = runId();
  // Stage an email: suspending a user notifies them (delivered by NotificationDispatchJob; jobs don't run on their own here).
  const person = await arrangeTestUser(`E2E delivery target ${id}`);
  const api = await adminApi();
  await api.post(`/admin/users/${person.id}/suspend`, { reason: `E2E ${id}: delivery check`, confirm: true });

  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin
    .getByRole('navigation', { name: 'Admin navigation' })
    .getByRole('link', { name: 'Jobs & notifications' })
    .click();
  await expect(admin.getByRole('heading', { level: 1, name: 'Jobs & notifications' })).toBeVisible();
  await admin.getByRole('button', { name: 'Run NotificationDispatchJob now' }).click();
  await modal(admin, 'Run NotificationDispatchJob now?').getByRole('button', { name: 'Run now' }).click();
  await expect(toast(admin, 'NotificationDispatchJob finished')).toBeVisible();

  await admin.getByRole('tab', { name: 'Run history' }).click();
  await expect(
    admin
      .getByRole('table', { name: 'Job runs' })
      .getByRole('row')
      .filter({ hasText: 'NotificationDispatchJob' })
      .first(),
  ).toBeVisible();

  await admin.getByRole('tab', { name: 'Notification deliveries' }).click();
  await admin.getByRole('searchbox', { name: 'Search deliveries' }).fill(person.email);
  const row = admin
    .getByRole('table', { name: 'Notification deliveries' })
    .getByRole('row')
    .filter({ hasText: person.email });
  await expect(row.first()).toBeVisible();
  await expect(row.first()).toContainText(/Email/);
  await expect(row.first()).toContainText(/Sent/);

  // The manual run is audited.
  await admin.goto('/admin/audit');
  const filters = admin.getByRole('search', { name: 'Audit log filters' });
  await filters.getByLabel('Action').fill('admin.job_run_requested');
  await filters.getByRole('button', { name: 'Apply filters' }).click();
  await expect(
    admin.getByRole('region', { name: 'Audit entries' }).getByRole('listitem').first(),
  ).toContainText('NotificationDispatchJob');
  errors.expectClean('running a job');
});

test('Ctrl+K search is scoped to what the searcher may see', async ({ as }) => {
  const id = runId();
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.keyboard.press('Control+k');
  const palette = modal(admin, 'Search');
  const box = palette.getByRole('combobox', { name: 'Search pages and records' });
  await expect(box).toBeFocused();
  await box.fill('Sara Khan');
  const results = palette.getByRole('listbox', { name: 'Results' });
  await expect(results.getByRole('option', { name: /Sara Khan/ })).toBeVisible();
  await results.getByRole('option', { name: /Sara Khan/ }).click();
  await expect(admin).toHaveURL(/\/admin\/users\/[0-9a-f-]+$/);
  await expect(admin.getByRole('heading', { level: 1, name: 'Sara Khan' })).toBeVisible();
  errors.expectClean('searching as the admin');

  // A CRM-only custom role holder (agency portal) finds CRM records, never users.
  const crm = await arrangeTestUser(`E2E CRM searcher ${id}`);
  await arrangeRole(`E2E CRM search ${id}`, ['crm.view'], [crm.id]);
  const page = await as(crm, landing.agency);
  const pageErrors = watchErrors(page);
  await page.keyboard.press('Control+k');
  const box2 = modal(page, 'Search').getByRole('combobox', { name: 'Search pages and records' });
  await box2.fill('Sara');
  await expect(
    page
      .getByRole('status')
      .filter({ hasText: /result|No matches|Nothing/i })
      .first(),
  ).toBeVisible();
  await expect(
    page.getByRole('listbox', { name: 'Results' }).getByRole('option', { name: /Sara Khan/ }),
  ).toHaveCount(0);
  const api = await ApiSession.login(crm.email, crm.password);
  const found = await api.get<{ groups: { type: string }[] }>('/search?q=Sara');
  expect(found.groups.map((g) => g.type)).not.toContain('users');
  pageErrors.expectClean('searching as a CRM-only member');

  // A content-only admin (no searchable permission) still gets the page shortcuts without an error.
  const editor = await arrangeTestUser(`E2E content editor ${id}`);
  await arrangeRole(`E2E content only ${id}`, ['content.manage'], [editor.id]);
  const editorPage = await as(editor, landing.admin);
  const editorErrors = watchErrors(editorPage);
  await editorPage.keyboard.press('Control+k');
  const box3 = modal(editorPage, 'Search').getByRole('combobox', { name: 'Search pages and records' });
  await box3.fill('Content');
  await expect(
    editorPage
      .getByRole('listbox', { name: 'Results' })
      .getByRole('option', { name: /Content/ })
      .first(),
  ).toBeVisible();
  await box3.fill('Sara');
  await expect(
    editorPage.getByRole('listbox', { name: 'Results' }).getByRole('option', { name: /Sara/ }),
  ).toHaveCount(0);
  await editorPage.keyboard.press('Escape');
  editorErrors.expectClean('searching as a content-only editor');
});

test('negatives: no roles.manage → 403 in UI and API; stale stamps → 409; double submit; boundary values', async ({
  as,
}) => {
  const id = runId();
  // A support agent (users.view + support.manage, no roles.manage).
  const agent = await arrangeTestUser(`E2E support agent ${id}`);
  await arrangeRole(`E2E support agents ${id}`, ['users.view', 'support.manage'], [agent.id]);
  // Also a participant: lands in the participant app and opens the admin portal from there.
  const page = await as(agent, landing.participant);
  const errors = watchErrors(page);
  await page.goto('/admin');
  // Wait for the portal (the session is restored from the refresh cookie first), then check its navigation.
  const adminNav = page.getByRole('navigation', { name: 'Admin navigation' });
  await expect(adminNav.getByRole('link', { name: 'Support tickets' })).toBeVisible();
  await expect(adminNav.getByRole('link', { name: 'Roles & permissions' })).toHaveCount(0);
  await page.goto('/admin/roles');
  await expect(page.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
  // On a user's page the custom roles are read-only, and no admin actions are offered.
  await page.goto(`/admin/users/${agent.id}`);
  await expect(page.getByRole('region', { name: 'Custom roles' })).toContainText(`E2E support agents ${id}`);
  await expect(page.getByRole('region', { name: 'Custom roles' }).getByRole('checkbox')).toHaveCount(0);
  for (const name of ['Suspend', 'Change roles', 'Change tier', 'Log in as'])
    await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0);
  errors.expectClean('the support agent');
  const agentApi = await ApiSession.login(agent.email, agent.password);
  expect(await codeOf(agentApi.get('/admin/roles'))).toMatch(/^403\b/);
  expect(
    await codeOf(agentApi.post('/admin/roles', { name: `x ${id}`, permissions: ['users.view'] })),
  ).toMatch(/^403\b/);
  expect(
    await codeOf(
      agentApi.put(`/admin/users/${agent.id}/roles`, { roles: ['Admin'], reason: 'escalate', confirm: true }),
    ),
  ).toMatch(/^403\b/);
  expect(await codeOf(agentApi.post('/admin/test-users', { roles: ['Participant'] }))).toMatch(/^403\b/);
  expect(await statusOf(agentApi.get('/admin/users'))).toBe(200);

  // Stale concurrency stamp on a custom role: the second editor is refused with 409 in the dialog.
  const api = await adminApi();
  const role = await arrangeRole(`E2E stamped ${id}`, ['crm.view']);
  const admin = await as(accounts.admin, landing.admin);
  const adminErrors = watchErrors(admin);
  adminErrors.ignore(/HTTP 409 PUT .*\/api\/v1\/admin\/roles\//);
  adminErrors.ignore(/HTTP 400 POST .*\/api\/v1\/admin\/roles$/);
  await admin.goto('/admin/roles');
  await admin.getByRole('button', { name: `Edit ${role.name}` }).click();
  const editor = modal(admin, `Edit ${role.name}`);
  await api.put(`/admin/roles/${role.id}`, {
    name: role.name,
    description: 'changed elsewhere',
    permissions: ['crm.view'],
    concurrencyStamp: role.concurrencyStamp,
  });
  await editor.getByLabel('Description').fill('my change');
  await editor.getByRole('button', { name: 'Save role' }).click();
  await expect(editor.getByRole('alert')).toContainText(/changed|someone else|reload|conflict/i);
  expect(
    await codeOf(
      api.put(`/admin/roles/${role.id}`, {
        name: role.name,
        permissions: ['crm.view'],
        concurrencyStamp: role.concurrencyStamp,
      }),
    ),
  ).toMatch(/^409\b/);
  await editor.getByRole('button', { name: 'Cancel' }).click();

  // Double submit: a double-clicked "Create role" makes exactly one role.
  const once = `E2E once ${id}`;
  await admin.getByRole('button', { name: 'New role' }).click();
  const create = modal(admin, 'New role');
  await create.getByLabel('Name').fill(once);
  await create.getByRole('searchbox', { name: 'Search permissions' }).fill('crm.view');
  await create.getByRole('checkbox', { name: /^View CRM crm\.view/ }).check();
  await create.getByRole('button', { name: 'Create role' }).dblclick();
  await expect(toast(admin, 'Role created')).toBeVisible();
  const all = await api.get<{ custom: { name: string }[] }>('/admin/roles');
  expect(all.custom.filter((r) => r.name === once)).toHaveLength(1);
  // …and the second click never reached the API (no 409 "name taken" from a duplicate request).
  adminErrors.expectClean('a double-clicked create');
  adminErrors.ignore(/HTTP 409 POST .*\/api\/v1\/admin\/roles$/); // the duplicate name below, on purpose

  // Boundary values: role names of 1 and 81 characters, a duplicate (case-insensitive) and a built-in name.
  await admin.getByRole('button', { name: 'New role' }).click();
  const bad = modal(admin, 'New role');
  await bad.getByLabel('Name').fill('x');
  await bad.getByRole('searchbox', { name: 'Search permissions' }).fill('crm.view');
  await bad.getByRole('checkbox', { name: /^View CRM crm\.view/ }).check();
  await bad.getByRole('button', { name: 'Create role' }).click();
  await expect(bad.getByText('Enter a name of at least 2 characters.')).toBeVisible();
  await bad.getByLabel('Name').fill(once.toUpperCase());
  await bad.getByRole('button', { name: 'Create role' }).click();
  await expect(bad.getByText(/already|taken|in use/i).first()).toBeVisible();
  await bad.getByLabel('Name').fill('Finance');
  await bad.getByRole('button', { name: 'Create role' }).click();
  await expect(bad.getByText('This name belongs to a built-in role.')).toBeVisible();
  await bad.getByRole('button', { name: 'Cancel' }).click();
  expect(
    await codeOf(api.post('/admin/roles', { name: `${id}-`.padEnd(81, 'y'), permissions: ['crm.view'] })),
  ).toMatch(/^400\b/);
  expect(
    await codeOf(api.post('/admin/roles', { name: `${id}-`.padEnd(80, 'y'), permissions: ['crm.view'] })),
  ).toBeUndefined();
  // Reasons: 2 characters are refused, 500 accepted, 501 refused.
  const target = await arrangeTestUser(`E2E reasons ${id}`);
  expect(await codeOf(api.put(`/admin/users/${target.id}/tier`, { tier: 'Gold', reason: 'ab' }))).toMatch(
    /^400\b/,
  );
  expect(
    await codeOf(api.put(`/admin/users/${target.id}/tier`, { tier: 'Gold', reason: 'r'.repeat(501) })),
  ).toMatch(/^400\b/);
  expect(
    await codeOf(api.put(`/admin/users/${target.id}/tier`, { tier: 'Gold', reason: 'r'.repeat(500) })),
  ).toBeUndefined();
  adminErrors.expectClean('negative paths as the admin');
});
