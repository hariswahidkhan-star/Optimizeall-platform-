import {
  type PostDto,
  accounts,
  api,
  expect,
  landing,
  modal,
  need,
  profile,
  runId,
  state,
  test,
  watchErrors,
} from './support/social';

/**
 * Interrupted workflows: a session that expires while composing (nothing half-saved, back to the page after signing in),
 * decisions taken on a post that changed underneath (stale internal approval, stale client approval), and "log in as"
 * a social media manager: the banner is shown and connection changes are refused in the UI.
 */

async function draft(title: string, text = 'A post that changes underneath.') {
  const social = await api(accounts.socialManager);
  return social.post<PostDto>('/agency/social/posts', {
    clientAccountId: need('client').id,
    title,
    variants: [
      { profileId: profile('facebook').id, text, mediaIds: [], altTexts: [], hashtags: [], mentions: [] },
    ],
  });
}

test('a session that expires while composing: sign in again, back on the composer, nothing half-saved', async ({
  as,
}) => {
  const title = `Expired session ${runId()}`;
  const page = await as(accounts.socialManager, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/social/compose?client=${need('client').id}`);
  await page.getByRole('region', { name: 'Post' }).getByLabel('Internal title').fill(title);
  await page
    .getByRole('region', { name: 'Post' })
    .getByRole('checkbox', { name: `Facebook · @${profile('facebook').handle}` })
    .check();
  await page
    .getByRole('region', { name: 'Content per network' })
    .getByLabel('Facebook text')
    .fill('Written before the session expired.');
  // Let the live validation settle first, so the save is the request that meets the expired session.
  await expect(page.getByRole('status').filter({ hasText: 'Ready for review' })).toBeVisible();

  await page.context().clearCookies();
  await page.route('**/api/v1/agency/**', (route) =>
    route.fulfill({ status: 401, contentType: 'application/json', body: '{"title":"Unauthorized"}' }),
  );
  errors.ignore(/HTTP 401 /);
  errors.ignore(/console: .*401/);
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByText(/session has expired/i)).toBeVisible();
  await page.unroute('**/api/v1/agency/**');
  await page.getByLabel('Email', { exact: true }).fill(accounts.socialManager.email);
  await page.getByLabel('Password', { exact: true }).fill(accounts.socialManager.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/agency\/social\/compose/);
  await expect(page.getByRole('heading', { level: 1, name: 'New post' })).toBeVisible();
  const social = await api(accounts.socialManager);
  const found = await social.get<{ total: number }>(
    `/agency/social/posts?search=${encodeURIComponent(title)}`,
  );
  expect(found.total, 'the interrupted save created nothing').toBe(0);
  errors.expectClean('the expired session');
});

test('an internal approval of a post that was edited meanwhile is refused (409) with a clear message', async ({
  as,
}) => {
  const created = await draft(`Race internal ${runId()}`);
  const social = await api(accounts.socialManager);
  await social.post(`/agency/social/posts/${created.id}/submit`, {});

  const page = await as(accounts.socialManager, landing.agency);
  const errors = watchErrors(page);
  errors.ignore(/HTTP 409 POST \S+\/approve$/);
  await page.goto(`/agency/social/posts/${created.id}`);
  await expect(page.getByText('Internal review', { exact: true }).first()).toBeVisible();

  // Meanwhile the content creator edits it (back to Draft).
  const content = await api(accounts.content);
  const current = await content.get<PostDto & { variants: { profileId: string }[] }>(
    `/agency/social/posts/${created.id}`,
  );
  await content.put(`/agency/social/posts/${created.id}`, {
    clientAccountId: need('client').id,
    title: created.title,
    variants: [{ profileId: current.variants[0]!.profileId, text: 'Changed while it was being reviewed.' }],
    concurrencyStamp: current.concurrencyStamp,
  });

  await page.getByRole('button', { name: 'Approve', exact: true }).click();
  const dialog = modal(page, 'Approve');
  await dialog.getByRole('button', { name: 'Approve' }).click();
  await expect(dialog.getByRole('alert')).toContainText(/changed by someone else|cannot be moved/);
  const after = await social.get<PostDto>(`/agency/social/posts/${created.id}`);
  expect(after.status).toBe('Draft');
  errors.expectClean('a stale internal approval');
});

test('a client approval of a post the agency pulled back meanwhile is refused in the dialog', async ({
  as,
}) => {
  const created = await draft(`Race client ${runId()}`);
  const social = await api(accounts.socialManager);
  await social.post(`/agency/social/posts/${created.id}/submit`, {});
  await social.post(`/agency/social/posts/${created.id}/approve`, {});

  const page = await as(state().approver, landing.client);
  const errors = watchErrors(page);
  // The post went back to Draft, which clients never see: the decision is refused (404) and the dialog says so.
  errors.ignore(/HTTP 404 POST \S+\/client\/social\/posts\/\S+\/approve$/);
  await page.goto('/client/social/approvals');
  await page.getByRole('article', { name: created.title }).getByRole('button', { name: 'Review' }).click();
  const review = modal(page, created.title);
  await expect(review.getByText('A post that changes underneath.').first()).toBeVisible();

  // The agency requests changes itself while the client is reading.
  await social.post(`/agency/social/posts/${created.id}/request-changes`, {
    comment: 'Pulling this back: wrong date.',
  });

  await review.getByRole('button', { name: 'Approve' }).click();
  await expect(review.getByRole('alert')).toContainText(/not found/i);
  const after = await social.get<PostDto>(`/agency/social/posts/${created.id}`);
  expect(after.status).toBe('Draft');
  expect(after.comments.filter((c) => c.isClient && c.kind === 'Approved')).toHaveLength(0);
  errors.expectClean('a stale client approval');
});

test('log in as the social media manager: the banner is shown and connection changes are refused', async ({
  as,
}) => {
  const fb = profile('facebook');
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/users');
  await admin.getByRole('searchbox', { name: 'Search users' }).fill(accounts.socialManager.email);
  await admin.getByRole('button', { name: `Log in as ${accounts.socialManager.displayName}` }).click();
  const confirm = modal(admin, `Log in as ${accounts.socialManager.displayName}?`);
  await confirm
    .getByLabel(/Why do you need to view this account/)
    .fill('E2E: check social connection guard in the UI');
  await confirm
    .getByLabel(`Type ${accounts.socialManager.email} to confirm`)
    .fill(accounts.socialManager.email);
  await confirm.getByRole('button', { name: 'Log in as user' }).click();
  await expect(admin).toHaveURL(landing.agency);
  const banner = admin.getByRole('region', { name: 'Impersonation' });
  await expect(banner).toContainText(`You are viewing as ${accounts.socialManager.displayName}`);

  errors.ignore(/HTTP 403 POST \S+\/disconnect$/);
  await admin.goto(`/agency/social/profiles?client=${need('client').id}`);
  await admin.getByRole('button', { name: `Actions for Facebook @${fb.handle}` }).click();
  await admin.getByRole('menuitem', { name: 'Disconnect' }).click();
  await expect(
    admin
      .getByRole('region', { name: 'Notifications' })
      .getByText(/not available while you are viewing as another user/),
  ).toBeVisible();
  await expect(
    admin
      .getByRole('row', { name: new RegExp(`Facebook.*${fb.handle}`) })
      .getByText('Connected', { exact: true }),
  ).toBeVisible();
  await banner.getByRole('button', { name: 'Exit' }).click();
  await expect(admin).toHaveURL(/\/admin\/users/);
  errors.expectClean('impersonation');
});
