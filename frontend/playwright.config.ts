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
 */
const suite = process.env.E2E_SUITE ?? 'smoke';
/** Suites whose mobile project runs only responsive.spec.ts (and whose desktop project runs everything else). */
const responsiveSplit = suite === 'agency' || suite === 'platform';
/** Full-stack suites share one database and build on earlier steps: serial, one worker, no retries. */
const journeys = suite === 'journeys' || responsiveSplit;
const mobileOnly = responsiveSplit ? /responsive\.spec\.ts$/ : /participant\.spec\.ts$/;
const baseURL = process.env.E2E_BASE_URL || process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5173';
const suiteSetup = `./e2e/${suite}/global-setup.ts`;

export default defineConfig({
  testDir: `./e2e/${suite}`,
  outputDir: './test-results',
  fullyParallel: !journeys,
  forbidOnly: !!process.env.CI,
  retries: journeys ? 0 : process.env.CI ? 1 : 0,
  ...(journeys ? { workers: 1, timeout: 120_000, expect: { timeout: 15_000 } } : {}),
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  globalSetup: existsSync(suiteSetup) ? suiteSetup : undefined,
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'desktop-chromium',
      use: { ...devices['Desktop Chrome'] },
      ...(responsiveSplit ? { testIgnore: mobileOnly } : {}),
    },
    {
      name: 'mobile-chromium',
      use: { ...devices['Pixel 7'] },
      ...(journeys ? { testMatch: mobileOnly } : {}),
    },
  ],
  webServer: process.env.E2E_BASE_URL || process.env.PLAYWRIGHT_BASE_URL
    ? undefined
    : {
        command: 'npm run build && npx vite preview --port 5173 --strictPort',
        url: 'http://localhost:5173',
        reuseExistingServer: !process.env.CI,
        timeout: 180_000,
      },
});
