import { type Page, expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  anonGet,
  api,
  landing,
  modal,
  notFound,
  openPublic,
  refused,
  runId,
  sitemapPaths,
  state,
  toast,
  watchErrors,
} from './support/content';

/**
 * Redirects (Website → Redirects), as the website editor and the admin:
 *   renaming a live CMS page in the editor → the old address answers a full page load with a real 301 (query string
 *   kept) and in-app links to it land on the new page (client-side) → the sitemap lists only the new address → the
 *   automatic redirect is listed → a manual redirect to the old address is collapsed to the final one, works, and is
 *   deleted again → renaming back reclaims the address (no loop). Renamed blog posts, services, service lines and case
 *   studies redirect the same way. Negatives: a redirect from a live address, from a built-in page, or to another site.
 */

interface SitePage {
  id: string;
  slug: string;
  title: string;
  isPublished: boolean;
  concurrencyStamp: string;
}

interface Redirect {
  id: string;
  fromPath: string;
  toPath: string;
  source: string;
}

const richPage = (slug: string, title: string, markdown: string) => ({
  slug,
  title,
  kind: 'Standard',
  blocks: [{ type: 'richText', data: { markdown } }],
  seo: {},
  isPublished: true,
});

/** Status and Location of a full page load of `path` (without following redirects). */
async function hop(page: Page, path: string) {
  const res = await page.request.get(path, { maxRedirects: 0 });
  return { status: res.status(), location: res.headers()['location'] ?? null };
}

test.describe.serial('Redirects', () => {
  test('renaming a live page: 301 for full loads, client-side redirect for app links, sitemap, Website → Redirects', async ({
    browser,
  }) => {
    test.setTimeout(240_000);
    const id = runId();
    const [oldSlug, newSlug] = [`e2e-our-approach-${id}`, `e2e-how-we-work-${id}`];
    const title = `How we work ${id}`;
    const editorApi = await api(state().editor);
    const moving = await editorApi.post<SitePage>(
      '/agency/website/pages',
      richPage(oldSlug, title, 'Discovery, then delivery.'),
    );
    await editorApi.post<SitePage>(
      '/agency/website/pages',
      richPage(`e2e-links-${id}`, `Links ${id}`, `Read [our approach](/${oldSlug}) before we start.`),
    );

    // ---------------------------------------------------------------- rename in the editor
    const editor = await actor(browser, state().editor, landing.agency);
    const errors = watchErrors(editor);
    await editor.goto(`/agency/website/pages/${moving.id}`);
    const form = editor.getByRole('form', { name: 'Page editor' });
    await form.getByLabel('Slug', { exact: true }).fill(newSlug);
    await form.getByRole('button', { name: 'Save page' }).click();
    await expect(toast(editor, 'Page saved')).toBeVisible();
    errors.expectClean('renaming a page');

    // ---------------------------------------------------------------- the old address
    const visitor = await openPublic(browser, `/${newSlug}`);
    await expect(visitor.getByRole('heading', { level: 1, name: title })).toBeVisible();
    // A full page load (browsers, crawlers) gets a real 301 from the web server, keeping the query string.
    expect(await hop(visitor, `/${oldSlug}?utm_source=e2e`)).toEqual({
      status: 301,
      location: `/${newSlug}?utm_source=e2e`,
    });
    const followed = await openPublic(browser, `/${oldSlug}`);
    await expect(followed).toHaveURL(new RegExp(`/${newSlug}$`));
    await expect(followed.getByRole('heading', { level: 1, name: title })).toBeVisible();
    // A link inside the app (client-side navigation) lands on the new page too.
    const reader = await openPublic(browser, `/e2e-links-${id}`);
    const readerErrors = watchErrors(reader);
    readerErrors.ignore(/HTTP 404 GET \S+\/public\/pages\//);
    await reader.getByRole('main').getByRole('link', { name: 'our approach' }).click();
    await expect(reader).toHaveURL(new RegExp(`/${newSlug}$`));
    await expect(reader.getByRole('heading', { level: 1, name: title })).toBeVisible();
    readerErrors.expectClean('following an in-app link to a moved page');
    // Search engines are told about the new address only.
    const sitemap = await sitemapPaths();
    expect(sitemap).toContain(`/${newSlug}`);
    expect(sitemap).not.toContain(`/${oldSlug}`);

    // ---------------------------------------------------------------- Website → Redirects
    await editor
      .getByRole('navigation', { name: 'Agency navigation' })
      .getByRole('link', { name: 'Redirects', exact: true })
      .click();
    await expect(editor.getByRole('heading', { level: 1, name: 'Redirects' })).toBeVisible();
    await editor.getByRole('searchbox', { name: 'Search addresses' }).fill(id);
    const table = editor.getByRole('table', { name: 'Redirects' });
    const row = table.getByRole('row').filter({ hasText: `/${oldSlug}` });
    await expect(row).toContainText(`/${newSlug}`);
    await expect(row).toContainText('Page renamed');

    // A manual redirect to the old address points at the final one (no chains).
    const campaign = `/e2e-spring-campaign-${id}`;
    await editor.getByRole('button', { name: 'Add redirect' }).click();
    const dialog = modal(editor, 'Add redirect');
    await dialog.getByLabel(/Old address/).fill(campaign);
    await dialog.getByLabel(/New address/).fill(`/${oldSlug}`);
    await dialog.getByRole('button', { name: 'Add redirect' }).click();
    await expect(toast(editor, 'Redirect added')).toBeVisible();
    await expect(table.getByRole('row').filter({ hasText: campaign })).toContainText(`/${newSlug}`);
    expect(await hop(visitor, campaign)).toEqual({ status: 301, location: `/${newSlug}` });

    // Refused: a live address, a built-in page, another site.
    await editor.getByRole('button', { name: 'Add redirect' }).click();
    await dialog.getByLabel(/Old address/).fill(`/${newSlug}`);
    await dialog.getByLabel(/New address/).fill('/contact');
    await dialog.getByRole('button', { name: 'Add redirect' }).click();
    await expect(dialog.getByText('This address shows published content.', { exact: false })).toBeVisible();
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    const admin = await api(accounts.admin);
    expect(
      (await refused(admin.post('/agency/website/redirects', { fromPath: '/blog', toPath: '/x' }))).status,
    ).toBe(400);
    expect(
      (
        await refused(
          admin.post('/agency/website/redirects', {
            fromPath: `/e2e-x-${id}`,
            toPath: 'https://evil.example/',
          }),
        )
      ).status,
    ).toBe(400);

    // Delete the manual redirect: the address is a 404 again.
    await editor.getByRole('button', { name: `Actions for ${campaign}` }).click();
    await editor.getByRole('menuitem', { name: 'Delete redirect…' }).click();
    await modal(editor, `Delete the redirect from ${campaign}?`)
      .getByRole('button', { name: 'Delete redirect' })
      .click();
    await expect(toast(editor, 'Redirect deleted')).toBeVisible();
    await expect(table.getByRole('row').filter({ hasText: campaign })).toHaveCount(0);
    // A real 404 (the server-rendered not-found page, docs/SEO_CRO.md § 9.2), with no redirect; the app shows "not found".
    expect(await hop(visitor, campaign)).toEqual({ status: 404, location: null });
    await expect(notFound(await openPublic(browser, campaign))).toBeVisible();

    // ---------------------------------------------------------------- renaming back reclaims the address (no loop)
    const current = await editorApi.get<SitePage & Record<string, unknown>>(
      `/agency/website/pages/${moving.id}`,
    );
    await editorApi.put(`/agency/website/pages/${moving.id}`, { ...current, slug: oldSlug });
    expect((await hop(visitor, `/${oldSlug}`)).status).toBe(200);
    expect(await hop(visitor, `/${newSlug}`)).toEqual({ status: 301, location: `/${oldSlug}` });
    const list = await editorApi.get<{ items: Redirect[] }>(`/agency/website/redirects?search=${id}`);
    expect(list.items.find((r) => r.fromPath === `/${oldSlug}`)).toBeUndefined();
    expect(list.items.every((r) => r.fromPath !== r.toPath)).toBe(true);
  });

  test('renamed posts, services, service lines and case studies redirect to their new addresses', async ({
    browser,
  }) => {
    const id = runId();
    const admin = await api(accounts.admin);
    const visitor = await openPublic(browser, '/');

    // Blog post (renamed while live).
    const body = Array(6)
      .fill('Redirects keep old links and search rankings working after a rename.')
      .join(' ');
    let post = await admin.post<{ id: string; concurrencyStamp: string }>('/agency/website/blog/posts', {
      slug: `e2e-moved-post-${id}`,
      title: `Moved post ${id}`,
      excerpt: 'A post that changes its address.',
      bodyMarkdown: body,
    });
    post = await admin.post(`/agency/website/blog/posts/${post.id}/publish`, {
      concurrencyStamp: post.concurrencyStamp,
    });
    await admin.put(`/agency/website/blog/posts/${post.id}`, {
      slug: `e2e-renamed-post-${id}`,
      title: `Moved post ${id}`,
      excerpt: 'A post that changes its address.',
      bodyMarkdown: body,
      concurrencyStamp: post.concurrencyStamp,
    });
    expect(await hop(visitor, `/blog/e2e-moved-post-${id}`)).toEqual({
      status: 301,
      location: `/blog/e2e-renamed-post-${id}`,
    });
    const blog = await openPublic(browser, `/blog/e2e-moved-post-${id}`);
    await expect(blog).toHaveURL(new RegExp(`/blog/e2e-renamed-post-${id}$`));
    await expect(blog.getByRole('heading', { level: 1, name: `Moved post ${id}` })).toBeVisible();
    const sitemap = await sitemapPaths();
    expect(sitemap).toContain(`/blog/e2e-renamed-post-${id}`);
    expect(sitemap).not.toContain(`/blog/e2e-moved-post-${id}`);

    // Service line and service.
    const line = await admin.post<{ id: string; concurrencyStamp: string }>(
      '/agency/website/service-categories',
      {
        slug: `e2e-line-${id}`,
        name: `E2E line ${id}`,
        isPublished: true,
      },
    );
    const service = (slug: string, stamp?: string) => ({
      categoryId: line.id,
      slug,
      name: `E2E moving service ${id}`,
      tagline: 'Moves around',
      overviewMarkdown: 'An overview.',
      deliverables: ['A deliverable'],
      isPublished: true,
      concurrencyStamp: stamp,
    });
    const svc = await admin.post<{ id: string; concurrencyStamp: string }>(
      '/agency/website/services',
      service(`e2e-svc-${id}`),
    );
    await admin.put(`/agency/website/services/${svc.id}`, service(`e2e-svc-new-${id}`, svc.concurrencyStamp));
    expect(await hop(visitor, `/services/e2e-svc-${id}`)).toEqual({
      status: 301,
      location: `/services/e2e-svc-new-${id}`,
    });
    await admin.put(`/agency/website/service-categories/${line.id}`, {
      slug: `e2e-line-new-${id}`,
      name: `E2E line ${id}`,
      isPublished: true,
      concurrencyStamp: line.concurrencyStamp,
    });
    const filtered = await openPublic(browser, `/services?category=e2e-line-${id}`);
    await expect(filtered).toHaveURL(new RegExp(`/services\\?category=e2e-line-new-${id}$`));
    await expect(filtered.getByRole('button', { name: `E2E line ${id}`, pressed: true })).toBeVisible();

    // Case study.
    const caseStudy = (slug: string, stamp?: string) => ({
      slug,
      title: `E2E moving case ${id}`,
      clientName: 'Acme',
      summary: 'Results that moved.',
      isPublished: true,
      metrics: [{ label: 'Leads', value: '+50%', measurement: 'Measured' }],
      concurrencyStamp: stamp,
    });
    const cs = await admin.post<{ id: string; concurrencyStamp: string }>(
      '/agency/website/case-studies',
      caseStudy(`e2e-case-${id}`),
    );
    await admin.put(
      `/agency/website/case-studies/${cs.id}`,
      caseStudy(`e2e-case-new-${id}`, cs.concurrencyStamp),
    );
    expect(await hop(visitor, `/case-studies/e2e-case-${id}`)).toEqual({
      status: 301,
      location: `/case-studies/e2e-case-new-${id}`,
    });
    expect((await anonGet(`/api/v1/public/case-studies/e2e-case-${id}`)).status).toBe(404);

    // Clean up the catalog entries (the redirects stay, pointing at addresses that are now gone).
    await admin.delete(`/agency/website/services/${svc.id}`);
    await admin.delete(`/agency/website/service-categories/${line.id}`);
    await admin.delete(`/agency/website/case-studies/${cs.id}`);
  });
});
