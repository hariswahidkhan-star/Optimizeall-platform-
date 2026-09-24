import type { Page } from '@playwright/test';
import {
  CLIENT_PASSWORD,
  DUTIES,
  type Duty,
  accounts,
  clientUser,
  errorOf,
  expect,
  fakePng,
  landing,
  login,
  mailLink,
  modal,
  need,
  pdf,
  png,
  raw,
  runId,
  saveState,
  staffNames,
  statusOf,
  test,
  watchErrors,
} from './support/delivery';

/**
 * Onboarding a new client: the account manager creates the client (details, SLA, logo), builds the account team, works
 * the onboarding checklist (tick, edit, add, remove steps), fills the brand kit (with an upload and a rejected fake
 * image), invites one client user per duty (Owner, Approver, Billing, Viewer) who each set a password from the emailed
 * link and sign in to the client portal, and finally activates the client.
 */

const clientName = () => `Lumen Labs ${runId()}`;

async function openClientTab(am: Page, tab: string) {
  await am
    .getByRole('tablist', { name: 'Client sections' })
    .getByRole('tab', { name: tab, exact: true })
    .click();
}

test('the account manager creates the client with its details, SLA and logo', async ({ as }) => {
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am
    .getByRole('navigation', { name: 'Agency navigation' })
    .getByRole('link', { name: 'Clients' })
    .click();
  await expect(am.getByRole('heading', { level: 1, name: 'Clients' })).toBeVisible();
  await am.getByRole('button', { name: 'New client' }).click();
  const dialog = modal(am, 'New client');
  await dialog.getByLabel('Name').fill(clientName());
  await dialog.getByLabel('Industry').fill('Consumer electronics');
  await dialog.getByLabel('Website').fill('https://lumen-labs.example.com');
  await dialog.getByLabel('Country').selectOption('GB');
  await dialog.getByLabel('Time zone').selectOption('Europe/London');
  await dialog.getByLabel('Currency').selectOption('GBP');
  await dialog.getByLabel('Account manager').selectOption({ label: staffNames.am });
  // Double-clicking "Create client" must still create exactly one client.
  await dialog.getByRole('button', { name: 'Create client' }).dblclick();
  await expect(am).toHaveURL(/\/agency\/clients\/[0-9a-f-]{36}/);
  await expect(am.getByRole('heading', { level: 1, name: clientName() })).toBeVisible();
  const clientId = am.url().split('/').pop()!.split('?')[0]!;
  saveState({ client: { id: clientId, name: clientName() } });

  const api = await login(accounts.am);
  const list = await api.get<{ items: { id: string }[] }>(
    `/agency/clients?search=${encodeURIComponent(clientName())}`,
  );
  expect(
    list.items.map((c) => c.id),
    'one client per double click',
  ).toEqual([clientId]);

  // A new client starts Onboarding, in its own currency and time zone, with the default 3-day SLA.
  await expect(am.getByText('Onboarding', { exact: true }).first()).toBeVisible();
  const profile = am.getByRole('region', { name: 'Profile' });
  await expect(profile).toContainText('GB · Europe/London');
  await expect(profile).toContainText('GBP');
  await expect(profile).toContainText('3 business day(s)');
  await expect(profile).toContainText(staffNames.am);

  // Edit: feedback SLA 5 days, billing contact and a logo.
  await am.getByRole('button', { name: 'Edit', exact: true }).click();
  const edit = modal(am, 'Edit client');
  await edit.getByLabel('Client feedback SLA (business days)').fill('5');
  await edit.getByLabel('Billing contact').fill('Lena Park');
  await edit.getByLabel('Billing email').fill(`billing.${runId()}@lumen.e2e.optimizeall.test`);
  await edit.getByLabel('Logo').setInputFiles(png(11, 'lumen-logo.png'));
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit).toBeHidden();
  await expect(profile).toContainText('5 business day(s)');
  await expect(profile).toContainText('Lena Park');

  // Boundaries of the SLA (1–30 business days) and auto-approve (1–60) are enforced by the API too.
  const detail = await api.get<Record<string, unknown> & { concurrencyStamp: string }>(
    `/agency/clients/${clientId}`,
  );
  const base = {
    name: clientName(),
    countryCode: 'GB',
    timeZone: 'Europe/London',
    currency: 'GBP',
    approvalSlaDays: 5,
    autoApproveAfterDays: null,
    concurrencyStamp: detail.concurrencyStamp,
  };
  expect(
    (await errorOf(api.put(`/agency/clients/${clientId}`, { ...base, approvalSlaDays: 0 }))).status,
  ).toBe(400);
  expect(
    (await errorOf(api.put(`/agency/clients/${clientId}`, { ...base, approvalSlaDays: 31 }))).status,
  ).toBe(400);
  expect(
    (await errorOf(api.put(`/agency/clients/${clientId}`, { ...base, autoApproveAfterDays: 61 }))).status,
  ).toBe(400);
  // A stale concurrency stamp (someone else saved in between) is a conflict, not a silent overwrite.
  await api.put(`/agency/clients/${clientId}`, { ...base, summary: 'Smart lighting for homes.' });
  expect(
    (await errorOf(api.put(`/agency/clients/${clientId}`, { ...base, summary: 'Overwritten?' }))).status,
  ).toBe(409);
  errors.expectClean('creating and editing the client');
});

test('the account manager builds the account team and works the onboarding checklist', async ({ as }) => {
  const { id: clientId } = need('client');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/clients/${clientId}?tab=team`);
  const addForm = am.getByRole('form', { name: 'Add to account team' });
  for (const [person, role] of [
    [staffNames.strategist, 'Strategist'],
    [staffNames.designer, 'Design'],
    [staffNames.content, 'Content'],
  ] as const) {
    await addForm.getByLabel('Staff member').selectOption({ label: person });
    await addForm.getByLabel('Service role').selectOption(role);
    await addForm.getByRole('button', { name: 'Add' }).click();
    await expect(am.getByRole('list', { name: 'Account team' }).getByText(person)).toBeVisible();
  }
  // Remove the content writer again.
  await am.getByRole('button', { name: new RegExp(`^Remove ${staffNames.content} as `) }).click();
  await expect(am.getByRole('list', { name: 'Account team' }).getByText(staffNames.content)).toHaveCount(0);

  // ---------------------------------------------------------------- onboarding checklist
  await openClientTab(am, 'Onboarding');
  const checklist = am.getByRole('list', { name: 'Onboarding checklist' });
  await expect(checklist.getByRole('listitem').first()).toBeVisible();
  const items = await checklist.getByRole('listitem').count();
  expect(items, 'a new client starts with the standard checklist').toBeGreaterThanOrEqual(3);
  const progress = am.getByRole('progressbar', { name: 'Onboarding complete' });
  await expect(progress).toHaveAttribute('aria-valuetext', `0 of ${items} steps`);

  // Tick the first step. It is the client's (GA4 access), so ticking it is confirmed as done on the client's behalf.
  const firstTitle = (
    await checklist.getByRole('listitem').first().locator('.dl-list__title').innerText()
  ).trim();
  await expect(checklist.getByRole('listitem').first()).toContainText('Client action');
  await am.getByLabel(`Status of ${firstTitle}`).selectOption('Done');
  await modal(am, /done on the client’s behalf\?$/)
    .getByRole('button', { name: 'Mark done for the client' })
    .click();
  await expect(progress).toHaveAttribute('aria-valuetext', `1 of ${items} steps`);
  await expect(checklist.getByRole('listitem').first()).toContainText(
    `by ${staffNames.am} on behalf of the client`,
  );

  // Edit the second step: new title, description, and hand it to the client.
  const secondTitle = (
    await checklist.getByRole('listitem').nth(1).locator('.dl-list__title').innerText()
  ).trim();
  const edited = `Grant GA4 and Search Console access (${runId()})`;
  await am.getByRole('button', { name: `Edit ${secondTitle}` }).click();
  const editDialog = modal(am, 'Edit onboarding step');
  await editDialog.getByLabel('Title').fill(edited);
  await editDialog.getByLabel('Description').fill('Add analytics@agency as an Editor.');
  await editDialog.getByLabel('Who completes it').selectOption('Client');
  await editDialog.getByRole('button', { name: 'Save' }).click();
  await expect(editDialog).toBeHidden();
  const editedItem = checklist.getByRole('listitem').filter({ hasText: edited });
  await expect(editedItem).toContainText('Client action');
  await expect(editedItem).toContainText('Add analytics@agency as an Editor.');

  // Add a client-owned step, then remove another step (with confirmation).
  const added = `Share the Meta Business Manager (${runId()})`;
  await am.getByLabel('Add a step').fill(added);
  await am.getByLabel('Who completes it').selectOption('Client');
  await am.getByRole('button', { name: 'Add step' }).click();
  await expect(checklist.getByRole('listitem').filter({ hasText: added })).toContainText('Client action');
  const lastTitle = (
    await checklist
      .getByRole('listitem')
      .nth(items - 1)
      .locator('.dl-list__title')
      .innerText()
  ).trim();
  await am.getByRole('button', { name: `Remove ${lastTitle}` }).click();
  await modal(am, /from this client’s checklist\?$/)
    .getByRole('button', { name: 'Remove step' })
    .click();
  await expect(checklist.getByRole('listitem').filter({ hasText: lastTitle })).toHaveCount(0);
  await expect(progress).toHaveAttribute('aria-valuetext', `1 of ${items} steps`);

  // Kept after a reload.
  await am.reload();
  await expect(checklist.getByRole('listitem').filter({ hasText: edited })).toBeVisible();
  await expect(checklist.getByRole('listitem').filter({ hasText: added })).toBeVisible();
  // A blank title is refused by the API (the dialog disables Save too).
  const api = await login(accounts.am);
  const onboarding = await api.get<{ items: { id: string; title: string }[] }>(
    `/agency/clients/${clientId}/onboarding`,
  );
  const target = onboarding.items.find((i) => i.title === added)!;
  expect(
    await statusOf(
      api.put(`/agency/clients/${clientId}/onboarding/${target.id}/details`, {
        title: ' ',
        category: 'Access',
        owner: 'Client',
      }),
    ),
  ).toBe(400);
  errors.expectClean('the account team and onboarding checklist');
});

test('the brand kit takes colours, voice and files, and rejects a fake image', async ({ as }) => {
  const { id: clientId } = need('client');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/clients/${clientId}?tab=brand`);
  await am.getByRole('button', { name: 'Edit brand kit' }).click();
  const form = am.getByRole('form', { name: 'Edit brand kit' });
  await form.getByLabel('Colours').fill('Lumen Yellow: #FFD23F\nNight: #1B1B3A');
  await form.getByLabel('Fonts').fill('Inter\nSpace Grotesk');
  await form.getByLabel('Tone of voice').fill('Warm, precise, a little playful.');
  await form.getByLabel('Audience personas').fill('Smart-home Sam: early adopter who automates everything');
  await form.getByLabel('Do', { exact: true }).fill('Show real homes');
  await form.getByLabel("Don't").fill('Use stock photos of offices');
  await form.getByLabel('Competitors').fill('Glowline');
  await form.getByRole('button', { name: 'Save brand kit' }).click();
  await expect(form).toBeHidden();
  await expect(am.getByRole('list', { name: 'Brand colours' })).toContainText('#FFD23F');
  await expect(am.getByRole('region', { name: 'Tone of voice' })).toContainText('Warm, precise');

  const upload = am.getByRole('form', { name: 'Upload brand asset' });
  // A text file named .png: the content is checked, not the extension.
  errors.ignore(/HTTP 400 POST \S+\/brand-kit\/assets$/);
  await upload.getByLabel('Upload an asset').setInputFiles(fakePng('lumen-logo-fake.png'));
  await upload.getByRole('button', { name: 'Upload', exact: true }).click();
  await expect(upload.getByRole('alert')).toContainText('Upload a PNG, JPEG, WebP, PDF or MP4 file');
  await expect(am.getByRole('list', { name: 'Brand assets' })).toHaveCount(0);

  await upload.getByLabel('Upload an asset').setInputFiles(png(12, 'lumen-logo.png'));
  await upload.getByRole('button', { name: 'Upload', exact: true }).click();
  const assets = am.getByRole('list', { name: 'Brand assets' });
  await expect(assets.getByRole('listitem')).toHaveCount(1);
  await expect(assets).toContainText('Image');
  await upload.getByLabel('Upload an asset').setInputFiles(pdf('lumen-guidelines.pdf'));
  await upload.getByRole('button', { name: 'Upload', exact: true }).click();
  await expect(assets.getByRole('listitem')).toHaveCount(2);
  await expect(assets).toContainText('Guideline');
  errors.expectClean('editing the brand kit');
});

/** Opens the emailed set-password link, chooses the password and signs in to the client portal. */
async function acceptInvitation(page: Page, email: string, displayName: string) {
  const link = await mailLink(email, '/reset-password');
  await page.goto(`${link.pathname}${link.search}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Choose a new password' })).toBeVisible();
  await page.getByLabel('New password', { exact: true }).fill(CLIENT_PASSWORD);
  await page.getByLabel('Confirm new password').fill(CLIENT_PASSWORD);
  await page
    .getByRole('button', { name: /password/i })
    .last()
    .click();
  await expect(page).toHaveURL(/\/login\?reset=1/);
  await page.getByLabel('Email', { exact: true }).fill(email);
  await page.getByLabel('Password', { exact: true }).fill(CLIENT_PASSWORD);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(landing.client);
  await expect(page.getByRole('heading', { level: 1, name: 'Welcome back' })).toBeVisible();
  await expect(page.getByRole('button', { name: `Account menu for ${displayName}` })).toBeVisible();
}

test('the account manager invites one client user per duty; each sets a password and signs in', async ({
  as,
  anonymous,
}) => {
  const { id: clientId, name } = need('client');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/clients/${clientId}?tab=users`);
  await expect(am.getByText('No client users yet')).toBeVisible();

  const users: Partial<Record<Duty, { id: string; email: string; password: string; displayName: string }>> =
    {};
  for (const duty of DUTIES) {
    const email = `${duty.toLowerCase()}.${runId()}@lumen.e2e.optimizeall.test`;
    const displayName = `Lumen ${duty} ${runId()}`;
    await am.getByRole('button', { name: 'Invite client user' }).click();
    const dialog = modal(am, 'Invite a client user');
    await dialog.getByLabel('Email').fill(email);
    await dialog.getByLabel('Name').fill(displayName);
    await dialog.getByLabel('Duty').selectOption(duty);
    await dialog.getByRole('button', { name: 'Send invitation' }).click();
    await expect(dialog).toBeHidden();
    const row = am.getByRole('row').filter({ hasText: email });
    await expect(row).toContainText('Invitation pending');
    await expect(row.getByLabel(`Duty of ${displayName}`)).toHaveValue(duty);
    users[duty] = { id: '', email, password: CLIENT_PASSWORD, displayName };
  }

  // Inviting the same person twice is refused with a clear message.
  errors.ignore(/HTTP 409 POST \S+\/members$/);
  await am.getByRole('button', { name: 'Invite client user' }).click();
  const again = modal(am, 'Invite a client user');
  await again.getByLabel('Email').fill(users.Viewer!.email.toUpperCase());
  await again.getByLabel('Name').fill('Duplicate');
  await again.getByRole('button', { name: 'Send invitation' }).click();
  await expect(again.getByRole('alert')).toContainText('already a member');
  await again.getByRole('button', { name: 'Cancel' }).click();
  // Staff accounts can't become client users.
  const api = await login(accounts.am);
  expect(
    (
      await errorOf(
        api.post(`/agency/clients/${clientId}/members`, {
          email: accounts.strategist.email,
          displayName: 'Staff',
          role: 'Viewer',
        }),
      )
    ).code,
  ).toBe('client.invite_staff_account');

  // Each invited person sets a password from the email and signs in to the client portal.
  for (const duty of DUTIES) {
    const user = users[duty]!;
    const page = await anonymous();
    const clientErrors = watchErrors(page);
    await acceptInvitation(page, user.email, user.displayName);
    await expect(page.getByRole('main').getByText(name).first()).toBeVisible();
    // One organization: no switcher.
    await expect(page.getByLabel('Organization')).toHaveCount(0);
    clientErrors.expectClean(`the ${duty}'s first sign-in`);
  }
  const members = await api.get<{ userId: string; email: string; role: Duty; hasSignedIn: boolean }[]>(
    `/agency/clients/${clientId}/members`,
  );
  for (const duty of DUTIES) {
    const member = members.find((m) => m.email === users[duty]!.email)!;
    expect(member.role).toBe(duty);
    expect(member.hasSignedIn).toBe(true);
    users[duty]!.id = member.userId;
  }
  saveState({
    users: users as Record<Duty, { id: string; email: string; password: string; displayName: string }>,
  });
  await am.reload();
  await expect(am.getByText('Invitation pending')).toHaveCount(0);
  errors.expectClean('inviting the client users');
});

test('the Owner sees the account team and manages colleagues; other duties cannot', async ({ as }) => {
  const { id: clientId } = need('client');
  const owner = await as(clientUser('Owner'), landing.client);
  const errors = watchErrors(owner);
  await owner.getByRole('navigation').getByRole('link', { name: 'Team' }).first().click();
  const team = owner.getByRole('list', { name: 'Account team' });
  await expect(team.getByText(staffNames.am)).toBeVisible();
  await expect(team.getByText('Account manager')).toBeVisible();
  await expect(team.getByText(staffNames.strategist)).toBeVisible();
  await expect(team.getByText(staffNames.designer)).toBeVisible();
  await expect(team.getByText(staffNames.content)).toHaveCount(0);
  const members = owner.getByRole('table', { name: 'Organization members' });
  for (const duty of DUTIES) await expect(members).toContainText(clientUser(duty).displayName);

  // The Owner is the only duty that invites colleagues (UI and API).
  await expect(owner.getByRole('button', { name: 'Invite' })).toBeVisible();
  for (const duty of ['Approver', 'Billing', 'Viewer'] as const) {
    const api = await login(clientUser(duty));
    expect(
      await statusOf(
        api.post(`/client/orgs/${clientId}/members`, {
          email: `x.${duty}.${runId()}@e2e.optimizeall.test`,
          displayName: 'Xavier Test',
          role: 'Viewer',
        }),
      ),
      `${duty} invites a colleague`,
    ).toBe(403);
  }
  const viewer = await as(clientUser('Viewer'), landing.client);
  await viewer.goto('/client/team');
  await expect(viewer.getByRole('table', { name: 'Organization members' })).toBeVisible();
  await expect(viewer.getByRole('button', { name: 'Invite' })).toHaveCount(0);

  // The last Owner can't demote themselves (every organization keeps an Owner).
  const ownerApi = await login(clientUser('Owner'));
  expect(
    (
      await raw(ownerApi, 'PUT', `/client/orgs/${clientId}/members/${clientUser('Owner').id}`, {
        json: { role: 'Viewer' },
      })
    ).status,
  ).toBe(409);

  // Brand kit: the Owner uploads an asset in the portal; the Viewer only reads.
  await owner.goto('/client/brand');
  await expect(owner.getByRole('list', { name: 'Brand colours' })).toContainText('#FFD23F');
  const uploadCard = owner.getByRole('region', { name: 'Upload brand assets' });
  await uploadCard.getByLabel('File').setInputFiles(png(13, 'lumen-storefront.png'));
  await uploadCard.getByLabel('Label').fill('Storefront photo');
  await uploadCard.getByRole('button', { name: 'Upload', exact: true }).click();
  await expect(owner.getByRole('list', { name: 'Brand assets' })).toContainText('Storefront photo');
  await viewer.goto('/client/brand');
  await expect(viewer.getByRole('list', { name: 'Brand assets' })).toContainText('Storefront photo');
  await expect(viewer.getByRole('region', { name: 'Upload brand assets' })).toHaveCount(0);
  const viewerApi = await login(clientUser('Viewer'));
  const form = new FormData();
  form.append('file', new Blob([png(14).buffer], { type: 'image/png' }), 'viewer.png');
  form.append('kind', 'Logo');
  expect((await raw(viewerApi, 'POST', `/client/orgs/${clientId}/brand-kit/assets`, { form })).status).toBe(
    403,
  );

  // Home: onboarding progress with the steps waiting on the client.
  await owner.goto('/client');
  const onboarding = owner.getByRole('region', { name: 'Onboarding' });
  await expect(onboarding.getByText('Steps waiting on you:')).toBeVisible();
  await expect(onboarding).toContainText(`Share the Meta Business Manager (${runId()})`);
  errors.expectClean('the Owner’s team and brand pages');
});

test('the account manager activates the client once onboarding is done', async ({ as }) => {
  const { id: clientId } = need('client');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/clients/${clientId}`);
  await am.getByRole('button', { name: 'Change status' }).click();
  const dialog = modal(am, 'Change client status');
  await dialog.getByLabel('Status').selectOption('Active');
  await dialog.getByRole('button', { name: 'Save status' }).click();
  await expect(dialog).toBeHidden();
  await expect(am.getByText('Active', { exact: true }).first()).toBeVisible();
  // Pausing needs a reason (UI disables Save; the API refuses too).
  await am.getByRole('button', { name: 'Change status' }).click();
  await dialog.getByLabel('Status').selectOption('Paused');
  await expect(dialog.getByRole('button', { name: 'Save status' })).toBeDisabled();
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  const api = await login(accounts.am);
  const detail = await api.get<{ concurrencyStamp: string; status: string }>(`/agency/clients/${clientId}`);
  expect(detail.status).toBe('Active');
  expect(
    await statusOf(
      api.post(`/agency/clients/${clientId}/status`, {
        status: 'Paused',
        reason: null,
        concurrencyStamp: detail.concurrencyStamp,
      }),
    ),
  ).toBe(400);
  errors.expectClean('activating the client');
});
