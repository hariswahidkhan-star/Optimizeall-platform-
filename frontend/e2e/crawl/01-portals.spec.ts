import { appendFileSync } from 'node:fs';
import { type Page, expect, test } from '@playwright/test';
import {
  PageWatcher,
  type RoleReport,
  auditPage,
  chooseClient,
  emptyStates,
  exercise,
  firstDetailLink,
  navLinks,
  portalBases,
  headerLinks,
  linkShape,
  navigate,
  mainLinks,
  primaryActions,
  roles,
  signIn,
  subNavLinks,
  tabs,
  writeReport,
} from './support/crawl';

/**
 * Every demo role signs in and walks every page its portals link to: each nav link, each in-page sub-navigation link
 * (e.g. Email → Campaigns, Audience…), each tab, the first detail page each list links to, and each page's primary
 * header action (opened, then cancelled). Pages that need a client first (social, ads UTM…) are also seen with the
 * first demo client chosen. Fails on console/page errors, failed API calls, error boundaries, not-found pages, pages
 * without an h1 and actions that do nothing; empty states are only reported (test-results/crawl/<role>.json) because
 * "empty" is sometimes the right answer for a role.
 */
test.describe.configure({ mode: 'parallel' });

const debug = (line: string) => {
  if (process.env.E2E_CRAWL_DEBUG) appendFileSync(process.env.E2E_CRAWL_DEBUG, `${line}\n`);
};

async function open(page: Page, watcher: PageWatcher, path: string) {
  watcher.path = path;
  // A page may open a dialog by itself (e.g. /app/social-accounts?add=1); close it so the nav is clickable again.
  if (await page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').count())
    await page.keyboard.press('Escape');
  const link = page.locator(`a[href="${path}"]:visible`).first();
  const clicked =
    (await link.count()) &&
    (await link
      .click({ timeout: 5_000 })
      .then(() => true)
      .catch(() => false));
  if (!clicked) await navigate(page, path);
  await page.waitForURL((url) => url.pathname === path.split('?')[0], { timeout: 10_000 }).catch(() => {});
  await watcher.settle();
}

for (const role of roles) {
  test(`${role.key}: every page loads cleanly`, async ({ page }) => {
    const watcher = new PageWatcher(page, role.key);
    watcher.path = '/login';
    await signIn(page, role.user, /\/(admin|finance|agency|manage|review|app|client)(\/|$)/);
    await watcher.settle();

    const report: RoleReport = {
      role: role.key,
      visited: [],
      empty: {},
      actions: {},
      findings: watcher.findings,
    };
    const visited = new Set<string>();
    let navPaths = new Set<string>();
    /** Every in-app link seen in <main>, one per shape (ids replaced), to check that none leads nowhere. */
    const linkShapes = new Map<string, string>();

    async function recordEmpty(key: string) {
      const empty = await emptyStates(page);
      if (empty.length) report.empty[key] = [...new Set(empty)];
    }

    async function pressPrimaryActions(where: string) {
      for (const name of await primaryActions(page)) {
        const key = `${where} :: ${name}`;
        if (key in report.actions) continue;
        report.actions[key] = await exercise(page, watcher, name).catch((error: Error) => {
          watcher.add('inert-action', `"${name}" failed: ${error.message.split('\n')[0]}`);
          return 'failed';
        });
      }
    }

    async function visit(path: string, depth: number, portal: string) {
      if (visited.has(path)) return;
      visited.add(path);
      report.visited.push(path);
      const started = Date.now();
      debug(`> ${role.key} ${path}`);
      await open(page, watcher, path);
      await auditPage(page, watcher);
      await recordEmpty(path);
      for (const href of await mainLinks(page))
        if (!linkShapes.has(linkShape(href))) linkShapes.set(linkShape(href), href);
      // Where to go next, as the page first shows (before tabs or filters change the list).
      const subNav = depth > 1 ? [] : await subNavLinks(page, portal);
      const forms = depth > 1 ? [] : await headerLinks(page, portal);
      // A portal home links to the pages its nav already lists: those are visited as nav pages, not as its detail.
      const detail = depth > 1 || path === portal ? undefined : await firstDetailLink(page, path, navPaths);
      debug(`  next: ${detail ?? '-'} | ${forms.join(' ')} | ${subNav.join(' ')}`);

      // Links such as /app/social-accounts?add=1 open their dialog on arrival: close it before going on.
      const opened = page.locator('[role="dialog"]:visible, [role="alertdialog"]:visible').first();
      if (await opened.count()) {
        await page.keyboard.press('Escape');
        await opened.waitFor({ state: 'hidden', timeout: 3_000 }).catch(() => {
          watcher.add('dialog', 'the dialog the page opened does not close with Escape');
        });
      }
      await pressPrimaryActions(path);

      // Every tab of the page (the first is already on screen).
      const tabList = await tabs(page);
      for (const tab of tabList.slice(1)) {
        if (!(await tab.isVisible().catch(() => false))) continue;
        const name = (await tab.innerText()).trim();
        await tab.click().catch(() => {});
        await watcher.settle();
        await auditPage(page, watcher);
        await recordEmpty(`${path} [tab ${name}]`);
      }

      // Pages that list one client's records: pick the first client and look again.
      if (await chooseClient(page)) {
        await watcher.settle();
        await auditPage(page, watcher);
        await recordEmpty(`${path} [client chosen]`);
        await pressPrimaryActions(`${path} [client chosen]`);
      }
      debug(`  ${Date.now() - started}ms findings=${watcher.findings.length}`);

      if (detail) await visit(detail, 2, portal);
      for (const form of forms) await visit(form, 2, portal);
      for (const sub of subNav) await visit(sub, depth + 1, portal);
    }

    for (const base of portalBases) {
      debug(`portal ${role.key} ${base}`);
      watcher.path = base;
      await navigate(page, base);
      await page.waitForURL((url) => url.pathname !== '/login', { timeout: 10_000 }).catch(() => {});
      await watcher.settle();
      if (!new URL(page.url()).pathname.startsWith(base)) continue;
      if (!(await page.locator('nav.portal-nav').count())) continue;
      const nav = await navLinks(page);
      navPaths = new Set(nav);
      for (const path of nav) await visit(path, 0, base);
    }

    // Every other kind of link the pages offer (row links, "View all", cross-links between areas) opens a real page.
    const visitedShapes = new Set(report.visited.map(linkShape));
    for (const [shape, href] of linkShapes) {
      // Public website links are the public spec's job.
      if (!portalBases.some((b) => href === b || href.startsWith(`${b}/`))) continue;
      if (visitedShapes.has(shape) || visited.has(href)) continue;
      visitedShapes.add(shape);
      report.visited.push(href);
      debug(`> ${role.key} link ${href}`);
      await open(page, watcher, href);
      await auditPage(page, watcher);
    }

    writeReport(role.key, report);
    expect(report.visited.length, `${role.key} visits at least one page`).toBeGreaterThan(0);
    expect(watcher.findings, `${role.key}: problems found (see test-results/crawl/${role.key}.json)`).toEqual(
      [],
    );
  });
}
