import { expect, test } from '@playwright/test';
import {
  accounts,
  axeViolations,
  expectNoHorizontalScroll,
  landing,
  signIn,
  watchErrors,
} from './support/agency';

/**
 * Phone layout (390×844, touch) of the public home page and the client portal home. Runs in the mobile-chromium
 * project only (see playwright.config.ts).
 */
test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

test('public home on a phone: no horizontal scroll, menu drawer, no console errors', async ({ page }) => {
  const errors = watchErrors(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden();
  await expectNoHorizontalScroll(page, 'the public home page');

  await page.getByRole('button', { name: 'Open menu' }).click();
  const drawer = page.getByRole('navigation', { name: 'Mobile' });
  await expect(drawer).toBeVisible();
  await drawer.getByRole('link', { name: 'Pricing' }).click();
  await expect(page).toHaveURL(/\/pricing$/);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expectNoHorizontalScroll(page, 'the pricing page');

  await page.goto('/');
  expect(await axeViolations(page), 'axe violations on the phone home page').toEqual([]);
  errors.expectClean('the public site on a phone');
});

test('client portal home on a phone: no horizontal scroll, navigation drawer', async ({ page }) => {
  await signIn(page, accounts.nimbusApprover, landing.client);
  const errors = watchErrors(page);
  await expect(page.getByRole('heading', { level: 1, name: 'Welcome back' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Awaiting your approval' })).toBeVisible();
  await expectNoHorizontalScroll(page, 'the client portal home');

  // The sidebar collapses into a drawer behind the top bar's menu button.
  await expect(page.getByRole('navigation', { name: 'Client portal navigation' })).toBeHidden();
  await page.getByRole('button', { name: 'Open navigation' }).click();
  const drawerNav = page.getByRole('dialog').getByRole('navigation', { name: 'Client portal navigation' });
  await expect(drawerNav).toBeVisible();
  await drawerNav.getByRole('link', { name: 'Approvals', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Approvals' })).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expectNoHorizontalScroll(page, 'client approvals');
  errors.expectClean('the client portal on a phone');
});
