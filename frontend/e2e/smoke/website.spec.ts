import { expect, type Page, type Route, test } from '@playwright/test';
import { hasHorizontalScroll, mockApi } from '../support/mockApi';

/**
 * Agency website smoke tests against `vite preview` with every /api call mocked (see docs/WEBSITE.md). Fixture shapes
 * follow docs/api/website.md; the component-level cases live in src/features/public/website.test.tsx.
 */

const ok = (body: unknown) => (route: Route) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

const seoPackage = {
  id: 'p1',
  name: 'Growth',
  description: 'For growing brands.',
  price: 1200,
  currency: 'USD',
  billingPeriod: 'Monthly',
  setupFee: null,
  features: ['Technical audit', 'Four articles a month'],
  isMostPopular: true,
  isCustomQuote: false,
};
const seoCard = { id: 's1', slug: 'seo', name: 'Search engine optimization', tagline: 'Rank for what buyers search.', icon: 'search', categorySlug: 'search', categoryName: 'Search', startingPrice: { amount: 1200, currency: 'USD', billingPeriod: 'Monthly' } };
const groups = [{ slug: 'search', name: 'Search', description: 'Get found by buyers.', icon: 'search', services: [seoCard] }];
const seo = (title: string) => ({ title, description: `${title} description`, ogImageUrl: null, canonicalUrl: null, noIndex: false });

function site(analytics: Record<string, string | null> = { ga4MeasurementId: null, gtmContainerId: null, metaPixelId: null }) {
  return {
    siteName: 'Optimize All',
    tagline: 'Full-service digital marketing agency',
    header: {
      menu: [
        { label: 'Services', url: '/services', description: null, children: [] },
        { label: 'Industries', url: '/industries', description: null, children: null },
        { label: 'Case studies', url: '/case-studies', description: null, children: null },
        { label: 'Pricing', url: '/pricing', description: null, children: null },
        { label: 'Blog', url: '/blog', description: null, children: null },
      ],
      cta: { label: 'Get a free audit', url: '/free-audit' },
    },
    footer: { blurb: 'One accountable team for every channel.', columns: [{ title: 'Company', links: [{ label: 'About', url: '/about' }, { label: 'Careers', url: '/careers' }] }], legalLinks: [{ label: 'Privacy policy', url: '/privacy-policy' }] },
    contact: { email: 'hello@optimizeall.com', phone: '+1 415 555 0100', whatsApp: '+14155550100', address: null, hours: null },
    social: [{ platform: 'LinkedIn', url: 'https://www.linkedin.com/company/optimizeall' }],
    trustLogos: [],
    announcement: { enabled: true, text: 'Free marketing audits are open for Q4.', linkLabel: 'Request yours', linkUrl: '/free-audit' },
    seo: { siteUrl: 'https://www.optimizeall.com', titleTemplate: '%s | Optimize All', defaultTitle: 'Optimize All', defaultDescription: 'Digital marketing that proves its results.', defaultOgImageUrl: null, twitterHandle: null },
    analytics,
    serviceMenu: [{ slug: 'search', name: 'Search', description: null, icon: 'search', services: [{ slug: 'seo', name: 'Search engine optimization', tagline: 'Rank.', icon: null }] }],
    consent: {
      formVersion: 'forms-2026-09',
      formText: 'I agree that Optimize All may use my details to respond to my request.',
      newsletterVersion: 'newsletter-2026-09',
      newsletterText: 'Send me the newsletter. I can unsubscribe at any time.',
      careersVersion: 'careers-2026-09',
      careersText: 'I agree that Optimize All may process my application.',
    },
    bookingEnabled: true,
  };
}

const home = {
  serviceCategories: groups,
  featuredCaseStudies: [
    { slug: 'northwind-seo', title: 'Tripling organic leads for Northwind', clientName: 'Northwind Dental', summary: 'Local SEO for a 12-clinic group.', industrySlug: null, industryName: null, serviceSlugs: ['seo'], serviceNames: ['Search engine optimization'], coverImageUrl: null, highlights: [{ label: 'Organic leads', value: '+212%', measurement: 'Measured', context: null }], isFeatured: true },
  ],
  testimonials: [{ id: 't1', quote: 'They doubled our pipeline in two quarters.', authorName: 'Priya Shah', authorRole: 'CMO', company: 'Northwind', rating: 5, avatarUrl: null, serviceSlug: 'seo' }],
  industries: [{ slug: 'healthcare', name: 'Healthcare', summary: 'Compliant growth for clinics.', icon: null }],
  latestPosts: [],
  pricingTeaser: [{ serviceSlug: 'seo', serviceName: 'Search engine optimization', package: seoPackage }],
  stats: [{ label: 'Client revenue influenced', value: '$48M', measurement: 'Estimated', context: 'last 12 months' }],
  trustLogos: [],
  seo: seo('Optimize All'),
  jsonLd: [{ '@context': 'https://schema.org', '@type': 'Organization', name: 'Optimize All' }],
};

const service = {
  ...seoCard,
  heroTitle: null,
  heroBody: 'Technical SEO, content and links that compound.',
  overviewMarkdown: '## Why SEO\n\nOrganic search is your **cheapest** channel over time.',
  problemsSolved: ['Traffic has plateaued'],
  deliverables: ['Technical audit'],
  processSteps: [{ title: 'Audit', description: 'Crawl and benchmark.' }],
  tools: ['Search Console'],
  kpis: ['Organic sessions'],
  faqs: [{ question: 'How long does SEO take?', answer: 'Most clients see movement in 3–6 months.' }],
  heroImageUrl: null,
  ctaLabel: null,
  ctaUrl: null,
  packages: [seoPackage],
  relatedServices: [],
  caseStudies: [],
  testimonials: [],
  seo: seo('SEO services'),
  jsonLd: [{ '@context': 'https://schema.org', '@type': 'Service', name: 'Search engine optimization' }],
};

const formToken = {
  token: 'form-token-1',
  minFillSeconds: 0,
  budgetRanges: [{ value: '2k-5k', label: '$2,000–$5,000 / month' }],
  timelines: [{ value: 'asap', label: 'As soon as possible' }],
};

function inDays(days: number, hourUtc: number) {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + days);
  d.setUTCHours(hourUtc, 0, 0, 0);
  return d.toISOString().replace('.000Z', 'Z');
}

async function mockWebsite(page: Page, extra: Parameters<typeof mockApi>[1] = {}, siteBody = site()) {
  return mockApi(page, {
    'GET /public/site': ok(siteBody),
    'GET /public/home': ok(home),
    'GET /public/services': ok(groups),
    'GET /public/services/seo': ok(service),
    'GET /public/pricing': ok({ services: [{ service: seoCard, packages: [seoPackage] }] }),
    'GET /public/industries': ok(home.industries),
    'GET /public/case-studies': ok(home.featuredCaseStudies),
    'GET /public/blog': ok({ items: [], total: 0, page: 1, pageSize: 9, categories: [], tags: [] }),
    'GET /public/careers': ok([]),
    'GET /public/forms/token': ok(formToken),
    'GET /public/consultations/slots': ok({ timeZone: 'UTC', slotMinutes: 30, enabled: true, slots: [inDays(3, 15), inDays(3, 16), inDays(4, 17)] }),
    ...extra,
  });
}

test.describe('agency website', () => {
  test('home renders from the API and the services menu leads to a service with JSON-LD', async ({ page, isMobile }) => {
    test.skip(isMobile, 'desktop navigation');
    await mockWebsite(page);
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: /Learn AI, marketing and growth/ })).toBeVisible();
    await expect(page.getByText('Free marketing audits are open for Q4.')).toBeVisible();
    await expect(page.getByText('Tripling organic leads for Northwind')).toBeVisible();
    await expect(page.getByText('$48M')).toBeVisible();

    const nav = page.getByRole('navigation', { name: 'Main' });
    await nav.getByRole('button', { name: 'Services' }).click();
    await nav.getByRole('link', { name: 'Search engine optimization' }).click();
    await expect(page).toHaveURL(/\/services\/seo$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Search engine optimization' })).toBeVisible();
    await expect(page).toHaveTitle('SEO services | Optimize All');
    const ld = await page.locator('script[type="application/ld+json"]').first().textContent();
    expect(JSON.parse(ld ?? '{}')).toMatchObject({ '@type': 'Service' });
  });

  test('quote wizard validates each step and submits', async ({ page }) => {
    const calls = await mockWebsite(page, {
      'POST /public/inquiries/quote': (route) => route.fulfill({ status: 202, contentType: 'application/json', body: JSON.stringify({ reference: 'Q-1001', message: 'Thanks' }) }),
    });
    await page.goto('/get-a-quote?service=seo');
    await expect(page.getByRole('heading', { name: 'Step 1 of 3: Services' })).toBeVisible();
    await expect(page.getByRole('checkbox', { name: 'Search engine optimization', exact: true })).toBeChecked();
    await page.getByRole('button', { name: 'Next' }).click();

    await page.getByRole('button', { name: 'Next' }).click();
    await expect(page.getByText('Pick a budget range.')).toBeVisible();
    await page.getByLabel('Monthly budget').selectOption('2k-5k');
    await page.getByRole('radio', { name: 'As soon as possible' }).check();
    await page.getByLabel('Project details').fill('We need a steady flow of local leads.');
    await page.getByRole('button', { name: 'Next' }).click();

    await page.getByLabel('Full name').fill('Ada Lovelace');
    await page.getByLabel('Work email').fill('ada@example.com');
    await page.getByRole('checkbox', { name: /may use my details/ }).check();
    await page.getByRole('button', { name: 'Request my quote' }).click();
    await expect(page.getByText('Quote request received')).toBeVisible();
    expect(calls.find((c) => c.path === '/public/inquiries/quote')?.body).toMatchObject({ serviceSlugs: ['seo'], budgetRange: '2k-5k', timeline: 'asap', consent: true, nickname: '' });
  });

  test('booking a consultation slot', async ({ page }) => {
    const calls = await mockWebsite(page, {
      'POST /public/consultations': (route) =>
        route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ reference: 'B-2001', slotStart: inDays(4, 17), message: 'Booked' }) }),
    });
    await page.goto('/book-a-consultation');
    const days = page.getByRole('group', { name: 'Day' }).getByRole('button');
    await expect(days).toHaveCount(2);
    await days.nth(1).click();
    const times = page.getByRole('group', { name: /^Times on/ }).getByRole('button');
    await expect(times).toHaveCount(1);
    await times.first().click();
    await expect(times.first()).toHaveAttribute('aria-pressed', 'true');
    await page.getByLabel('Full name').fill('Ada Lovelace');
    await page.getByLabel('Work email').fill('ada@example.com');
    await page.getByRole('checkbox', { name: /may use my details/ }).check();
    await page.getByRole('button', { name: /^Book .+ at / }).click();
    await expect(page.getByText('See you soon')).toBeVisible();
    expect(calls.find((c) => c.method === 'POST' && c.path === '/public/consultations')?.body).toMatchObject({ slotStart: inDays(4, 17) });
  });

  test('analytics tags load only after consent', async ({ page }) => {
    const tagRequests: string[] = [];
    await page.route(/googletagmanager\.com|connect\.facebook\.net/, (route) => {
      tagRequests.push(route.request().url());
      return route.fulfill({ status: 200, contentType: 'text/javascript', body: '' });
    });
    await mockWebsite(page, {}, site({ ga4MeasurementId: 'G-TEST12345', gtmContainerId: null, metaPixelId: null }));
    await page.goto('/');
    const banner = page.getByRole('region', { name: 'Your privacy choices' });
    await expect(banner).toBeVisible();
    await page.waitForLoadState('networkidle');
    expect(tagRequests).toEqual([]);
    expect(await page.locator('script[src*="googletagmanager"]').count()).toBe(0);

    await banner.getByRole('button', { name: 'Accept all' }).click();
    await expect(banner).toBeHidden();
    await expect.poll(() => tagRequests.length).toBeGreaterThan(0);
    expect(tagRequests[0]).toContain('gtag/js?id=G-TEST12345');
  });

  test('no horizontal scroll at 360px and the mobile menu works', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 780 });
    await mockWebsite(page);
    for (const path of ['/', '/services', '/services/seo', '/pricing', '/industries', '/case-studies', '/blog', '/careers', '/contact', '/free-audit', '/get-a-quote', '/book-a-consultation']) {
      await page.goto(path);
      await expect(page.locator('h1').first()).toBeVisible();
      expect(await hasHorizontalScroll(page), `horizontal scroll on ${path}`).toBe(false);
    }
    await page.goto('/');
    await page.getByRole('button', { name: 'Open menu' }).click();
    const drawer = page.getByRole('navigation', { name: 'Mobile' });
    await expect(drawer).toBeVisible();
    await drawer.getByRole('link', { name: 'Pricing' }).click();
    await expect(page).toHaveURL(/\/pricing$/);
    expect(await hasHorizontalScroll(page)).toBe(false);
  });
});
