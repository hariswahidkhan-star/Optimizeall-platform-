import { expect, test } from '@playwright/test';
import { BASE, locs, parseHead, sitemapUrls } from './support/seo';

/**
 * Discovery files, as a crawler reads them: the sitemap index and every sitemap (each URL answers 200, is indexable and
 * canonical to itself), the image and video sitemaps, robots.txt with the crawler policy, llms.txt / llms-full.txt and
 * the Markdown page versions, security.txt, humans.txt, the web manifest and the favicons.
 */
test.describe('sitemaps, robots.txt, llms.txt and well-known files', () => {
  test('every sitemap URL is a live, indexable, self-canonical page', async ({ request }) => {
    const urls = await sitemapUrls(request);
    expect(urls.length).toBeGreaterThan(40);
    expect(new Set(urls).size, 'no duplicate URLs').toBe(urls.length);
    for (const path of [
      '/',
      '/services',
      '/services/seo',
      '/pricing',
      '/blog',
      '/about',
      '/creators',
      '/faq',
    ])
      expect(urls).toContain(path === '/' ? `${BASE}/` : `${BASE}${path}`);
    for (const url of urls) {
      expect(url.startsWith(BASE), `${url} uses the site URL`).toBe(true);
      expect(url).not.toMatch(
        /^https?:\/\/[^/]+\/(login|register|search|agency|admin|app|client|manage|review|finance)(\/|$|\?)/,
      );
      const res = await request.get(url, { maxRedirects: 0 });
      expect(res.status(), url).toBe(200);
      const head = parseHead(await res.text());
      expect(head.robots, url).toMatch(/^index, follow/);
      expect(head.canonical, url).toBe(url);
      expect(head.title, url).toBeTruthy();
    }
  });

  test('the sitemap index is valid XML with lastmod, and image/video sitemaps use Google extensions', async ({
    request,
  }) => {
    const index = await request.get(`${BASE}/sitemap.xml`);
    expect(index.headers()['content-type']).toContain('xml');
    const xml = await index.text();
    expect(xml).toContain('<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">');
    expect([...xml.matchAll(/<lastmod>(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z)<\/lastmod>/g)].length).toBe(
      locs(xml).length,
    );
    const files = locs(xml).map((l) => new URL(l).pathname);
    for (const name of ['pages', 'services', 'case-studies', 'blog', 'images'])
      expect(files).toContain(`/sitemaps/${name}.xml`);
    const images = await (await request.get(`${BASE}/sitemaps/images.xml`)).text();
    expect(images).toContain('xmlns:image="http://www.google.com/schemas/sitemap-image/1.1"');
    expect(images).toMatch(/<image:loc>https?:\/\//);
    expect((await request.get(`${BASE}/sitemaps/unknown.xml`)).status()).toBe(404);
  });

  test('robots.txt allows search and AI assistants on public pages and closes private areas', async ({
    request,
  }) => {
    const res = await request.get(`${BASE}/robots.txt`);
    expect(res.status()).toBe(200);
    expect(res.headers()['content-type']).toContain('text/plain');
    const text = await res.text();
    for (const agent of [
      'Googlebot',
      'Bingbot',
      'GPTBot',
      'OAI-SearchBot',
      'ChatGPT-User',
      'ClaudeBot',
      'Claude-SearchBot',
      'anthropic-ai',
      'PerplexityBot',
      'Google-Extended',
      'Applebot-Extended',
      'CCBot',
      'Bytespider',
      'Meta-ExternalAgent',
    ])
      expect(text).toContain(`User-agent: ${agent}\n`);
    expect(text).toContain(`Sitemap: ${BASE}/sitemap.xml`);
    for (const area of ['/app', '/agency', '/admin', '/client', '/login']) {
      expect(text).toContain(`Disallow: ${area}$`);
      expect(text).toContain(`Disallow: ${area}/`);
    }
    expect(text).toContain('Disallow: /api/');
    const everyone = text.slice(text.indexOf('User-agent: *'));
    expect(everyone).toContain('Allow: /\n');
    expect(everyone).not.toContain('Disallow: /\n');
  });

  test('llms.txt, llms-full.txt and Markdown page versions are generated from published content', async ({
    request,
  }) => {
    const llms = await request.get(`${BASE}/llms.txt`);
    expect(llms.status()).toBe(200);
    const text = await llms.text();
    expect(text).toMatch(/^# Optimize All\n\n> /);
    for (const section of [
      '## Key pages',
      '## Services',
      '## Case studies',
      '## Blog',
      '## Machine-readable',
    ])
      expect(text).toContain(section);
    const links = [...text.matchAll(/\]\((https?:\/\/[^)]+\.md)\)/g)].map((m) => m[1]);
    expect(links.length).toBeGreaterThan(20);
    for (const link of links.slice(0, 12)) {
      const md = await request.get(BASE + new URL(link).pathname);
      expect(md.status(), link).toBe(200);
      expect(md.headers()['content-type'], link).toContain('text/markdown');
      expect(await md.text(), link).toMatch(/^---\ntitle: "/);
    }
    const full = await (await request.get(`${BASE}/llms-full.txt`)).text();
    expect(full).toContain(`URL: ${BASE}/services/seo`);
    expect((await request.get(`${BASE}/login.md`)).status()).toBe(404);
  });

  test('security.txt, humans.txt, the web manifest and favicons are served', async ({ request }) => {
    const security = await (await request.get(`${BASE}/.well-known/security.txt`)).text();
    expect(security).toMatch(/^Contact: mailto:/);
    expect(security).toMatch(/Expires: \d{4}-\d{2}-\d{2}T00:00:00Z/);
    expect((await request.get(`${BASE}/humans.txt`)).status()).toBe(200);
    const manifest = await request.get(`${BASE}/site.webmanifest`);
    expect(manifest.status()).toBe(200);
    const json = (await manifest.json()) as { icons: { src: string; purpose?: string }[] };
    expect(json.icons.some((i) => i.purpose === 'maskable')).toBe(true);
    for (const icon of [
      '/favicon.ico',
      '/favicon.svg',
      '/favicon-32.png',
      '/apple-touch-icon.png',
      '/icon-192.png',
      '/icon-512.png',
      '/og-default.png',
    ])
      expect((await request.get(`${BASE}${icon}`)).status(), icon).toBe(200);
  });
});
