import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { PublicLandingPage } from './api';
import { EmbeddedFormPage, PublicLandingPageView } from './publicRoutes';

const page: PublicLandingPage = {
  pageId: 'p1',
  clientName: 'Wanderly Travel',
  clientSlug: 'wanderly-travel',
  slug: 'autumn-city-breaks',
  title: 'Autumn city breaks · Wanderly',
  metaDescription: 'Three-night city breaks with flights and hotel.',
  ogImageUrl: null,
  noIndex: true,
  versionId: 'v1',
  version: 3,
  variantKey: 'B',
  experimentId: 'e1',
  blocks: [
    { id: 'hero', type: 'hero', props: { headline: 'Autumn city breaks from £299', subheadline: 'Flights and hotel included.', align: 'center', theme: 'brand', ctaLabel: 'Get the brochure', ctaHref: '#form' } },
    { id: 'faq', type: 'faq', props: { heading: 'Questions', items: [{ question: 'Is it ATOL protected?', answer: 'Yes, every package.' }] } },
    { id: 'bad', type: 'cta', props: { heading: 'Book now', buttonLabel: 'Book', buttonHref: 'javascript:alert(1)', style: 'primary' } },
  ],
  forms: [],
};

afterEach(() => {
  window.localStorage.clear();
  document.head.querySelectorAll('meta[name="robots"],meta[name="description"]').forEach((m) => m.remove());
});

describe('public landing page (/lp/:client/:slug)', () => {
  it('renders the published blocks as semantic HTML, sends a sticky visitor id and honours noindex', async () => {
    const { calls } = mockFetch({ 'GET /public/lp/wanderly-travel/autumn-city-breaks': () => json(200, page) });
    const { container } = renderWithApp(<PublicLandingPageView />, { withAuth: false, route: '/lp/wanderly-travel/autumn-city-breaks', path: '/lp/:client/:slug' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Autumn city breaks from £299' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Get the brochure' })).toHaveAttribute('href', '#form');
    // Unsafe hrefs are never rendered as links.
    expect(screen.queryByRole('link', { name: 'Book' })).not.toBeInTheDocument();
    expect(document.title).toBe('Autumn city breaks · Wanderly');
    expect(document.head.querySelector('meta[name="robots"]')).toHaveAttribute('content', 'noindex, nofollow');
    const request = calls.find((c) => c.path.startsWith('/public/lp/'));
    expect(request?.headers['X-Visitor-Id']).toBeTruthy();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('shows a friendly message when the page is not published', async () => {
    mockFetch({ 'GET /public/lp/wanderly-travel/gone': () => problem(404, 'not_found', 'Not found') });
    renderWithApp(<PublicLandingPageView />, { withAuth: false, route: '/lp/wanderly-travel/gone', path: '/lp/:client/:slug' });
    expect(await screen.findByRole('heading', { name: 'This page isn’t available' })).toBeInTheDocument();
  });
});

describe('embedded form (/f/:formId)', () => {
  it('explains when the form may not be embedded on this site', async () => {
    mockFetch({ 'GET /public/forms/f1': () => problem(403, 'forms.origin_not_allowed', 'Not allowed') });
    renderWithApp(<EmbeddedFormPage />, { withAuth: false, route: '/f/f1', path: '/f/:formId' });
    expect(await screen.findByText('This form isn’t available here.')).toBeInTheDocument();
  });
});
