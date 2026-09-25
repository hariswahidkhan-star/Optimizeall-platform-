import { expect, test } from '@playwright/test';
import { BASE, decode, locs, parseHead } from './support/seo';

/**
 * Link unfurlers and AI assistants (docs/SEO_AUDIT_2026-09.md): every indexable page shares a real 1200×630 PNG (a
 * generated brand card when the page has no image of its own, never an SVG), declares its language version, and the
 * academy is described for assistants in /llms/academy.txt.
 */
test.describe('social cards, hreflang and the llms.txt academy guide', () => {
  const pages = ['/', '/services/seo', '/blog', '/learn', '/about'];

  for (const path of pages)
    test(`${path} shares a 1200×630 PNG card and declares its language`, async ({ request }) => {
      const res = await request.get(`${BASE}${path}`, { maxRedirects: 0 });
      expect(res.status()).toBe(200);
      const html = await res.text();
      const head = parseHead(html);
      const image = decode(head.ogImage!);
      expect(image).toMatch(/^https?:\/\/[^/]+\/og\/.+\.png\?v=[0-9a-f]{12}$/);
      expect(html).toContain('<meta property="og:image:width" content="1200"');
      expect(html).toContain('<meta property="og:image:height" content="630"');
      expect(html).toMatch(/<link rel="alternate" hreflang="x-default" href="[^"]+"/);

      const card = await request.get(`${BASE}${new URL(image).pathname}${new URL(image).search}`);
      expect(card.status()).toBe(200);
      expect(card.headers()['content-type']).toBe('image/png');
      expect(card.headers()['cache-control']).toContain('immutable');
      const bytes = await card.body();
      expect([...bytes.subarray(1, 4)].map((b) => String.fromCharCode(b)).join('')).toBe('PNG');
      expect(bytes.readInt32BE(16)).toBe(1200);
      expect(bytes.readInt32BE(20)).toBe(630);
    });

  test('course and lesson pages never share an SVG badge as their social image', async ({ request }) => {
    const learn = await (await request.get(`${BASE}/sitemaps/learn.xml`)).text();
    const urls = locs(learn);
    const course = urls.find((u) => new URL(u).pathname.split('/').length === 3)!;
    const lesson = urls.find((u) => new URL(u).pathname.split('/').length === 4)!;
    for (const url of [course, lesson]) {
      const head = parseHead(await (await request.get(`${BASE}${new URL(url).pathname}`)).text());
      expect(head.ogImage, url).not.toContain('.svg');
      expect(head.ogImage, url).toContain('/og/learn/');
    }
  });

  test('llms.txt links the academy guide, which lists every lesson', async ({ request }) => {
    const llms = await (await request.get(`${BASE}/llms.txt`)).text();
    expect(llms).toContain('## Academy (free courses)');
    expect(llms).toContain('/llms/academy.txt');
    const guide = await request.get(`${BASE}/llms/academy.txt`);
    expect(guide.status()).toBe(200);
    expect(guide.headers()['content-type']).toContain('text/plain');
    const text = await guide.text();
    const lessons = locs(await (await request.get(`${BASE}/sitemaps/learn.xml`)).text()).filter(
      (u) => new URL(u).pathname.split('/').length === 4,
    );
    expect(lessons.length).toBeGreaterThan(0);
    for (const lesson of lessons) expect(text).toContain(`(${lesson}.md)`);
  });
});
