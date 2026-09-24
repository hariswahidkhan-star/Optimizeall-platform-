import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PublicLandingPageView } from '@/features/agency/pages/publicRoutes';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { NotFound } from '../NotFound';
import { ServiceDetailPage, ServicesPage } from '../pages/ServicePages';
import * as fx from '../testFixtures';
import { isRedirectCandidate } from './redirects';

const anonymous = { 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') };

/** The redirect lookups the page made (the `path` query parameter of each). */
function lookups(fn: ReturnType<typeof mockFetch>['fn']): string[] {
  return fn.mock.calls
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .filter((url) => url.pathname.endsWith('/public/redirects'))
    .map((url) => url.searchParams.get('path') ?? '');
}

describe('moved public addresses (client-side navigation)', () => {
  it('navigates from an old address to its new one, keeping the query string', async () => {
    const { fn } = mockFetch({
      ...anonymous,
      'GET /public/redirects': () => json(200, { location: '/who-we-are?utm_source=news', statusCode: 301 }),
    });
    const { router } = renderWithApp(<NotFound />, {
      route: '/about-us?utm_source=news',
      routes: [{ path: '/who-we-are', element: <p>Who we are</p> }],
    });
    expect(await screen.findByText('Who we are')).toBeInTheDocument();
    expect(router.state.location.pathname + router.state.location.search).toBe('/who-we-are?utm_source=news');
    expect(lookups(fn)).toEqual(['/about-us?utm_source=news']);
    // Replaced, not pushed: Back does not return to the dead address.
    expect(router.state.historyAction).toBe('REPLACE');
  });

  it('shows the 404 page when the address was never moved', async () => {
    mockFetch({ ...anonymous, 'GET /public/redirects': () => problem(404, 'website.redirect_not_found', 'Not redirected') });
    renderWithApp(<NotFound />, { route: '/no-such-page' });
    expect(await screen.findByText('404')).toBeInTheDocument();
  });

  it('never looks up the app’s own areas and ignores unsafe targets', async () => {
    expect(isRedirectCandidate('/agency/website/nope')).toBe(false);
    expect(isRedirectCandidate('/api/v1/x')).toBe(false);
    expect(isRedirectCandidate('/')).toBe(false);
    expect(isRedirectCandidate('/about-us')).toBe(true);
    expect(isRedirectCandidate('/lp/nimbus/offer')).toBe(true);

    const portal = mockFetch({ ...anonymous });
    renderWithApp(<NotFound />, { route: '/agency/website/nope' });
    expect(await screen.findByText('404')).toBeInTheDocument();
    expect(lookups(portal.fn)).toEqual([]);

    mockFetch({ ...anonymous, 'GET /public/redirects': () => json(200, { location: '//evil.example/x', statusCode: 301 }) });
    const { router } = renderWithApp(<NotFound />, { route: '/old-page' });
    expect(await screen.findByText('404')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/old-page');
  });

  it('follows a renamed service from its detail page', async () => {
    mockFetch({
      ...anonymous,
      'GET /public/services/old-seo': () => problem(404, 'website.not_found', 'Service was not found.'),
      'GET /public/services/seo': () => json(200, fx.service),
      'GET /public/redirects': () => json(200, { location: '/services/seo', statusCode: 301 }),
    });
    const { router } = renderWithApp(<ServiceDetailPage />, { route: '/services/old-seo', path: '/services/:slug' });
    expect(await screen.findByRole('heading', { level: 1, name: fx.service.heroTitle ?? fx.service.name })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/services/seo');
  });

  it('follows a renamed service line filter', async () => {
    const { fn } = mockFetch({
      ...anonymous,
      'GET /public/services': () => json(200, fx.serviceGroups),
      'GET /public/redirects': () => json(200, { location: `/services?category=${fx.serviceGroups[0]!.slug}`, statusCode: 301 }),
    });
    const { router } = renderWithApp(<ServicesPage />, { route: '/services?category=old-line', path: '/services' });
    await screen.findByRole('button', { name: fx.serviceGroups[0]!.name, pressed: true });
    expect(router.state.location.search).toBe(`?category=${fx.serviceGroups[0]!.slug}`);
    expect(lookups(fn)).toEqual(['/services?category=old-line']);
  });

  it('follows a landing page that was published under a new address', async () => {
    mockFetch({
      ...anonymous,
      'GET /public/lp/nimbus/old-offer': () => problem(404, 'not_found', 'Page not found'),
      'GET /public/redirects': () => json(200, { location: '/lp/nimbus/spring-offer', statusCode: 301 }),
    });
    const { router } = renderWithApp(<PublicLandingPageView />, {
      route: '/lp/nimbus/old-offer',
      path: '/lp/:client/:slug',
      routes: [{ path: '/lp/nimbus/spring-offer', element: <p>Spring offer</p> }],
    });
    expect(await screen.findByText('Spring offer')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/lp/nimbus/spring-offer');
  });
});
