import { expect, test } from '@playwright/test';
import { accounts, landing, raw } from './support/auth';

/**
 * Open redirects: the sign-in page's `next` (and the Google `returnTo`) only ever lead to a same-origin path. Every
 * off-site or script target lands on the user's portal instead; a same-origin path the user may not open lands on the
 * portal too, never on a 403 page.
 */
const hostile = [
  'https://evil.example/phish',
  '//evil.example/phish',
  '/\\evil.example/phish',
  '\\\\evil.example',
  '%2F%2Fevil.example',
  'javascript:alert(document.domain)',
  ' https://evil.example',
  '/\t/evil.example',
  'data:text/html,<script>alert(1)</script>',
  'http:evil.example',
];

test('hostile `next` values never leave the app', async ({ browser }) => {
  let navigatedAway: string | null = null;
  const context = await browser.newContext();
  context.on('request', (r) => {
    if (r.isNavigationRequest() && !new URL(r.url()).host.startsWith('localhost')) navigatedAway = r.url();
  });
  const page = await context.newPage();
  page.on('dialog', (d) => {
    navigatedAway = `dialog: ${d.message()}`;
    void d.dismiss();
  });
  for (const [i, next] of hostile.entries()) {
    await page.goto(`/login?next=${encodeURIComponent(next)}`);
    if (i === 0) {
      await page.getByLabel('Email', { exact: true }).fill(accounts.participant.email);
      await page.getByLabel('Password', { exact: true }).fill(accounts.participant.password);
      await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    }
    // Signed in: the sign-in page forwards straight to a safe place.
    await expect(page, `next=${next}`).toHaveURL(landing.participant);
    expect(new URL(page.url()).origin).toBe(new URL(test.info().project.use.baseURL!).origin);
    expect(navigatedAway, `next=${next}`).toBeNull();
  }
  // An encoded double slash is just an (unknown) path of this app: it stays on the app's origin.
  await page.goto(`/login?next=${encodeURIComponent('/%2F%2Fevil.example')}`);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  expect(new URL(page.url()).origin).toBe(new URL(test.info().project.use.baseURL!).origin);
  expect(navigatedAway).toBeNull();
  await context.close();
});

test('the sign-in form itself honours only same-origin paths', async ({ page }) => {
  await page.goto(`/login?next=${encodeURIComponent('//evil.example/steal')}`);
  await page.getByLabel('Email', { exact: true }).fill(accounts.am.email);
  await page.getByLabel('Password', { exact: true }).fill(accounts.am.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(landing.agency);

  // A legitimate deep link with a query and a hash survives.
  await page.context().clearCookies();
  await page.goto('/login?next=' + encodeURIComponent('/agency/crm/deals?view=board#top'));
  await page.getByLabel('Email', { exact: true }).fill(accounts.am.email);
  await page.getByLabel('Password', { exact: true }).fill(accounts.am.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/agency\/crm\/deals\?view=board#top$/);

  // A same-origin page of a portal the user cannot open lands on their own portal, not on a 403 page.
  await page.context().clearCookies();
  await page.goto('/login?next=' + encodeURIComponent('/finance/ledger'));
  await page.getByLabel('Email', { exact: true }).fill(accounts.nimbusApprover.email);
  await page.getByLabel('Password', { exact: true }).fill(accounts.nimbusApprover.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(landing.client);
});

test('Google sign-in is off: its endpoints answer 404 whatever returnTo says', async () => {
  const start = await raw('POST', '/auth/google/start', {
    body: { returnTo: 'https://evil.example' },
    headers: { 'X-Requested-With': 'fetch' },
  });
  expect(start.status).toBe(404);
  const callback = await raw('POST', '/auth/google/callback', {
    body: { code: 'x', state: 'y' },
    headers: { 'X-Requested-With': 'fetch' },
  });
  expect(callback.status).toBe(404);
});
