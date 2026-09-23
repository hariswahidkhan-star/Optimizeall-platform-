import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { ClientSeoPage, type ClientSeoOverview } from './ClientSeoPage';

const overview: ClientSeoOverview = {
  from: '2026-08-24',
  to: '2026-09-23',
  organizations: [
    {
      clientAccountId: 'cl1',
      clientName: 'Nimbus Fitness',
      kpis: {
        sites: [
          {
            siteId: 's1',
            name: 'Nimbus website',
            domain: 'www.nimbusfit.example',
            healthScore: 86,
            healthScoreChange: 4,
            lastAuditAt: '2026-09-22T08:00:00Z',
            trackedKeywords: 40,
            top3: 6,
            top10: 18,
            averagePosition: 14.2,
            averagePositionChange: 1.5,
            clicks: 1830,
            impressions: 64200,
            liveBacklinks: 57,
            lostBacklinks: 2,
            shareOfVoice: 0.21,
          },
        ],
        leads: {
          views: 2400,
          submissions: 96,
          conversionRate: 0.04,
          pages: [{ pageId: 'p1', name: 'Free trial', slug: 'free-trial', views: 2400, submissions: 96, conversionRate: 0.04 }],
          daily: [
            { date: '2026-09-21', submissions: 3 },
            { date: '2026-09-22', submissions: 5 },
          ],
        },
      },
      topKeywords: [
        { keyword: 'home workout app', position: 3, change: 2, siteName: 'Nimbus website' },
        { keyword: 'hiit timer', position: 12, change: -4, siteName: 'Nimbus website' },
      ],
    },
  ],
};

describe('client portal SEO overview', () => {
  it('shows site health, rankings and landing-page leads, with no axe violations', async () => {
    const client = makeUser({ roles: ['Client'], permissions: ['client.portal'] });
    mockFetch({
      'POST /auth/refresh': () => json(200, session(client)),
      'GET /client/seo/overview': () => json(200, overview),
    });
    const { container } = renderWithApp(<ClientSeoPage />);
    const site = await screen.findByRole('region', { name: 'Nimbus website' });
    expect(within(site).getByText('Health 86/100')).toBeInTheDocument();
    expect(within(site).getByText('18 of 40')).toBeInTheDocument();
    const keywords = screen.getByRole('table', { name: 'Top keywords' });
    expect(within(keywords).getByText('Up 2')).toBeInTheDocument();
    expect(within(keywords).getByText('Down 4')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Landing-page leads' })).toHaveTextContent('96');
    expect(await axeViolations(container)).toEqual([]);
  });
});
