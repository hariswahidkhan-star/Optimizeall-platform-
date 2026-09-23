import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { CampaignLandingPage } from './CampaignLandingPage';
import { invitationRegisterPath, JoinPage } from './JoinPage';
import type { PublicCampaignLanding, PublicInvitationLanding } from './landingApi';

const anonymous = { 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') };

const campaignInvite: PublicInvitationLanding = {
  code: 'Ab12Cd34',
  type: 'campaign',
  headline: 'Share the Nimbus launch',
  body: 'Creators earn a fixed reward for every approved post.',
  heroImageUrl: '/api/v1/files/0f8fad5b-d9cb-469f-a165-70867728950e',
  campaign: {
    slug: 'nimbus-launch',
    title: 'Nimbus Fitness launch',
    summary: 'Launch campaign',
    platforms: ['Instagram', 'X'],
    reward: { currency: 'USD', baseAmount: 8.5 },
    startsAt: '2026-09-01T00:00:00Z',
    endsAt: '2026-10-31T00:00:00Z',
    category: { name: 'Fitness', slug: 'fitness' },
  },
  utm: { source: 'newsletter', medium: 'email', campaign: 'launch' },
  experiment: null,
};

const publicCampaign: PublicCampaignLanding = {
  slug: 'nimbus-launch',
  title: 'Nimbus Fitness launch',
  summary: 'Launch campaign',
  headline: 'Get paid to share the Nimbus launch',
  body: 'Join creators sharing the launch.',
  heroImageUrl: null,
  platforms: ['TikTok'],
  reward: { currency: 'USD', baseAmount: 6 },
  startsAt: '2026-09-01T00:00:00Z',
  endsAt: '2026-10-31T00:00:00Z',
  submissionDeadline: '2026-11-03T00:00:00Z',
  category: null,
  assets: [{ id: 'a1', title: 'Launch visual', url: '/api/v1/files/7c9e6679-7425-40de-944b-e07fc1f90ae7' }],
  disclosure: 'Paid partnership with Nimbus #ad',
  experiment: { experimentId: 'e1', variantId: 'v2', key: 'B' },
};

afterEach(() => {
  window.localStorage.clear();
});

describe('invitation landing (/join/:code)', () => {
  it('renders a campaign invitation with its CTA to registration (keeping ?ref=)', async () => {
    mockFetch({ ...anonymous, 'GET /public/invitations/Ab12Cd34': () => json(200, campaignInvite) });
    const { container } = renderWithApp(<JoinPage />, { route: '/join/Ab12Cd34?ref=FRIEND7', path: '/join/:code' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Share the Nimbus launch' })).toBeInTheDocument();
    expect(screen.getByText('Creators earn a fixed reward for every approved post.')).toBeInTheDocument();
    expect(screen.getByText('Invitation · Nimbus Fitness launch')).toBeInTheDocument();
    expect(screen.getByText(/\$8\.50|8\.50/)).toBeInTheDocument();
    expect(screen.getByText('Instagram')).toBeInTheDocument();
    expect(screen.getByText('X (Twitter)')).toBeInTheDocument();
    expect(screen.getByText('Fitness')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Paid posts are always disclosed' })).toBeInTheDocument();
    expect(container.querySelector('img')?.getAttribute('src')).toBe(campaignInvite.heroImageUrl);
    expect(screen.getByRole('link', { name: 'Accept invitation' })).toHaveAttribute(
      'href',
      '/register?invite=Ab12Cd34&ref=FRIEND7',
    );
    expect(await axeViolations(container)).toEqual([]);
  });

  it('renders the platform variant without campaign details', async () => {
    mockFetch({
      ...anonymous,
      'GET /public/invitations/Plat0001': () =>
        json(200, { ...campaignInvite, code: 'Plat0001', type: 'platform', campaign: null, heroImageUrl: null }),
    });
    renderWithApp(<JoinPage />, { route: '/join/Plat0001', path: '/join/:code' });
    expect(await screen.findByText('You’re invited to Optimize All')).toBeInTheDocument();
    expect(screen.queryByText('Platforms')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Accept invitation' })).toHaveAttribute('href', '/register?invite=Plat0001');
  });

  it('shows a friendly page for expired or used-up invitations (404)', async () => {
    mockFetch({ ...anonymous, 'GET /public/invitations/Gone1234': () => problem(404, 'not_found', 'Invitation not found') });
    renderWithApp(<JoinPage />, { route: '/join/Gone1234', path: '/join/:code' });
    expect(await screen.findByRole('heading', { name: 'This invitation is no longer available' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Create an account' })).toHaveAttribute('href', '/register');
  });

  it('builds safe registration links', () => {
    expect(invitationRegisterPath('Ab12', null)).toBe('/register?invite=Ab12');
    expect(invitationRegisterPath('Ab12', 'REF-1')).toBe('/register?invite=Ab12&ref=REF-1');
    expect(invitationRegisterPath('Ab12', 'x"><script>')).toBe('/register?invite=Ab12');
  });
});

describe('campaign landing (/c/:slug)', () => {
  it('sends a persistent X-Visitor-Id so A/B variants apply, and invites anonymous visitors to register', async () => {
    const { calls } = mockFetch({ ...anonymous, 'GET /public/campaigns/nimbus-launch': () => json(200, publicCampaign) });
    const { container } = renderWithApp(<CampaignLandingPage />, { route: '/c/nimbus-launch', path: '/c/:slug' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Get paid to share the Nimbus launch' })).toBeInTheDocument();
    const request = calls.find((c) => c.path === '/public/campaigns/nimbus-launch')!;
    const visitorId = request.headers['X-Visitor-Id'];
    expect(visitorId).toBeTruthy();
    expect(window.localStorage.getItem('oa.visitorId')).toBe(visitorId);

    expect(screen.getByRole('link', { name: 'Join to take part' })).toHaveAttribute('href', '/register');
    expect(screen.getByText('Paid partnership with Nimbus #ad')).toBeInTheDocument();
    const gallery = screen.getByRole('region', { name: 'Content you’ll share' });
    expect(within(gallery).getByText('Launch visual')).toBeInTheDocument();
    expect(gallery.querySelector('img')).toHaveAttribute('src', publicCampaign.assets[0]!.url);
    expect(await axeViolations(container)).toEqual([]);
  });

  it('sends signed-in users to the campaign in the participant portal', async () => {
    mockFetch({
      'POST /auth/refresh': () => json(200, session(makeUser())),
      'GET /public/campaigns/nimbus-launch': () => json(200, publicCampaign),
    });
    renderWithApp(<CampaignLandingPage />, { route: '/c/nimbus-launch', path: '/c/:slug' });
    expect(await screen.findByRole('link', { name: 'Open the campaign' })).toHaveAttribute(
      'href',
      '/app/campaigns/nimbus-launch',
    );
  });

  it('keeps working when localStorage is unavailable', async () => {
    const original = Object.getOwnPropertyDescriptor(window, 'localStorage')!;
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      get() {
        throw new Error('blocked');
      },
    });
    try {
      const { calls } = mockFetch({ ...anonymous, 'GET /public/campaigns/nimbus-launch': () => json(200, publicCampaign) });
      renderWithApp(<CampaignLandingPage />, { route: '/c/nimbus-launch', path: '/c/:slug' });
      expect(await screen.findByRole('heading', { level: 1 })).toBeInTheDocument();
      expect(calls.find((c) => c.path === '/public/campaigns/nimbus-launch')?.headers['X-Visitor-Id']).toBeTruthy();
    } finally {
      Object.defineProperty(window, 'localStorage', original);
    }
  });

  it('shows a friendly page for unpublished or private campaigns (404)', async () => {
    mockFetch({ ...anonymous, 'GET /public/campaigns/secret': () => problem(404, 'not_found', 'Campaign not found') });
    renderWithApp(<CampaignLandingPage />, { route: '/c/secret', path: '/c/:slug' });
    expect(await screen.findByRole('heading', { name: 'This campaign isn’t available' })).toBeInTheDocument();
  });
});
