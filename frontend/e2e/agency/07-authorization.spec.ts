import { type Page, expect, test } from '@playwright/test';
import { ApiSession, accounts, actor, landing, watchErrors } from './support/agency';

/**
 * Authorization across portals: deep links answer the 403 page (not a blank screen or a redirect loop), the portal
 * switcher never offers a portal the user cannot open, and the API refuses the same requests.
 */
const FORBIDDEN = 'You don’t have access to this page';

async function expectForbidden(page: Page, path: string) {
  await page.goto(path);
  await expect(page.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
  await expect(page.getByText('403')).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Agency navigation' })).toHaveCount(0);
}

const status = (p: Promise<unknown>) =>
  p.then(
    () => 200,
    (e: { status?: number }) => e.status ?? 0,
  );

test('a client user cannot open the agency portal', async ({ browser }) => {
  const client = await actor(browser, accounts.nimbusApprover, landing.client);
  const errors = watchErrors(client);
  for (const path of ['/agency', '/agency/crm/deals', '/agency/billing/invoices', '/agency/clients']) {
    await expectForbidden(client, path);
  }
  // The client portal still works and offers no way into the agency portal.
  await client.goto('/client');
  await expect(client.getByRole('heading', { level: 1, name: 'Welcome back' })).toBeVisible();
  const switcher = client.getByRole('button', { name: /Switch portal/ });
  if (await switcher.count()) {
    await switcher.click();
    await expect(client.getByRole('menuitem', { name: /Agency/ })).toHaveCount(0);
    await client.keyboard.press('Escape');
  }
  errors.expectClean('the client portal');

  const api = await ApiSession.login(accounts.nimbusApprover.email, accounts.nimbusApprover.password);
  expect(await status(api.get('/agency/crm/deals'))).toBe(403);
  expect(await status(api.get('/agency/clients'))).toBe(403);
});

test('a participant cannot see the agency or client portals', async ({ browser }) => {
  const participant = await actor(browser, accounts.participant, landing.participant);
  const errors = watchErrors(participant);
  await expect(participant.getByRole('navigation', { name: 'Agency navigation' })).toHaveCount(0);
  await expectForbidden(participant, '/agency');
  await expectForbidden(participant, '/agency/proposals');
  await expectForbidden(participant, '/client');
  errors.expectClean('the participant 403 pages');

  const api = await ApiSession.login(accounts.participant.email, accounts.participant.password);
  expect(await status(api.get('/agency/crm/deals'))).toBe(403);
  expect(await status(api.get('/client/orgs'))).toBe(403);
});

test('agency staff only reach the areas their role allows', async ({ browser }) => {
  // Account managers have no forms.manage: landing pages are hidden and the deep link answers 403.
  const am = await actor(browser, accounts.am, landing.agency);
  await expect(
    am.getByRole('navigation', { name: 'Agency navigation' }).getByRole('link', { name: 'Landing pages' }),
  ).toHaveCount(0);
  await expectForbiddenInPortal(am, '/agency/pages');
  // Designers have no CRM.
  const designer = await actor(browser, accounts.designer, landing.agency);
  await expect(
    designer.getByRole('navigation', { name: 'Agency navigation' }).getByRole('link', { name: 'Sales CRM' }),
  ).toHaveCount(0);
  await expectForbiddenInPortal(designer, '/agency/crm/deals');
});

test('signed-out visitors are sent to sign in', async ({ page }) => {
  await page.goto('/agency/crm/deals');
  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
});

/** Inside a portal the user may open, a forbidden route renders the 403 page within the portal chrome. */
async function expectForbiddenInPortal(page: Page, path: string) {
  await page.goto(path);
  await expect(page.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Agency navigation' })).toBeVisible();
}
