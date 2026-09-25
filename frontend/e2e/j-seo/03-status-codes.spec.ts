import { expect, test } from '@playwright/test';
import { BASE, PRIVATE_PAGES, noJsPage, parseHead } from './support/seo';

/**
 * HTTP semantics crawlers rely on: unknown URLs are real 404s (no soft 404s) with helpful links, duplicate URL forms
 * are 301s to the one canonical form, private areas and personal links are noindex (header and meta), search results
 * are noindex but followable, and API responses are never indexed.
 */
test.describe('status codes, redirects and noindex', () => {
  for (const path of [
    '/this-page-does-not-exist',
    '/services/not-a-service',
    '/blog/not-a-post',
    '/case-studies/nope',
    '/lp/nobody/nothing',
  ]) {
    test(`${path} is a real 404 with helpful links`, async ({ browser, request }) => {
      const res = await request.get(`${BASE}${path}`);
      expect(res.status()).toBe(404);
      expect(res.headers()['x-robots-tag']).toContain('noindex');
      expect(parseHead(await res.text()).robots).toContain('noindex');

      // Without JavaScript the visitor still reads a useful page; with it, the app shows its own 404.
      const page = await noJsPage(browser);
      expect((await page.goto(path))!.status()).toBe(404);
      await expect(page.locator('h1')).toHaveCount(1);
      await expect(page.locator('#oa-ssr main a[href="/services"]')).toBeVisible();
      await page.context().close();
    });
  }

  test('the app still boots on a 404 and shows its not-found page', async ({ page }) => {
    const res = await page.goto('/this-page-does-not-exist');
    expect(res!.status()).toBe(404);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('#oa-ssr')).toHaveCount(0);
  });

  for (const [from, to] of [
    ['/services/', '/services'],
    ['/Services/SEO', '/services/seo'],
    ['/blog//', '/blog'],
    ['/pricing/?utm_source=newsletter', '/pricing?utm_source=newsletter'],
  ]) {
    test(`${from} redirects permanently to ${to}`, async ({ request }) => {
      const res = await request.get(`${BASE}${from}`, { maxRedirects: 0 });
      expect(res.status()).toBe(301);
      expect(
        new URL(res.headers()['location'], BASE).pathname + new URL(res.headers()['location'], BASE).search,
      ).toBe(to);
    });
  }

  for (const path of PRIVATE_PAGES) {
    test(`${path} is never indexed`, async ({ request }) => {
      const res = await request.get(`${BASE}${path}`);
      expect(res.status()).toBe(200);
      expect(res.headers()['x-robots-tag']).toBe('noindex, nofollow');
      const head = parseHead(await res.text());
      expect(head.canonical).toBeNull();
      expect(head.jsonLdCount).toBe(0);
    });
  }

  test('personal links and search results are noindex; API responses carry X-Robots-Tag', async ({
    request,
  }) => {
    for (const path of ['/p/some-proposal-token', '/join/ABC123', '/email/unsubscribe/token']) {
      const res = await request.get(`${BASE}${path}`);
      expect(res.headers()['x-robots-tag'], path).toBe('noindex, nofollow');
    }
    const search = await request.get(`${BASE}/search?q=seo`);
    expect(search.headers()['x-robots-tag']).toBe('noindex, follow');
    const api = await request.get(`${BASE}/api/v1/public/site`);
    expect(api.headers()['x-robots-tag']).toBe('noindex');
    const md = await request.get(`${BASE}/services/seo.md`);
    expect(md.headers()['x-robots-tag']).toBe('noindex');
  });
});
