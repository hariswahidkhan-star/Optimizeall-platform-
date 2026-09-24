import {
  ApiSession,
  accounts,
  api,
  errorOf,
  expect,
  landing,
  modal,
  need,
  raw,
  runId,
  saveState,
  state,
  statusOf,
  test,
  toast,
  watchErrors,
} from './support/social';

/**
 * Brand profiles and their connections for the journey's client: the social media manager adds the Facebook Page,
 * LinkedIn page and X account; OAuth needs developer app credentials the installation does not have (clear state and
 * 409), so the admin pastes a Page token instead; expired and disconnected tokens; forged OAuth callbacks; and who may
 * do what (content creator without social.publish, staff without social permissions, impersonation, other tenants).
 */

interface Profile {
  id: string;
  network: string;
  handle: string;
  connectionState: string;
  connectionStatus: string;
  statusMessage: string | null;
  externalId: string | null;
  tokenExpiresAt: string | null;
  concurrencyStamp: string;
}

const profilesUrl = () => `/agency/social/profiles?client=${need('client').id}`;

test('the social media manager adds the client’s brand profiles; bad input is refused with a clear message', async ({
  as,
}) => {
  const { name } = need('client');
  const page = await as(accounts.socialManager, landing.agency);
  const errors = watchErrors(page);
  await page.goto(profilesUrl());
  await expect(page.getByRole('heading', { level: 1, name: 'Profiles & connections' })).toBeVisible();
  await expect(page.getByLabel('Client', { exact: true })).toHaveValue(need('client').id);
  await expect(page.getByRole('switch', { name: 'Require client approval' })).toHaveAttribute(
    'aria-checked',
    'true',
  );
  await expect(page.getByText('No profiles yet')).toBeVisible();

  const add = async (
    network: string,
    handle: string,
    displayName: string,
    extra: { url?: string; id?: string } = {},
  ) => {
    await page.getByRole('button', { name: 'Add profile' }).click();
    const dialog = modal(page, 'Add brand profile');
    await dialog.getByLabel('Network').selectOption({ label: network });
    await dialog.getByLabel('Handle').fill(handle);
    await dialog.getByLabel('Display name').fill(displayName);
    if (extra.url) await dialog.getByLabel('Profile URL').fill(extra.url);
    if (extra.id) await dialog.getByLabel('Platform id').fill(extra.id);
    await dialog.getByRole('button', { name: 'Add profile' }).click();
    return dialog;
  };

  const id = runId();
  await expect(
    await add('Facebook', `helio.coffee.${id}`, `${name}`, {
      url: 'https://facebook.com/helio',
      id: `9${Date.now()}`,
    }),
  ).toBeHidden();
  await expect(await add('LinkedIn', `helio-coffee-${id}`, `${name} (LinkedIn)`)).toBeHidden();
  await expect(await add('X', `@HelioCoffee_${id}`, `${name} (X)`)).toBeHidden();

  // The same handle twice (case and "@" do not matter) → 409 with the reason in the dialog.
  errors.ignore(/HTTP (409|400) POST \S+\/profiles$/);
  const again = await add('X', `helioCOFFEE_${id}`, 'Duplicate');
  await expect(again.getByRole('alert')).toContainText('already has that profile');
  await again.getByRole('button', { name: 'Cancel' }).click();
  // Profile URLs must be https.
  const insecure = await add('Instagram', `helio.ig.${id}`, 'Instagram', {
    url: 'http://instagram.com/helio',
  });
  await expect(insecure.getByRole('alert')).toContainText('https');
  await insecure.getByRole('button', { name: 'Cancel' }).click();

  const table = page.getByRole('table', { name: /profiles/i });
  for (const network of ['Facebook', 'LinkedIn', 'X'])
    await expect(table.getByRole('row', { name: new RegExp(network) }).first()).toBeVisible();
  // No developer app credentials are configured: the state says so and OAuth cannot start.
  await expect(page.getByText('Some networks have no developer app configured.')).toBeVisible();
  await expect(table.getByText('App credentials required').first()).toBeVisible();
  // LinkedIn has no publishing adapter: publish manually.
  await expect(table.getByRole('row', { name: /LinkedIn/ })).toContainText('No publishing adapter');

  const social = await api(accounts.socialManager);
  const profiles = await social.get<Profile[]>(`/agency/social/clients/${need('client').id}/profiles`);
  expect(profiles.map((p) => p.network).sort()).toEqual(['Facebook', 'LinkedIn', 'X']);
  const byNetwork = (n: string) => profiles.find((p) => p.network === n)!;
  expect(byNetwork('X').handle).toBe(`heliocoffee_${id}`); // normalised
  saveState({
    profiles: {
      facebook: { id: byNetwork('Facebook').id, handle: byNetwork('Facebook').handle },
      linkedin: { id: byNetwork('LinkedIn').id, handle: byNetwork('LinkedIn').handle },
      x: { id: byNetwork('X').id, handle: byNetwork('X').handle },
    },
  });

  const connect = await errorOf(
    social.post(`/agency/social/profiles/${byNetwork('Facebook').id}/connect/start`),
  );
  expect(connect).toEqual({ status: 409, code: 'social.app_credentials_required' });

  // An undefined network value (the JSON converter also accepts numbers) is a validation error, not a stored profile.
  const bogus = await raw(social, 'POST', `/agency/social/clients/${need('client').id}/profiles`, {
    json: { network: 99, handle: `bogus${id}`, displayName: 'Bogus' },
  });
  expect(bogus.status).toBe(400);
  const after = await social.get<Profile[]>(
    `/agency/social/clients/${need('client').id}/profiles?includeArchived=true`,
  );
  expect(after).toHaveLength(3);
  errors.expectClean('adding brand profiles');
});

test('the admin pastes a Page token (manual connection); expired tokens and disconnects are visible', async ({
  as,
}) => {
  const fb = need('profiles').facebook;
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto(profilesUrl());
  const row = admin.getByRole('row', { name: new RegExp(`Facebook.*${fb.handle}`) });
  await admin.getByRole('button', { name: `Actions for Facebook @${fb.handle}` }).click();
  await admin.getByRole('menuitem', { name: 'Paste an access token…' }).click();
  const dialog = modal(admin, 'Paste an access token');
  await expect(dialog.getByRole('button', { name: 'Save token' })).toBeDisabled();
  await dialog.getByLabel('Access token').fill('e2e-token-ok-facebook-page');
  await dialog.getByRole('button', { name: 'Save token' }).click();
  await expect(dialog).toBeHidden();
  await expect(row.getByText('Connected', { exact: true })).toBeVisible();

  // The token is stored encrypted and never returned.
  const adminApi = await api(accounts.admin);
  const listed = await raw(adminApi, 'GET', `/agency/social/clients/${need('client').id}/profiles`);
  expect(JSON.stringify(listed.body)).not.toContain('e2e-token-ok');

  // A token for a Meta profile needs the Page id.
  const x = need('profiles').x;
  const li = need('profiles').linkedin;
  expect(
    await statusOf(adminApi.post(`/agency/social/profiles/${li.id}/token`, { accessToken: 'short' })),
    'tokens shorter than 10 characters are refused',
  ).toBe(400);

  // A token with an expiry is stored with it; the table shows the profile as connected until a publish proves otherwise.
  const withExpiry = await adminApi.post<Profile>(`/agency/social/profiles/${x.id}/token`, {
    accessToken: 'e2e-token-ok-x-account',
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
  });
  expect(withExpiry.tokenExpiresAt).not.toBeNull();
  expect(withExpiry.connectionState).toBe('Connected');
  await admin.reload();
  await expect(
    admin.getByRole('row', { name: new RegExp(`X.*${x.handle}`) }).getByText('Connected', { exact: true }),
  ).toBeVisible();

  // Disconnect (social.publish) → the token is no longer used.
  await admin.getByRole('button', { name: `Actions for X @${x.handle}` }).click();
  await admin.getByRole('menuitem', { name: 'Disconnect' }).click();
  await expect(toast(admin, 'Profile disconnected')).toBeVisible();
  // Without developer app credentials the table's next step for a disconnected profile is the app setup.
  await expect(
    admin.getByRole('row', { name: new RegExp(`X.*${x.handle}`) }).getByText('Connected', { exact: true }),
  ).toHaveCount(0);
  const afterDisconnect = await adminApi.get<Profile[]>(
    `/agency/social/clients/${need('client').id}/profiles`,
  );
  expect(afterDisconnect.find((p) => p.id === x.id)!.connectionStatus).toBe('Disconnected');
  errors.expectClean('connecting profiles');
});

test('a forged or foreign OAuth callback is refused and shown on the callback page', async ({ as }) => {
  const page = await as(accounts.socialManager, landing.agency);
  const errors = watchErrors(page);
  errors.ignore(/HTTP 400 POST \S+\/oauth\/callback$/);
  await page.goto('/agency/social/connect/callback?code=abc123&state=forged.state.value');
  const alert = page.getByRole('alert').filter({ hasText: 'The profile was not connected' });
  await expect(alert).toContainText('The connection request is invalid. Start again.');
  // A provider refusal (error=access_denied) without a valid state is refused the same way.
  await page.goto('/agency/social/connect/callback?error=access_denied&state=');
  await expect(page.getByRole('alert').filter({ hasText: 'The profile was not connected' })).toBeVisible();
  errors.expectClean('the OAuth callback page');
});

test('who may connect: content creator (no social.publish), staff without social access, other tenants, impersonation', async ({
  as,
}) => {
  const { facebook: fb, linkedin: li } = need('profiles');
  const clientId = need('client').id;

  // Content creator: social.manage only — no Connect/Disconnect/Queue entries, and the API refuses them.
  const content = await as(accounts.content, landing.agency);
  const contentErrors = watchErrors(content);
  await content.goto(profilesUrl());
  await content.getByRole('button', { name: `Actions for Facebook @${fb.handle}` }).click();
  await expect(content.getByRole('menuitem', { name: /Connect/ })).toHaveCount(0);
  await expect(content.getByRole('menuitem', { name: 'Disconnect' })).toHaveCount(0);
  await expect(content.getByRole('menuitem', { name: 'Archive profile' })).toBeDisabled();
  await content.keyboard.press('Escape');
  const contentApi = await api(accounts.content);
  expect(await statusOf(contentApi.post(`/agency/social/profiles/${fb.id}/disconnect`))).toBe(403);
  expect(
    await statusOf(
      contentApi.post(`/agency/social/profiles/${fb.id}/token`, { accessToken: 'e2e-token-ok-content' }),
    ),
  ).toBe(403);
  expect(
    await statusOf(
      contentApi.put(`/agency/social/clients/${clientId}/settings`, {
        requireClientApproval: false,
        defaultUtmMedium: 'x',
      }),
    ),
  ).toBe(403);
  contentErrors.expectClean('the profiles page as a content creator');

  // Staff without social permissions: no social navigation, and 403 from the API.
  const designer = await as(accounts.designerStaff, landing.agency);
  await expect(
    designer
      .getByRole('navigation', { name: 'Agency navigation' })
      .getByRole('link', { name: 'Profiles & connections' }),
  ).toHaveCount(0);
  const designerApi = await api(accounts.designerStaff);
  expect(await statusOf(designerApi.get(`/agency/social/clients/${clientId}/profiles`))).toBe(403);
  // The ads specialist has ads.manage but no social access.
  expect(await statusOf((await api(accounts.ads)).get(`/agency/social/clients/${clientId}/profiles`))).toBe(
    403,
  );

  // A client user (another tenant, and even the journey's own client) never reaches the agency API.
  expect(
    await statusOf((await api(accounts.nimbusOwner)).get(`/agency/social/clients/${clientId}/profiles`)),
  ).toBe(403);
  expect(
    await statusOf((await api(state().approver)).get(`/agency/social/clients/${clientId}/profiles`)),
  ).toBe(403);

  // Impersonating the social media manager: reads work, credential changes are refused.
  const admin = await api(accounts.admin);
  const social = await api(accounts.socialManager);
  const as_ = await admin.post<{ accessToken: string }>(`/admin/users/${social.user.id}/impersonate`, {
    reason: 'E2E: check the social connection guard',
    confirm: true,
  });
  const imp = Object.assign(Object.create(ApiSession.prototype) as ApiSession, {
    token: as_.accessToken,
    user: social.user,
  });
  expect(await statusOf(imp.get(`/agency/social/clients/${clientId}/profiles`))).toBe(200);
  for (const call of [
    () => imp.post(`/agency/social/profiles/${fb.id}/disconnect`),
    () => imp.post(`/agency/social/profiles/${li.id}/connect/start`),
    () => imp.post('/agency/social/oauth/callback', { code: 'x', state: 'y' }),
  ]) {
    const e = await errorOf(call());
    expect(e.status).toBe(403);
    expect(e.code).toBe('auth.impersonation_forbidden_action');
  }
  // The Facebook profile is still connected after all of this.
  const profiles = await social.get<Profile[]>(`/agency/social/clients/${clientId}/profiles`);
  expect(profiles.find((p) => p.id === fb.id)!.connectionState).toBe('Connected');
});
