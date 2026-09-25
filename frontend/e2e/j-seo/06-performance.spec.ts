import { mkdirSync, writeFileSync } from 'node:fs';
import { expect, test, type Browser } from '@playwright/test';
import { BASE } from './support/seo';

/**
 * Core Web Vitals of key public pages, measured in the browser with PerformanceObserver (Lighthouse is not available
 * offline): Largest Contentful Paint, Cumulative Layout Shift, First Contentful Paint and TTFB. Each page is measured
 * as served now (server-rendered HTML + app) and, for comparison, as it was served before (the bare app shell for every
 * route, emulated by answering the document request with /index.html). Results are written to
 * test-results/j-seo/web-vitals.json and printed. CLS must stay under 0.1 ("good").
 */
const PAGES = ['/', '/services', '/services/seo', '/pricing', '/blog', '/about'];

interface Vitals {
  lcp: number;
  cls: number;
  fcp: number;
  ttfb: number;
  /** Time until the page's h1 is on screen (what a visitor waits for). */
  h1: number;
}

async function measure(browser: Browser, path: string, shellOnly: string | null): Promise<Vitals> {
  const context = await browser.newContext({ baseURL: BASE, viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.addInitScript(() => {
    const w = window as unknown as { __vitals: { lcp: number; cls: number } };
    w.__vitals = { lcp: 0, cls: 0 };
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) w.__vitals.lcp = entry.startTime;
    }).observe({ type: 'largest-contentful-paint', buffered: true });
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries() as (PerformanceEntry & {
        value: number;
        hadRecentInput: boolean;
      })[])
        if (!entry.hadRecentInput) w.__vitals.cls += entry.value;
    }).observe({ type: 'layout-shift', buffered: true });
  });
  if (shellOnly !== null)
    await page.route(`${BASE}${path}`, (route) =>
      route.fulfill({ status: 200, contentType: 'text/html', body: shellOnly }),
    );
  const started = Date.now();
  await page.goto(path);
  await page.getByRole('heading', { level: 1 }).first().waitFor({ state: 'visible', timeout: 60_000 });
  const h1 = Date.now() - started;
  // Let late content (images, fonts, below-the-fold sections) settle before reading LCP and CLS.
  await page.waitForLoadState('networkidle', { timeout: 30_000 }).catch(() => undefined);
  await page.waitForTimeout(1000);
  const vitals = await page.evaluate(() => {
    const w = window as unknown as { __vitals: { lcp: number; cls: number } };
    const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming;
    const fcp = performance.getEntriesByName('first-contentful-paint')[0]?.startTime ?? 0;
    return { lcp: w.__vitals.lcp, cls: w.__vitals.cls, fcp, ttfb: nav.responseStart };
  });
  await context.close();
  return { ...vitals, h1 };
}

test.describe('Core Web Vitals of public pages', () => {
  test.setTimeout(10 * 60_000);

  test('LCP, CLS and FCP with server-rendered pages vs the bare app shell', async ({ browser, request }) => {
    const shell = await (await request.get(`${BASE}/index.html`)).text();
    const results: Record<string, { ssr: Vitals; shell: Vitals }> = {};
    for (const path of PAGES) {
      // Warm-up (the first visit pays for compiling/caching on a loaded machine), then one measured run each.
      await measure(browser, path, null);
      results[path] = { ssr: await measure(browser, path, null), shell: await measure(browser, path, shell) };
    }
    mkdirSync('test-results/j-seo', { recursive: true });
    writeFileSync('test-results/j-seo/web-vitals.json', JSON.stringify(results, null, 2));
    const fmt = (v: Vitals) =>
      `LCP ${Math.round(v.lcp)} ms · CLS ${v.cls.toFixed(3)} · FCP ${Math.round(v.fcp)} ms · TTFB ${Math.round(v.ttfb)} ms · h1 ${v.h1} ms`;
    for (const [path, r] of Object.entries(results))
      process.stdout.write(`${path}\n  now:    ${fmt(r.ssr)}\n  before: ${fmt(r.shell)}\n`);

    for (const [path, r] of Object.entries(results)) {
      expect(r.ssr.cls, `${path} CLS`).toBeLessThan(0.1);
      expect(r.ssr.lcp, `${path} LCP`).toBeGreaterThan(0);
      // Generous bound: the CI machine is shared; a regression to multi-second LCP still fails.
      expect(r.ssr.lcp, `${path} LCP`).toBeLessThan(8000);
    }
  });
});
