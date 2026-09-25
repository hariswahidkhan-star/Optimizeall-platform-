import { expect, test, type Page } from '@playwright/test';
import { watchErrors } from '../agency/support/agency';

/**
 * Partners on the public site (Baseline + Demo seed): the home "Official marketing partner of" strip, the footer line,
 * /partners and each profile page (h1, sections, JSON-LD, head tags), a sponsored ad unit on a blog post, and — on every
 * surface — outbound partner links with rel="sponsored noopener", a new tab and the click counter that adds UTM tags.
 */

async function expectSponsoredOutbound(page: Page) {
  const outbound = page.locator('a[href*="/api/v1/public/partners/"][href*="/visit"], a[href*="pciai.org"], a[href*="certuvo.com"]');
  const count = await outbound.count();
  expect(count, 'at least one outbound partner link').toBeGreaterThan(0);
  for (let i = 0; i < count; i++) {
    await expect(outbound.nth(i)).toHaveAttribute('rel', 'sponsored noopener');
    await expect(outbound.nth(i)).toHaveAttribute('target', '_blank');
  }
}

test('home strip and footer state the partnership and link to the profiles', async ({ page }) => {
  const errors = watchErrors(page);
  await page.goto('/');
  const strip = page.getByRole('region', { name: 'Official marketing partner of' });
  await expect(strip.getByRole('img', { name: 'PCI AI logo' })).toBeVisible();
  await expect(strip.getByRole('img', { name: 'Certuvo logo' })).toBeVisible();
  await expect(strip).toContainText('Optimize All is the official marketing partner of PCI AI and Certuvo.');
  const footer = page.locator('footer .site-footer__partners');
  await expect(footer).toHaveText('Optimize All is the official marketing partner of PCI AI and Certuvo.');
  await footer.getByRole('link', { name: 'Certuvo' }).click();
  await expect(page).toHaveURL(/\/partners\/certuvo$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Certuvo' })).toBeVisible();
  errors.expectClean('home and footer');
});

test('/partners and the profile pages are indexable, content-rich and mark every partner link as sponsored', async ({ page }) => {
  const errors = watchErrors(page);
  await page.goto('/partners');
  await expect(page.getByRole('heading', { level: 1, name: 'Our partners' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2, name: 'PCI AI' })).toBeVisible();
  await expect(page.getByRole('note')).toContainText('marked as sponsored');
  await expectSponsoredOutbound(page);
  await expect(page.locator('script[type="application/ld+json"]').first()).toBeAttached();
  expect(await page.locator('script[type="application/ld+json"]').allTextContents()).toEqual(
    expect.arrayContaining([expect.stringContaining('"CollectionPage"')]),
  );

  await page.getByRole('link', { name: 'About PCI AI' }).click();
  await expect(page).toHaveURL(/\/partners\/pci-ai$/);
  await expect(page.getByRole('heading', { level: 1, name: 'PCI AI' })).toBeVisible();
  await expect(page).toHaveTitle(/PCI AI — PCL-AI, PFL-AI and PML-AI certifications/);
  await expect(page.locator('#pcl-ai')).toContainText('USD 350 exam fee');
  await expect(page.locator('#pml-ai')).toContainText('PCI Project Management Leader – AI');
  await expect(page.getByRole('heading', { name: 'Related partners' })).toBeVisible();
  await expect(page.locator('meta[property="og:image"]')).toHaveAttribute('content', /\/partners\/pci-ai\.png$/);
  await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', /\/partners\/pci-ai$/);
  const ld = (await page.locator('script[type="application/ld+json"]').allTextContents()).join('\n');
  expect(ld).toContain('"Organization"');
  expect(ld).toContain('https://pciai.org');
  expect(ld).not.toMatch(/"(sponsor|funder|member|memberOf)"/);
  await expectSponsoredOutbound(page);

  // The click counter redirects to the partner with UTM tags (not followed: the partner site is external).
  const visit = await page.getByRole('link', { name: /Visit pciai\.org/ }).first().getAttribute('href');
  expect(visit).toMatch(/^\/api\/v1\/public\/partners\/pci-ai\/visit\?slot=partners\.profile&path=%2Fpartners%2Fpci-ai$/);
  const res = await page.request.get(visit!, { maxRedirects: 0 });
  expect(res.status()).toBe(302);
  expect(res.headers()['location']).toBe(
    'https://pciai.org/?utm_source=optimizeall&utm_medium=partner&utm_campaign=partners.profile',
  );

  // Certuvo links back to the PCI AI certification sections.
  await page.goto('/partners/certuvo');
  await page.locator('#pcl-ai').getByRole('link', { name: 'PCL-AI' }).click();
  await expect(page).toHaveURL(/\/partners\/pci-ai#pcl-ai$/);
  errors.expectClean('partner pages');
});

test('a blog post shows one sponsored unit at its end and the sitemap lists the partner pages', async ({ page }) => {
  const errors = watchErrors(page);
  const blog = await (await page.request.get('/api/v1/public/blog')).json();
  const slug: string = blog.items[0].slug;
  await page.goto(`/blog/${slug}`);
  const unit = page.locator('[data-partner-slot="blog.end"]');
  await expect(unit).toBeVisible();
  await expect(unit.getByText('Sponsored', { exact: true })).toBeVisible();
  await expect(unit).toContainText('Optimize All is the official marketing partner of');
  await expectSponsoredOutbound(page);

  const sitemap = await (await page.request.get('/api/v1/public/sitemap.xml')).text();
  expect(sitemap).toMatch(/\/partners<\/loc>/);
  expect(sitemap).toMatch(/\/partners\/pci-ai<\/loc>/);
  expect(sitemap).toMatch(/\/partners\/certuvo<\/loc>/);
  errors.expectClean('blog post');
});
