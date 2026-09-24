import {
  type PostDto,
  accounts,
  api,
  errorOf,
  expect,
  landing,
  modal,
  need,
  post,
  state,
  statusOf,
  test,
  toast,
  watchErrors,
} from './support/social';

/**
 * Approvals: the social media manager approves the launch post internally → it goes to the client (client approval
 * required); the client's Viewer can see it but not decide; another organisation cannot even see it; the Approver
 * requests changes (a note is required), the team fixes and resubmits, and the Approver approves (double click = one
 * approval). Internal notes and failure details never reach the client.
 */

test('internal approval by the social media manager sends the post to the client', async ({ as }) => {
  const launch = post('launch');
  const page = await as(accounts.socialManager, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/social/approvals?client=${need('client').id}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Approvals' })).toBeVisible();
  await page
    .getByRole('table', { name: 'Posts awaiting internal review' })
    .getByRole('link', { name: launch.title })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: launch.title })).toBeVisible();
  // An internal note: never shown to the client.
  const discussion = page.getByRole('region', { name: 'Approvals & comments' });
  await discussion
    .getByLabel('Add a comment')
    .fill('Internal: double-check the roast date with the roastery.');
  await expect(
    discussion.getByRole('checkbox', { name: 'Internal note (hidden from the client)' }),
  ).toBeChecked();
  await discussion.getByRole('button', { name: 'Comment' }).click();
  await expect(discussion.getByText('Internal: double-check the roast date')).toBeVisible();

  await page.getByRole('button', { name: 'Approve', exact: true }).click();
  const approve = modal(page, 'Approve');
  await expect(approve.getByText('The client will be asked to approve it in their portal.')).toBeVisible();
  await approve.getByRole('button', { name: 'Approve' }).click();
  await expect(toast(page, 'Approve: done')).toBeVisible();
  await expect(page.getByText('Client approval', { exact: true }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Schedule', exact: true })).toHaveCount(0);

  // Scheduling before the client decided is refused.
  const social = await api(accounts.socialManager);
  const e = await errorOf(
    social.post(`/agency/social/posts/${launch.id}/schedule`, {
      scheduledAt: new Date(Date.now() + 86_400_000).toISOString(),
    }),
  );
  expect(e.status).toBe(409);
  errors.expectClean('internal approval');
});

test('the client Viewer sees the post but cannot decide; another organisation cannot see it at all', async ({
  as,
}) => {
  const launch = post('launch');
  const { viewer } = state();
  const page = await as(viewer, landing.client);
  const errors = watchErrors(page);
  await page.getByRole('navigation').getByRole('link', { name: 'Post approvals' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Approvals' })).toBeVisible();
  await expect(page.getByText(/Your role \(Viewer\) can see posts but not approve them/)).toBeVisible();
  const card = page
    .getByRole('list', { name: 'Posts awaiting approval' })
    .getByRole('article', { name: launch.title });
  await card.getByRole('button', { name: 'Review' }).click();
  const review = modal(page, launch.title);
  await expect(review.getByText(/Autumn roast is here|Our autumn roast/).first()).toBeVisible();
  await expect(review.getByRole('button', { name: 'Approve' })).toHaveCount(0);
  // Internal notes and the internal submit comment are hidden.
  await expect(review.getByText('Internal: double-check the roast date')).toHaveCount(0);
  await review.getByRole('button', { name: 'Close', exact: true }).click();
  errors.expectClean('the viewer’s approvals page');

  const viewerApi = await api(viewer);
  expect((await errorOf(viewerApi.post(`/client/social/posts/${launch.id}/approve`, {}))).code).toBe(
    'client.insufficient_role',
  );
  const seen = await viewerApi.get<PostDto>(`/client/social/posts/${launch.id}`);
  expect(seen.comments.some((c) => c.isInternal)).toBe(false);
  expect(seen.comments.map((c) => c.body).join(' ')).not.toContain('double-check the roast date');

  // Tenancy: Nimbus' owner gets 404 for Helio's post, calendar, approvals and performance.
  const nimbus = await api(accounts.nimbusOwner);
  const clientId = need('client').id;
  expect(await statusOf(nimbus.get(`/client/social/posts/${launch.id}`))).toBe(404);
  expect(await statusOf(nimbus.post(`/client/social/posts/${launch.id}/approve`, {}))).toBe(404);
  expect(await statusOf(nimbus.post(`/client/social/posts/${launch.id}/comments`, { body: 'hi' }))).toBe(404);
  expect(await statusOf(nimbus.get(`/client/social/approvals?clientId=${clientId}`))).toBe(404);
  expect(await statusOf(nimbus.get(`/client/social/performance?clientId=${clientId}`))).toBe(404);
  const now = new Date();
  expect(
    await statusOf(
      nimbus.get(
        `/client/social/calendar?clientId=${clientId}&from=${now.toISOString()}&to=${new Date(now.getTime() + 86_400_000).toISOString()}`,
      ),
    ),
  ).toBe(404);
  // …and only its own organisation is listed.
  const orgs = await nimbus.get<{ id: string }[]>('/client/social/organizations');
  expect(orgs.map((o) => o.id)).not.toContain(clientId);
});

test('the client Approver requests changes (a note is required); the team fixes it; the Approver approves once', async ({
  as,
}) => {
  const launch = post('launch');
  const { approver } = state();
  const page = await as(approver, landing.client);
  const errors = watchErrors(page);
  await page.goto('/client/social/approvals');
  const card = page.getByRole('article', { name: launch.title });
  await card.getByRole('button', { name: 'Review' }).click();
  let review = modal(page, launch.title);
  await expect(review.getByRole('button', { name: 'Request changes' })).toBeDisabled();
  expect(
    (await errorOf((await api(approver)).post(`/client/social/posts/${launch.id}/request-changes`, {}))).code,
  ).toBe('social.comment_required');
  await review
    .getByRole('textbox', { name: 'Comment' })
    .fill('Please mention the 250 g bag and the price (€12.90).');
  await review.getByRole('button', { name: 'Request changes' }).click();
  await expect(toast(page, 'Changes requested')).toBeVisible();
  await expect(page.getByRole('article', { name: launch.title })).toHaveCount(0);

  // The team sees the client's note, edits, resubmits and approves internally.
  const social = await api(accounts.socialManager);
  let dto = await social.get<PostDto>(`/agency/social/posts/${launch.id}`);
  expect(dto.status).toBe('Draft');
  expect(
    dto.comments.some((c) => c.isClient && c.kind === 'ChangesRequested' && c.body.includes('250 g')),
  ).toBe(true);
  const full = await social.get<Record<string, unknown> & { variants: Record<string, unknown>[] }>(
    `/agency/social/posts/${launch.id}`,
  );
  await social.put(`/agency/social/posts/${launch.id}`, {
    clientAccountId: need('client').id,
    title: launch.title,
    scheduledAt: full.scheduledAt,
    variants: full.variants.map((v) => ({
      profileId: v.profileId,
      text:
        v.network === 'X'
          ? 'Autumn roast is here: 250 g for €12.90. ☕'
          : `${String(v.text).slice(0, 200)} 250 g bag, €12.90.`,
      title: v.title,
      mediaIds: v.mediaIds,
      altTexts: v.altTexts,
      link: v.link,
      firstComment: v.firstComment,
      hashtags: v.hashtags,
      mentions: v.mentions,
    })),
    concurrencyStamp: full.concurrencyStamp,
  });
  await social.post(`/agency/social/posts/${launch.id}/submit`, {});
  await social.post(`/agency/social/posts/${launch.id}/approve`, {});

  // The Approver approves; a double click is one approval.
  await page.reload();
  await page.getByRole('article', { name: launch.title }).getByRole('button', { name: 'Review' }).click();
  review = modal(page, launch.title);
  await expect(review.getByText(/250 g/).first()).toBeVisible();
  await review.getByRole('button', { name: 'Approve' }).dblclick();
  await expect(toast(page, 'Post approved')).toBeVisible();
  await expect(page.getByText("You're all caught up")).toBeVisible();
  dto = await social.get<PostDto>(`/agency/social/posts/${launch.id}`);
  expect(dto.status).toBe('Approved');
  expect(dto.comments.filter((c) => c.isClient && c.kind === 'Approved')).toHaveLength(1);
  expect(dto.allowedActions).toEqual(expect.arrayContaining(['schedule', 'queue', 'markPublished']));
  // Approving again is a 409, not a second approval.
  expect(
    (await errorOf((await api(approver)).post(`/client/social/posts/${launch.id}/approve`, {}))).status,
  ).toBe(409);
  errors.expectClean('the approver’s decisions');
});

test('the client Approver’s "Request changes" sends one request on a double click', async ({ as }) => {
  const social = await api(accounts.socialManager);
  const fb = need('profiles').facebook;
  const created = await social.post<PostDto>('/agency/social/posts', {
    clientAccountId: need('client').id,
    title: `Double request ${state().runId}`,
    variants: [
      {
        profileId: fb.id,
        text: 'Cold brew season is over.',
        mediaIds: [],
        altTexts: [],
        hashtags: [],
        mentions: [],
      },
    ],
  });
  await social.post(`/agency/social/posts/${created.id}/submit`, {});
  await social.post(`/agency/social/posts/${created.id}/approve`, {});

  const page = await as(state().approver, landing.client);
  const errors = watchErrors(page);
  await page.goto('/client/social/approvals');
  await page.getByRole('article', { name: created.title }).getByRole('button', { name: 'Review' }).click();
  const review = modal(page, created.title);
  await review.getByRole('textbox', { name: 'Comment' }).fill('Not this week, please.');
  await review.getByRole('button', { name: 'Request changes' }).dblclick();
  await expect(toast(page, 'Changes requested')).toBeVisible();
  const dto = await social.get<PostDto>(`/agency/social/posts/${created.id}`);
  expect(dto.status).toBe('Draft');
  expect(
    dto.comments.filter((c) => c.isClient && c.kind === 'ChangesRequested'),
    'one request per double click',
  ).toHaveLength(1);
  errors.expectClean('a double-clicked change request');
});
