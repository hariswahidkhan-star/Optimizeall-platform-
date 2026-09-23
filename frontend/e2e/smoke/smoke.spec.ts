import { expect, test } from '@playwright/test';
import { authResponse, hasHorizontalScroll, mockApi, participant, problem } from '../support/mockApi';

test.describe('public site', () => {
  test('creator landing page (/creators) renders the value proposition and calls to action', async ({ page }) => {
    await mockApi(page);
    await page.goto('/creators');
    await expect(
      page.getByRole('heading', { level: 1, name: /Get paid to share brands you believe in/ }),
    ).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Three steps from post to payout' })).toBeVisible();
    await expect(
      page.getByRole('heading', { name: 'Fair for you, honest with your audience' }),
    ).toBeVisible();
    await expect(page.getByRole('link', { name: 'Create your free account' })).toBeVisible();
  });

  test('no horizontal scroll at 360px', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 780 });
    await mockApi(page);
    for (const path of ['/', '/creators', '/login', '/register', '/faq']) {
      await page.goto(path);
      await expect(page.locator('h1').first()).toBeVisible();
      expect(await hasHorizontalScroll(page), `horizontal scroll on ${path}`).toBe(false);
    }
  });

  test('FAQ falls back gracefully while the content API is unavailable', async ({ page }) => {
    await mockApi(page);
    await page.goto('/faq');
    await expect(page.getByRole('heading', { name: 'Answers are on their way' })).toBeVisible();
  });
});

test.describe('auth', () => {
  test('register form validates before calling the API', async ({ page }) => {
    const calls = await mockApi(page);
    await page.goto('/register?ref=FRIEND42');
    await expect(page.getByText('You were invited')).toBeVisible();

    await page.getByLabel('Email', { exact: true }).fill('not-an-email');
    await page.getByLabel('Password', { exact: true }).fill('password123');
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(page.getByLabel('Email', { exact: true })).toBeFocused();
    await expect(page.getByLabel('Email', { exact: true })).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByText('Enter a valid email address, like name@example.com.')).toBeVisible();
    await expect(page.getByText('This password is too common.')).toBeVisible();
    expect(calls.some((c) => c.path === '/auth/register')).toBe(false);
  });

  test('register submits and shows the check-email page', async ({ page }) => {
    const calls = await mockApi(page, {
      'POST /auth/register': (route) =>
        route.fulfill({
          status: 202,
          contentType: 'application/json',
          body: JSON.stringify({ message: 'Check your inbox.' }),
        }),
    });
    await page.goto('/register');
    await page.getByLabel('Email', { exact: true }).fill('grace@example.com');
    await page.getByLabel('Password', { exact: true }).fill('Correct-Horse-42');
    await page.getByLabel('Display name').fill('Grace Hopper');
    await page.getByLabel('Country').selectOption('GB');
    await page.getByRole('checkbox', { name: /accept the participant rules/i }).check();
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await expect(page.getByText('grace@example.com')).toBeVisible();
    const body = calls.find((c) => c.path === '/auth/register')?.body as Record<string, unknown>;
    expect(body).toMatchObject({ email: 'grace@example.com', countryCode: 'GB', acceptTerms: true });
  });

  test('login shows the server error message', async ({ page }) => {
    await mockApi(page, {
      'POST /auth/login': (route) =>
        route.fulfill(problem(401, 'auth.invalid_credentials', 'The email or password is incorrect.')),
    });
    await page.goto('/login');
    await page.getByLabel('Email', { exact: true }).fill('ada@example.com');
    await page.getByLabel('Password', { exact: true }).fill('wrong-password');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByRole('alert')).toContainText('The email or password is incorrect.');
  });

  test('login lands a participant in their portal', async ({ page }) => {
    await mockApi(page, { 'POST /auth/login': (route) => route.fulfill(authResponse(participant)) });
    await page.goto('/login');
    await page.getByLabel('Email', { exact: true }).fill('ada@example.com');
    await page.getByLabel('Password', { exact: true }).fill('Correct-Horse-42');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/app$/);
    await expect(page.getByRole('heading', { level: 1, name: /Ada/ })).toBeVisible();
  });
});

test.describe('portal shell', () => {
  test('mobile navigation drawer opens and closes', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 780 });
    await mockApi(page, { 'POST /auth/refresh': (route) => route.fulfill(authResponse(participant)) });
    await page.goto('/app');
    await expect(page.getByRole('navigation', { name: 'Quick navigation' })).toBeVisible();

    await page.getByRole('button', { name: 'Open navigation' }).click();
    const drawer = page.getByRole('dialog', { name: 'Participant menu' });
    await expect(drawer).toBeVisible();
    await expect(drawer.getByRole('link', { name: 'Social accounts' })).toBeVisible();
    await drawer.getByRole('link', { name: 'Social accounts' }).click();
    await expect(drawer).toBeHidden();
    await expect(page).toHaveURL(/\/app\/social-accounts$/);
    expect(await hasHorizontalScroll(page)).toBe(false);
  });

  test('unauthenticated visitors are sent to sign in', async ({ page }) => {
    await mockApi(page);
    await page.goto('/finance/ledger');
    await expect(page).toHaveURL(/\/login\?next=%2Ffinance%2Fledger/);
  });
});
