import { waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { site } from '../testFixtures';
import { applyTitleTemplate, INDEXABLE_ROBOTS, useDocumentHead, type DocumentHead } from './head';

function Head(props: DocumentHead) {
  useDocumentHead(props);
  return <h1>Page</h1>;
}

const all = (selector: string) => document.head.querySelectorAll(selector);
const content = (selector: string) => document.head.querySelector(selector)?.getAttribute('content') ?? null;

/** What a server-rendered document puts in the head (SeoDocumentWriter), marked data-oa-ssr. */
function serverHead(types: string[]) {
  document.head.insertAdjacentHTML(
    'beforeend',
    `<meta name="description" content="Server description" data-oa-head data-oa-ssr>
     <meta name="robots" content="${INDEXABLE_ROBOTS}" data-oa-head data-oa-ssr>
     <link rel="canonical" href="https://www.optimizeall.com/" data-oa-head data-oa-ssr>
     <meta property="og:title" content="Server title" data-oa-head data-oa-ssr>
     <meta property="og:image:width" content="1200" data-oa-head data-oa-ssr>
     <link rel="next" href="https://www.optimizeall.com/blog?page=2" data-oa-ssr>` +
      types.map((t) => `<script type="application/ld+json" data-oa-head data-oa-ssr>{"@context":"https://schema.org","@type":"${t}"}</script>`).join(''),
  );
}

function jsonLdTypes() {
  return [...all('script[type="application/ld+json"]')].map((s) => (JSON.parse(s.textContent!) as { '@type': string })['@type']);
}

afterEach(() => {
  document.head.innerHTML = '';
});

describe('head manager', () => {
  it('applies the title template unless the title already names the site', () => {
    expect(applyTitleTemplate('Pricing', '%s | Optimize All', 'Optimize All')).toBe('Pricing | Optimize All');
    expect(applyTitleTemplate('Get paid | Optimize All', '%s | Optimize All', 'Optimize All')).toBe('Get paid | Optimize All');
    // The suffix is dropped when it would push a title that fits on its own past 60 characters.
    const long = 'Tripling organic revenue for an outdoor retailer';
    expect(applyTitleTemplate(long, '%s | Optimize All', 'Optimize All')).toBe(long);
  });

  it('writes one set of tags with the default social image and indexable robots', async () => {
    mockFetch({ 'GET /public/site': () => json(200, site) });
    renderWithApp(<Head title="Pricing" description="Clear prices." />, { route: '/pricing', withAuth: false });
    await waitFor(() => expect(document.title).toBe('Pricing | Optimize All'));
    await waitFor(() => expect(content('meta[property="og:image"]')).toBe('https://www.optimizeall.com/og-default.png'));
    expect(content('meta[name="description"]')).toBe('Clear prices.');
    expect(content('meta[name="robots"]')).toBe(INDEXABLE_ROBOTS);
    expect(document.head.querySelector('link[rel="canonical"]')!.getAttribute('href')).toBe('https://www.optimizeall.com/pricing');
    expect(all('meta[name="description"]')).toHaveLength(1);
  });

  it('noindex pages can let crawlers follow their links', async () => {
    mockFetch({ 'GET /public/site': () => json(200, site) });
    renderWithApp(<Head title="Search" noIndex follow />, { route: '/search', withAuth: false });
    await waitFor(() => expect(content('meta[name="robots"]')).toBe('noindex, follow'));
  });

  it('on the page the server rendered, keeps its tags and JSON-LD without adding duplicates', async () => {
    // jsdom's window.location is "/", which is the page "the server rendered" for this test.
    serverHead(['Organization', 'WebSite']);
    mockFetch({ 'GET /public/site': () => json(200, site) });
    renderWithApp(<Head title={null} description="Client description" jsonLd={[{ '@context': 'https://schema.org', '@type': 'Organization' }]} />, {
      route: '/',
      withAuth: false,
    });
    await waitFor(() => expect(content('meta[name="description"]')).toBe('Client description'));
    expect(all('meta[name="description"]')).toHaveLength(1);
    expect(all('link[rel="canonical"]')).toHaveLength(1);
    expect(all('meta[name="robots"]')).toHaveLength(1);
    expect(jsonLdTypes()).toEqual(['Organization', 'WebSite']);
  });

  it('after client navigation, replaces the server-only tags and JSON-LD with the page’s own', async () => {
    serverHead(['Organization', 'WebSite']);
    mockFetch({ 'GET /public/site': () => json(200, site) });
    renderWithApp(<Head title="SEO" jsonLd={[{ '@context': 'https://schema.org', '@type': 'Service' }]} />, {
      route: '/services/seo',
      withAuth: false,
    });
    await waitFor(() => expect(jsonLdTypes()).toEqual(['Service']));
    expect(all('link[rel="next"]')).toHaveLength(0);
    expect(all('meta[property="og:image:width"]')).toHaveLength(0);
    expect(all('meta[property="og:title"]')).toHaveLength(1);
    await waitFor(() => expect(content('meta[property="og:title"]')).toBe('SEO | Optimize All'));
  });
});
