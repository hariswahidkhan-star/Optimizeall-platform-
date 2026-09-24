import { existsSync } from 'node:fs';
import { defineConfig, devices } from '@playwright/test';

/**
 * E2E configuration.
 *
 * Suites live in e2e/<suite>/ and are selected with E2E_SUITE (default "smoke"):
 *  - smoke:    runs against `vite preview` with every /api call mocked via page.route (no backend needed).
 *  - journeys: full-stack journeys against a running backend; add e2e/journeys/*.spec.ts and, if needed,
 *              e2e/journeys/global-setup.ts (any e2e/<suite>/global-setup.ts is picked up automatically) to seed
 *              data / create users.
 *  - agency:   full-stack agency-platform journeys (public website, CRM → proposal → invoice, delivery approvals,
 *              email, social, landing pages, authorization) against the Demo seed's accounts and clients.
 *  - platform: full-stack journeys through the newest platform features (test users, "log in as", custom roles, the
 *              payments hub, editing across areas, Google sign-in off) against the Demo seed.
 *
 * E2E_BASE_URL (or PLAYWRIGHT_BASE_URL) points the tests at an already running app; without it Playwright builds the
 * app and serves it with `vite preview` on :5173.
 *
 * The journeys share one database and build on each other (participant → reviewer → finance → admin), so they run
 * serially on one worker, in file order, without retries; the mobile project runs only the participant journey.
 * Run them with scripts/e2e-journeys.sh (fresh database, API, `vite preview`, teardown).
 *
 * The agency suite runs the same way (serial, one worker, no retries); its mobile project runs only
 * responsive.spec.ts (which pins a 390×844 viewport) and the desktop project runs everything else. Run it with
 * `E2E_SUITE=agency E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The platform suite (test users and "log in as", custom roles, the payments hub, editing across areas, Google sign-in
 * off) runs the same way against the Demo seed with the non-production test sign-in on: desktop runs everything but
 * responsive.spec.ts, which the mobile project runs alone (390×844). Run it with
 * `E2E_SUITE=platform E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The j-participant suite walks one participant's whole lifecycle (registration and email verification, profile,
 * social profiles and eligibility, campaigns, submissions through correction/withdrawal/rejection/appeal/approval,
 * earnings, payout details and a paid payout, referrals, notifications and preferences, support, password change,
 * sign-out and session refresh) against the Baseline seed, with the staff side driven through the API. It runs like
 * the agency suite (serial, one worker, no retries): desktop runs everything but responsive.spec.ts, which the mobile
 * project runs alone. Run it with `E2E_SUITE=j-participant E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The j-delivery suite walks one complete client-delivery journey through the client portal (onboarding a new client,
 * inviting a user per duty, project from a template and kanban, deliverable versions and client approvals, time and
 * timesheets, messages and meetings, reports, briefs, feedback, dashboard numbers, and the negatives: tenancy, duties,
 * concurrent edits, double clicks, interrupted uploads, file limits, expired sessions). Its specs build on each other
 * (01-onboarding creates the client the others use), so it runs serially like the agency suite; the mobile project runs
 * only responsive.spec.ts. Run it with `E2E_SUITE=j-delivery E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The j-campaigns suite walks the campaign manager + reviewer journey end to end (build a campaign in the editor, assets,
 * disclosures, versioned reward rules, preview, publish and the scheduler, lifecycle actions, duplicate, invitation link
 * and public landing page, A/B experiment, the review queue with claims and races, live checks, appeals, stats and the
 * analytics dashboard, plus budget/cap/permission/concurrency negatives). Serial on one worker, desktop only, against
 * Baseline + Demo: `E2E_SUITE=j-campaigns E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The j-admin suite walks the platform-administration journey end to end (users, suspensions, built-in and custom
 * roles with their guardrails, test accounts and "log in as", audit, settings, content, jobs, global search and the
 * negative paths) against the Demo seed with the non-production test sign-in on. Serial, one worker, desktop project
 * only. Run it with `E2E_SUITE=j-admin E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The j-content suite walks the staff side that controls the public site (CMS pages with revisions and schedules, the
 * service catalog and pricing, case studies/testimonials/team, the blog workflow, site settings and page texts, SEO:
 * head tags, JSON-LD, robots.txt and sitemap.xml, the landing-page and form builders, and the permission negatives). It
 * runs like j-campaigns (serial, one worker, desktop only) against Baseline + Demo:
 * `E2E_SUITE=j-content E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The j-social suite walks the agency's social media + paid ads journey (profiles and connections, the content calendar,
 * internal and client approvals, the publishing job against a local Graph API stub, post analytics, ad accounts, spend
 * import, budgets and client ad reports, plus permissions, tenancy, impersonation, conflicts and double clicks). Serial
 * on one worker, desktop only, against Baseline + Demo: `E2E_SUITE=j-social E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The a11y suite (accessibility & responsive layout: axe WCAG 2.2 A/AA in the light and dark theme, no horizontal
 * scroll at 360/768/1280 px, keyboard and focus behaviour) runs against the Demo seed too. It never changes data, so
 * its tests run in parallel (two workers); each test sets its own viewport, so only the desktop project runs it. Run
 * it with `E2E_SUITE=a11y E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh` (see docs/ACCESSIBILITY.md).
 * The crawl suite signs in as every demo role, visits every nav link of its portals (plus the first detail page of each
 * list and each page's primary action, cancelled) and every public website link, and fails on console errors, failed API
 * calls, error boundaries and pages without an h1. It never saves anything, so its roles run in parallel
 * (E2E_CRAWL_WORKERS, default 2) on desktop only. Run it with `E2E_SUITE=crawl E2E_DB_PROVIDER=sqlite
 * scripts/e2e-journeys.sh`; each role's visited pages, empty states and findings land in test-results/crawl/.
 *
 * The j-finance suite walks the finance money journey end to end (ledger, four-eyes adjustments, FX, holds, schedule,
 * a payout batch from preparation to reconciliation, the payments hub's incoming flows and the negative paths) against
 * the Demo seed. Its steps build on each other: serial, one worker, no retries, desktop only. Run it with
 * `E2E_SUITE=j-finance E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The j-lead-to-cash suite follows one business journey end to end against the Demo seed: an anonymous visitor's
 * contact/audit/quote forms, consultation booking and newsletter double opt-in → the inquiry for staff → CRM contact,
 * deal, score and pipeline → a proposal from a template with catalog lines and tax → the client accepts on /p/:token →
 * client account and owner invitation → retainer contract → the recurring invoice job → /i/:token and the client
 * portal → "I've paid" → finance confirms → paid; plus spam, replay, permission, tenancy and concurrency negatives. Its
 * specs build on each other (serial, one worker, desktop only). Run it with
 * `E2E_SUITE=j-lead-to-cash E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 *
 * The j-rates suite walks person-level pricing end to end (a rate card and a rate group with bulk-added members, the
 * card assigned to the group, an expiring personal deal with "explain this rate", participants seeing only their own
 * rate, submit → approve → ledger rate source, a new card version that leaves approved earnings unchanged, four-eyes on
 * a large raise and the deal's expiry). Serial, one worker, desktop only, against Baseline + Demo:
 * `E2E_SUITE=j-rates E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh`.
 */
const suite = process.env.E2E_SUITE ?? 'smoke';
/** Suites whose mobile project runs only responsive.spec.ts (and whose desktop project runs everything else). */
const responsiveSplit =
  suite === 'agency' || suite === 'platform' || suite === 'j-participant' || suite === 'j-delivery';
/**
 * Serial journeys run on desktop only: their screens are staff tools (campaign manager/reviewer, finance, admin…) whose
 * phone layouts are covered by the a11y and responsive specs.
 */
const desktopJourney = [
  'j-campaigns',
  'j-finance',
  'j-lead-to-cash',
  'j-admin',
  'j-content',
  'j-social',
  'j-rates',
].includes(suite);
/** The finance journey compares datetime-local input (browser time) with UTC periods, so its browser runs in UTC. */
const finance = suite === 'j-finance';
/** Full-stack suites share one database and build on earlier steps: serial, one worker, no retries. */
const journeys = suite === 'journeys' || responsiveSplit || desktopJourney;
/** The crawl is read-only: roles run in parallel, desktop only. */
const crawl = suite === 'crawl';
const mobileOnly = responsiveSplit ? /responsive\.spec\.ts$/ : /participant\.spec\.ts$/;
/** Read-only full-stack audit: parallel, no retries, desktop project only (tests pick their own viewports). */
const a11y = suite === 'a11y';
const baseURL = process.env.E2E_BASE_URL || process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5173';
const suiteSetup = `./e2e/${suite}/global-setup.ts`;

export default defineConfig({
  testDir: `./e2e/${suite}`,
  outputDir: './test-results',
  fullyParallel: !journeys,
  forbidOnly: !!process.env.CI,
  retries: journeys || a11y ? 0 : process.env.CI ? 1 : 0,
  ...(journeys ? { workers: 1, timeout: 120_000, expect: { timeout: 15_000 } } : {}),
  ...(a11y ? { workers: 2, timeout: 120_000, expect: { timeout: 15_000 } } : {}),
  ...(crawl ? { workers: Number(process.env.E2E_CRAWL_WORKERS ?? 2), retries: 0, timeout: 10 * 60_000 } : {}),
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  globalSetup: existsSync(suiteSetup) ? suiteSetup : undefined,
  use: {
    baseURL,
    trace: 'retain-on-failure',
    // The crawl presses buttons it does not know; a covered one must fail fast, not hang until the test times out.
    ...(crawl ? { actionTimeout: 10_000, navigationTimeout: 20_000 } : {}),
    screenshot: 'only-on-failure',
    // The finance journey types dates and times into datetime-local inputs (browser time) and compares them with UTC
    // periods: its browsers run in UTC so the arithmetic is the same on every machine.
    ...(finance ? { timezoneId: 'UTC', locale: 'en-US' } : {}),
  },
  projects: [
    {
      name: 'desktop-chromium',
      use: { ...devices['Desktop Chrome'] },
      ...(responsiveSplit ? { testIgnore: mobileOnly } : {}),
    },
    ...(a11y || crawl || desktopJourney
      ? []
      : [
          {
            name: 'mobile-chromium',
            use: { ...devices['Pixel 7'] },
            ...(journeys ? { testMatch: mobileOnly } : {}),
          },
        ]),
  ],
  webServer:
    process.env.E2E_BASE_URL || process.env.PLAYWRIGHT_BASE_URL
      ? undefined
      : {
          command: 'npm run build && npx vite preview --port 5173 --strictPort',
          url: 'http://localhost:5173',
          reuseExistingServer: !process.env.CI,
          timeout: 180_000,
        },
});
