import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type Page, expect, test } from '@playwright/test';
import type { Credentials } from '../journeys/support/fixtures';
import { accounts, landing, raw, signIn } from './support/auth';

/**
 * Security headers. The API sets its own (nosniff, DENY framing, a deny-all CSP, no-store); the SPA's come from nginx
 * (frontend/nginx/snippets/security-headers.conf), which `vite preview` does not run — so the suite serves every page
 * of the app under that exact CSP and fails on any violation, and checks that framing the app is refused.
 */
const NGINX = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'nginx');
const snippet = readFileSync(join(NGINX, 'snippets', 'security-headers.conf'), 'utf8');
const header = (name: string) => {
  const m = snippet.match(new RegExp(`add_header ${name} "([^"]+)" always;`));
  if (!m) throw new Error(`${name} missing from security-headers.conf`);
  return m[1]!
    .replace('$oa_img_src_extra', '')
    .replace(/\s+;/g, ';')
    .replace(/\s{2,}/g, ' ');
};
const SPA_HEADERS = {
  'content-security-policy': header('Content-Security-Policy'),
  'x-frame-options': header('X-Frame-Options'),
  'x-content-type-options': header('X-Content-Type-Options'),
  'referrer-policy': header('Referrer-Policy'),
};

/** Serves the app's documents with the production headers and records CSP violations. */
async function underProductionHeaders(page: Page) {
  const violations: string[] = [];
  await page.route('**/*', async (route) => {
    if (route.request().resourceType() !== 'document') return route.fallback();
    const response = await route.fetch();
    return route.fulfill({ response, headers: { ...response.headers(), ...SPA_HEADERS } });
  });
  await page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      (window as unknown as { __csp: string[] }).__csp ??= [];
      (window as unknown as { __csp: string[] }).__csp.push(`${e.violatedDirective} ${e.blockedURI}`);
    });
  });
  page.on('console', (m) => {
    if (m.type() === 'error' && /Content Security Policy/i.test(m.text())) violations.push(m.text());
  });
  return {
    async expectNone(where: string) {
      const inPage = await page.evaluate(() => (window as unknown as { __csp?: string[] }).__csp ?? []);
      expect([...violations, ...inPage], `CSP violations on ${where}`).toEqual([]);
    },
  };
}

test('the API answers with nosniff, DENY framing, a deny-all CSP and no-store', async () => {
  for (const path of ['/auth/providers', '/auth/me', '/public/site']) {
    const res = await raw('GET', path);
    expect(res.headers.get('x-content-type-options'), path).toBe('nosniff');
    expect(res.headers.get('x-frame-options'), path).toBe('DENY');
    expect(res.headers.get('content-security-policy'), path).toContain("frame-ancestors 'none'");
    expect(res.headers.get('content-security-policy'), path).toContain("default-src 'none'");
    expect(res.headers.get('cache-control'), path).toBe('no-store');
    expect(res.headers.get('referrer-policy'), path).toBe('strict-origin-when-cross-origin');
    expect(res.headers.get('server') ?? '', path).not.toMatch(/\d/); // no version banner
  }
  // Errors do not leak internals either.
  const notFound = await raw('GET', `/me/support/tickets/${'0'.repeat(8)}-0000-0000-0000-000000000000`);
  expect(notFound.status).toBe(401);
  expect(JSON.stringify(notFound.json ?? {})).not.toMatch(/Exception|StackTrace|at OptimizeAll/);
});

test('nginx sends the SPA headers from every static location', () => {
  const conf = readFileSync(join(NGINX, 'default.conf.template'), 'utf8');
  for (const location of ['location /assets/', 'location = /index.html', 'location / {']) {
    const block = conf.slice(conf.indexOf(location));
    expect(block.slice(0, block.indexOf('\n    }')), location).toContain('security-headers.conf');
  }
  expect(SPA_HEADERS['content-security-policy']).toContain("frame-ancestors 'none'");
  expect(SPA_HEADERS['content-security-policy']).toContain("script-src 'self'");
  expect(SPA_HEADERS['content-security-policy']).not.toContain('unsafe-eval');
  expect(SPA_HEADERS['x-frame-options']).toBe('DENY');
});

const tour: { who: Credentials | null; landing: RegExp | null; paths: string[] }[] = [
  {
    who: null,
    landing: null,
    paths: ['/', '/login', '/register', '/forgot-password', '/reset-password?token=x'],
  },
  {
    who: accounts.participant,
    landing: landing.participant,
    paths: ['/app', '/app/campaigns', '/app/earnings', '/app/profile/security'],
  },
  { who: accounts.admin, landing: landing.admin, paths: ['/admin', '/admin/users', '/admin/audit'] },
  { who: accounts.am, landing: landing.agency, paths: ['/agency', '/agency/crm/deals'] },
  { who: accounts.nimbusApprover, landing: landing.client, paths: ['/client'] },
];

for (const stop of tour) {
  test(`the app runs under the production CSP: ${stop.who?.email ?? 'public pages'}`, async ({ browser }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    const csp = await underProductionHeaders(page);
    if (stop.who) await signIn(page, stop.who, stop.landing!);
    for (const path of stop.paths) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 }).first(), path).toBeVisible();
      await page.waitForLoadState('networkidle').catch(() => undefined);
      await csp.expectNone(path);
    }
    await context.close();
  });
}

test('another site cannot frame the app (clickjacking)', async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();
  await underProductionHeaders(page);
  // A page on a different origin (127.0.0.1 vs localhost) that frames the sign-in page.
  const appUrl = new URL('/login', test.info().project.use.baseURL!).toString();
  await page.route('http://127.0.0.1:1/evil', (route) =>
    route.fulfill({
      contentType: 'text/html',
      body: `<h1>evil</h1><iframe id="f" src="${appUrl}"></iframe>`,
    }),
  );
  await page.goto('http://127.0.0.1:1/evil');
  await page.waitForTimeout(2_000);
  const framed = page.frames().find((f) => f !== page.mainFrame());
  // The frame never renders the app: no sign-in form inside it.
  const inside = framed
    ? await framed
        .locator('form')
        .count()
        .catch(() => 0)
    : 0;
  expect(inside).toBe(0);
  await context.close();
});
