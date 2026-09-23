// Manual visual check of the admin portal against a running app (not part of the Playwright suites).
// Usage: BASE_URL=http://127.0.0.1:5195 OUT=/tmp/shots node e2e/admin/visual-check.mjs
import { mkdirSync } from 'node:fs';
import { chromium } from '@playwright/test';

const base = process.env.BASE_URL ?? 'http://127.0.0.1:5195';
const out = process.env.OUT ?? './admin-shots';
mkdirSync(out, { recursive: true });

const pages = [
  ['overview', '/admin'],
  ['users', '/admin/users'],
  ['settings', '/admin/settings'],
  ['content', '/admin/content'],
  ['content-faqs', '/admin/content?tab=faqs'],
  ['content-onboarding', '/admin/content?tab=onboarding'],
  ['content-announcements', '/admin/content?tab=announcements'],
  ['categories', '/admin/categories'],
  ['support', '/admin/support'],
  ['audit', '/admin/audit'],
  ['jobs', '/admin/jobs'],
  ['jobs-runs', '/admin/jobs?tab=runs'],
  ['jobs-deliveries', '/admin/jobs?tab=deliveries'],
  ['analytics', '/admin/analytics'],
];

const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
const problems = [];
page.on('console', (msg) => {
  if (msg.type() === 'error') problems.push(`console: ${msg.text()}`);
});
page.on('response', (res) => {
  if (res.url().includes('/api/') && res.status() >= 400 && !res.url().includes('/auth/refresh'))
    problems.push(`${res.status()} ${res.request().method()} ${res.url()}`);
});

await page.goto(`${base}/login`);
await page.getByLabel('Email').fill(process.env.ADMIN_EMAIL ?? 'admin@optimizeall.local');
await page.getByLabel('Password', { exact: true }).fill(process.env.ADMIN_PASSWORD ?? 'Admin#Demo2026!');
await page.getByRole('button', { name: /sign in/i }).click();
await page.waitForURL(/\/admin/, { timeout: 15000 });

for (const width of [1280, 360]) {
  await page.setViewportSize({ width, height: width === 360 ? 780 : 900 });
  for (const [name, path] of pages) {
    // Client-side navigation: a full reload would re-run the (rate-limited) silent refresh every time.
    await page.evaluate((to) => {
      window.history.pushState({}, '', to);
      window.dispatchEvent(new PopStateEvent('popstate'));
    }, path);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(300);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    if (overflow > 1) problems.push(`${name}@${width}: horizontal overflow ${overflow}px`);
    await page.screenshot({ path: `${out}/${name}-${width}.png`, fullPage: true });
  }
}

// Extra flows passed as EXTRA=1 are run by the caller separately.
console.log(JSON.stringify({ problems }, null, 2));
await browser.close();
