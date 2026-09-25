import { expect, test } from '@playwright/test';
import {
  BASE,
  BOT_AGENTS,
  PUBLIC_PAGES,
  expectValidJsonLd,
  noJsPage,
  parseHead,
  readHead,
} from './support/seo';

/**
 * Every public URL, fetched with JavaScript disabled (a real browser with scripts off, and raw HTTP): status 200, a
 * unique title of at most 60 characters with the brand, a 70–155 character description, a self-referencing absolute
 * canonical, indexable robots directives, Open Graph and Twitter tags with an absolute image, valid JSON-LD, one h1
 * and readable content with crawlable links — and the same HTML for search engines, AI crawlers and link unfurlers.
 */
test.describe('public pages without JavaScript', () => {
  const titles = new Map<string, string>();
  const descriptions = new Map<string, string>();

  for (const path of PUBLIC_PAGES) {
    test(`${path} is complete HTML`, async ({ browser }) => {
      const page = await noJsPage(browser);
      const response = await page.goto(path);
      expect(response!.status()).toBe(200);
      expect(response!.headers()['content-type']).toContain('text/html');
      expect(response!.headers()['x-robots-tag']).toBeUndefined();

      const head = await readHead(page);
      expect(head.title.length, head.title).toBeLessThanOrEqual(60);
      expect(head.title.length, head.title).toBeGreaterThanOrEqual(20);
      expect(head.title).toContain('Optimize All');
      expect(head.description, 'meta description').not.toBeNull();
      expect(head.description!.length, head.description!).toBeGreaterThanOrEqual(50);
      expect(head.description!.length, head.description!).toBeLessThanOrEqual(155);
      expect(head.canonical).toBe(path === '/' ? `${BASE}/` : `${BASE}${path}`);
      expect(head.robots).toMatch(/^index, follow/);
      expect(head.og['og:title']).toBe(head.title);
      expect(head.og['og:description']).toBe(head.description);
      expect(head.og['og:url']).toBe(head.canonical);
      expect(head.og['og:image']).toMatch(/^https?:\/\//);
      expect(head.og['og:site_name']).toBe('Optimize All');
      expect(head.twitter['twitter:card']).toBe('summary_large_image');
      expect(head.jsonLd.length, 'JSON-LD blocks').toBeGreaterThan(0);
      head.jsonLd.forEach((node, i) => expectValidJsonLd(node, `${path} JSON-LD #${i}`));
      if (path !== '/') expect(head.jsonLd.map((n) => n['@type'])).toContain('BreadcrumbList');

      // Visible content: one h1, body copy, links into the site, and no "needs JavaScript" wall.
      await expect(page.locator('h1')).toHaveCount(1);
      await expect(page.locator('h1')).toBeVisible();
      const text = (await page.locator('#oa-ssr main').innerText()).trim();
      expect(text.length, 'readable main content').toBeGreaterThan(150);
      expect(await page.locator('#oa-ssr a[href="/services"]').count()).toBeGreaterThan(0);
      await expect(page.getByText('needs JavaScript to run')).toHaveCount(0);

      expect(titles.has(head.title), `${path} repeats the title of ${titles.get(head.title)}`).toBe(false);
      expect(
        descriptions.has(head.description!),
        `${path} repeats the description of ${descriptions.get(head.description!)}`,
      ).toBe(false);
      titles.set(head.title, path);
      descriptions.set(head.description!, path);
      await page.context().close();
    });
  }

  test('search engines, AI crawlers and link unfurlers get the same HTML as visitors (no cloaking)', async ({
    request,
  }) => {
    for (const path of ['/', '/services/seo', '/blog', '/about']) {
      const visitor = parseHead(await (await request.get(`${BASE}${path}`)).text());
      expect(visitor.title).toBeTruthy();
      expect(visitor.h1).toBeTruthy();
      for (const [name, agent] of Object.entries(BOT_AGENTS)) {
        const res = await request.get(`${BASE}${path}`, { headers: { 'user-agent': agent } });
        expect(res.status(), `${name} ${path}`).toBe(200);
        expect(parseHead(await res.text()), `${name} ${path}`).toEqual(visitor);
      }
    }
  });

  test('a blog post and a case study carry article metadata and JSON-LD', async ({ browser, request }) => {
    const blog = await (await request.get(`${BASE}/api/v1/public/blog`)).json();
    const cases = await (await request.get(`${BASE}/api/v1/public/case-studies`)).json();
    const post = blog.items[0].slug as string;
    const study = cases[0].slug as string;
    for (const [path, type] of [
      [`/blog/${post}`, 'BlogPosting'],
      [`/case-studies/${study}`, 'Article'],
    ]) {
      const page = await noJsPage(browser);
      expect((await page.goto(path))!.status()).toBe(200);
      const head = await readHead(page);
      expect(head.og['og:type']).toBe('article');
      expect(await page.locator('meta[property="article:modified_time"]').count()).toBe(1);
      expect(head.jsonLd.map((n) => n['@type'])).toContain(type);
      head.jsonLd.forEach((node, i) => expectValidJsonLd(node, `${path} JSON-LD #${i}`));
      await expect(page.locator('h1')).toHaveCount(1);
      await page.context().close();
    }
  });

  test('a job opening carries JobPosting JSON-LD', async ({ browser, request }) => {
    const jobs = (await (await request.get(`${BASE}/api/v1/public/careers`)).json()) as { slug: string }[];
    test.skip(jobs.length === 0, 'no open roles in the seed');
    const page = await noJsPage(browser);
    expect((await page.goto(`/careers/${jobs[0].slug}`))!.status()).toBe(200);
    const head = await readHead(page);
    const posting = head.jsonLd.find((n) => n['@type'] === 'JobPosting');
    expect(posting).toBeTruthy();
    expectValidJsonLd(posting!, 'JobPosting');
    await page.context().close();
  });

  test('blog pagination has self canonicals and prev/next links', async ({ browser, request }) => {
    const blog = await (await request.get(`${BASE}/api/v1/public/blog?pageSize=9`)).json();
    test.skip(blog.total <= blog.pageSize, 'one page of posts only');
    const page = await noJsPage(browser);
    await page.goto('/blog');
    expect(await page.locator('link[rel="next"]').getAttribute('href')).toBe(`${BASE}/blog?page=2`);
    await page.goto('/blog?page=2');
    const head = await readHead(page);
    expect(head.canonical).toBe(`${BASE}/blog?page=2`);
    expect(await page.locator('link[rel="prev"]').getAttribute('href')).toBe(`${BASE}/blog`);
    await page.context().close();
  });
});
