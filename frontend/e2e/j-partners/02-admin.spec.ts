import { expect, test } from '@playwright/test';
import { accounts, landing, signIn, watchErrors } from '../agency/support/agency';

/**
 * Website → Partners as the demo admin: the list, editing a partner (confirming Certuvo's seeded launch offer, changing
 * the tagline) and seeing it on the public profile, switching a placement off, and the placement report.
 */
test('an editor confirms an offer and edits a partner, and the public site follows', async ({ page }) => {
  test.setTimeout(180_000);
  const errors = watchErrors(page);
  await signIn(page, accounts.admin, landing.admin);
  await page.goto('/agency/website/partners');
  await expect(page.getByRole('heading', { level: 1, name: 'Partners' })).toBeVisible();
  const row = page.getByRole('row', { name: /Certuvo/ });
  await expect(row).toContainText('certuvo.com');
  await expect(row.getByText('Needs review')).toBeVisible();

  await row.getByRole('button', { name: /Edit/ }).click();
  const drawer = page.getByRole('dialog');
  await expect(drawer.getByLabel(/^Name\b/)).toHaveValue('Certuvo');
  await expect(drawer.getByRole('img', { name: 'Certuvo logo preview' })).toBeVisible();
  await expect(drawer.getByLabel(/^Code\b/)).toHaveValue('LAUNCH50');
  await drawer.getByLabel(/^Tagline\b/).fill('Pass your next exam with total confidence — now with an AI Coach');
  await drawer.getByRole('switch', { name: 'I checked this offer is still valid' }).click();
  await drawer.getByRole('switch', { name: 'Case studies' }).click();
  await drawer.getByRole('button', { name: /^Save/ }).click();
  await expect(drawer).toBeHidden();
  await expect(page.getByRole('row', { name: /Certuvo/ }).getByText('Shown')).toBeVisible();

  await page.goto('/partners/certuvo');
  await expect(page.getByText('Pass your next exam with total confidence — now with an AI Coach')).toBeVisible();
  await expect(page.locator('.partner-offer-box')).toContainText('LAUNCH50');
  const offerLink = page.locator('.partner-offer-box a');
  await expect(offerLink).toHaveAttribute('rel', 'sponsored noopener');

  await page.goto('/agency/website/partners/report');
  await expect(page.getByRole('heading', { level: 1, name: 'Placement report' })).toBeVisible();
  await expect(page.getByRole('group', { name: 'Impressions' })).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export CSV' }).click();
  expect((await download).suggestedFilename()).toBe('partner-placements.csv');
  errors.expectClean('partner admin');
});
