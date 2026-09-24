import { expect, test } from '@playwright/test';
import {
  API_URL,
  accounts,
  actor,
  anonGet,
  api,
  canonical,
  jsonLd,
  landing,
  meta,
  openPublic,
  recall,
  refused,
  runId,
  sitemapPaths,
  state,
  toast,
  watchErrors,
} from './support/content';

/**
 * Site-wide settings and SEO, as the website editor:
 *   announcement bar, a header menu item, the footer blurb and the title template → visible on every public page; an
 *   invalid analytics id and a non-https site URL are refused per field; a stale settings save is a 409.
 *   Page texts (built-in copy): change the home hero eyebrow → live; markup is stripped; reset → default.
 *   SEO infrastructure: the organization JSON-LD on the home page, robots.txt, and the site URL driving canonical links,
 *   the sitemap and robots. Everything is restored at the end (other suites share the demo site).
 */

interface SettingsEnvelope {
  settings: Record<string, unknown> & {
    announcement: { enabled: boolean; text: string | null; linkLabel: string | null; linkUrl: string | null };
    header: {
      menu: { label: string; url: string | null; description: string | null; children: unknown[] | null }[];
      cta: unknown;
    };
    footer: { blurb: string | null; columns: unknown[]; legalLinks: unknown[] };
    seo: {
      siteUrl: string | null;
      titleTemplate: string;
      defaultTitle: string;
      defaultDescription: string | null;
    };
    analytics: { ga4MeasurementId: string | null; gtmContainerId: string | null; metaPixelId: string | null };
    organization: Record<string, unknown>;
  };
  concurrencyStamp: string;
}

test.describe.serial('site settings, page texts and SEO', () => {
  let original: SettingsEnvelope;

  test.beforeAll(async () => {
    original = await (await api(state().editor)).get<SettingsEnvelope>('/agency/website/settings');
  });

  test.afterAll(async () => {
    // Restore the settings the demo site started with (whatever happened above).
    const editorApi = await api(state().editor);
    const current = await editorApi.get<SettingsEnvelope>('/agency/website/settings');
    await editorApi.put('/agency/website/settings', {
      settings: original.settings,
      concurrencyStamp: current.concurrencyStamp,
    });
  });

  test('announcement, navigation, footer and title template reach every public page; invalid fields are refused', async ({
    browser,
  }) => {
    const id = runId();
    const promise = recall<{ slug: string; title: string }>('promisePage');
    const editor = await actor(browser, state().editor, landing.agency);
    const errors = watchErrors(editor);
    errors.ignore(/HTTP 400 PUT \S+\/agency\/website\/settings$/);
    await editor
      .getByRole('navigation', { name: 'Agency navigation' })
      .getByRole('link', { name: 'Site settings' })
      .click();
    await expect(editor.getByRole('heading', { level: 1, name: 'Site settings' })).toBeVisible();

    // General: the announcement bar.
    await editor.getByRole('switch', { name: 'Show the announcement bar' }).click();
    await editor.getByLabel('Text (optional)').fill(`Spring audit slots are open ${id}`);
    await editor.getByLabel('Link label (optional)').fill('Book yours');
    await editor.getByLabel('Link (optional)').fill('/free-audit');

    // Navigation: a new top-level item pointing at the CMS page from 01.
    await editor.getByRole('tab', { name: /^Navigation/ }).click();
    const menu = editor.getByRole('group', { name: 'Header menu' });
    await menu.getByRole('button', { name: 'Add menu item' }).last().click();
    const items = menu.getByRole('listitem');
    const last = items.filter({ has: editor.getByLabel('Label', { exact: true }) }).last();
    await last.getByLabel('Label', { exact: true }).fill('Promise');
    await last.getByLabel('Link (optional)').first().fill(`/${promise.slug}`);

    // Footer blurb and the title template.
    await editor.getByRole('tab', { name: /^Footer/ }).click();
    await editor.getByLabel('Footer blurb (optional)').fill(`Growth marketing, measured. (${id})`);
    await editor.getByRole('tab', { name: /^SEO & organization/ }).click();
    await editor.getByLabel('Title template').fill('%s — Optimize All Agency');

    // An invalid analytics id is refused with a field error (and the tab is marked).
    await editor.getByRole('tab', { name: /^Analytics/ }).click();
    await editor.getByLabel('GA4 measurement ID (optional)').fill('UA-12345-6');
    await editor.getByRole('button', { name: 'Save settings' }).click();
    await expect(editor.getByText('A GA4 measurement ID looks like G-ABC123XYZ9.')).toBeVisible();
    await expect(editor.getByRole('tab', { name: 'Analytics •' })).toBeVisible();
    await editor.getByLabel('GA4 measurement ID (optional)').fill('');
    await editor.getByRole('button', { name: 'Save settings' }).click();
    await expect(toast(editor, 'Site settings saved')).toBeVisible();
    errors.expectClean('the site settings');

    // ---------------------------------------------------------------- the public site
    const home = await openPublic(browser, '/');
    const homeErrors = watchErrors(home);
    await expect(home.getByText(`Spring audit slots are open ${id}`)).toBeVisible();
    await expect(home.getByRole('link', { name: 'Book yours' })).toHaveAttribute('href', '/free-audit');
    const nav = home.getByRole('navigation', { name: 'Main' });
    await expect(nav.getByRole('link', { name: 'Promise', exact: true })).toHaveAttribute(
      'href',
      `/${promise.slug}`,
    );
    await expect(home.getByRole('contentinfo')).toContainText(`Growth marketing, measured. (${id})`);
    const org = (await jsonLd(home)).find((x) => x['@type'] === 'Organization') as
      { name?: string; url?: string } | undefined;
    expect(org?.name).toBeTruthy();
    homeErrors.expectClean('the home page');

    await nav.getByRole('link', { name: 'Promise', exact: true }).click();
    await expect(home).toHaveURL(new RegExp(`/${promise.slug}$`));
    await expect(home.getByRole('heading', { level: 1, name: promise.title, exact: true })).toBeVisible();
    await expect(home).toHaveTitle(/ — Optimize All Agency$/);

    // Server-side validation: a site URL that is not an https origin, a template without %s.
    const editorApi = await api(state().editor);
    const now = await editorApi.get<SettingsEnvelope>('/agency/website/settings');
    const bad = await refused(
      editorApi.put('/agency/website/settings', {
        settings: {
          ...now.settings,
          seo: { ...now.settings.seo, siteUrl: 'http://example.com/path', titleTemplate: 'No placeholder' },
        },
        concurrencyStamp: now.concurrencyStamp,
      }),
    );
    expect(bad.status).toBe(400);
    const fieldErrors = (bad.body as { errors: Record<string, string[]> }).errors;
    expect(fieldErrors['seo.siteUrl']?.[0]).toContain('https://');
    expect(fieldErrors['seo.titleTemplate']?.[0]).toContain('%s');
    // A stale stamp (someone saved since) → 409, and the settings are unchanged.
    const stale = await refused(
      editorApi.put('/agency/website/settings', {
        settings: now.settings,
        concurrencyStamp: original.concurrencyStamp,
      }),
    );
    expect(stale.status).toBe(409);
  });

  test('page texts: edit a built-in headline → live; markup stripped; reset → default', async ({
    browser,
  }) => {
    const id = runId();
    const editor = await actor(browser, state().editor, landing.agency);
    await editor
      .getByRole('navigation', { name: 'Agency navigation' })
      .getByRole('link', { name: 'Page texts' })
      .click();
    await editor.getByLabel('Search all texts').fill('home.hero.eyebrow');
    const field = editor.getByLabel('Hero eyebrow', { exact: true });
    await field.fill(`Growth partners since 2014 <b>${id}</b>`);
    await editor.getByRole('button', { name: 'Save 1 change' }).click();
    await expect(toast(editor, 'Texts saved')).toBeVisible();
    await expect(field).toHaveValue(`Growth partners since 2014 ${id}`);

    const home = await openPublic(browser, '/');
    await expect(home.getByText(`Growth partners since 2014 ${id}`)).toBeVisible();
    expect(await home.locator('main b').filter({ hasText: id }).count()).toBe(0);

    // Unknown placeholders and multi-line text in a single-line field are refused.
    const editorApi = await api(state().editor);
    const catalog = await editorApi.get<{
      groups: { entries: { key: string; concurrencyStamp: string | null }[] }[];
    }>('/agency/website/copy');
    const entry = catalog.groups.flatMap((g) => g.entries).find((e) => e.key === 'home.hero.eyebrow')!;
    const invalid = await refused(
      editorApi.put('/agency/website/copy', {
        changes: [
          {
            key: 'home.hero.eyebrow',
            value: 'Hello {name}\nsecond line',
            concurrencyStamp: entry.concurrencyStamp,
          },
        ],
      }),
    );
    expect(invalid.status).toBe(400);
    // A stale stamp → 409.
    const stale = await refused(
      editorApi.put('/agency/website/copy', {
        changes: [{ key: 'home.hero.eyebrow', value: 'Stale', concurrencyStamp: null }],
      }),
    );
    expect(stale.status).toBe(409);

    // Reset to the default wording.
    await editor.getByRole('button', { name: 'Reset to default' }).first().click();
    await editor.getByRole('button', { name: 'Save 1 change' }).click();
    await expect(toast(editor, 'Texts saved')).toBeVisible();
    const again = await openPublic(browser, '/');
    await expect(again.getByText('Full-service digital marketing agency').first()).toBeVisible();
  });

  test('robots.txt, sitemap and canonical links follow the configured site URL', async ({ browser }) => {
    const robots = await anonGet('/robots.txt');
    expect(robots.status).toBe(200);
    for (const path of ['/app', '/agency', '/client', '/admin', '/finance', '/review', '/manage', '/api'])
      expect(robots.text).toMatch(new RegExp(`^Disallow: ${path}$`, 'm'));
    expect(robots.text).toMatch(/^Allow: \/api\/v1\/public\/sitemap\.xml$/m);
    const paths = await sitemapPaths();
    expect(paths).toEqual(expect.arrayContaining(['/', '/services', '/blog', '/case-studies', '/pricing']));
    expect(paths.some((p) => /^\/(agency|client|admin|app)(\/|$)/.test(p))).toBe(false);
    expect(new Set(paths).size, 'no duplicate URLs in the sitemap').toBe(paths.length);

    // Set the public site URL: sitemap locations, robots' Sitemap line, canonical links and JSON-LD all use it.
    const editorApi = await api(state().editor);
    const now = await editorApi.get<SettingsEnvelope>('/agency/website/settings');
    await editorApi.put('/agency/website/settings', {
      settings: {
        ...now.settings,
        seo: { ...now.settings.seo, siteUrl: 'https://www.optimizeall-e2e.example' },
      },
      concurrencyStamp: now.concurrencyStamp,
    });
    const xml = (await anonGet('/api/v1/public/sitemap.xml')).text;
    const locs = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]!);
    expect(locs.length).toBeGreaterThan(5);
    expect(locs.every((l) => l.startsWith('https://www.optimizeall-e2e.example/'))).toBe(true);
    expect((await anonGet('/robots.txt')).text).toContain(
      'Sitemap: https://www.optimizeall-e2e.example/api/v1/public/sitemap.xml',
    );
    const promise = recall<{ slug: string }>('promisePage');
    const page = await openPublic(browser, `/${promise.slug}`);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect.poll(() => canonical(page)).toBe(`https://www.optimizeall-e2e.example/${promise.slug}`);
    expect(await meta(page, 'og:url')).toBe(`https://www.optimizeall-e2e.example/${promise.slug}`);
    const crumbs = (await jsonLd(page)).find((x) => x['@type'] === 'BreadcrumbList') as {
      itemListElement: { item: string }[];
    };
    expect(crumbs.itemListElement[0]!.item).toBe('https://www.optimizeall-e2e.example/');
    // The API origin itself still serves everything (the site URL only changes absolute links).
    expect((await fetch(`${API_URL}/api/v1/public/site`)).status).toBe(200);
    // Staff without site.manage cannot change settings.
    expect((await refused((await api(accounts.writer)).get('/agency/website/settings'))).status).toBe(403);
  });
});
