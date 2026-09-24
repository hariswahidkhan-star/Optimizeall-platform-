import {
  type PostDto,
  accounts,
  api,
  errorOf,
  expect,
  fakePng,
  landing,
  localInput,
  makePng,
  modal,
  need,
  png,
  profile,
  raw,
  rememberPost,
  runId,
  sizedPng,
  statusOf,
  test,
  toast,
  watchErrors,
} from './support/social';

/**
 * The content calendar: a content creator (social.manage, no social.publish) uploads media, composes a Facebook + X post
 * with per-network variants, runs into X's 280-character limit (counter, server validation, submit refused), fixes it
 * and submits it; concurrent edits (409), double clicks, planned dates moved on the calendar (dialog and keyboard),
 * deleting a draft, and media rules (type, size, in use).
 */

const clientParam = () => `client=${need('client').id}`;
const title = () => `Autumn roast launch ${runId()}`;
const mediaTitle = () => `Autumn roast bag ${runId()}`;

test('media library: an image is uploaded; a fake image and an oversized file are refused', async ({
  as,
}) => {
  const page = await as(accounts.content, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/social/library?${clientParam()}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Library' })).toBeVisible();
  await expect(page.getByText('No media yet')).toBeVisible();

  // A text file that claims to be a PNG: the server checks the content.
  errors.ignore(/HTTP (400|413|415) POST \S+\/media\/upload$/);
  await page.getByRole('button', { name: 'Upload image' }).click();
  let dialog = modal(page, 'Upload image');
  await dialog.locator('input[type="file"]').setInputFiles(fakePng('roast.png'));
  await dialog.getByRole('button', { name: 'Upload' }).click();
  await expect(dialog.getByRole('alert').first()).toBeVisible();
  await dialog.getByRole('button', { name: 'Cancel' }).click();

  // Over the 10 MB limit: refused in the browser before anything is sent.
  await page.getByRole('button', { name: 'Upload image' }).click();
  dialog = modal(page, 'Upload image');
  await dialog.locator('input[type="file"]').setInputFiles(sizedPng(11 * 1024 * 1024, 'huge.png'));
  await expect(dialog.getByText(/The limit is 10 MB/).first()).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Upload' })).toBeDisabled();
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  // 9 MB is within the 10 MB the dialog promises (and the server accepts): no browser-side refusal.
  await page.getByRole('button', { name: 'Upload image' }).click();
  dialog = modal(page, 'Upload image');
  await dialog.locator('input[type="file"]').setInputFiles(sizedPng(9 * 1024 * 1024, 'nine-mb.png'));
  await expect(dialog.getByText(/The limit is/)).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Upload' })).toBeEnabled();
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  // …and the API refuses what the browser would have refused, when a client skips the browser check.
  const content = await api(accounts.content);
  const form = new FormData();
  form.append(
    'file',
    new Blob([sizedPng(Math.round(10.5 * 1024 * 1024)).buffer], { type: 'image/png' }),
    'huge.png',
  );
  expect(
    (await errorOf(content.upload(`/agency/social/clients/${need('client').id}/media/upload`, form))).code,
  ).toBe('file.too_large');
  // Images smaller than 200×200 px are refused by the server with the reason.
  await page.getByRole('button', { name: 'Upload image' }).click();
  dialog = modal(page, 'Upload image');
  await dialog.locator('input[type="file"]').setInputFiles(png(6, 'tiny.png'));
  await dialog.getByRole('button', { name: 'Upload' }).click();
  await expect(dialog.getByRole('alert').first()).toContainText('at least 200×200');
  await dialog.getByRole('button', { name: 'Cancel' }).click();

  // A real PNG with a title and alt text.
  await page.getByRole('button', { name: 'Upload image' }).click();
  dialog = modal(page, 'Upload image');
  await dialog
    .locator('input[type="file"]')
    .setInputFiles({ name: 'autumn-roast.png', mimeType: 'image/png', buffer: makePng(7, 400, 300) });
  await dialog.getByLabel('Title').fill(mediaTitle());
  await dialog.getByLabel('Alt text').fill('A bag of Helio autumn roast on a wooden table');
  await dialog.getByRole('button', { name: 'Upload' }).click();
  await expect(dialog).toBeHidden();
  const card = page.getByRole('listitem').filter({ hasText: mediaTitle() });
  await expect(card).toContainText('Image · 400×300');
  await expect(card.getByText('Public URL')).toBeVisible();

  // Media must be https when linked by URL.
  errors.ignore(/HTTP 400 POST \S+\/media\/url$/);
  expect(
    (
      await errorOf(
        content.post(`/agency/social/clients/${need('client').id}/media/url`, {
          kind: 'Video',
          url: 'http://videos.example.com/a.mp4',
          title: 'x',
        }),
      )
    ).code,
  ).toBe('social.invalid_url');
  errors.expectClean('the media library');
});

test('compose: per-network variants, the X character limit, server validation and submit for review', async ({
  as,
}) => {
  const { name } = need('client');
  const fb = profile('facebook');
  const x = profile('x');
  const page = await as(accounts.content, landing.agency);
  const errors = watchErrors(page);
  await page
    .getByRole('navigation', { name: 'Agency navigation' })
    .getByRole('link', { name: 'Compose' })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'New post' })).toBeVisible();
  const setup = page.getByRole('region', { name: 'Post' });
  await setup.getByLabel('Client').selectOption({ label: name });
  await setup.getByLabel('Internal title').fill(title());
  await setup.getByRole('checkbox', { name: `Facebook · @${fb.handle}` }).check();
  await setup.getByRole('checkbox', { name: `X · @${x.handle}` }).check();
  await setup.getByLabel('Planned date and time').fill(localInput(3 * 24 * 60));

  const variants = page.getByRole('region', { name: 'Content per network' });
  const long = `Our autumn roast is here: notes of fig, cocoa and toasted hazelnut. ${'Slow-roasted in small batches. '.repeat(8)}`;
  await variants.getByLabel('Facebook text').fill(long);
  await variants.getByRole('button', { name: 'Use this text for all networks' }).click();
  // The X variant inherited the long text: the counter and the server validation both flag it.
  await variants.getByRole('tab', { name: new RegExp(`X @${x.handle}`) }).click();
  await expect(variants.getByText(/over the limit/)).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'Fix before submitting' })).toBeVisible();
  await expect(variants.getByRole('tab', { name: new RegExp(`X @${x.handle}.*error`) })).toBeVisible();
  // Attach the uploaded image to the Facebook variant with alt text.
  await variants.getByRole('tab', { name: new RegExp(`Facebook @${fb.handle}`) }).click();
  await variants.getByRole('checkbox', { name: mediaTitle() }).check();
  await expect(variants.getByLabel('Alt text')).toHaveValue('A bag of Helio autumn roast on a wooden table');

  // Double-clicking "Save draft" still creates exactly one draft (a draft may break limits; submitting may not).
  await page.getByRole('button', { name: 'Save draft' }).dblclick();
  await expect(toast(page, 'Draft created')).toBeVisible();
  await expect(page).toHaveURL(/\/agency\/social\/posts\/[0-9a-f-]{36}$/);
  const postId = page.url().split('/').pop()!;
  rememberPost('launch', { id: postId, title: title() });
  const content = await api(accounts.content);
  const drafts = await content.get<{ items: { id: string }[] }>(
    `/agency/social/posts?search=${encodeURIComponent(title())}`,
  );
  expect(
    drafts.items.map((p) => p.id),
    'one draft per double click',
  ).toEqual([postId]);

  // Submitting an invalid post is refused with the reason in the dialog.
  errors.ignore(/HTTP 400 POST \S+\/submit$/);
  await page.getByRole('button', { name: 'Submit for review' }).click();
  let submit = modal(page, 'Submit for review');
  await submit.getByRole('button', { name: 'Submit for review' }).click();
  await expect(submit.getByRole('alert')).toContainText(/Fix the validation errors first/);
  await submit.getByRole('button', { name: 'Cancel' }).click();

  // Fix the X text; the counter and validation clear; save and submit.
  await variants.getByRole('tab', { name: new RegExp(`X @${x.handle}`) }).click();
  await variants.getByLabel('X text').fill('Autumn roast is here. Fig, cocoa, hazelnut. ☕');
  await variants.getByLabel('Hashtags').fill('#AutumnRoast #HelioCoffee');
  await expect(page.getByRole('status').filter({ hasText: 'Ready for review' })).toBeVisible();
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(toast(page, 'Post saved')).toBeVisible();
  await page.getByRole('button', { name: 'Submit for review' }).click();
  submit = modal(page, 'Submit for review');
  await submit.getByRole('button', { name: 'Submit for review' }).click();
  await expect(toast(page, 'Submit for review: done')).toBeVisible();
  await expect(page.getByText('Internal review', { exact: true }).first()).toBeVisible();
  // No social.publish: no Approve or Schedule, and the API refuses them.
  await expect(page.getByRole('button', { name: 'Approve', exact: true })).toHaveCount(0);
  expect(await statusOf(content.post(`/agency/social/posts/${postId}/approve`, {}))).toBe(403);
  expect(
    await statusOf(
      content.post(`/agency/social/posts/${postId}/schedule`, {
        scheduledAt: new Date(Date.now() + 86_400_000).toISOString(),
      }),
    ),
  ).toBe(403);

  const saved = await content.get<PostDto>(`/agency/social/posts/${postId}`);
  expect(saved.status).toBe('InternalReview');
  expect(saved.variants.map((v) => v.network).sort()).toEqual(['Facebook', 'X']);
  expect(saved.variants.find((v) => v.network === 'Facebook')!.publishStatus).toBe('Pending');
  errors.expectClean('composing a post');
});

test('concurrent edits: the second save of a stale post is refused (409) and nothing is overwritten', async ({
  as,
}) => {
  const fb = profile('facebook');
  const content = await api(accounts.content);
  const draft = await content.post<PostDto>('/agency/social/posts', {
    clientAccountId: need('client').id,
    title: `Stale edit ${runId()}`,
    variants: [
      { profileId: fb.id, text: 'Original text', mediaIds: [], altTexts: [], hashtags: [], mentions: [] },
    ],
  });
  const first = await as(accounts.content, landing.agency);
  const second = await as(accounts.socialManager, landing.agency);
  const secondErrors = watchErrors(second);
  secondErrors.ignore(/HTTP 409 PUT \S+\/posts\/[0-9a-f-]{36}$/);
  await first.goto(`/agency/social/posts/${draft.id}`);
  await second.goto(`/agency/social/posts/${draft.id}`);
  const text = (p: typeof first) =>
    p.getByRole('region', { name: 'Content per network' }).getByLabel('Facebook text');
  await expect(text(second)).toHaveValue('Original text');

  await text(first).fill('Edited by Priya');
  await first.getByRole('button', { name: 'Save changes' }).click();
  await expect(toast(first, 'Post saved')).toBeVisible();

  await text(second).fill('Edited by Sofia (stale)');
  const put = second.waitForResponse(
    (r) => r.request().method() === 'PUT' && r.url().endsWith(`/agency/social/posts/${draft.id}`),
  );
  await second.getByRole('button', { name: 'Save changes' }).click();
  expect((await put).status()).toBe(409);
  await expect(second.getByRole('alert').filter({ hasText: 'Could not save' })).toContainText(
    'changed by someone else',
  );
  const now = await content.get<PostDto>(`/agency/social/posts/${draft.id}`);
  expect(now.variants[0]!.publishStatus).toBe('Pending');
  expect(
    (await content.get<{ variants: { text: string }[] }>(`/agency/social/posts/${draft.id}`)).variants[0]!
      .text,
  ).toBe('Edited by Priya');
  // The API requires the stamp on edits.
  expect(
    (
      await errorOf(
        content.put(`/agency/social/posts/${draft.id}`, {
          clientAccountId: need('client').id,
          title: 'x',
          variants: [{ profileId: fb.id, text: 'y' }],
        }),
      )
    ).code,
  ).toBe('concurrency.stamp_required');
  secondErrors.expectClean('the stale edit');
  rememberPost('stale', { id: draft.id, title: `Stale edit ${runId()}` });
});

test('calendar: move a planned post with the dialog and the keyboard; delete a draft', async ({ as }) => {
  const li = profile('linkedin');
  const content = await api(accounts.content);
  const planned = new Date(Date.now() + 2 * 86_400_000);
  planned.setUTCHours(9, 0, 0, 0);
  const moveTitle = `Brew guide ${runId()}`;
  const post = await content.post<PostDto>('/agency/social/posts', {
    clientAccountId: need('client').id,
    title: moveTitle,
    scheduledAt: planned.toISOString(),
    variants: [
      {
        profileId: li.id,
        text: 'How we brew a V60 at home.',
        mediaIds: [],
        altTexts: [],
        hashtags: [],
        mentions: [],
      },
    ],
  });

  const page = await as(accounts.content, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/social?${clientParam()}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Content calendar' })).toBeVisible();
  const chip = page.getByRole('button', { name: new RegExp(`^${moveTitle}, Draft`) });
  await expect(chip).toBeVisible();

  // The "Move" dialog (the non-drag alternative).
  await page.getByRole('button', { name: `Move ${moveTitle} to another date` }).click();
  const dialog = modal(page, 'Move post');
  const target = new Date(planned.getTime() + 86_400_000);
  const pad = (n: number) => String(n).padStart(2, '0');
  await dialog
    .getByLabel('New date and time')
    .fill(
      `${target.getFullYear()}-${pad(target.getMonth() + 1)}-${pad(target.getDate())}T${pad(target.getHours())}:${pad(target.getMinutes())}`,
    );
  await dialog.getByRole('button', { name: 'Move' }).click();
  await expect(toast(page, 'Post moved')).toBeVisible();
  let saved = await content.get<PostDto>(`/agency/social/posts/${post.id}`);
  expect(new Date(saved.scheduledAt!).getTime()).toBe(target.getTime());
  expect(saved.status, 'moving keeps the workflow state').toBe('Draft');

  // Keyboard: Alt+→ moves a day. Pressed twice in a row it moves two days (no "changed by someone else"), and the chip
  // keeps the focus in its new day.
  const cell = (d: Date) =>
    page.getByRole('gridcell', {
      name: new RegExp(
        `^${d.toLocaleDateString('en-US', { weekday: 'long', day: 'numeric', month: 'long' })}`,
      ),
    });
  const chipIn = (d: Date) => cell(d).getByRole('button', { name: new RegExp(`^${moveTitle}, Draft`) });
  await expect(chipIn(target)).toBeVisible(); // the calendar reloaded after the dialog move
  await chipIn(target).focus();
  await page.keyboard.press('Alt+ArrowRight');
  await page.keyboard.press('Alt+ArrowRight');
  const twoDaysLater = new Date(target.getTime() + 2 * 86_400_000);
  await expect
    .poll(
      async () =>
        new Date((await content.get<PostDto>(`/agency/social/posts/${post.id}`)).scheduledAt!).getTime(),
      { timeout: 15_000 },
    )
    .toBe(twoDaysLater.getTime());
  await expect(chipIn(twoDaysLater)).toBeFocused();
  await page.keyboard.press('Alt+ArrowLeft');
  await expect
    .poll(
      async () =>
        new Date((await content.get<PostDto>(`/agency/social/posts/${post.id}`)).scheduledAt!).getTime(),
      { timeout: 15_000 },
    )
    .toBe(twoDaysLater.getTime() - 86_400_000);
  await expect(
    page.getByRole('region', { name: 'Notifications' }).getByText('Could not move the post'),
  ).toHaveCount(0);

  // A stale move from the API is refused.
  expect(
    (
      await errorOf(
        content.post(`/agency/social/posts/${post.id}/reschedule`, {
          scheduledAt: planned.toISOString(),
          concurrencyStamp: post.concurrencyStamp,
        }),
      )
    ).status,
  ).toBe(409);

  // Delete the draft from the composer.
  await page.goto(`/agency/social/posts/${post.id}`);
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await modal(page, 'Delete?').getByRole('button', { name: 'Delete' }).click();
  await expect(toast(page, 'Post deleted')).toBeVisible();
  await expect(page).toHaveURL(/\/agency\/social$/);
  expect(await statusOf(content.get(`/agency/social/posts/${post.id}`))).toBe(404);
  saved = (await raw(content, 'GET', `/agency/social/posts?search=${encodeURIComponent(moveTitle)}`))
    .body as never;
  expect((saved as unknown as { total: number }).total).toBe(0);

  // Media used by a post cannot be deleted from the library.
  const media = await content.get<{ items: { id: string; title: string }[] }>(
    `/agency/social/clients/${need('client').id}/media`,
  );
  const bag = media.items.find((m) => m.title.startsWith('Autumn roast bag'))!;
  expect((await errorOf(content.delete(`/agency/social/media/${bag.id}`))).code).toBe('social.media_in_use');
  errors.expectClean('the calendar');
});
