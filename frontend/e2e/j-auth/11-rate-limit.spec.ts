import { expect, test } from '@playwright/test';
import { accounts, raw } from './support/auth';

/**
 * Rate limits, last because they throttle the whole suite's address for a minute. scripts/e2e-journeys.sh relaxes the
 * 10/minute credential limit and the 300/minute global one (the suites sign in and load pages far more often than one
 * person), so this spec trips the API's global per-address limiter (RateLimiting__GlobalPerMinute) for real, checks the 429 contract and the sign-in page's message while
 * it lasts, and waits for the window to pass. The 10/minute credential policy itself is covered by
 * backend/tests/.../Auth/AuthRateLimitTests.cs.
 */
test('too many requests: a real 429 with Retry-After, and a friendly message on the sign-in form', async ({
  page,
}) => {
  test.setTimeout(240_000);
  await page.goto('/login');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();

  // Burn the address's budget with a cheap public endpoint until the API refuses, and keep burning (the bucket refills
  // once a minute) while the sign-in form is submitted.
  let limited: Awaited<ReturnType<typeof raw>> | undefined;
  let burning = true;
  const burn = async () => {
    while (burning) {
      const batch = await Promise.all(Array.from({ length: 20 }, () => raw('GET', '/meta/currencies')));
      limited ??= batch.find((r) => r.status === 429);
    }
  };
  const burner = burn();
  await expect.poll(() => limited !== undefined, { timeout: 60_000 }).toBe(true);
  expect(limited!.json).toMatchObject({ status: 429, code: 'rate_limited' });
  expect(limited!.headers.get('content-type')).toContain('application/problem+json');
  expect(Number(limited!.headers.get('retry-after'))).toBeGreaterThan(0);

  // The sign-in form now gets a 429 as well and says so in words, keeping what was typed.
  await page.getByLabel('Email', { exact: true }).fill(accounts.participant.email);
  await page.getByLabel('Password', { exact: true }).fill(accounts.participant.password);
  const answer = page.waitForResponse((r) => r.url().endsWith('/api/v1/auth/login'));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  const status = (await answer).status();
  burning = false;
  await burner;
  expect(status).toBe(429);
  const alert = page.getByRole('alert');
  await expect(alert).toContainText('Too many requests. Please wait and try again.');
  await expect(alert).not.toContainText(/rate_limited|429|Exception/);
  await expect(page.getByLabel('Email', { exact: true })).toHaveValue(accounts.participant.email);
  await expect(page).toHaveURL(/\/login$/);

  // Once the window has passed, the same sign-in goes through.
  await expect
    .poll(async () => (await raw('GET', '/meta/currencies')).status, { timeout: 90_000, intervals: [5_000] })
    .toBe(200);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/app(\/|$)/);
});
