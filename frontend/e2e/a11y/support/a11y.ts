import AxeBuilder from '@axe-core/playwright';
import { type Page, expect } from '@playwright/test';
import { DEMO_PASSWORD } from '../../agency/support/agency';
import type { Credentials } from '../../journeys/support/fixtures';

export { expectNoHorizontalScroll, signIn } from '../../journeys/support/ui';

/**
 * Accessibility & responsive suite (E2E_SUITE=a11y): a representative page set per portal, audited with axe
 * (WCAG 2.0/2.1/2.2 A + AA, colour contrast in the light and the dark theme) and checked for horizontal page scroll
 * at 360, 768 and 1280 px. Runs against the Demo seed (scripts/e2e-journeys.sh with E2E_SUITE=a11y) and never
 * changes data, so the specs run in parallel.
 */

const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

export const actors = {
  participant: demo('sara.participant@demo.optimizeall.app', 'Sara'),
  reviewer: demo('reviewer1@demo.optimizeall.app', 'Reviewer 1'),
  manager: demo('manager@demo.optimizeall.app', 'Campaign Manager'),
  finance: demo('finance1@demo.optimizeall.app', 'Finance'),
  admin: demo('admin@demo.optimizeall.app', 'Demo Admin'),
  client: demo('owner@nimbus.demo.optimizeall.app', 'Nimbus Owner'),
} as const;

export type ActorId = keyof typeof actors;

/** The 360 / 768 / 1280 px layouts every page is checked at (phone, tablet, desktop). */
export const widths = [
  { name: 'phone', width: 360, height: 780, isMobile: true },
  { name: 'tablet', width: 768, height: 1024, isMobile: false },
  { name: 'desktop', width: 1280, height: 900, isMobile: false },
] as const;

/**
 * A page to audit: a fixed path, or — for detail pages whose ids come from the seed — the first link on `from` whose
 * path matches `detail`.
 */
export type AuditTarget = { path: string } | { from: string; detail: RegExp; name: string };

export interface PortalSet {
  id: string;
  /** Who signs in (none for the public website). */
  actor?: ActorId;
  /** Where the actor lands after signing in. */
  landing?: RegExp;
  pages: AuditTarget[];
}

const id = '[0-9a-f-]{36}';
const detail = (from: string, pattern: string, name: string): AuditTarget => ({
  from,
  detail: new RegExp(`^${pattern.replace(':id', id)}(\\?.*)?$`),
  name,
});

export const portalSets: PortalSet[] = [
  {
    id: 'public',
    pages: [
      { path: '/' },
      { path: '/services' },
      detail('/services', '/services/[a-z0-9-]+', 'a service page'),
      { path: '/industries' },
      { path: '/case-studies' },
      detail('/case-studies', '/case-studies/[a-z0-9-]+', 'a case study'),
      { path: '/pricing' },
      { path: '/team' },
      { path: '/careers' },
      { path: '/blog' },
      detail('/blog', '/blog/[a-z0-9-]+', 'a blog post'),
      { path: '/contact' },
      { path: '/get-a-quote' },
      { path: '/faq' },
      { path: '/creators' },
      // Standalone public pages: a client landing page, a public campaign page and a tokenized document link.
      { path: '/lp/karachi-eats/iftar-event' },
      { path: '/c/nimbus-fitness-app-launch' },
      { path: '/i/not-a-valid-invoice-link' },
      { path: '/login' },
      { path: '/register' },
      { path: '/forgot-password' },
    ],
  },
  {
    id: 'participant',
    actor: 'participant',
    landing: /\/app(\/|$)/,
    pages: [
      { path: '/app' },
      { path: '/app/campaigns' },
      detail('/app/campaigns', '/app/campaigns/[a-z0-9-]+', 'a campaign'),
      { path: '/app/submissions' },
      { path: '/app/earnings' },
      { path: '/app/payouts' },
      { path: '/app/social-accounts' },
      { path: '/app/referrals' },
      { path: '/app/achievements' },
      { path: '/app/notifications' },
      { path: '/app/support' },
      { path: '/app/profile' },
    ],
  },
  {
    id: 'reviewer',
    actor: 'reviewer',
    landing: /\/review(\/|$)/,
    pages: [
      { path: '/review' },
      { path: '/review/queue' },
      { path: '/review/live-checks' },
      { path: '/review/appeals' },
      { path: '/review/social-verification' },
    ],
  },
  {
    id: 'manager',
    actor: 'manager',
    landing: /\/manage(\/|$)/,
    pages: [
      { path: '/manage' },
      { path: '/manage/campaigns' },
      detail('/manage/campaigns', '/manage/campaigns/:id', 'a campaign'),
      { path: '/manage/templates' },
      { path: '/manage/calendar' },
      { path: '/manage/invitations' },
      { path: '/manage/experiments' },
      detail('/manage/experiments', '/manage/experiments/:id', 'an experiment'),
      { path: '/manage/analytics' },
      { path: '/manage/achievements' },
    ],
  },
  {
    id: 'finance',
    actor: 'finance',
    landing: /\/(finance|agency)(\/|$)/,
    pages: [
      { path: '/finance' },
      { path: '/finance/payments' },
      { path: '/finance/batches' },
      detail('/finance/batches', '/finance/batches/:id', 'a payout batch'),
      { path: '/finance/ledger' },
      { path: '/finance/approvals' },
      { path: '/finance/holds' },
      { path: '/finance/exchange-rates' },
      { path: '/finance/schedule' },
    ],
  },
  {
    id: 'admin',
    actor: 'admin',
    landing: /\/admin(\/|$)/,
    pages: [
      { path: '/admin' },
      { path: '/admin/users' },
      detail('/admin/users', '/admin/users/:id', 'a user'),
      { path: '/admin/roles' },
      { path: '/admin/settings' },
      { path: '/admin/content' },
      { path: '/admin/support' },
      { path: '/admin/audit' },
      { path: '/admin/jobs' },
      { path: '/admin/analytics' },
    ],
  },
  {
    id: 'agency-core',
    actor: 'admin',
    landing: /\/admin(\/|$)/,
    pages: [
      { path: '/agency' },
      { path: '/agency/clients' },
      detail('/agency/clients', '/agency/clients/:id', 'a client'),
      { path: '/agency/projects' },
      detail('/agency/projects', '/agency/projects/:id', 'a project board'),
      { path: '/agency/tasks' },
      { path: '/agency/deliverables' },
      { path: '/agency/time' },
      { path: '/agency/reports' },
      detail('/agency/reports', '/agency/reports/:id', 'a report'),
      { path: '/agency/crm' },
      { path: '/agency/crm/deals' },
      detail('/agency/crm/deals', '/agency/crm/deals/:id', 'a deal'),
      { path: '/agency/proposals' },
      detail('/agency/proposals', '/agency/proposals/:id', 'a proposal'),
      { path: '/agency/contracts' },
      detail('/agency/contracts', '/agency/contracts/:id', 'a contract'),
      { path: '/agency/billing' },
      { path: '/agency/billing/invoices' },
      detail('/agency/billing/invoices', '/agency/billing/invoices/:id', 'an invoice'),
    ],
  },
  {
    id: 'agency-marketing',
    actor: 'admin',
    landing: /\/admin(\/|$)/,
    pages: [
      { path: '/agency/email' },
      { path: '/agency/email/automations' },
      detail('/agency/email/automations', '/agency/email/automations/:id', 'an automation'),
      { path: '/agency/email/campaigns' },
      { path: '/agency/social' },
      { path: '/agency/social/compose' },
      { path: '/agency/social/analytics' },
      { path: '/agency/social/inbox' },
      { path: '/agency/ads' },
      { path: '/agency/ads/alerts' },
      { path: '/agency/ads/pacing' },
      { path: '/agency/seo' },
      { path: '/agency/pages' },
      detail('/agency/pages', '/agency/pages/:id', 'a landing page builder'),
      { path: '/agency/pages/forms' },
      detail('/agency/pages/forms', '/agency/pages/forms/:id', 'a form builder'),
      { path: '/agency/integrations' },
      { path: '/agency/website/overview' },
      { path: '/agency/website/inquiries' },
      { path: '/agency/website/pages' },
      detail('/agency/website/pages', '/agency/website/pages/:id', 'a CMS page editor'),
      { path: '/agency/website/blog' },
      { path: '/agency/website/careers' },
      { path: '/agency/website/settings' },
    ],
  },
  {
    id: 'client',
    actor: 'client',
    landing: /\/client(\/|$)/,
    pages: [
      { path: '/client' },
      { path: '/client/approvals' },
      { path: '/client/projects' },
      { path: '/client/reports' },
      { path: '/client/briefs' },
      { path: '/client/messages' },
      { path: '/client/brand' },
      { path: '/client/team' },
      { path: '/client/billing' },
      detail('/client/billing', '/client/billing/invoices/:id', 'an invoice'),
      { path: '/client/email' },
      { path: '/client/seo' },
      { path: '/client/social' },
    ],
  },
];

/**
 * Targeted, documented exceptions (see docs/ACCESSIBILITY.md → Known exceptions). Each one names the rule, the element
 * it applies to and why; axe rules are never switched off globally.
 */
export const knownExceptions: { rule: string; selector: string; reason: string }[] = [];

const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22a', 'wcag22aa'];

function builder(page: Page) {
  let axe = new AxeBuilder({ page }).withTags(WCAG_TAGS);
  for (const exception of knownExceptions) axe = axe.exclude(exception.selector);
  return axe;
}

function format(violations: Awaited<ReturnType<AxeBuilder['analyze']>>['violations']) {
  return violations.map((v) => ({
    id: v.id,
    impact: v.impact,
    help: v.help,
    targets: v.nodes.slice(0, 5).map((n) => n.target.join(' ')),
  }));
}

async function setTheme(page: Page, theme: 'light' | 'dark') {
  await page.evaluate((t) => {
    document.documentElement.dataset.theme = t;
  }, theme);
}

/** axe (WCAG 2.0–2.2 A/AA) on the current page, in the light theme. */
export async function axeLight(page: Page) {
  await setTheme(page, 'light');
  return format((await builder(page).analyze()).violations);
}

/** Colour contrast only, in the dark theme (every token switches with `data-theme`). Restores the light theme. */
export async function axeDarkContrast(page: Page) {
  await setTheme(page, 'dark');
  try {
    return format((await builder(page).withRules(['color-contrast']).analyze()).violations);
  } finally {
    await setTheme(page, 'light');
  }
}

/** Waits until the page's data has loaded: a level-one heading and no loading skeletons or busy regions. */
export async function settle(page: Page) {
  await page.waitForLoadState('networkidle').catch(() => undefined);
  await expect(page.locator('h1').first()).toBeVisible();
  // Loading placeholders normally clear within a second or two; a page that keeps one is still audited.
  await expect(page.locator('main .ui-skeleton, main [aria-busy="true"]'))
    .toHaveCount(0, { timeout: 10_000 })
    .catch(() => undefined);
}

/**
 * Opens `path` inside the running single-page app with a client-side navigation (history.pushState + popstate, which
 * the router follows) — the way people move around — once the app is loaded; the first visit is a full page load.
 * Besides matching real use, it keeps the suite far below the API's per-IP limits for the session refresh (every
 * full page load refreshes the session) and the public endpoints.
 */
export async function visit(page: Page, path: string) {
  const loaded =
    page.url().startsWith('http') && (await page.evaluate(() => !!document.querySelector('#root > *')));
  if (!loaded) {
    await page.goto(path);
    return;
  }
  await page.evaluate((to) => {
    // Mark the current headings so we can tell when the next page has replaced them.
    document.querySelectorAll('h1').forEach((h) => h.setAttribute('data-a11y-previous', h.textContent ?? ''));
    window.history.pushState(null, '', to);
    window.dispatchEvent(new PopStateEvent('popstate', { state: null }));
  }, path);
  await page.waitForURL((url) => `${url.pathname}${url.search}` === path);
  // The URL changes at once; the route (often lazy-loaded) renders a moment later. Wait for a new or changed h1 — or
  // give up after a few seconds when the next page legitimately keeps the same heading (tabs of one area).
  await page
    .waitForFunction(
      () => {
        const h = document.querySelector('h1');
        return !!h && h.getAttribute('data-a11y-previous') !== h.textContent;
      },
      null,
      { timeout: 5_000 },
    )
    .catch(() => undefined);
  await page.evaluate(() =>
    document.querySelectorAll('[data-a11y-previous]').forEach((h) => h.removeAttribute('data-a11y-previous')),
  );
}

/** Resolves a target to a concrete path (following the first matching link for detail pages). */
export async function resolveTarget(
  page: Page,
  target: AuditTarget,
): Promise<{ path: string; name: string }> {
  if ('path' in target) return { path: target.path, name: target.path };
  await visit(page, target.from);
  await settle(page);
  const links = () =>
    page.locator('main a[href]').evaluateAll((all) => all.map((a) => a.getAttribute('href') ?? ''));
  // The list may still be rendering its rows after the page heading appears.
  await expect
    .poll(async () => (await links()).some((href) => target.detail.test(href)), {
      message: `a link to ${target.name} on ${target.from}`,
    })
    .toBe(true);
  const path = (await links()).find((href) => target.detail.test(href))!;
  return { path, name: `${target.name} (${path})` };
}

/** Horizontal page overflow in px (0 when the page fits the viewport). */
export function horizontalOverflow(page: Page) {
  return page.evaluate(() => Math.max(0, document.documentElement.scrollWidth - window.innerWidth));
}
