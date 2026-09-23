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
 *
 * E2E_BASE_URL points the tests at an already running app; without it Playwright builds the app and serves it with
 * `vite preview` on :5173.
 */
const suite = process.env.E2E_SUITE ?? 'smoke';
const baseURL = process.env.E2E_BASE_URL || 'http://localhost:5173';
const suiteSetup = `./e2e/${suite}/global-setup.ts`;

export default defineConfig({
  testDir: `./e2e/${suite}`,
  outputDir: './test-results',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  globalSetup: existsSync(suiteSetup) ? suiteSetup : undefined,
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop-chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'mobile-chromium', use: { ...devices['Pixel 7'] } },
  ],
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: 'npm run build && npx vite preview --port 5173 --strictPort',
        url: 'http://localhost:5173',
        reuseExistingServer: !process.env.CI,
        timeout: 180_000,
      },
});
