import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Locator, Page, Request } from '@playwright/test';
import type { Credentials } from '../../journeys/support/fixtures';
import { DEMO_PASSWORD } from '../../agency/support/agency';

export { API_URL, ApiSession } from '../../journeys/support/api';
export { signIn } from '../../journeys/support/ui';

/**
 * Crawl-suite helpers. The suite signs in as every demo role (Demo seed), walks every nav link of every portal the
 * role can open (plus the first detail page each list links to), and records what a user would trip over: console and
 * page errors, failed API calls, error boundaries, pages without an h1 and primary actions that do nothing. It never
 * submits a form: dialogs it opens are cancelled, so it can run against the same database as the other suites.
 */
const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

export interface CrawlRole {
  key: string;
  user: Credentials;
}

/** Every demo role (docs/DEMO.md), staff first, then client users. */
export const roles: CrawlRole[] = [
  ['admin', 'admin@demo.optimizeall.app'],
  ['participant', 'sara.participant@demo.optimizeall.app'],
  ['reviewer', 'reviewer1@demo.optimizeall.app'],
  ['manager', 'manager@demo.optimizeall.app'],
  ['finance1', 'finance1@demo.optimizeall.app'],
  ['finance2', 'finance2@demo.optimizeall.app'],
  ['am', 'am@demo.optimizeall.app'],
  ['sales', 'sales@demo.optimizeall.app'],
  ['strategist', 'strategist@demo.optimizeall.app'],
  ['content', 'content@demo.optimizeall.app'],
  ['designer', 'designer@demo.optimizeall.app'],
  ['seo', 'seo@demo.optimizeall.app'],
  ['ads', 'ads@demo.optimizeall.app'],
  ['social', 'social@demo.optimizeall.app'],
  ['nimbusOwner', 'owner@nimbus.demo.optimizeall.app'],
  ['nimbusApprover', 'approver@nimbus.demo.optimizeall.app'],
  ['nimbusBilling', 'billing@nimbus.demo.optimizeall.app'],
  ['auroraOwner', 'owner@aurora.demo.optimizeall.app'],
].map(([key, email]) => ({ key, user: demo(email, key) }));

/** Portal base paths (frontend/src/app/portals.ts), in the order the crawler visits them. */
export const portalBases = [
  '/admin',
  '/finance',
  '/agency',
  '/manage',
  '/review',
  '/app',
  '/client',
] as const;

export type FindingKind =
  'console' | 'pageerror' | 'http' | 'error-boundary' | 'no-h1' | 'inert-action' | 'dialog' | 'not-found';

export interface Finding {
  role: string;
  path: string;
  kind: FindingKind;
  detail: string;
}

/**
 * Problems the crawler provokes on purpose or that are the app's documented answer, never bugs:
 *  - the anonymous session probe (POST /auth/refresh → 401) before sign-in;
 *  - "Failed to load resource" console lines (each is already reported once as an `http` finding);
 *  - Chromium refusing Playwright's trace script in sandboxed srcdoc previews (only with tracing on).
 */
const ignored: RegExp[] = [
  /^HTTP 401 POST .*\/api\/v1\/auth\/refresh$/,
  /^Failed to load resource/,
  /^Blocked script execution in 'about:srcdoc'/,
];

/** Records problems on one page: console errors, page errors and failed (4xx/5xx or aborted) API calls. */
export class PageWatcher {
  readonly findings: Finding[] = [];
  private inflight = new Set<Request>();
  path = '';

  constructor(
    readonly page: Page,
    readonly role: string,
  ) {
    page.on('request', (req) => this.inflight.add(req));
    page.on('requestfinished', (req) => this.inflight.delete(req));
    page.on('requestfailed', (req) => {
      this.inflight.delete(req);
      const failure = req.failure()?.errorText ?? '';
      // Navigations away from a page abort its pending requests; that is the browser, not the app.
      if (/ERR_ABORTED|NS_BINDING_ABORTED/.test(failure)) return;
      if (new URL(req.url()).pathname.startsWith('/api/'))
        this.add('http', `FAILED ${req.method()} ${req.url()} ${failure}`);
    });
    page.on('response', (res) => {
      if (res.status() < 400) return;
      const url = new URL(res.url());
      if (!url.pathname.startsWith('/api/') && !url.pathname.startsWith('/t/')) return;
      this.add('http', `HTTP ${res.status()} ${res.request().method()} ${url.pathname}${url.search}`);
    });
    page.on('console', (msg) => {
      if (msg.type() === 'error') this.add('console', msg.text());
    });
    page.on('pageerror', (err) => this.add('pageerror', err.message));
  }

  add(kind: FindingKind, detail: string) {
    const text = detail.replace(/\s+/g, ' ').slice(0, 400);
    if (ignored.some((re) => re.test(text))) return;
    if (this.findings.some((f) => f.kind === kind && f.detail === text && f.path === this.path)) return;
    this.findings.push({ role: this.role, path: this.path, kind, detail: text });
  }

  /**
   * Waits until the page is quiet: no request in flight (polling queries finish quickly) and no skeleton, spinner or
   * busy region, for 300 ms in a row. Gives up after `timeout` without failing (a page that never settles still gets
   * audited).
   */
  async settle(timeout = 10_000) {
    const deadline = Date.now() + timeout;
    let quietSince = 0;
    while (Date.now() < deadline) {
      const busy =
        this.inflight.size > 0 ||
        (await this.page
          .evaluate(() =>
            Boolean(
              document.querySelector(
                'main .ui-skeleton, main .ui-skeleton-lines, main [aria-busy="true"], main .ui-spinner, .full-loader',
              ),
            ),
          )
          .catch(() => true));
      if (busy) quietSince = 0;
      else if (!quietSince) quietSince = Date.now();
      else if (Date.now() - quietSince >= 300) return;
      await this.page.waitForTimeout(50);
    }
  }
}

/** Audits the page on screen: error boundary, 404 page, error states and the page's h1. */
export async function auditPage(page: Page, watcher: PageWatcher) {
  const main = page.locator('main').first();
  const boundary = page.getByRole('heading', { name: /^Something went wrong$/ });
  if (await boundary.count()) {
    const alert = await page
      .locator('.ui-state--error, .status-page')
      .first()
      .innerText()
      .catch(() => '');
    watcher.add('error-boundary', alert || 'Something went wrong');
  }
  if (
    await page
      .getByRole('heading', { name: /^(We couldn’t find that page|Page not found|Not available yet)$/ })
      .count()
  )
    watcher.add('not-found', 'the page shows a not-found message');
  const errorStates = page.locator('main .ui-state--error');
  for (let i = 0; i < (await errorStates.count()); i++) {
    const text = await errorStates.nth(i).innerText();
    if (!/Something went wrong/.test(text)) watcher.add('error-boundary', text);
  }
  if ((await main.count()) && !(await page.locator('h1:visible').count()))
    watcher.add('no-h1', 'no visible <h1>');
}

/** Empty states on screen (title text), so the report can flag lists that should show demo data. */
export async function emptyStates(page: Page): Promise<string[]> {
  return page
    .locator('main .ui-state:not(.ui-state--error) .ui-state__title, main .ui-table-empty')
    .evaluateAll((els) => els.map((e) => (e.textContent ?? '').replace(/\s+/g, ' ').trim()).filter(Boolean));
}

/** Nav links of the portal on screen (the sidebar), as absolute paths in DOM order. */
export async function navLinks(page: Page): Promise<string[]> {
  const hrefs = await page
    .locator('nav.portal-nav a[href]')
    .evaluateAll((els) => els.map((e) => (e as HTMLAnchorElement).getAttribute('href') ?? ''));
  return [...new Set(hrefs.filter((h) => h.startsWith('/')))];
}

/**
 * In-page sub-navigation of a portal page (e.g. Email → Campaigns, Audience, Templates; Social → Compose, Inbox…): links
 * inside <main>'s own navs and link tab lists that stay in `portal`. Breadcrumbs are not sub-navigation.
 */
export async function subNavLinks(page: Page, portal: string): Promise<string[]> {
  const hrefs = await page
    .locator('main nav:not([aria-label="Breadcrumb"]) a[href], main [role="tablist"] a[href]')
    .evaluateAll((els) => els.map((e) => (e as HTMLAnchorElement).getAttribute('href') ?? ''));
  return [...new Set(hrefs.filter((h) => h.startsWith(`${portal}/`)))];
}

/** The page's tabs (button tabs of the Tabs component; link tabs are sub-navigation). */
export async function tabs(page: Page): Promise<Locator[]> {
  return page.locator('main [role="tab"]:not(a)').all();
}

/**
 * Pages that list one client's records start with a "Choose a client" picker. Picks Nimbus Fitness (the demo client
 * with every kind of record), or the first client, and answers true when it chose one.
 */
export async function chooseClient(page: Page): Promise<boolean> {
  const pickers = page.locator('main select:visible');
  for (let i = 0; i < (await pickers.count()); i++) {
    const select = pickers.nth(i);
    const state = await select.evaluate((el) => {
      const s = el as HTMLSelectElement;
      return {
        value: s.value,
        placeholder: s.options[0]?.text ?? '',
        options: Array.from(s.options)
          .slice(1)
          .map((o) => ({ value: o.value, text: o.text })),
      };
    });
    if (state.value !== '' || state.placeholder !== 'Choose a client' || state.options.length === 0) continue;
    const choice = state.options.find((o) => o.text === 'Nimbus Fitness') ?? state.options[0]!;
    await select.selectOption(choice.value);
    return true;
  }
  return false;
}

/**
 * The first link in <main> to a page below `path` (a list's first row → its detail page), if any. Pages of the portal
 * nav (`nav`) are never details.
 */
export async function firstDetailLink(
  page: Page,
  path: string,
  nav: ReadonlySet<string> = new Set(),
): Promise<string | undefined> {
  const hrefs = await page
    .locator('main a[href]')
    .evaluateAll((els) => els.map((e) => (e as HTMLAnchorElement).getAttribute('href') ?? ''));
  const prefix = `${path.replace(/\/$/, '')}/`;
  return hrefs.find(
    (h) =>
      h.startsWith(prefix) &&
      !nav.has(h) &&
      // "New …" pages are create forms, not details; the primary-action check covers them.
      !/\/(new|create)(\/|$|\?)/.test(h.slice(prefix.length - 1)),
  );
}

/**
 * Header actions it is safe to press: they open a dialog, a menu or a form and never change data by themselves.
 * Anything that sounds like it writes (publish, send, approve, delete…) is skipped.
 */
const safeAction =
  /^(new|create|add|invite|edit|upload|record|compose|import|log|request|write|plan|build|connect)\b/i;
const unsafeAction =
  /delete|remove|archive|publish|send|approve|reject|finali[sz]e|pay\b|void|cancel|suspend|disable|revoke|reset|run|sync|refresh|duplicate|copy|export|download|sign out|log in as|submit|mark|close|reopen|restore|resend|retry|clone|regenerate|generate|activate|pause|resume|accept|decline|verify|draft|save/i;

const accessibleName = async (el: Locator) =>
  ((await el.getAttribute('aria-label')) ?? (await el.innerText())).replace(/\s+/g, ' ').trim();

/**
 * The actions worth pressing on a page, by accessible name: every safe page-header button, plus the first row-level
 * "Edit …" button in <main> (lists that edit in a dialog).
 */
export async function primaryActions(page: Page): Promise<string[]> {
  const names: string[] = [];
  // Submit buttons (type=submit or tied to a form) save the page's form: never pressed.
  const pressable = 'button:visible:not([disabled]):not([type="submit"]):not([form]):not([role])';
  for (const button of await page.locator(`main .ui-page-header__actions ${pressable}`).all()) {
    const name = await accessibleName(button);
    if (safeAction.test(name) && !unsafeAction.test(name) && !names.includes(name)) names.push(name);
  }
  for (const button of await page.locator(`main ${pressable}`).all()) {
    const name = await accessibleName(button).catch(() => '');
    if (/^Edit\b/.test(name) && !unsafeAction.test(name)) {
      if (!names.includes(name)) names.push(name);
      break;
    }
  }
  return names;
}

/** The enabled button called `name` in <main>. */
export function actionButton(page: Page, name: string): Locator {
  return page
    .locator('main')
    .getByRole('button', { name, exact: true })
    .and(page.locator(':visible:enabled'))
    .first();
}

/** Safe page-header links (e.g. "New campaign" → /…/new): create/edit pages the crawler should open, never submit. */
export async function headerLinks(page: Page, portal: string): Promise<string[]> {
  const links = page.locator('main .ui-page-header__actions a[href]:visible');
  const hrefs: string[] = [];
  for (const link of await links.all()) {
    const name = await accessibleName(link);
    const href = (await link.getAttribute('href')) ?? '';
    if (href.startsWith(`${portal}/`) && safeAction.test(name) && !unsafeAction.test(name)) hrefs.push(href);
  }
  return [...new Set(hrefs)];
}

/**
 * Presses `button` and checks that something happens (a dialog or menu opens, the URL changes or <main> changes),
 * then backs out without saving: Escape (or the dialog's Cancel) for dialogs and menus, history back for pages.
 */
export async function exercise(page: Page, watcher: PageWatcher, name: string): Promise<string> {
  const button = actionButton(page, name);
  if (!(await button.count())) return 'gone';
  const before = page.url();
  await page.evaluate(() => {
    const w = window as unknown as { __crawlMutations: number; __crawlObserver?: MutationObserver };
    w.__crawlMutations = 0;
    w.__crawlObserver?.disconnect();
    w.__crawlObserver = new MutationObserver((m) => (w.__crawlMutations += m.length));
    w.__crawlObserver.observe(document.body, { childList: true, subtree: true, attributes: true });
  });
  await button.click();
  const overlay = page.locator(
    '[role="dialog"]:visible, [role="alertdialog"]:visible, [role="menu"]:visible',
  );
  const opened = await overlay
    .first()
    .waitFor({ state: 'visible', timeout: 3_000 })
    .then(() => true)
    .catch(() => false);
  if (opened) {
    const role = await overlay.first().getAttribute('role');
    if (role !== 'menu') {
      const label =
        (await overlay.first().getAttribute('aria-label')) ??
        (await overlay.first().getAttribute('aria-labelledby'));
      if (!label) watcher.add('dialog', `"${name}" opens a dialog without an accessible name`);
      await watcher.settle(5_000);
      await auditDialog(page, watcher, name);
      const cancel = overlay
        .first()
        .getByRole('button', { name: /^(cancel|close)$/i })
        .first();
      if (await cancel.isVisible().catch(() => false)) await cancel.click();
      else await page.keyboard.press('Escape');
    } else {
      await page.keyboard.press('Escape');
    }
    const closed = await page
      .locator('[role="dialog"]:visible, [role="alertdialog"]:visible, [role="menu"]:visible')
      .first()
      .waitFor({ state: 'hidden', timeout: 3_000 })
      .then(() => true)
      .catch(() => false);
    if (!closed) {
      watcher.add('dialog', `"${name}": the dialog does not close with Cancel/Escape`);
      await page.goto(before);
    }
    return role === 'menu' ? 'menu' : 'dialog';
  }
  if (page.url() !== before) {
    await watcher.settle();
    await auditPage(page, watcher);
    const to = new URL(page.url()).pathname;
    await page.goBack();
    await watcher.settle();
    return `page ${to}`;
  }
  const mutations = await page.evaluate(
    () => (window as unknown as { __crawlMutations: number }).__crawlMutations,
  );
  if (mutations === 0) {
    watcher.add('inert-action', `"${name}" does nothing`);
    return 'inert';
  }
  return 'inline';
}

/** A dialog's own problems: an error state inside it (its form failed to load its options). */
async function auditDialog(page: Page, watcher: PageWatcher, name: string) {
  const errors = page.locator(
    '[role="dialog"] .ui-state--error, [role="dialog"] [role="alert"]:has-text("wrong")',
  );
  if (await errors.count())
    watcher.add('dialog', `"${name}": ${(await errors.first().innerText()).slice(0, 200)}`);
}

// ------------------------------------------------------------------ report

export const REPORT_DIR = join(
  dirname(fileURLToPath(import.meta.url)),
  '..',
  '..',
  '..',
  'test-results',
  'crawl',
);

export interface RoleReport {
  role: string;
  visited: string[];
  empty: Record<string, string[]>;
  /** "path :: action" → what pressing it did (dialog, menu, page …, inline, inert). */
  actions: Record<string, string>;
  findings: Finding[];
}

/** Writes test-results/crawl/<name>.json (visited pages, empty states and findings) for triage. */
export function writeReport(name: string, report: unknown) {
  mkdirSync(REPORT_DIR, { recursive: true });
  writeFileSync(join(REPORT_DIR, `${name}.json`), JSON.stringify(report, null, 2));
}
