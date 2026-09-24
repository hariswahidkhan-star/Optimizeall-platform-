import { type Page, expect, test } from '@playwright/test';
import { API_URL, PageWatcher, auditPage, writeReport } from './support/crawl';

/**
 * The public website as an anonymous visitor: every link of the header (including the menus' hidden links), the footer
 * and every URL of the API-generated sitemap renders a page with an h1, no not-found page, no console/page errors and
 * no failed API call. Links to other sites and mailto:/tel: links are only checked to be well formed.
 */
test.describe.configure({ mode: 'serial' });

/** Internal paths (and external hrefs) linked from the site header and footer on `path`. */
async function chromeLinks(page: Page) {
  const hrefs = await page
    .locator('header a[href], footer a[href], nav[aria-label="Main"] a[href]')
    .evaluateAll((els) => els.map((e) => (e as HTMLAnchorElement).getAttribute('href') ?? ''));
  const internal = new Set<string>();
  const external = new Set<string>();
  for (const href of hrefs) {
    if (href.startsWith('/') && !href.startsWith('//')) internal.add(href.split('#')[0] || '/');
    else if (href && !href.startsWith('#')) external.add(href);
  }
  return { internal: [...internal], external: [...external] };
}

/**
 * Opens `path` the way a visitor clicking a link does: a client-side navigation once the site is loaded (a full reload
 * per page would refetch the site settings and copy every time and trip the API's per-IP rate limit, which every
 * request of the run shares).
 */
async function audit(page: Page, watcher: PageWatcher, path: string, retry = true) {
  watcher.path = path;
  const before = watcher.findings.length;
  const loaded = await page.evaluate(() => Boolean(document.querySelector('#root > *'))).catch(() => false);
  if (!loaded || !page.url().startsWith('http')) await page.goto(path);
  else
    await page.evaluate((to) => {
      window.history.pushState({}, '', to);
      window.dispatchEvent(new PopStateEvent('popstate'));
    }, path);
  await page.waitForURL((url) => `${url.pathname}${url.search}` === path);
  await watcher.settle();
  await auditPage(page, watcher);
  // The API limits public requests per IP (RateLimiting.Public, 120/min) and the whole run, portal crawls included,
  // comes from one address. A 429 is the limiter doing its job, not a broken page: wait for the window and look again.
  const mine = watcher.findings.slice(before);
  if (retry && mine.some((f) => / 429 /.test(f.detail))) {
    watcher.findings.splice(before);
    await page.waitForTimeout(61_000);
    await page.goto(path);
    await audit(page, watcher, path, false);
  }
}

test('every header and footer link opens a real page', async ({ page }) => {
  const watcher = new PageWatcher(page, 'visitor');
  await audit(page, watcher, '/');
  const { internal, external } = await chromeLinks(page);
  expect(internal.length, 'the header and footer link to site pages').toBeGreaterThan(5);

  for (const href of external) {
    const ok = /^(https:\/\/|mailto:[^@\s]+@[^@\s]+|tel:\+?[\d\s()-]+$)/.test(href);
    if (!ok) watcher.add('not-found', `malformed external link ${href}`);
  }
  const visited: string[] = [];
  for (const path of internal) {
    // Portal links (sign in, dashboards) belong to the portal crawl; /login is enough here.
    if (/^\/(admin|finance|agency|manage|review|app|client)(\/|$)/.test(path)) continue;
    visited.push(path);
    await audit(page, watcher, path);
  }
  writeReport('public-links', { visited, external, findings: watcher.findings });
  expect(watcher.findings, 'problems on pages the site header and footer link to').toEqual([]);
});

test('every sitemap URL renders a page and robots.txt points to the sitemap', async ({
  page,
  request,
  baseURL,
}) => {
  const robots = await request.get('/robots.txt');
  expect(robots.status()).toBe(200);
  expect(await robots.text(), 'robots.txt is served by the API through the web origin').toMatch(
    /^Sitemap: \S+$/m,
  );

  const sitemap = await request.get('/sitemap.xml');
  expect(sitemap.status()).toBe(200);
  expect(sitemap.headers()['content-type']).toMatch(/xml/);
  const xml = await sitemap.text();
  const urls = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]!);
  expect(urls.length, 'the sitemap lists the public pages').toBeGreaterThan(10);
  expect((await request.get(`${API_URL}/api/v1/public/sitemap.xml`)).status()).toBe(200);

  const watcher = new PageWatcher(page, 'visitor');
  const origin = new URL(baseURL!).origin;
  for (const url of urls) {
    const { pathname, search } = new URL(url);
    await audit(page, watcher, `${pathname}${search}`);
    expect(new URL(page.url()).origin).toBe(origin);
  }
  writeReport('public-sitemap', { visited: urls, findings: watcher.findings });
  expect(watcher.findings, 'problems on sitemap pages').toEqual([]);
});
