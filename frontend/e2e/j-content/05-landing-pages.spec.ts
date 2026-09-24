import { type Page, expect, test } from '@playwright/test';
import {
  API_URL,
  accounts,
  actor,
  anonGet,
  api,
  clients,
  imageForm,
  landing,
  meta,
  modal,
  openPublic,
  recall,
  refused,
  remember,
  runId,
  toast,
  watchErrors,
} from './support/content';

/**
 * Landing-page builder as the designer (forms.manage), for Nimbus Fitness:
 *   new blank page → blocks (hero, text with markup typed as text, the client's form) → SEO settings (title,
 *   description, uploaded Open Graph image, noindex) → publish → the public /lp page (and its head tags) → draft edits
 *   (content and the URL slug) stay private until the next publish → versions and restoring one → unpublish, archive,
 *   restore. Negatives: duplicate slug, unsafe links, another client's form, editing an archived page.
 *   A/B test: two variants, sticky assignment per visitor, bots not counted, views/uniques/submissions per variant.
 */

interface Detail {
  id: string;
  clientAccountId: string;
  slug: string;
  name: string;
  status: string;
  publicPath: string;
  concurrencyStamp: string;
  hasUnpublishedChanges: boolean;
  publishedVersion: number | null;
  metaTitle: string | null;
  metaDescription: string | null;
  ogImageUrl: string | null;
  noIndex: boolean;
  experimentEnabled: boolean;
  variants: {
    key: string;
    name: string;
    weight: number;
    blocks: { id: string; type: string; props: Record<string, unknown> }[];
  }[];
}

type Blocks = Detail['variants'][number]['blocks'];

const put = (d: Detail, patch: Partial<Detail>) => {
  const m = { ...d, ...patch };
  return {
    name: m.name,
    slug: m.slug,
    metaTitle: m.metaTitle,
    metaDescription: m.metaDescription,
    ogImageUrl: m.ogImageUrl,
    noIndex: m.noIndex,
    experimentEnabled: m.experimentEnabled,
    variants: m.variants,
    concurrencyStamp: m.concurrencyStamp,
  };
};

const blocks = (headline: string, formId: string): Blocks => [
  {
    id: 'hero',
    type: 'hero',
    props: {
      headline,
      subheadline: 'Eight weeks, three sessions a week, one coach.',
      align: 'center',
      theme: 'brand',
    },
  },
  {
    id: 'story',
    type: 'text',
    props: {
      heading: 'Why it works',
      body: 'Small groups. Real coaching. <b onclick="window.__pwned=1">No gimmicks.</b>',
    },
  },
  { id: 'signup', type: 'form', props: { formId, heading: 'Save your spot' } },
];

async function openBuilder(page: Page, id: string) {
  await page.goto(`/agency/pages/${id}`);
  await expect(page.getByRole('button', { name: 'Publish', exact: true })).toBeVisible();
}

test.describe.serial('landing pages', () => {
  test('build → publish → public page; draft edits (content and slug) stay private until the next publish', async ({
    browser,
  }) => {
    const id = runId();
    const name = `E2E Bootcamp ${id}`;
    const slug = `bootcamp-${id}`;
    const designerApi = await api(accounts.designer);
    const options = await designerApi.get<{ id: string; slug: string; name: string }[]>(
      '/agency/pages/client-options',
    );
    const nimbus = options.find((c) => c.slug === clients.nimbus.slug)!;
    const aurora = options.find((c) => c.slug === clients.aurora.slug)!;
    // The client's sign-up form (the form builder is covered in 06).
    const form = await designerApi.post<{ id: string }>('/agency/pages/forms', {
      clientAccountId: nimbus.id,
      name: `Bootcamp sign-up ${id}`,
      templateKey: 'contact',
    });
    const auroraForm = await designerApi.post<{ id: string }>('/agency/pages/forms', {
      clientAccountId: aurora.id,
      name: `Aurora form ${id}`,
      templateKey: 'contact',
    });
    remember('bootcampForm', { id: form.id, clientId: nimbus.id, auroraClientId: aurora.id });

    // ---------------------------------------------------------------- create a blank page in the UI
    const designer = await actor(browser, accounts.designer, landing.agency);
    const errors = watchErrors(designer);
    await designer.goto('/agency/pages');
    await designer.getByRole('button', { name: 'New page' }).first().click();
    const create = modal(designer, 'New landing page');
    await create.getByLabel('Client', { exact: true }).selectOption({ label: clients.nimbus.name });
    await create.getByLabel('Page name', { exact: true }).fill(name);
    await create.getByLabel(/^URL slug/).fill(slug);
    await expect(create.getByLabel('Template', { exact: true })).toHaveValue('');
    await create.getByRole('button', { name: 'Create page' }).click();
    await expect(toast(designer, 'Page created')).toBeVisible();
    await expect(designer).toHaveURL(/\/agency\/pages\/[0-9a-f-]{36}$/);
    const pageId = designer.url().split('/').pop()!;
    remember('bootcampPage', { id: pageId, slug, name });
    const publicPath = `/lp/${clients.nimbus.slug}/${slug}`;

    // A duplicate slug for the same client is refused; the same slug for another client is fine.
    const dup = await refused(
      designerApi.post('/agency/pages/landing-pages', { clientAccountId: nimbus.id, name: 'Dup', slug }),
    );
    expect(dup.status).toBe(409);
    expect(dup.code).toBe('landing.slug_taken');
    const other = await designerApi.post<Detail>('/agency/pages/landing-pages', {
      clientAccountId: aurora.id,
      name: 'Same slug elsewhere',
      slug,
    });
    await designerApi.delete(`/agency/pages/landing-pages/${other.id}`);

    // ---------------------------------------------------------------- blocks (arranged through the API), validated server-side
    let page = await designerApi.get<Detail>(`/agency/pages/landing-pages/${pageId}`);
    const unsafe = await refused(
      designerApi.put(
        `/agency/pages/landing-pages/${pageId}`,
        put(page, {
          variants: [
            {
              key: 'A',
              name: 'Control',
              weight: 50,
              blocks: [
                {
                  id: 'h',
                  type: 'hero',
                  props: { headline: 'x', ctaLabel: 'Go', ctaHref: 'javascript:alert(1)' },
                },
              ],
            },
          ],
        }),
      ),
    );
    expect(unsafe.status).toBe(400);
    expect(JSON.stringify(unsafe.body)).toContain('Links must be https/http URLs');
    const foreign = await refused(
      designerApi.put(
        `/agency/pages/landing-pages/${pageId}`,
        put(page, {
          variants: [{ key: 'A', name: 'Control', weight: 50, blocks: blocks('x', auroraForm.id) }],
        }),
      ),
    );
    expect(foreign.status, "another client's form cannot be placed on this client's page").toBe(400);
    const script = await refused(
      designerApi.put(
        `/agency/pages/landing-pages/${pageId}`,
        put(page, {
          variants: [
            {
              key: 'A',
              name: 'Control',
              weight: 50,
              blocks: [{ id: 't', type: 'text', props: { body: 'Hi <script>alert(1)</script>' } }],
            },
          ],
        }),
      ),
    );
    expect(script.status).toBe(400);
    expect(JSON.stringify(script.body)).toContain('Scripts, frames and executable URLs are not allowed');
    page = await designerApi.put<Detail>(
      `/agency/pages/landing-pages/${pageId}`,
      put(page, {
        variants: [
          { key: 'A', name: 'Control', weight: 50, blocks: blocks('Get fit in eight weeks', form.id) },
        ],
      }),
    );

    // ---------------------------------------------------------------- SEO settings in the UI, then publish
    await openBuilder(designer, pageId);
    await designer.getByRole('tab', { name: 'SEO & settings' }).click();
    await designer.getByLabel('SEO title', { exact: true }).fill(`Nimbus Bootcamp ${id}`);
    await designer
      .getByLabel('Meta description', { exact: true })
      .fill('An eight-week small-group bootcamp in central Manchester.');
    const og = await designerApi.upload<{ url: string }>(
      '/agency/pages/images',
      imageForm((Number.parseInt(id.slice(-4), 36) % 977) + 3, 1200, 630),
    );
    await designer.getByLabel('Open Graph image URL (optional)').fill(og.url);
    await designer.getByRole('button', { name: 'Save draft' }).click();
    await expect(designer.getByRole('button', { name: 'Save draft' })).toBeDisabled();

    // Not public before publishing.
    const before = await openPublic(browser, publicPath);
    await expect(before.getByRole('heading', { level: 1, name: 'This page isn’t available' })).toBeVisible();

    await designer.getByRole('button', { name: 'Publish', exact: true }).click();
    await expect(toast(designer, 'Version 1 is live')).toBeVisible();
    await expect(designer.getByRole('link', { name: 'View live' })).toHaveAttribute('href', publicPath);
    errors.expectClean('the page builder');

    // ---------------------------------------------------------------- the public page
    const pub = await openPublic(browser, publicPath);
    const pubErrors = watchErrors(pub);
    await expect(pub.getByRole('heading', { level: 1, name: 'Get fit in eight weeks' })).toBeVisible();
    const main = pub.getByRole('main');
    await expect(
      main.getByText('<b onclick="window.__pwned=1">No gimmicks.</b>', { exact: false }),
    ).toBeVisible(); // shown as text
    expect(await main.locator('b[onclick]').count()).toBe(0);
    expect(await pub.evaluate(() => (window as { __pwned?: number }).__pwned)).toBeUndefined();
    await expect(main.getByLabel('Full name')).toBeVisible();
    await expect(pub).toHaveTitle(`Nimbus Bootcamp ${id}`);
    expect(await meta(pub, 'description')).toBe('An eight-week small-group bootcamp in central Manchester.');
    expect(await meta(pub, 'robots')).toBeNull();
    // The Open Graph settings of the page reach the head (title, description, image as an absolute URL).
    await expect.poll(() => meta(pub, 'og:image')).toMatch(new RegExp(`^https?://[^/]+${og.url}$`));
    expect(await meta(pub, 'og:title')).toBe(`Nimbus Bootcamp ${id}`);
    expect(await meta(pub, 'og:description')).toBe(
      'An eight-week small-group bootcamp in central Manchester.',
    );
    const ogImage = await fetch(`${API_URL}${og.url}`);
    expect(ogImage.status).toBe(200);
    pubErrors.expectClean('the public landing page');

    // ---------------------------------------------------------------- a draft edit is not live until it is published
    await designer.getByRole('tab', { name: 'Build' }).click();
    page = await designerApi.get<Detail>(`/agency/pages/landing-pages/${pageId}`);
    page = await designerApi.put<Detail>(
      `/agency/pages/landing-pages/${pageId}`,
      put(page, {
        variants: [
          { key: 'A', name: 'Control', weight: 50, blocks: blocks('Get fit in six weeks', form.id) },
        ],
      }),
    );
    expect(page.hasUnpublishedChanges).toBe(true);
    const stillOld = (await anonGet(`/api/v1/public/lp/${clients.nimbus.slug}/${slug}`)).json() as {
      blocks: Blocks;
      version: number;
    };
    expect(stillOld.version).toBe(1);
    expect(stillOld.blocks[0]!.props.headline).toBe('Get fit in eight weeks');

    // The URL slug is part of the page too: renaming it in the draft must not move (or break) the live page.
    const newSlug = `bootcamp-six-weeks-${id}`;
    page = await designerApi.put<Detail>(
      `/agency/pages/landing-pages/${pageId}`,
      put(page, { slug: newSlug }),
    );
    expect(
      (await anonGet(`/api/v1/public/lp/${clients.nimbus.slug}/${slug}`)).status,
      'the live URL keeps working until the rename is published',
    ).toBe(200);
    expect(
      (await anonGet(`/api/v1/public/lp/${clients.nimbus.slug}/${newSlug}`)).status,
      'the new URL is not live before publishing',
    ).toBe(404);
    expect(page.publicPath, '"View live" still points at the live URL').toBe(publicPath);
    // Nobody else can take the live address while the rename is pending.
    const squat = await refused(
      designerApi.post('/agency/pages/landing-pages', { clientAccountId: nimbus.id, name: 'Squatter', slug }),
    );
    expect(squat.status).toBe(409);

    await openBuilder(designer, pageId);
    await expect(designer.getByText('Unpublished changes')).toBeVisible();
    await designer.getByRole('button', { name: 'Publish', exact: true }).click();
    await expect(toast(designer, 'Version 2 is live')).toBeVisible();
    const newPath = `/lp/${clients.nimbus.slug}/${newSlug}`;
    await expect(designer.getByRole('link', { name: 'View live' })).toHaveAttribute('href', newPath);
    expect((await anonGet(`/api/v1/public/lp/${clients.nimbus.slug}/${slug}`)).status).toBe(404);
    const moved = await openPublic(browser, newPath);
    await expect(moved.getByRole('heading', { level: 1, name: 'Get fit in six weeks' })).toBeVisible();
    remember('bootcampPage', { id: pageId, slug: newSlug, name });

    // ---------------------------------------------------------------- versions: restore v1 into the draft and publish it
    const versions = await designerApi.get<{ id: string; version: number; isCurrent: boolean }[]>(
      `/agency/pages/landing-pages/${pageId}/versions`,
    );
    expect(versions.map((v) => [v.version, v.isCurrent])).toEqual([
      [2, true],
      [1, false],
    ]);
    await designerApi.post(`/agency/pages/landing-pages/${pageId}/versions/${versions[1]!.id}/restore`);
    await designerApi.post(`/agency/pages/landing-pages/${pageId}/publish`);
    const restored = (await anonGet(`/api/v1/public/lp/${clients.nimbus.slug}/${newSlug}`)).json() as {
      blocks: Blocks;
      version: number;
    };
    expect(restored.version).toBe(3);
    expect(restored.blocks[0]!.props.headline).toBe('Get fit in eight weeks');

    // ---------------------------------------------------------------- noindex, unpublish, archive, restore
    page = await designerApi.get<Detail>(`/agency/pages/landing-pages/${pageId}`);
    page = await designerApi.put<Detail>(
      `/agency/pages/landing-pages/${pageId}`,
      put(page, { noIndex: true }),
    );
    await designerApi.post(`/agency/pages/landing-pages/${pageId}/publish`);
    const hidden = await openPublic(browser, newPath);
    await expect(hidden.getByRole('heading', { level: 1 })).toBeVisible();
    await expect.poll(() => meta(hidden, 'robots')).toBe('noindex, nofollow');
    // Landing pages are never listed in the site's sitemap (they belong to clients).
    expect((await anonGet('/api/v1/public/sitemap.xml')).text).not.toContain('/lp/');

    await designerApi.post(`/agency/pages/landing-pages/${pageId}/unpublish`);
    const offline = await openPublic(browser, newPath);
    await expect(offline.getByRole('heading', { level: 1, name: 'This page isn’t available' })).toBeVisible();
    await designerApi.delete(`/agency/pages/landing-pages/${pageId}`);
    const archived = await designerApi.get<Detail>(`/agency/pages/landing-pages/${pageId}`);
    expect(archived.status).toBe('Archived');
    const editArchived = await refused(
      designerApi.put(
        `/agency/pages/landing-pages/${pageId}`,
        put(archived, { name: 'Edited while archived' }),
      ),
    );
    expect(editArchived.status).toBe(409);
    expect(editArchived.code).toBe('landing.archived');
    expect((await refused(designerApi.post(`/agency/pages/landing-pages/${pageId}/publish`))).code).toBe(
      'landing.archived',
    );
    const list = await designerApi.get<{ items: { id: string }[] }>(
      `/agency/pages/landing-pages?clientId=${nimbus.id}&pageSize=200`,
    );
    expect(list.items.map((p) => p.id)).not.toContain(pageId);

    const restoredDraft = await designerApi.post<Detail>(`/agency/pages/landing-pages/${pageId}/restore`);
    expect(restoredDraft.status).toBe('Draft');
    await designerApi.post(`/agency/pages/landing-pages/${pageId}/publish`);
    expect((await anonGet(`/api/v1/public/lp/${clients.nimbus.slug}/${newSlug}`)).status).toBe(200);
  });

  test('A/B test: sticky variants per visitor, bots not counted, conversions per variant', async () => {
    const id = runId();
    const designerApi = await api(accounts.designer);
    const { id: formId, clientId } = recall<{ id: string; clientId: string }>('bootcampForm');
    const created = await designerApi.post<Detail>('/agency/pages/landing-pages', {
      clientAccountId: clientId,
      name: `Split test ${id}`,
      slug: `split-${id}`,
    });
    // Turning the test on with a single variant is refused.
    const single = await refused(
      designerApi.put(`/agency/pages/landing-pages/${created.id}`, put(created, { experimentEnabled: true })),
    );
    expect(single.code).toBe('landing.experiment_needs_variants');
    const page = await designerApi.put<Detail>(
      `/agency/pages/landing-pages/${created.id}`,
      put(created, {
        experimentEnabled: true,
        variants: [
          { key: 'A', name: 'Control', weight: 50, blocks: blocks('Headline A', formId) },
          { key: 'B', name: 'Urgency', weight: 50, blocks: blocks('Only 12 spots left', formId) },
        ],
      }),
    );
    expect(page.experimentEnabled).toBe(true);
    await designerApi.post(`/agency/pages/landing-pages/${created.id}/publish`);

    const view = async (visitor: string, ua = 'Mozilla/5.0 (X11; Linux x86_64) E2E') => {
      const res = await fetch(`${API_URL}/api/v1/public/lp/${clients.nimbus.slug}/split-${id}`, {
        headers: { 'X-Visitor-Id': visitor, 'User-Agent': ua, 'X-Requested-With': 'fetch' },
      });
      expect(res.status).toBe(200);
      return (await res.json()) as {
        variantKey: string;
        forms: { id: string; token: string }[];
        pageId: string;
      };
    };
    const seen: Record<string, string> = {};
    for (let i = 0; i < 16; i++) seen[`visitor-${id}-${i}`] = (await view(`visitor-${id}-${i}`)).variantKey;
    const keys = new Set(Object.values(seen));
    expect(keys, 'both variants are served across 16 visitors').toEqual(new Set(['A', 'B']));
    // Sticky: the same visitor always sees the same variant.
    for (const visitor of Object.keys(seen).slice(0, 4))
      expect((await view(visitor)).variantKey).toBe(seen[visitor]);
    // Crawlers are not counted (and are not assigned).
    await view(`bot-${id}`, 'Googlebot/2.1 (+http://www.google.com/bot.html)');

    // One conversion from a visitor who saw B (through the public form endpoint, with its render token).
    const visitorB = Object.keys(seen).find((v) => seen[v] === 'B')!;
    const lp = await view(visitorB);
    const form = lp.forms[0]!;
    const { minFillSeconds } = await designerApi.get<{ minFillSeconds: number }>(
      `/agency/pages/forms/${formId}`,
    );
    await new Promise((r) => setTimeout(r, (minFillSeconds + 1) * 1000));
    const res = await fetch(`${API_URL}/api/v1/public/forms/${form.id}/submissions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
      body: JSON.stringify({
        token: form.token,
        landingPageId: lp.pageId,
        variantKey: 'B',
        values: {
          name: 'Bea Variant',
          email: `bea.${id}@e2e.optimizeall.test`,
          topic: 'sales-enquiry',
          message: 'Saw the urgency banner, want a spot.',
          consent: true,
        },
      }),
    });
    expect(res.status).toBe(201);

    const analytics = await designerApi.get<{
      views: number;
      uniqueVisitors: number;
      submissions: number;
      variants: {
        key: string;
        views: number;
        uniqueVisitors: number;
        submissions: number;
        assigned: number;
      }[];
    }>(`/agency/pages/landing-pages/${created.id}/analytics`);
    const byKey = Object.fromEntries(analytics.variants.map((v) => [v.key, v]));
    expect(analytics.views).toBe(16 + 4 + 1);
    expect(analytics.uniqueVisitors).toBe(16);
    expect(byKey.A!.uniqueVisitors + byKey.B!.uniqueVisitors).toBe(16);
    expect(byKey.A!.assigned + byKey.B!.assigned).toBe(16);
    expect(byKey.B!.submissions).toBe(1);
    expect(byKey.A!.submissions).toBe(0);
    expect(analytics.submissions).toBe(1);
    // The submission is attributed to the page and variant.
    const subs = await designerApi.get<{
      items: { email: string; landingPageName: string; variantKey: string }[];
    }>(`/agency/pages/forms/${formId}/submissions?landingPageId=${created.id}`);
    expect(subs.items).toEqual([
      expect.objectContaining({
        email: `bea.${id}@e2e.optimizeall.test`,
        landingPageName: `Split test ${id}`,
        variantKey: 'B',
      }),
    ]);
    await designerApi.delete(`/agency/pages/landing-pages/${created.id}`);
  });
});
