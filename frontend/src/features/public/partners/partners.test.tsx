import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { interludeIndex, Markdown, parseMarkdown } from '../site/Markdown';
import * as fx from '../testFixtures';
import type { PartnerCard, PartnerDirectory, PartnerProfile } from './api';
import { StaticPartnerLinks } from './PartnerLinksContext';
import { applyPartnerLink, matchPartnerLink, type PartnerLinkRule, visitHref } from './partnerLinks';
import { PartnerProfilePage, PartnersPage } from './PartnerPages';
import { PartnerAd, PartnerSlot, SponsoredLink } from './PartnerSlot';
import { PARTNER_SLOTS } from './slots';
import { flushImpressions, pendingImpressions, recordImpression, resetImpressionTracking } from './tracking';

const rules: PartnerLinkRule[] = [
  { slug: 'pci-ai', host: 'pciai.org', utmSource: 'optimizeall', utmMedium: 'partner', utmCampaign: null },
  { slug: 'certuvo', host: 'certuvo.com', utmSource: 'optimizeall', utmMedium: 'partner', utmCampaign: 'spring' },
];

const pci: PartnerCard = {
  slug: 'pci-ai',
  name: 'PCI AI',
  logoUrl: '/partners/pci-ai.png',
  tagline: 'The credential for the people who control projects',
  relationshipLabel: 'Optimize All is the official marketing partner of PCI AI',
  brandColor: '#14285A',
  websiteHost: 'pciai.org',
  visitUrl: '/api/v1/public/partners/pci-ai/visit',
  profilePath: '/partners/pci-ai',
  slots: ['home.partners', 'footer.partners', 'blog.end'],
  offer: null,
};

const certuvo: PartnerCard = {
  ...pci,
  slug: 'certuvo',
  name: 'Certuvo',
  logoUrl: '/partners/certuvo.jpg',
  tagline: 'Pass your next exam with total confidence',
  relationshipLabel: 'Optimize All is the official marketing partner of Certuvo',
  websiteHost: null,
  visitUrl: null,
  profilePath: '/partners/certuvo',
  slots: ['footer.partners'],
};

const directory: PartnerDirectory = {
  partners: [pci, certuvo],
  linkRules: rules,
  seo: { title: 'Our partners', description: 'Optimize All is the official marketing partner of PCI AI and Certuvo.', ogImageUrl: null, canonicalUrl: null, noIndex: false },
  jsonLd: [{ '@context': 'https://schema.org', '@type': 'CollectionPage', name: 'Our partners' }],
};

beforeEach(() => resetImpressionTracking());
afterEach(() => vi.unstubAllGlobals());

describe('partner links (mirror of the backend PartnerLinkPolicy)', () => {
  it.each([
    ['https://pciai.org/certifications.html', 'pci-ai'],
    ['https://www.pciai.org/', 'pci-ai'],
    ['https://exams.certuvo.com/cpa', 'certuvo'],
    ['HTTPS://CERTUVO.COM', 'certuvo'],
    ['https://notcertuvo.com/', null],
    ['https://certuvo.com.evil.example/', null],
    ['/partners/certuvo', null],
    ['javascript:alert(1)', null],
  ])('%s → %s', (href, slug) => {
    expect(matchPartnerLink(href, rules)?.slug ?? null).toBe(slug);
  });

  it('adds rel="sponsored noopener", a new tab and UTM tags, keeping explicit ones', () => {
    expect(applyPartnerLink('https://pciai.org/certifications.html#pcl', rules)).toEqual({
      href: 'https://pciai.org/certifications.html?utm_source=optimizeall&utm_medium=partner&utm_campaign=editorial#pcl',
      rel: 'sponsored noopener',
      target: '_blank',
      partnerSlug: 'pci-ai',
    });
    expect(applyPartnerLink('https://certuvo.com/?utm_campaign=newsletter&x=1', rules)?.href).toBe(
      'https://certuvo.com/?utm_campaign=newsletter&x=1&utm_source=optimizeall&utm_medium=partner',
    );
    expect(applyPartnerLink('https://certuvo.com', rules)?.href).toBe(
      'https://certuvo.com/?utm_source=optimizeall&utm_medium=partner&utm_campaign=spring',
    );
    expect(applyPartnerLink('https://example.com', rules)).toBeNull();
  });

  it('builds the click-counter address only for our own visit endpoint', () => {
    expect(visitHref(pci.visitUrl, 'blog.end', '/blog/a b')).toBe('/api/v1/public/partners/pci-ai/visit?slot=blog.end&path=%2Fblog%2Fa+b');
    expect(visitHref(null, 'blog.end', '/')).toBeNull();
    expect(visitHref('https://evil.example/x', 'blog.end', '/')).toBeNull();
  });

  it('knows the backend slot list', () => {
    expect(PARTNER_SLOTS.map((s) => s.name)).toContain('learn.course');
  });
});

describe('Markdown in editorial content', () => {
  it('marks links to partner websites as sponsored and leaves other links alone', () => {
    renderWithApp(
      <StaticPartnerLinks rules={rules}>
        <Markdown source={'Read [PCI AI](https://pciai.org/certifications.html), **[Certuvo](https://www.certuvo.com)** and [MDN](https://developer.mozilla.org).'} />
      </StaticPartnerLinks>,
    );
    const partner = screen.getByRole('link', { name: /PCI AI/ });
    expect(partner).toHaveAttribute('rel', 'sponsored noopener');
    expect(partner).toHaveAttribute('target', '_blank');
    expect(partner.getAttribute('href')).toContain('utm_campaign=editorial');
    expect(screen.getByRole('link', { name: /Certuvo/ })).toHaveAttribute('rel', 'sponsored noopener');
    const other = screen.getByRole('link', { name: /MDN/ });
    expect(other).toHaveAttribute('rel', 'noopener noreferrer');
    expect(other.getAttribute('href')).toBe('https://developer.mozilla.org');
  });

  it('places the interlude before the second section of long texts only', () => {
    expect(interludeIndex(parseMarkdown('## A\n\ntext\n\n## B\n\nmore'))).toBe(2);
    expect(interludeIndex(parseMarkdown('Intro\n\nmore\n\n## Only\n\ntext'))).toBe(2);
    expect(interludeIndex(parseMarkdown('one\n\ntwo'))).toBe(-1);
    renderWithApp(<Markdown source={'## A\n\ntext\n\n## B\n\nmore'} interlude={<aside>unit</aside>} />);
    const prose = document.querySelector('.site-prose')!;
    expect([...prose.children].map((c) => c.tagName)).toEqual(['H2', 'P', 'ASIDE', 'H2', 'P']);
  });
});

describe('ad units', () => {
  it('are labelled Sponsored and link out with rel="sponsored noopener" through the click counter', () => {
    renderWithApp(<PartnerAd partner={pci} slot="blog.end" />, { route: '/blog/seo-tips', path: '/blog/:slug' });
    const unit = screen.getByRole('complementary', { name: 'Sponsored: PCI AI' });
    expect(within(unit).getByText('Sponsored')).toBeInTheDocument();
    expect(within(unit).getByText('Optimize All is the official marketing partner of PCI AI.')).toBeInTheDocument();
    const out = within(unit).getByRole('link', { name: /Visit pciai.org/ });
    expect(out).toHaveAttribute('rel', 'sponsored noopener');
    expect(out).toHaveAttribute('target', '_blank');
    expect(out).toHaveAttribute('href', '/api/v1/public/partners/pci-ai/visit?slot=blog.end&path=%2Fblog%2Fseo-tips');
    expect(within(unit).getByRole('link', { name: 'About PCI AI' })).toHaveAttribute('href', '/partners/pci-ai');
    // Counted once (no IntersectionObserver in jsdom → counted on mount).
    expect(pendingImpressions()).toEqual([{ partner: 'pci-ai', slot: 'blog.end', path: '/blog/seo-tips' }]);
  });

  it('hide every outbound link while the partner has no website', () => {
    renderWithApp(
      <>
        <PartnerAd partner={certuvo} slot="blog.end" />
        <SponsoredLink partner={certuvo} slot="partners.profile">
          Visit
        </SponsoredLink>
      </>,
    );
    expect(screen.queryByRole('link', { name: /Visit/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'About Certuvo' })).toBeInTheDocument();
  });

  it('PartnerSlot asks the API for the unit of its slot with the page keywords, and renders nothing without one', async () => {
    const { calls } = mockFetch({
      'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired'),
      'GET /public/partners/placement': (req) => json(200, { slot: 'blog.end', partner: req.path ? pci : null }),
    });
    renderWithApp(<PartnerSlot slot="blog.end" keywords={['earned value']} categories={['project-management']} />, { route: '/blog/ev' });
    expect(await screen.findByRole('complementary', { name: 'Sponsored: PCI AI' })).toBeInTheDocument();
    const url = String(vi.mocked(fetch).mock.calls.find((c) => String(c[0]).includes('/placement'))![0]);
    expect(url).toContain('slot=blog.end');
    expect(url).toContain('keywords=earned+value');
    expect(url).toContain('categories=project-management');
    expect(url).toContain('path=%2Fblog%2Fev');
    expect(calls.some((c) => c.path === '/public/partners/placement')).toBe(true);
  });

  it('renders nothing when no partner is placed', async () => {
    mockFetch({
      'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired'),
      'GET /public/partners/placement': () => json(200, { slot: 'learn.exam', partner: null }),
    });
    const { container } = renderWithApp(<PartnerSlot slot="learn.exam" />);
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalled());
    expect(container.querySelector('.partner-unit')).toBeNull();
  });
});

describe('list slots and partner pages', () => {
  const routes = {
    'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired'),
    'GET /public/site': () => json(200, fx.site),
    'GET /public/partners': () => json(200, directory),
  };

  it('the footer line states the partnership with internal profile links', async () => {
    mockFetch(routes);
    renderWithApp(<PartnerSlot slot="footer.partners" />);
    const line = await screen.findByText(/Optimize All is the official marketing partner of/);
    expect(line).toHaveTextContent('Optimize All is the official marketing partner of PCI AI and Certuvo.');
    expect(within(line).getByRole('link', { name: 'PCI AI' })).toHaveAttribute('href', '/partners/pci-ai');
  });

  it('the home strip lists only the partners enabled for it', async () => {
    mockFetch(routes);
    renderWithApp(<PartnerSlot slot="home.partners" />);
    const strip = await screen.findByRole('region', { name: 'Official marketing partner of' });
    expect(within(strip).getByRole('img', { name: 'PCI AI logo' })).toBeInTheDocument();
    expect(within(strip).queryByRole('img', { name: 'Certuvo logo' })).not.toBeInTheDocument();
  });

  it('/partners shows each partner with the disclosure and sponsored outbound links', async () => {
    mockFetch(routes);
    renderWithApp(<PartnersPage />, { route: '/partners', path: '/partners' });
    expect(await screen.findByRole('heading', { level: 2, name: 'PCI AI' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Our partners' })).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent('marked as sponsored');
    const out = screen.getByRole('link', { name: /Visit pciai.org/ });
    expect(out).toHaveAttribute('rel', 'sponsored noopener');
    expect(out.getAttribute('href')).toContain('slot=partners.directory');
    await waitFor(() => expect(document.head.querySelector('script[type="application/ld+json"]')?.textContent).toContain('CollectionPage'));
  });

  it('a profile page has the partner as h1, its offerings as sections and JSON-LD in the head', async () => {
    const profile: PartnerProfile = {
      ...pci,
      descriptionMarkdown: 'Exam preparation is available from [Certuvo](/partners/certuvo). See [pciai.org](https://pciai.org).',
      highlights: ['Fully online · scenario-based exam'],
      offerings: [
        { title: 'PCL-AI — PCI AI Project Controls Leader', summary: 'The integrated project-controls credential.', facts: ['USD 350 exam fee'], anchor: 'pcl-ai', link: null },
        { title: 'CPA', summary: null, facts: [], anchor: 'cpa', link: null },
      ],
      keywords: ['PCL-AI'],
      related: [certuvo],
      updatedAt: '2026-09-25T00:00:00Z',
      seo: { title: 'PCI AI — certifications', description: 'd', ogImageUrl: '/partners/pci-ai.png', canonicalUrl: 'http://app.test/partners/pci-ai', noIndex: false },
      jsonLd: [{ '@context': 'https://schema.org', '@type': 'Organization', name: 'PCI AI' }],
    };
    mockFetch({ ...routes, 'GET /public/partners/pci-ai': () => json(200, profile) });
    renderWithApp(
      <StaticPartnerLinks rules={rules}>
        <PartnerProfilePage />
      </StaticPartnerLinks>,
      { route: '/partners/pci-ai', path: '/partners/:slug' },
    );
    expect(await screen.findByRole('heading', { level: 1, name: 'PCI AI' })).toBeInTheDocument();
    expect(document.getElementById('pcl-ai')).toHaveTextContent('USD 350 exam fee');
    expect(document.getElementById('cpa')).toHaveTextContent('CPA');
    for (const link of screen.getAllByRole('link', { name: 'Certuvo' })) expect(link).toHaveAttribute('href', '/partners/certuvo');
    expect(screen.getAllByRole('link', { name: /pciai\.org/ }).length).toBeGreaterThan(1);
    for (const link of screen.getAllByRole('link').filter((a) => a.getAttribute('href')?.includes('pciai.org') || a.getAttribute('href')?.includes('/visit')))
      expect(link).toHaveAttribute('rel', 'sponsored noopener');
    await waitFor(() => expect(document.title).toContain('PCI AI — certifications'));
    expect(document.head.querySelector('meta[property="og:image"]')?.getAttribute('content')).toContain('/partners/pci-ai.png');
    expect(document.head.querySelector('script[type="application/ld+json"]')?.textContent).toContain('"Organization"');
  });
});

describe('impression tracking', () => {
  it('counts each partner, slot and page once and sends batches with keepalive', () => {
    const fetchMock = vi.fn(() => Promise.resolve(new Response(null, { status: 202 })));
    vi.stubGlobal('fetch', fetchMock);
    recordImpression({ partner: 'pci-ai', slot: 'blog.end', path: '/blog/a' });
    recordImpression({ partner: 'pci-ai', slot: 'blog.end', path: '/blog/a' });
    recordImpression({ partner: 'certuvo', slot: 'footer.partners', path: '/blog/a' });
    expect(pendingImpressions()).toHaveLength(2);
    flushImpressions();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/v1/public/partners/impressions');
    expect(init.keepalive).toBe(true);
    expect(JSON.parse(String(init.body)).items).toHaveLength(2);
    expect(pendingImpressions()).toHaveLength(0);
  });
});
