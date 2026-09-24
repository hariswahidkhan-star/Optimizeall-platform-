import {
  type PostDto,
  accounts,
  api,
  approveThroughClient,
  errorOf,
  expect,
  landing,
  localInput,
  modal,
  need,
  post,
  profile,
  rememberPost,
  runId,
  runPublishingJob,
  saveState,
  state,
  statusOf,
  test,
  toast,
  waitUntil,
  watchErrors,
} from './support/social';
import { delayStub, resetStub, stubRequests } from './support/stub';

/**
 * Publishing. The launch post is scheduled through the UI; four more posts are arranged through the API for the same
 * minute on profiles whose (stub) tokens behave differently: a healthy Page, a flaky Page (503 → retry with backoff), a
 * Page whose token expired (190 → authorization error, profile flagged) and LinkedIn (no adapter → publish manually).
 * The publishing job runs twice, concurrently, while the Graph stub answers slowly: every variant is sent at most once.
 * Then the failures are handled: mark as published (manual), reconnect + retry, and published posts are locked.
 */

interface Profile {
  id: string;
  handle: string;
  connectionState: string;
  statusMessage: string | null;
}

const text = (key: string) => `${key} ${runId()}: freshly roasted, delivered Friday.`;

async function extraFacebookProfile(key: string, token: string) {
  const social = await api(accounts.socialManager);
  const admin = await api(accounts.admin);
  const created = await social.post<Profile>(`/agency/social/clients/${need('client').id}/profiles`, {
    network: 'Facebook',
    handle: `helio.${key}.${runId()}`,
    displayName: `Helio ${key}`,
    externalId: `${key.length}${Date.now()}`,
  });
  await admin.post(`/agency/social/profiles/${created.id}/token`, { accessToken: token });
  return created;
}

test('schedule: the launch post through the UI, four more through the API; nothing is published before it is due', async ({
  as,
}) => {
  await resetStub();
  const launch = post('launch');
  const page = await as(accounts.socialManager, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/social/posts/${launch.id}`);
  await expect(page.getByText('Approved', { exact: true }).first()).toBeVisible();

  // A time in the past is refused in the dialog.
  errors.ignore(/HTTP 400 POST \S+\/schedule$/);
  await page.getByRole('button', { name: 'Schedule', exact: true }).click();
  const dialog = modal(page, 'Schedule post');
  await dialog.getByLabel('Publish at').fill(localInput(-60));
  await dialog.getByRole('button', { name: 'Schedule' }).click();
  await expect(dialog.getByRole('alert')).toContainText(/future/);
  await dialog.getByLabel('Publish at').fill(localInput(3));
  await dialog.getByRole('button', { name: 'Schedule' }).click();
  await expect(toast(page, 'Schedule: done')).toBeVisible();
  await expect(page.getByText('Scheduled', { exact: true }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Unschedule' })).toBeVisible();

  const social = await api(accounts.socialManager);
  const scheduled = await social.get<PostDto>(`/agency/social/posts/${launch.id}`);
  const due = scheduled.scheduledAt!;

  const fb = profile('facebook');
  const flaky = await extraFacebookProfile('flaky', 'e2e-token-flaky-page');
  const expired = await extraFacebookProfile('expired', 'e2e-token-expired-page');
  const li = profile('linkedin');
  const arrange = async (key: string, profileId: string) => {
    const created = await social.post<PostDto>('/agency/social/posts', {
      clientAccountId: need('client').id,
      title: `${key} ${runId()}`,
      variants: [{ profileId, text: text(key), mediaIds: [], altTexts: [], hashtags: [], mentions: [] }],
    });
    await approveThroughClient(created.id);
    await social.post(`/agency/social/posts/${created.id}/schedule`, { scheduledAt: due });
    rememberPost(key, { id: created.id, title: created.title });
  };
  await arrange('healthy', fb.id);
  await arrange('flaky', flaky.id);
  await arrange('expired', expired.id);
  await arrange('manual', li.id);
  saveState({
    profiles: {
      ...need('profiles'),
      flaky: { id: flaky.id, handle: flaky.handle },
      expired: { id: expired.id, handle: expired.handle },
    },
  });

  // Not due yet: the job leaves them alone and nothing of this run reaches the network.
  await runPublishingJob();
  for (const key of ['launch', 'healthy', 'flaky', 'expired', 'manual'])
    expect((await social.get<PostDto>(`/agency/social/posts/${post(key).id}`)).status, key).toBe('Scheduled');
  expect((await stubRequests()).filter((r) => JSON.stringify(r.form).includes(runId()))).toEqual([]);

  // The calendar shows them scheduled.
  await page.goto(`/agency/social?client=${need('client').id}`);
  await expect(
    page.getByRole('button', { name: new RegExp(`^healthy ${runId()}, Scheduled`) }),
  ).toBeVisible();
  errors.expectClean('scheduling');
  saveState({ due });
});

test('the publishing job publishes each variant once, even when two runs overlap', async ({ as }) => {
  // The posts are due up to three minutes after scheduling (the API refuses times less than a minute ahead).
  test.setTimeout(8 * 60_000);
  await waitUntil(need('due'));
  await delayStub(1500);
  // Two runs at once: the job lease and the per-post / per-variant claims keep it to one attempt each.
  const runs = await Promise.all([runPublishingJob(), runPublishingJob()]);
  await delayStub(0);
  expect(runs.map((r) => r.status)).toEqual(['Succeeded', 'Succeeded']);

  const social = await api(accounts.socialManager);
  const get = (key: string) => social.get<PostDto>(`/agency/social/posts/${post(key).id}`);

  const healthy = await get('healthy');
  expect(healthy.status).toBe('Published');
  expect(healthy.variants[0]!.attempts).toBe(1);
  expect(healthy.variants[0]!.externalPostId).toMatch(/_\d+$/);
  expect(healthy.variants[0]!.publishedUrl).toBe(
    `https://www.facebook.com/${healthy.variants[0]!.externalPostId}`,
  );
  const requests = await stubRequests();
  const feedFor = (key: string) =>
    requests.filter((r) => r.path.endsWith('/feed') && (r.form.message ?? '').includes(text(key)));
  expect(feedFor('healthy'), 'the healthy post was sent exactly once').toHaveLength(1);

  // 503 → transient: retried later with backoff, not now.
  const flaky = await get('flaky');
  expect(flaky.status).toBe('Scheduled');
  expect(flaky.variants[0]!.publishStatus).toBe('Pending');
  expect(flaky.variants[0]!.attempts).toBe(1);
  expect(new Date(flaky.variants[0]!.nextAttemptAt!).getTime()).toBeGreaterThan(Date.now() + 60_000);
  expect(feedFor('flaky')).toHaveLength(1);

  // 190 → the token expired: failed, and the profile is flagged for reconnection.
  const expired = await get('expired');
  expect(expired.status).toBe('Failed');
  expect(expired.variants[0]!.failureKind).toBe('Authorization');
  const profiles = await social.get<Profile[]>(`/agency/social/clients/${need('client').id}/profiles`);
  const flagged = profiles.find((p) => p.handle.startsWith('helio.expired'))!;
  expect(flagged.connectionState).toBe('Error');
  expect(flagged.statusMessage).toMatch(/expired|revoked/i);

  // LinkedIn has no adapter; the launch post's Facebook variant has a private image and its X account is disconnected.
  const manual = await get('manual');
  expect(manual.status).toBe('Failed');
  expect(manual.variants[0]!.failureKind).toBe('NotConfigured');
  const launch = await get('launch');
  expect(launch.status).toBe('Failed');
  expect(launch.failureReason).toMatch(/Facebook: .*URL/);
  expect(launch.failureReason).toMatch(/X: .*not connected/);

  // A third run later sends nothing new (published stays published; the flaky retry is not due yet).
  const ours = async () =>
    (await stubRequests()).filter((r) => JSON.stringify(r.form).includes(runId())).length;
  const before = await ours();
  await runPublishingJob();
  expect(await ours()).toBe(before);

  // The publishing log (UI).
  const page = await as(accounts.socialManager, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/social/publishing?client=${need('client').id}`);
  const log = page.getByRole('table', { name: 'Publishing log' });
  await expect(log.getByRole('row', { name: new RegExp(`healthy ${runId()}`) })).toContainText('Published');
  await expect(log.getByRole('row', { name: new RegExp(`expired ${runId()}`) })).toContainText('Failed');
  // The post creator is told about the failure.
  const notifications = await (
    await api(accounts.socialManager)
  ).get<{ items: { title: string }[] }>('/me/notifications?pageSize=50');
  expect(notifications.items.map((n) => n.title)).toContain(`Post failed to publish: expired ${runId()}`);
  errors.expectClean('the publishing log');
});

test('failures are handled: the client never sees them; mark as published; reconnect and retry; published posts are locked', async ({
  as,
}) => {
  const launch = post('launch');
  // The client sees the failed launch post as scheduled, without failure details.
  const approverApi = await api(state().approver);
  const clientView = await approverApi.get<PostDto>(`/client/social/posts/${launch.id}`);
  expect(clientView.failureReason).toBeNull();
  expect(clientView.status, 'the client sees "Scheduled" while the agency handles a failure').toBe(
    'Scheduled',
  );
  expect(clientView.variants.every((v) => v.failureReason === null)).toBe(true);
  expect(clientView.variants.every((v) => v.failureKind === 'None')).toBe(true);
  const client = await as(state().approver, landing.client);
  await client.goto('/client/social');
  await expect(client.getByRole('row', { name: new RegExp(launch.title) })).toContainText('Scheduled');
  await expect(client.getByText('Failed', { exact: true })).toHaveCount(0);

  const page = await as(accounts.socialManager, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/social/posts/${launch.id}`);
  await expect(page.getByRole('alert').filter({ hasText: 'Publishing failed' })).toBeVisible();

  // Mark the Facebook variant as published by hand: the live URL must be https.
  errors.ignore(/HTTP 400 POST \S+\/mark-published$/);
  await page.getByRole('button', { name: 'Mark as published' }).click();
  let dialog = modal(page, 'Mark as published');
  await dialog.getByLabel('Network').selectOption({ label: `Facebook @${profile('facebook').handle}` });
  await dialog.getByLabel('Live post URL').fill('http://facebook.com/helio/posts/1');
  await dialog.getByRole('button', { name: 'Mark as published' }).click();
  await expect(dialog.getByRole('alert')).toContainText(/https/);
  await dialog.getByLabel('Live post URL').fill('https://www.facebook.com/helio/posts/123456');
  await dialog.getByRole('button', { name: 'Mark as published' }).click();
  await expect(toast(page, 'Mark as published: done')).toBeVisible();
  const table = page.getByRole('table', { name: 'Publishing state per network' });
  await expect(table.getByRole('row', { name: /Facebook/ })).toContainText('Published (manual)');

  // Reconnect X (admin pastes a fresh token), then retry: only the failed X variant is sent.
  const admin = await api(accounts.admin);
  await admin.post(`/agency/social/profiles/${profile('x').id}/token`, {
    accessToken: 'e2e-token-ok-x-reconnected',
  });
  await page.reload();
  await page.getByRole('button', { name: 'Retry publishing' }).click();
  dialog = modal(page, 'Retry publishing');
  await dialog.getByRole('button', { name: 'Retry' }).click();
  await expect(toast(page, 'Retry publishing: done')).toBeVisible();
  await expect(page.getByText('Scheduled', { exact: true }).first()).toBeVisible();
  const tweets = async () =>
    (await stubRequests()).filter(
      (r) => r.path === '/x/2/tweets' && r.token === 'e2e-token-ok-x-reconnected',
    );
  const tweetsBefore = (await tweets()).length;
  await runPublishingJob();
  const social = await api(accounts.socialManager);
  const done = await social.get<PostDto>(`/agency/social/posts/${launch.id}`);
  expect(done.status).toBe('Published');
  expect(done.variants.find((v) => v.network === 'X')!.externalPostId).toMatch(/^\d+$/);
  expect(await tweets(), 'the X variant was sent once').toHaveLength(tweetsBefore + 1);
  // The manually published Facebook variant was not sent again.
  expect(
    (await stubRequests()).filter(
      (r) => r.token === 'e2e-token-ok-facebook-page' && (r.form.caption ?? '').includes('250 g'),
    ),
  ).toHaveLength(0);

  // Published: locked for editing and deletion; retry and mark-published are refused.
  await page.reload();
  await expect(page.getByText('This post is locked')).toBeVisible();
  await expect(
    page.getByTitle('Published posts are kept for reporting and cannot be deleted.'),
  ).toBeDisabled();
  expect((await errorOf(social.delete(`/agency/social/posts/${launch.id}`))).code).toBe(
    'social.not_deletable',
  );
  expect((await errorOf(social.post(`/agency/social/posts/${launch.id}/retry`, {}))).code).toBe(
    'social.nothing_to_retry',
  );
  const fbVariant = done.variants.find((v) => v.network === 'Facebook')!;
  expect(
    (
      await errorOf(
        social.post(`/agency/social/posts/${launch.id}/mark-published`, {
          variantId: fbVariant.id,
          url: 'https://www.facebook.com/x',
        }),
      )
    ).code,
  ).toBe('social.already_published');

  // LinkedIn (no adapter): published by hand from the API; a double submit is refused, not recorded twice.
  const manual = await social.get<PostDto>(`/agency/social/posts/${post('manual').id}`);
  const body = {
    variantId: manual.variants[0]!.id,
    url: 'https://www.linkedin.com/feed/update/urn:li:activity:1',
  };
  const results = await Promise.all([
    statusOf(social.post(`/agency/social/posts/${manual.id}/mark-published`, body)),
    statusOf(social.post(`/agency/social/posts/${manual.id}/mark-published`, body)),
  ]);
  expect(results.sort()).toEqual([200, 409]);
  const attempts = await social.get<{ outcome: string }[]>(`/agency/social/posts/${manual.id}/attempts`);
  expect(attempts.filter((a) => a.outcome === 'MarkedPublished')).toHaveLength(1);

  // The expired profile is flagged on the profiles page.
  await page.goto(`/agency/social/profiles?client=${need('client').id}`);
  await expect(page.getByRole('row', { name: /helio\.expired/ })).toContainText('Error — reconnect');
  errors.expectClean('handling publishing failures');
});

test('post analytics: imported post metrics show on the agency analytics page and the client’s performance page', async ({
  as,
}) => {
  const healthy = post('healthy');
  const social = await api(accounts.socialManager);
  const dto = await social.get<PostDto>(`/agency/social/posts/${healthy.id}`);
  const externalId = dto.variants[0]!.externalPostId!;
  // Metrics of yesterday: the client's "last 30 days" run through yesterday (complete days), the agency's through today.
  const yesterday = new Date(Date.now() - 86_400_000).toISOString().slice(0, 10);
  // The export's publish time counts for the period (the post went out today; the file says yesterday noon).
  const csv = `Post ID,Publish time,Date,Impressions,Reach,Engagements,Link clicks\n${externalId},${yesterday}T12:00:00Z,${yesterday},4210,3900,388,57\n`;
  const input = { kind: 'post', fileName: 'fb-posts.csv', csv, source: 'PlatformExport' };
  const preview = await social.post<{ suggestedMapping: Record<string, string>; errors: string[] }>(
    `/agency/social/profiles/${profile('facebook').id}/metrics/import/preview`,
    input,
  );
  expect(preview.errors).toEqual([]);
  const first = await social.post<{ rowsImported: number; rowsUpdated: number }>(
    `/agency/social/profiles/${profile('facebook').id}/metrics/import`,
    {
      ...input,
      mapping: preview.suggestedMapping,
    },
  );
  expect(first.rowsImported).toBe(1);
  // Re-importing the same file updates the row (idempotent).
  const again = await social.post<{ rowsImported: number; rowsUpdated: number }>(
    `/agency/social/profiles/${profile('facebook').id}/metrics/import`,
    {
      ...input,
      mapping: preview.suggestedMapping,
    },
  );
  expect(again).toMatchObject({ rowsImported: 0, rowsUpdated: 1 });
  // An undefined metric source is a 400, not a stored import.
  expect(
    await statusOf(
      social.post(`/agency/social/profiles/${profile('facebook').id}/metrics/import`, {
        ...input,
        mapping: preview.suggestedMapping,
        source: 42,
      }),
    ),
  ).toBe(400);

  const page = await as(accounts.socialManager, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/social/analytics?client=${need('client').id}`);
  const top = page.getByRole('table', { name: 'Top posts by engagements' });
  await expect(top.getByRole('row', { name: new RegExp(healthy.title) })).toContainText('388');
  errors.expectClean('social analytics');

  const client = await as(state().viewer, landing.client);
  const clientErrors = watchErrors(client);
  await client.goto('/client/social/performance');
  await expect(client.getByRole('heading', { level: 1, name: 'Social & ads performance' })).toBeVisible();
  const clientTop = client.getByRole('table', { name: 'Top posts' });
  // 388 engagements of 4,210 impressions = 9.2 %.
  await expect(clientTop.getByRole('row', { name: new RegExp(healthy.title) })).toContainText('388');
  await expect(clientTop.getByRole('row', { name: new RegExp(healthy.title) })).toContainText('9.2%');
  clientErrors.expectClean('the client performance page');
});
