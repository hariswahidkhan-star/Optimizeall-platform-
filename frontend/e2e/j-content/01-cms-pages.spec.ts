import { type Page, expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  anonGet,
  api,
  canonical,
  jsonLd,
  landing,
  meta,
  modal,
  notFound,
  openPublic,
  recall,
  refused,
  remember,
  runId,
  sitemapPaths,
  state,
  toast,
  watchErrors,
} from './support/content';

/**
 * CMS pages (Agency → Website → Pages) as the website editor, whose site.manage comes from a custom role:
 *   create a draft (rich text with hostile markup + FAQ) → live preview → not public, not in the sitemap → publish → the
 *   public page renders the sanitized content with its title, description, canonical, Open Graph and JSON-LD
 *   (BreadcrumbList + FAQPage) → edit → change visible → version history → restore an older version → schedule,
 *   noindex, unpublish, delete. Negatives: duplicate and reserved slugs, stale edits (409), missing stamps.
 */

interface SitePage {
  id: string;
  slug: string;
  title: string;
  isPublished: boolean;
  publishAt: string | null;
  version: number;
  concurrencyStamp: string;
  blocks: { id: string; type: string; data: Record<string, unknown> }[];
  seo: {
    title: string | null;
    description: string | null;
    noIndex: boolean;
    canonicalUrl: string | null;
    ogImageUrl: string | null;
  };
  kind: string;
  summary: string | null;
  sortOrder: number;
}

const HOSTILE = [
  '## What we promise',
  '',
  'We answer every enquiry within one working day.',
  '',
  '<script>window.__pwned = true</script>',
  '<img src=x onerror="window.__pwned = true">',
  '[Click for a prize](javascript:window.__pwned=true)',
  '[Read our playbook](/blog)',
].join('\n');

const editorForm = (page: Page) => page.getByRole('form', { name: 'Page editor' });

async function openPagesAdmin(page: Page) {
  await page
    .getByRole('navigation', { name: 'Agency navigation' })
    .getByRole('link', { name: 'Pages', exact: true })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Pages' })).toBeVisible();
}

/** Body for PUT /agency/website/pages/{id} from a loaded page. */
function input(p: SitePage, patch: Partial<SitePage> = {}) {
  const merged = { ...p, ...patch };
  return {
    slug: merged.slug,
    title: merged.title,
    summary: merged.summary,
    kind: merged.kind,
    blocks: merged.blocks.map((b) => ({ id: b.id, type: b.type, data: b.data })),
    seo: merged.seo,
    isPublished: merged.isPublished,
    publishAt: merged.publishAt,
    sortOrder: merged.sortOrder,
    concurrencyStamp: merged.concurrencyStamp,
  };
}

test.describe.serial('CMS pages', () => {
  test('draft → preview → publish → public page with sanitized content and complete SEO head', async ({
    browser,
  }) => {
    const id = runId();
    const slug = `e2e-promise-${id}`;
    const title = `Our service promise ${id}`;
    const editor = await actor(browser, state().editor, landing.agency);
    const errors = watchErrors(editor);
    await openPagesAdmin(editor);
    await editor.getByRole('link', { name: 'New page' }).click();
    await expect(editor.getByRole('heading', { level: 1, name: 'New page' })).toBeVisible();

    const form = editorForm(editor);
    await form.getByLabel('Title', { exact: true }).first().fill(title);
    await form.getByLabel('Slug', { exact: true }).fill(slug);
    await form
      .getByLabel('Summary (optional)')
      .fill('How quickly we respond, and what you can expect from us.');
    // Rich text (the default block type) with hostile markup, then an FAQ block.
    await form.getByRole('button', { name: 'Add block' }).click();
    const rich = form.getByRole('group', { name: 'Block 1: Rich text' });
    await rich.getByLabel('Content', { exact: true }).fill(HOSTILE);
    await form.getByLabel('Block type', { exact: true }).selectOption('faq');
    await form.getByRole('button', { name: 'Add block' }).click();
    const faq = form.getByRole('group', { name: 'Block 2: FAQ' });
    await faq.getByLabel('Question (optional)').fill('How fast do you reply?');
    await faq.getByLabel('Answer (Markdown) (optional)').fill('Within **one working day**, every time.');
    await form.getByLabel('SEO title (optional)').fill(`Service promise ${id}`);
    await form
      .getByLabel('Meta description (optional)')
      .fill('Response times and service levels for every client.');

    // The live preview renders the draft with the public site's renderer — the hostile parts never become markup.
    const preview = editor.getByRole('complementary', { name: 'Live preview' });
    await expect(preview.getByRole('heading', { name: 'What we promise' })).toBeVisible();
    await expect(preview.getByText('How fast do you reply?')).toBeVisible();
    expect(await editor.evaluate(() => (window as { __pwned?: boolean }).__pwned)).toBeUndefined();

    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(toast(editor, 'Page saved')).toBeVisible();
    await expect(editor).toHaveURL(/\/agency\/website\/pages\/[0-9a-f-]{36}$/);
    const pageId = editor.url().split('/').pop()!;
    remember('promisePage', { id: pageId, slug, title });

    // Stored content is sanitized server-side (defence in depth): no raw HTML, no javascript: links.
    const editorApi = await api(state().editor);
    const saved = await editorApi.get<SitePage>(`/agency/website/pages/${pageId}`);
    const markdown = String(saved.blocks[0]!.data.markdown);
    expect(markdown).toContain('## What we promise');
    expect(markdown).toContain('[Read our playbook](/blog)');
    expect(markdown).not.toMatch(/<script|onerror|javascript:/i);
    expect(markdown).toContain('Click for a prize'); // the link text survives, the link does not
    expect(saved.isPublished).toBe(false);
    expect(saved.version).toBe(1);

    // A draft is invisible: the public page 404s and the sitemap does not list it.
    const early = await openPublic(browser, `/${slug}`);
    await expect(notFound(early)).toBeVisible();
    expect((await anonGet(`/api/v1/public/pages/${slug}`)).status).toBe(404);
    expect(await sitemapPaths()).not.toContain(`/${slug}`);

    // Publish.
    await editor.getByRole('switch', { name: 'Published', exact: true }).click();
    await form.getByLabel('Change note (optional)').fill('First publish');
    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(toast(editor, 'Page saved')).toBeVisible();
    await expect(editor.getByRole('link', { name: /View page/ })).toHaveAttribute('href', `/${slug}`);
    errors.expectClean('the page editor');

    // ---------------------------------------------------------------- the public page
    const pub = await openPublic(browser, `/${slug}`);
    const pubErrors = watchErrors(pub);
    await expect(pub.getByRole('heading', { level: 1, name: title })).toBeVisible();
    const main = pub.getByRole('main');
    await expect(main.getByRole('heading', { level: 2, name: 'What we promise' })).toBeVisible();
    await expect(main.getByText('We answer every enquiry within one working day.')).toBeVisible();
    await expect(main.getByRole('link', { name: 'Read our playbook' })).toHaveAttribute('href', '/blog');
    await expect(main.getByText('Click for a prize')).toBeVisible();
    await expect(main.getByRole('link', { name: 'Click for a prize' })).toHaveCount(0);
    expect(await main.locator('script, img[onerror], a[href^="javascript"]').count()).toBe(0);
    expect(await pub.evaluate(() => (window as { __pwned?: boolean }).__pwned)).toBeUndefined();
    await main.getByText('How fast do you reply?').click();
    await expect(main.getByText('one working day', { exact: false }).last()).toBeVisible();

    // SEO head: title template, description, canonical, Open Graph, JSON-LD.
    await expect(pub).toHaveTitle(`Service promise ${id} | Optimize All`);
    expect(await meta(pub, 'description')).toBe('Response times and service levels for every client.');
    expect(await meta(pub, 'og:title')).toBe(`Service promise ${id} | Optimize All`);
    expect(await meta(pub, 'og:url')).toMatch(new RegExp(`/${slug}$`));
    // Indexable pages state it explicitly (the server-rendered HTML and the head manager agree), with rich previews.
    expect(await meta(pub, 'robots')).toBe(
      'index, follow, max-image-preview:large, max-snippet:-1, max-video-preview:-1',
    );
    expect(await canonical(pub)).toMatch(new RegExp(`^https?://[^/]+/${slug}$`));
    const ld = await jsonLd(pub);
    const breadcrumbs = ld.find((x) => x['@type'] === 'BreadcrumbList') as
      { itemListElement: { name: string; item: string }[] } | undefined;
    expect(breadcrumbs?.itemListElement.map((i) => i.name)).toEqual(['Home', title]);
    const faqLd = ld.find((x) => x['@type'] === 'FAQPage') as
      { mainEntity: { name: string; acceptedAnswer: { text: string } }[] } | undefined;
    expect(faqLd?.mainEntity[0]?.name).toBe('How fast do you reply?');
    expect(await sitemapPaths()).toContain(`/${slug}`);
    pubErrors.expectClean('the published CMS page');

    // ---------------------------------------------------------------- edit → the change is live
    await form.getByLabel('Title', { exact: true }).first().fill(`${title} (2026)`);
    await rich
      .getByLabel('Content', { exact: true })
      .fill('## What we promise\n\nWe now answer every enquiry within **four working hours**.');
    await form.getByLabel('Change note (optional)').fill('Faster replies');
    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(toast(editor, 'Page saved')).toBeVisible();
    const after = await openPublic(browser, `/${slug}`);
    await expect(after.getByRole('heading', { level: 1, name: `${title} (2026)` })).toBeVisible();
    await expect(after.getByRole('main').getByText(/four working hours/)).toBeVisible();

    // ---------------------------------------------------------------- version history and restore
    const history = editor.getByRole('region', { name: 'Version history' });
    await expect(history.getByText('Version 3', { exact: true })).toBeVisible();
    await expect(history.getByText('Faster replies')).toBeVisible();
    await history.getByRole('button', { name: 'Preview version 2' }).click();
    const old = editor.getByRole('complementary', { name: 'Preview of version 2' });
    await expect(old.getByText('We answer every enquiry within one working day.')).toBeVisible();
    await history.getByRole('button', { name: 'Restore version 2' }).click();
    await modal(editor, 'Restore version 2?').getByRole('button', { name: 'Restore version' }).click();
    await expect(toast(editor, 'Version 2 restored')).toBeVisible();
    await expect(history.getByText('Version 4', { exact: true })).toBeVisible();
    const restored = await openPublic(browser, `/${slug}`);
    await expect(restored.getByRole('heading', { level: 1, name: title, exact: true })).toBeVisible();
    await expect(
      restored.getByRole('main').getByText('We answer every enquiry within one working day.'),
    ).toBeVisible();
    // Restoring never changes the publishing state.
    expect((await editorApi.get<SitePage>(`/agency/website/pages/${pageId}`)).isPublished).toBe(true);
  });

  test('duplicate and reserved slugs are refused with a field error', async ({ browser }) => {
    const { slug } = recall<{ slug: string }>('promisePage');
    const editor = await actor(browser, state().editor, landing.agency);
    await editor.goto('/agency/website/pages/new');
    const form = editorForm(editor);
    await form.getByLabel('Title', { exact: true }).first().fill('Copycat page');
    await form.getByLabel('Slug', { exact: true }).fill(slug);
    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(form.getByText(`Another page already uses the slug '${slug}'.`)).toBeVisible();

    await form.getByLabel('Slug', { exact: true }).fill('services');
    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(form.getByText('This address is used by a built-in page. Pick another slug.')).toBeVisible();
    await form.getByLabel('Slug', { exact: true }).fill('Not A Slug!');
    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(
      form.getByText('Use lower-case letters, digits and single dashes, e.g. local-seo.'),
    ).toBeVisible();

    // Every single-segment route of the web app is reserved: a CMS page there would never be reachable (the built-in
    // route wins) while the sitemap advertised it.
    const editorApi = await api(state().editor);
    for (const reserved of ['verify-email', 'reset-password', 'forgot-password', 'check-email', 'lp', 'f']) {
      const error = await refused(
        editorApi.post('/agency/website/pages', {
          slug: reserved,
          title: `Shadowed ${reserved}`,
          kind: 'Standard',
          blocks: [{ type: 'richText', data: { markdown: 'Hidden behind a built-in route.' } }],
          seo: {},
          isPublished: true,
        }),
      );
      expect(error.status, `slug "${reserved}"`).toBe(400);
      expect(JSON.stringify(error.body)).toContain('This address is used by a built-in page');
    }
    // Publishing needs content.
    const empty = await refused(
      editorApi.post('/agency/website/pages', {
        slug: `e2e-empty-${runId()}`,
        title: 'Empty',
        kind: 'Standard',
        blocks: [],
        seo: {},
        isPublished: true,
      }),
    );
    expect(empty.status).toBe(400);
    expect(JSON.stringify(empty.body)).toContain('Add at least one block before publishing.');
  });

  test('stale edits are refused (409) in the editor and through the API', async ({ browser }) => {
    const { id: pageId } = recall<{ id: string }>('promisePage');
    const editor = await actor(browser, state().editor, landing.agency);
    const admin = await actor(browser, accounts.admin, landing.admin);
    await editor.goto(`/agency/website/pages/${pageId}`);
    await admin.goto(`/agency/website/pages/${pageId}`);
    await expect(editorForm(editor).getByLabel('Summary (optional)')).toBeVisible();
    await expect(editorForm(admin).getByLabel('Summary (optional)')).toBeVisible();

    await editorForm(editor).getByLabel('Summary (optional)').fill('Edited by the website editor first.');
    await editorForm(editor).getByRole('button', { name: 'Save page' }).click();
    await expect(toast(editor, 'Page saved')).toBeVisible();

    const adminErrors = watchErrors(admin);
    adminErrors.ignore(/HTTP 409 PUT \S+\/agency\/website\/pages\//);
    await editorForm(admin).getByLabel('Summary (optional)').fill('The admin edits a stale copy.');
    await editorForm(admin).getByRole('button', { name: 'Save page' }).click();
    await expect(
      editorForm(admin).getByText('Someone else saved this page since you opened it.', { exact: false }),
    ).toBeVisible();
    adminErrors.expectClean('the stale editor');

    // The first edit won.
    const editorApi = await api(state().editor);
    const current = await editorApi.get<SitePage>(`/agency/website/pages/${pageId}`);
    expect(current.summary).toBe('Edited by the website editor first.');
    // A missing stamp is treated as stale, not as "skip the check".
    const noStamp = await refused(
      editorApi.put(`/agency/website/pages/${pageId}`, { ...input(current), concurrencyStamp: undefined }),
    );
    expect(noStamp.status).toBe(409);
    // Restoring a revision with a stale stamp is refused too.
    const staleRestore = await refused(
      editorApi.post(`/agency/website/pages/${pageId}/revisions/1/restore`, {
        concurrencyStamp: '00000000-0000-0000-0000-000000000001',
      }),
    );
    expect(staleRestore.status).toBe(409);
  });

  test('schedule → hidden until go-live; noindex → robots meta and out of the sitemap; unpublish → 404; delete', async ({
    browser,
  }) => {
    test.setTimeout(300_000);
    const id = runId();
    const slug = `e2e-launch-${id}`;
    const editorApi = await api(state().editor);
    // Arrange a published page through the API (the editor UI is covered above), then schedule it in the UI.
    const created = await editorApi.post<SitePage>('/agency/website/pages', {
      slug,
      title: `Launch notes ${id}`,
      kind: 'Standard',
      blocks: [{ type: 'richText', data: { markdown: 'Everything that ships this quarter.' } }],
      seo: {},
      isPublished: false,
    });

    const editor = await actor(browser, state().editor, landing.agency);
    await editor.goto(`/agency/website/pages/${created.id}`);
    const form = editorForm(editor);
    await editor.getByRole('switch', { name: 'Published', exact: true }).click();
    // A go-live time about two minutes ahead (minute precision in the input).
    const goLive = new Date(Date.now() + 120_000);
    const pad = (n: number) => String(n).padStart(2, '0');
    const local = `${goLive.getFullYear()}-${pad(goLive.getMonth() + 1)}-${pad(goLive.getDate())}T${pad(goLive.getHours())}:${pad(goLive.getMinutes())}`;
    await form.getByLabel('Go live at (optional)').fill(local);
    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(toast(editor, 'Page saved')).toBeVisible();

    // Scheduled: listed as such, but not public yet and not in the sitemap.
    await editor.goto('/agency/website/pages');
    const row = editor.getByRole('row').filter({ hasText: `Launch notes ${id}` });
    await expect(row.getByText('Scheduled')).toBeVisible();
    expect((await anonGet(`/api/v1/public/pages/${slug}`)).status).toBe(404);
    expect(await sitemapPaths()).not.toContain(`/${slug}`);

    // Once the time passes it is live without anyone touching it.
    await expect
      .poll(async () => (await anonGet(`/api/v1/public/pages/${slug}`)).status, {
        timeout: 200_000,
        intervals: [5_000],
        message: 'the scheduled page goes live at its time',
      })
      .toBe(200);
    expect(await sitemapPaths()).toContain(`/${slug}`);
    await editor.reload();
    await expect(
      editor
        .getByRole('row')
        .filter({ hasText: `Launch notes ${id}` })
        .getByText('Published'),
    ).toBeVisible();

    // noindex: robots meta on the page, gone from the sitemap.
    await editor.goto(`/agency/website/pages/${created.id}`);
    await editor.getByRole('switch', { name: 'Hide from search engines (noindex)' }).click();
    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(toast(editor, 'Page saved')).toBeVisible();
    const hidden = await openPublic(browser, `/${slug}`);
    await expect(hidden.getByRole('heading', { level: 1, name: `Launch notes ${id}` })).toBeVisible();
    await expect.poll(() => meta(hidden, 'robots')).toBe('noindex, nofollow');
    expect(await sitemapPaths()).not.toContain(`/${slug}`);

    // Unpublish → 404 for visitors.
    await editor.getByRole('switch', { name: 'Published', exact: true }).click();
    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(toast(editor, 'Page saved')).toBeVisible();
    const gone = await openPublic(browser, `/${slug}`);
    await expect(notFound(gone)).toBeVisible();

    // Delete (API only: the editor has no delete button for pages) → the admin API 404s as well.
    await editorApi.delete(`/agency/website/pages/${created.id}`);
    expect((await refused(editorApi.get(`/agency/website/pages/${created.id}`))).status).toBe(404);
    // The slug is free again.
    const reused = await editorApi.post<SitePage>('/agency/website/pages', {
      slug,
      title: 'Reused address',
      kind: 'Standard',
      blocks: [{ type: 'richText', data: { markdown: 'A new page at an old address.' } }],
      seo: {},
      isPublished: false,
    });
    await editorApi.delete(`/agency/website/pages/${reused.id}`);
  });
});
