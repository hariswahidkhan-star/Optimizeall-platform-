import { type Page, expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  anonGet,
  api,
  imageForm,
  jsonLd,
  landing,
  localDateTime,
  makePng,
  modal,
  openPublic,
  refused,
  runId,
  sitemapPaths,
  state,
  toast,
  watchErrors,
} from './support/content';

/**
 * Blog editorial workflow: the writer (blog.write) drafts a post with a cover image and submits it for review — but can
 * never publish; the website editor (blog.publish from a custom role) is stopped until the cover image has alt text,
 * then publishes → /blog, /blog/:slug (BlogPosting JSON-LD), RSS and the sitemap → the live post is read-only for the
 * writer → unpublish → 404 → return to draft with a note → the writer deletes their draft. Scheduling: a post scheduled
 * a couple of minutes ahead stays hidden until the scheduler publishes it. Stale edits and an expired session on save.
 */

const BODY = [
  '## Why most CRO programmes stall',
  '',
  'Teams test button colours instead of offers. In this post we walk through the three experiments that moved revenue',
  'for our clients last quarter, and the two that did nothing at all — with the numbers.',
].join('\n');

const postForm = (page: Page) => page.getByRole('form', { name: 'Post editor' });

interface Post {
  id: string;
  slug: string;
  title: string;
  excerpt: string;
  bodyMarkdown: string;
  status: string;
  concurrencyStamp: string;
  coverImageUrl: string | null;
  coverImageAlt: string | null;
  categoryIds: string[];
  tags: string[];
  authorId: string | null;
  seo: Record<string, unknown>;
}

const inputOf = (p: Post, patch: Partial<Post> = {}) => {
  const m = { ...p, ...patch };
  return {
    slug: m.slug,
    title: m.title,
    excerpt: m.excerpt,
    bodyMarkdown: m.bodyMarkdown,
    coverImageUrl: m.coverImageUrl,
    coverImageAlt: m.coverImageAlt,
    authorId: m.authorId,
    categoryIds: m.categoryIds,
    tags: m.tags,
    seo: m.seo,
    concurrencyStamp: m.concurrencyStamp,
  };
};

test.describe.serial('blog workflow', () => {
  test('writer drafts and submits; the editor publishes once the cover has alt text; unpublish; return to draft', async ({
    browser,
  }) => {
    const id = runId();
    const title = `Three experiments that moved revenue ${id}`;
    const slug = `three-experiments-${id}`;

    // ---------------------------------------------------------------- writer: draft with a cover image, submit
    const writer = await actor(browser, accounts.writer, landing.agency);
    const writerErrors = watchErrors(writer);
    await writer
      .getByRole('navigation', { name: 'Agency navigation' })
      .getByRole('link', { name: 'Blog', exact: true })
      .click();
    await writer.getByRole('link', { name: 'New post' }).click();
    const form = postForm(writer);
    await form.getByLabel('Title', { exact: true }).fill(title);
    await form.getByLabel('Slug', { exact: true }).fill(slug);
    await form
      .getByLabel('Excerpt', { exact: true })
      .fill('The three tests that paid for the programme, and two that did not.');
    await form.getByLabel('Body', { exact: true }).fill(BODY);
    await form.getByLabel('Tags (optional)').fill('CRO, Experiments');
    await form
      .locator('input[type="file"]')
      .first()
      .setInputFiles({
        name: 'cover.png',
        mimeType: 'image/png',
        buffer: makePng(Number.parseInt(id.slice(-5), 36) % 991, 1200, 630),
      });
    await form.getByRole('button', { name: 'Upload image' }).click();
    await expect(toast(writer, 'Image uploaded')).toBeVisible();
    await form.getByRole('button', { name: 'Create draft' }).click();
    await expect(toast(writer, 'Post saved')).toBeVisible();
    await expect(writer).toHaveURL(/\/agency\/website\/blog\/[0-9a-f-]{36}$/);
    const postId = writer.url().split('/').pop()!;

    // The writer can submit, not publish or schedule.
    await expect(writer.getByRole('button', { name: 'Publish now' })).toHaveCount(0);
    await expect(writer.getByRole('button', { name: 'Schedule' })).toHaveCount(0);
    await writer.getByRole('button', { name: 'Submit for review' }).click();
    await expect(toast(writer, 'Submitted for review')).toBeVisible();
    await expect(writer.getByText('In review', { exact: true }).first()).toBeVisible();
    writerErrors.expectClean('the writer’s post editor');
    const writerApi = await api(accounts.writer);
    const submitted = await writerApi.get<Post>(`/agency/website/blog/posts/${postId}`);
    const denied = await refused(
      writerApi.post(`/agency/website/blog/posts/${postId}/publish`, {
        concurrencyStamp: submitted.concurrencyStamp,
      }),
    );
    expect(denied.status).toBe(403);
    expect(
      (await refused(writerApi.post('/agency/website/blog/categories', { slug: `nope-${id}`, name: 'Nope' })))
        .status,
    ).toBe(403);
    expect((await anonGet(`/api/v1/public/blog/${slug}`)).status).toBe(404);

    // ---------------------------------------------------------------- editor: publish is stopped until the cover has alt text
    const editor = await actor(browser, state().editor, landing.agency);
    const editorErrors = watchErrors(editor);
    editorErrors.ignore(/HTTP 400 POST \S+\/blog\/posts\/[0-9a-f-]+\/publish$/);
    await editor.goto(`/agency/website/blog/${postId}`);
    await expect(editor.getByRole('heading', { level: 1, name: title })).toBeVisible();
    await editor.getByRole('button', { name: 'Publish now' }).click();
    await expect(
      postForm(editor).getByText('Describe the cover image for screen-reader users before publishing.'),
    ).toBeVisible();
    expect((await anonGet(`/api/v1/public/blog/${slug}`)).status).toBe(404);
    await postForm(editor)
      .getByLabel('Cover image description (alt text) (optional)')
      .fill('A line chart of checkout conversion rising after the third test');
    await postForm(editor).getByRole('button', { name: 'Save', exact: true }).click();
    await expect(toast(editor, 'Post saved')).toBeVisible();
    await editor.getByRole('button', { name: 'Publish now' }).click();
    await expect(toast(editor, 'Published')).toBeVisible();
    editorErrors.expectClean('the editor’s post editor');

    // ---------------------------------------------------------------- public: post, index, JSON-LD, RSS, sitemap
    const pub = await openPublic(browser, `/blog/${slug}`);
    const pubErrors = watchErrors(pub);
    await expect(pub.getByRole('heading', { level: 1, name: title })).toBeVisible();
    await expect(
      pub.getByRole('img', { name: 'A line chart of checkout conversion rising after the third test' }),
    ).toBeVisible();
    await expect(
      pub.getByRole('main').getByRole('heading', { level: 2, name: 'Why most CRO programmes stall' }),
    ).toBeVisible();
    const ld = await jsonLd(pub);
    const article = ld.find((x) => x['@type'] === 'BlogPosting') as
      { headline?: string; url?: string } | undefined;
    expect(article?.headline).toBe(title);
    await expect(pub).toHaveTitle(new RegExp(`^${title}`));
    pubErrors.expectClean('the blog post');
    const index = (await anonGet('/api/v1/public/blog')).json() as {
      items: { slug: string; tags: string[] }[];
    };
    expect(index.items[0]?.slug).toBe(slug);
    expect(index.items[0]?.tags).toEqual(['cro', 'experiments']);
    const rss = await anonGet('/api/v1/public/blog/rss.xml');
    expect(rss.text).toContain(`<title>${title}</title>`);
    expect(await sitemapPaths()).toContain(`/blog/${slug}`);

    // ---------------------------------------------------------------- the live post is read-only for the writer
    await writer.reload();
    await expect(
      writer.getByText('This post is live or scheduled. Only editors with publishing rights can change it.'),
    ).toBeVisible();
    await expect(postForm(writer).getByLabel('Title', { exact: true })).toBeDisabled();
    const live = await writerApi.get<Post>(`/agency/website/blog/posts/${postId}`);
    const edit = await refused(
      writerApi.put(`/agency/website/blog/posts/${postId}`, inputOf(live, { title: 'Sneaky edit' })),
    );
    expect(edit.status).toBe(403);
    expect(edit.code).toBe('blog.publish_required');
    expect((await refused(writerApi.delete(`/agency/website/blog/posts/${postId}`))).status).toBe(403);

    // ---------------------------------------------------------------- unpublish → 404 and out of the feed
    await editor.getByRole('button', { name: 'Unpublish' }).click();
    await expect(toast(editor, 'Unpublished')).toBeVisible();
    expect((await anonGet(`/api/v1/public/blog/${slug}`)).status).toBe(404);
    expect((await anonGet('/api/v1/public/blog/rss.xml')).text).not.toContain(`<title>${title}</title>`);
    expect(await sitemapPaths()).not.toContain(`/blog/${slug}`);
    const gone = await openPublic(browser, `/blog/${slug}`);
    await expect(gone.getByRole('heading', { name: "We couldn't find that article" })).toBeVisible();

    // ---------------------------------------------------------------- return to draft (a note is required) → writer deletes it
    await editor.getByRole('button', { name: 'Return to draft' }).click();
    const back = modal(editor, 'Return this post to draft?');
    await back.getByRole('button', { name: 'Return to draft' }).click();
    await expect(back.getByText(/Enter a reason/)).toBeVisible(); // the note is required
    await back.getByLabel(/Note for the writer/).fill('Please add the numbers for test two.');
    await back.getByRole('button', { name: 'Return to draft' }).click();
    await expect(toast(editor, 'Returned to draft')).toBeVisible();

    await writer.reload();
    await expect(postForm(writer).getByLabel('Title', { exact: true })).toBeEnabled();
    await postForm(writer).getByRole('button', { name: 'Delete' }).click();
    await modal(writer, 'Delete this post?').getByRole('button', { name: 'Delete post' }).click();
    await expect(writer).toHaveURL(/\/agency\/website\/blog$/);
    expect((await refused(writerApi.get(`/agency/website/blog/posts/${postId}`))).status).toBe(404);
  });

  test('a scheduled post stays hidden until the scheduler publishes it', async ({ browser }) => {
    test.setTimeout(300_000);
    const id = runId();
    const slug = `scheduled-notes-${id}`;
    const editorApi = await api(state().editor);
    const post = await editorApi.post<Post>('/agency/website/blog/posts', {
      slug,
      title: `Scheduled notes ${id}`,
      excerpt: 'Goes live on its own.',
      bodyMarkdown: BODY,
      categoryIds: [],
      tags: [],
      seo: {},
    });
    // A time in the past (or within the next minute) is refused.
    const past = await refused(
      editorApi.post(`/agency/website/blog/posts/${post.id}/schedule`, {
        concurrencyStamp: post.concurrencyStamp,
        publishAt: new Date(Date.now() - 60_000).toISOString(),
      }),
    );
    expect(past.status).toBe(400);
    expect(JSON.stringify(past.body)).toContain('Pick a publish time in the future.');

    const editor = await actor(browser, state().editor, landing.agency);
    await editor.goto(`/agency/website/blog/${post.id}`);
    await editor.getByRole('button', { name: 'Schedule' }).click();
    const dialog = modal(editor, 'Schedule post');
    await dialog.getByLabel(/Publish at/).fill(localDateTime(3));
    await dialog.getByRole('button', { name: 'Schedule' }).click();
    await expect(toast(editor, 'Scheduled')).toBeVisible();
    await expect(editor.getByText(/This post goes live on/)).toBeVisible();
    expect((await anonGet(`/api/v1/public/blog/${slug}`)).status).toBe(404);
    expect(await sitemapPaths()).not.toContain(`/blog/${slug}`);

    // Background jobs are off in e2e: run the scheduler the way the job runner would, until the post is due.
    const admin = await api(accounts.admin);
    await expect
      .poll(
        async () => {
          await admin.post('/admin/jobs/BlogSchedulerJob/run');
          return (await anonGet(`/api/v1/public/blog/${slug}`)).status;
        },
        { timeout: 240_000, intervals: [10_000], message: 'the scheduled post goes live once due' },
      )
      .toBe(200);
    const published = await editorApi.get<Post & { publishedAt: string }>(
      `/agency/website/blog/posts/${post.id}`,
    );
    expect(published.status).toBe('Published');
    const pub = await openPublic(browser, `/blog/${slug}`);
    await expect(pub.getByRole('heading', { level: 1, name: `Scheduled notes ${id}` })).toBeVisible();
    // Clean up: unpublish so later list assertions are not affected.
    await editorApi.post(`/agency/website/blog/posts/${post.id}/unpublish`, {
      concurrencyStamp: published.concurrencyStamp,
    });
  });

  test('stale saves are refused; a save with an expired session goes through sign-in and changes nothing', async ({
    browser,
  }) => {
    const id = runId();
    const writerApi = await api(accounts.writer);
    const post = await writerApi.post<Post>('/agency/website/blog/posts', {
      slug: `concurrency-${id}`,
      title: `Concurrency ${id}`,
      excerpt: 'Two tabs, one post.',
      bodyMarkdown: 'Short draft.',
      categoryIds: [],
      tags: [],
      seo: {},
    });
    // Two saves from the same loaded copy: the second is stale.
    await writerApi.put(
      `/agency/website/blog/posts/${post.id}`,
      inputOf(post, { excerpt: 'First save wins.' }),
    );
    const stale = await refused(
      writerApi.put(
        `/agency/website/blog/posts/${post.id}`,
        inputOf(post, { excerpt: 'Second save loses.' }),
      ),
    );
    expect(stale.status).toBe(409);
    expect(stale.code).toBe('concurrency.conflict');
    // Duplicate slug.
    const dup = await refused(
      writerApi.post('/agency/website/blog/posts', {
        slug: `concurrency-${id}`,
        title: 'Dup',
        excerpt: 'Dup',
        bodyMarkdown: 'x',
        seo: {},
      }),
    );
    expect(dup.status).toBe(409);
    expect(dup.code).toBe('website.slug_taken');

    // Expired session while editing: the save is refused (401) and the writer is sent to sign in, then back.
    const writer = await actor(browser, accounts.writer, landing.agency);
    const errors = watchErrors(writer);
    await writer.goto(`/agency/website/blog/${post.id}`);
    await expect(postForm(writer).getByLabel('Excerpt', { exact: true })).toHaveValue('First save wins.');
    await postForm(writer).getByLabel('Excerpt', { exact: true }).fill('Typed after the session expired.');
    await writer.context().clearCookies();
    await writer.route('**/api/v1/**', (route) =>
      route.fulfill({ status: 401, contentType: 'application/json', body: '{"title":"Unauthorized"}' }),
    );
    errors.ignore(/HTTP 401 /);
    errors.ignore(/console: .*401/);
    await postForm(writer).getByRole('button', { name: 'Save', exact: true }).click();
    await expect(writer).toHaveURL(/\/login/);
    await expect(writer.getByText(/session has expired/i)).toBeVisible();
    await writer.unroute('**/api/v1/**');
    await writer.getByLabel('Email', { exact: true }).fill(accounts.writer.email);
    await writer.getByLabel('Password', { exact: true }).fill(accounts.writer.password);
    await writer.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(writer).toHaveURL(new RegExp(`/agency/website/blog/${post.id}$`));
    await expect(postForm(writer).getByLabel('Excerpt', { exact: true })).toHaveValue('First save wins.');
    errors.expectClean('the expired session');
    expect((await writerApi.get<Post>(`/agency/website/blog/posts/${post.id}`)).excerpt).toBe(
      'First save wins.',
    );
    await writerApi.delete(`/agency/website/blog/posts/${post.id}`);
    // (imageForm keeps the upload helper exercised for writers: blog.write may upload website images.)
    expect(
      (
        await writerApi.upload<{ url: string }>(
          '/agency/website/images',
          imageForm(Number.parseInt(id.slice(-3), 36) + 11),
        )
      ).url,
    ).toMatch(/^\/api\/v1\/files\//);
  });
});
