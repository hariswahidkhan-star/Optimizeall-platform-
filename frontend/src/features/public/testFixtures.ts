import type { FormToken, Home, Pricing, PublicService, PublicSite, ServiceCategoryGroup, Slots } from './site/api';

/** API fixtures for the public website tests (shapes match docs/api/website.md). */

export const site: PublicSite = {
  siteName: 'Optimize All',
  tagline: 'Full-service digital marketing agency',
  header: {
    menu: [
      { label: 'Services', url: '/services', description: null, children: [] },
      { label: 'Industries', url: '/industries', description: null, children: null },
      { label: 'Pricing', url: '/pricing', description: null, children: null },
    ],
    cta: { label: 'Get a free audit', url: '/free-audit' },
  },
  footer: {
    blurb: 'One accountable team for every channel.',
    columns: [{ title: 'Company', links: [{ label: 'About', url: '/about' }] }],
    legalLinks: [{ label: 'Privacy policy', url: '/privacy-policy' }],
  },
  contact: { email: 'hello@optimizeall.com', phone: null, whatsApp: null, address: null, hours: null },
  social: [],
  trustLogos: [],
  announcement: { enabled: false, text: null, linkLabel: null, linkUrl: null },
  seo: {
    siteUrl: 'https://www.optimizeall.com',
    titleTemplate: '%s | Optimize All',
    defaultTitle: 'Optimize All — digital marketing agency',
    defaultDescription: 'Digital marketing that proves its results.',
    defaultOgImageUrl: null,
    twitterHandle: null,
  },
  analytics: { ga4MeasurementId: null, gtmContainerId: null, metaPixelId: null },
  serviceMenu: [
    {
      slug: 'search',
      name: 'Search',
      description: null,
      icon: 'search',
      services: [
        { slug: 'seo', name: 'Search engine optimization', tagline: 'Rank for what buyers search.', icon: null },
        { slug: 'local-seo', name: 'Local SEO', tagline: 'Win the map pack.', icon: null },
      ],
    },
    {
      slug: 'paid-media',
      name: 'Paid media',
      description: null,
      icon: 'megaphone',
      services: [{ slug: 'google-ads', name: 'Google Ads', tagline: 'Profitable search ads.', icon: null }],
    },
  ],
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

export const serviceGroups: ServiceCategoryGroup[] = [
  {
    slug: 'search',
    name: 'Search',
    description: 'Get found by buyers.',
    icon: 'search',
    services: [
      { id: 's1', slug: 'seo', name: 'Search engine optimization', tagline: 'Rank for what buyers search.', icon: null, categorySlug: 'search', categoryName: 'Search', startingPrice: { amount: 1200, currency: 'USD', billingPeriod: 'Monthly' } },
      { id: 's2', slug: 'local-seo', name: 'Local SEO', tagline: 'Win the map pack.', icon: null, categorySlug: 'search', categoryName: 'Search', startingPrice: null },
    ],
  },
];

const seoPackage = {
  id: 'p1',
  name: 'Growth',
  description: 'For growing brands.',
  price: 1200,
  currency: 'USD',
  billingPeriod: 'Monthly' as const,
  setupFee: null,
  features: ['Technical audit', 'Four articles a month'],
  isMostPopular: true,
  isCustomQuote: false,
};

export const pricing: Pricing = {
  services: [{ service: serviceGroups[0].services[0], packages: [seoPackage] }],
};

export const home: Home = {
  serviceCategories: serviceGroups,
  featuredCaseStudies: [
    {
      slug: 'northwind-seo',
      title: 'Tripling organic leads for Northwind',
      clientName: 'Northwind Dental',
      summary: 'Local SEO and content for a 12-clinic group.',
      industrySlug: 'healthcare',
      industryName: 'Healthcare',
      serviceSlugs: ['seo'],
      serviceNames: ['Search engine optimization'],
      coverImageUrl: null,
      highlights: [{ label: 'Organic leads', value: '+212%', measurement: 'Measured', context: 'in 9 months' }],
      isFeatured: true,
    },
  ],
  testimonials: [
    { id: 't1', quote: 'They doubled our pipeline in two quarters.', authorName: 'Priya Shah', authorRole: 'CMO', company: 'Northwind', rating: 5, avatarUrl: null, serviceSlug: 'seo' },
  ],
  industries: [{ slug: 'healthcare', name: 'Healthcare', summary: 'Compliant growth for clinics.', icon: null }],
  latestPosts: [],
  pricingTeaser: [{ serviceSlug: 'seo', serviceName: 'Search engine optimization', package: seoPackage }],
  stats: [
    { label: 'Client revenue influenced', value: '$48M', measurement: 'Estimated', context: 'last 12 months' },
    { label: 'Average client retention', value: '3.4 yrs', measurement: 'Measured', context: null },
  ],
  trustLogos: [],
  seo: { title: 'Optimize All', description: 'Digital marketing that proves its results.', ogImageUrl: null, canonicalUrl: null, noIndex: false },
  jsonLd: [{ '@context': 'https://schema.org', '@type': 'Organization', name: 'Optimize All' }],
};

export const service: PublicService = {
  id: 's1',
  slug: 'seo',
  name: 'Search engine optimization',
  tagline: 'Rank for what buyers search.',
  heroTitle: null,
  heroBody: 'Technical SEO, content and links that compound.',
  overviewMarkdown: '## Why SEO\n\nOrganic search is your **cheapest** channel over time.',
  problemsSolved: ['Traffic has plateaued'],
  deliverables: ['Technical audit'],
  processSteps: [{ title: 'Audit', description: 'Crawl and benchmark.' }],
  tools: ['Search Console'],
  kpis: ['Organic sessions'],
  faqs: [{ question: 'How long does SEO take?', answer: 'Most clients see movement in 3–6 months.' }],
  icon: 'search',
  heroImageUrl: null,
  ctaLabel: null,
  ctaUrl: null,
  categorySlug: 'search',
  categoryName: 'Search',
  packages: [seoPackage],
  relatedServices: [],
  caseStudies: [],
  testimonials: [],
  seo: { title: 'SEO services', description: 'Technical SEO, content and links.', ogImageUrl: null, canonicalUrl: null, noIndex: false },
  jsonLd: [
    {
      '@context': 'https://schema.org',
      '@type': 'Service',
      name: 'Search engine optimization',
      provider: { '@type': 'Organization', name: 'Optimize All' },
      offers: [{ '@type': 'Offer', name: 'Growth', price: 1200, priceCurrency: 'USD' }],
    },
    {
      '@context': 'https://schema.org',
      '@type': 'FAQPage',
      mainEntity: [
        {
          '@type': 'Question',
          name: 'How long does SEO take?',
          // Hostile content must stay inert text inside the JSON-LD script.
          acceptedAnswer: { '@type': 'Answer', text: 'Soon</script><img src=x onerror=alert(1)>' },
        },
      ],
    },
  ],
};

export const formToken: FormToken = {
  token: 'form-token-1',
  minFillSeconds: 3,
  budgetRanges: [
    { value: 'under-2k', label: 'Under $2,000 / month' },
    { value: '2k-5k', label: '$2,000–$5,000 / month' },
  ],
  timelines: [
    { value: 'asap', label: 'As soon as possible' },
    { value: '1-3-months', label: 'In 1–3 months' },
  ],
};

export const slots: Slots = {
  timeZone: 'UTC',
  slotMinutes: 30,
  enabled: true,
  slots: ['2026-09-28T09:00:00Z', '2026-09-28T09:30:00Z', '2026-09-29T14:00:00Z'],
};
