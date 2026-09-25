import { expect, test, type Page } from '@playwright/test';
import { parseHead } from './support/seo';

/**
 * The React app boots over the server-rendered page: the server copy is never shown to visitors with JavaScript (no
 * flash of duplicate content), the head keeps exactly one title/description/canonical/robots and never duplicates
 * JSON-LD, the server's values match what the app writes, and client-side navigation replaces the page's metadata.
 */
async function headCounts(page: Page) {
  return page.evaluate(() => ({
    description: document.head.querySelectorAll('meta[name="description"]').length,
    canonical: document.head.querySelectorAll('link[rel="canonical"]').length,
    robots: document.head.querySelectorAll('meta[name="robots"]').length,
    ogTitle: document.head.querySelectorAll('meta[property="og:title"]').length,
    titles: document.head.querySelectorAll('title').length,
    jsonLdTypes: Array.from(document.head.querySelectorAll('script[type="application/ld+json"]')).map(
      (s) => (JSON.parse(s.textContent ?? '{}') as { '@type': string })['@type'],
    ),
    ssr: document.querySelectorAll('#oa-ssr').length,
    canonicalHref: document.head.querySelector('link[rel="canonical"]')?.getAttribute('href') ?? null,
    description0: document.head.querySelector('meta[name="description"]')?.getAttribute('content') ?? null,
  }));
}

test.describe('the app boots over the server-rendered HTML', () => {
  for (const path of ['/', '/services/seo', '/pricing', '/about', '/blog']) {
    test(`${path}: no duplicate head tags, server and app agree`, async ({ page, request }) => {
      const server = parseHead(await (await request.get(path)).text());
      const errors: string[] = [];
      // An anonymous visit's session probe (POST /auth/refresh → 401) is expected; anything else is a failure.
      page.on('console', (m) => {
        if (m.type() === 'error' && !/status of 401/.test(m.text())) errors.push(m.text());
      });
      page.on('pageerror', (e) => errors.push(e.message));
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      // Give the head manager its data (site settings, page payload).
      await expect.poll(async () => (await headCounts(page)).ssr).toBe(0);
      await expect.poll(async () => page.title()).toBe(server.title!.replace(/&amp;/g, '&'));
      const counts = await headCounts(page);
      expect(counts).toMatchObject({ description: 1, canonical: 1, robots: 1, ogTitle: 1, titles: 1 });
      expect(new Set(counts.jsonLdTypes).size, `duplicate JSON-LD: ${counts.jsonLdTypes.join(', ')}`).toBe(
        counts.jsonLdTypes.length,
      );
      expect(counts.jsonLdTypes.length).toBe(server.jsonLdCount);
      expect(counts.canonicalHref).toBe(server.canonical);
      expect(counts.description0).toBe(server.description!.replace(/&amp;/g, '&').replace(/&#39;/g, "'"));
      expect(errors, errors.join('\n')).toEqual([]);
    });
  }

  test('client-side navigation replaces the title, canonical and structured data', async ({ page }) => {
    await page.goto('/services/seo');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect.poll(async () => (await headCounts(page)).jsonLdTypes).toContain('Service');
    await page
      .getByRole('navigation', { name: 'Main' })
      .getByRole('link', { name: 'Pricing', exact: true })
      .first()
      .click();
    await expect(page).toHaveURL(/\/pricing$/);
    await expect.poll(() => page.title()).toBe('Pricing: Marketing Packages & Retainers | Optimize All');
    await expect.poll(async () => (await headCounts(page)).canonicalHref).toMatch(/\/pricing$/);
    const counts = await headCounts(page);
    expect(counts.jsonLdTypes).not.toContain('Service');
    expect(counts).toMatchObject({ description: 1, canonical: 1, robots: 1 });
  });

  test('visitors with JavaScript never see the server copy (no flash of duplicate content)', async ({
    page,
  }) => {
    // Hold back the app bundle: the server HTML alone is on screen, and it must be hidden for script-capable browsers.
    let release: () => void = () => {};
    const held = new Promise<void>((resolve) => (release = resolve));
    await page.route(/\/assets\/index-[^/]+\.js$|\/src\/main\.tsx$/, async (route) => {
      await held;
      await route.continue();
    });
    // Module scripts are deferred, so DOMContentLoaded waits for the held bundle: wait for the response only.
    await page.goto('/services', { waitUntil: 'commit' });
    await expect(page.locator('#oa-ssr')).toHaveCount(1);
    await expect(page.locator('#oa-ssr')).toBeHidden();
    release();
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('#oa-ssr')).toHaveCount(0);
  });
});
