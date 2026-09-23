import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { setViewportWidth } from '@/test/viewport';
import { AchievementsPage } from './achievements/AchievementsPage';
import { CampaignDetailPage } from './campaigns/CampaignDetailPage';
import { NotificationsPage } from './notifications/NotificationsPage';
import { PayoutsPage } from './payouts/PayoutsPage';
import { NotificationPreferencesPage } from './profile/NotificationPreferencesPage';
import { PayoutDetailsPage } from './profile/PayoutDetailsPage';
import { ProfileDetailsPage } from './profile/ProfileDetailsPage';
import { ReferralsPage } from './referrals/ReferralsPage';
import { SubmissionsPage } from './submissions/SubmissionsPage';
import { SupportPage, TicketDetailPage } from './support/SupportPages';
import { authRoutes, makeCampaign, makeSummary, paged } from './test/fixtures';

const routes = {
  ...authRoutes,
  'GET /campaigns/autumn-launch': () => json(200, makeCampaign()),
  'GET /campaigns/c1/experiment-variants': () => json(200, []),
  'POST /me/campaigns/c1/tracking-link': () =>
    json(409, { status: 409, code: 'tracking.not_enabled', title: 'No tracking' }),
  'GET /me/submissions': () =>
    json(
      200,
      paged([
        {
          id: 's1',
          campaign: { id: 'c1', slug: 'autumn-launch', title: 'Autumn launch' },
          platform: 'Instagram',
          postUrl: 'https://instagram.com/p/1',
          status: 'Approved',
          submittedAt: '2026-09-20T00:00:00Z',
          estimatedReward: 5,
          currency: 'USD',
          decisionReason: null,
        },
      ]),
    ),
  'GET /me/notifications': () =>
    json(
      200,
      paged([
        {
          id: 'n1',
          type: 'submission.decision',
          title: 'Your post was approved',
          body: 'Nice work.',
          linkUrl: '/app/submissions/s1',
          createdAt: '2026-09-22T00:00:00Z',
          readAt: null,
          isRead: false,
        },
      ]),
    ),
  'GET /me/notifications/unread-count': () => json(200, { count: 1 }),
  'GET /me/support/tickets': () =>
    json(
      200,
      paged([
        {
          id: 't1',
          reference: 'SUP-ABC123',
          subject: 'Payout question',
          category: 'Payout',
          status: 'AwaitingParticipant',
          priority: 'Normal',
          createdAt: '2026-09-20T00:00:00Z',
          updatedAt: '2026-09-21T00:00:00Z',
        },
      ]),
    ),
  'GET /me/support/tickets/t1': () =>
    json(200, {
      id: 't1',
      reference: 'SUP-ABC123',
      subject: 'Payout question',
      category: 'Payout',
      status: 'AwaitingParticipant',
      priority: 'Normal',
      submissionId: null,
      payoutItemId: null,
      createdAt: '2026-09-20T00:00:00Z',
      updatedAt: '2026-09-21T00:00:00Z',
      resolvedAt: null,
      canReply: true,
      messages: [
        {
          id: 'm1',
          body: 'When is my payout?',
          fromStaff: false,
          authorName: 'Ada',
          createdAt: '2026-09-20T00:00:00Z',
        },
        {
          id: 'm2',
          body: 'On 2 October.',
          fromStaff: true,
          authorName: 'Optimize All Support',
          createdAt: '2026-09-21T00:00:00Z',
        },
      ],
    }),
  'GET /me/referrals': () =>
    json(200, {
      code: 'K7QM2ZP4',
      link: 'https://app.example/register?ref=K7QM2ZP4',
      program: {
        enabled: true,
        rewardAmount: 5,
        currency: 'USD',
        qualifyingAction: 'FirstApprovedSubmission',
        qualifyWithinDays: 60,
      },
      stats: { registered: 1, qualified: 0, rewarded: 0, pendingReward: 0, expired: 0, rejected: 0 },
      items: [
        {
          id: 'r1',
          maskedName: 'Z***',
          status: 'Registered',
          registeredAt: '2026-09-01T00:00:00Z',
          qualifiedAt: null,
          qualifyBy: '2026-10-31T00:00:00Z',
          rewardStatus: null,
        },
      ],
    }),
  'GET /me/achievements': () =>
    json(200, [
      {
        key: 'first',
        name: 'First approved post',
        description: 'Your first post was approved.',
        icon: 'badge-check',
        criterion: 'ApprovedSubmissions',
        threshold: 1,
        progress: 1,
        awardedAt: '2026-09-21T00:00:00Z',
      },
      {
        key: 'five',
        name: 'Five approved posts',
        description: 'Five posts approved.',
        icon: 'star',
        criterion: 'ApprovedSubmissions',
        threshold: 5,
        progress: 1,
        awardedAt: null,
      },
    ]),
  'GET /me/payouts': () =>
    json(
      200,
      paged([
        {
          itemId: 'p1',
          batchReference: 'PB-2026-09-13',
          periodKey: '2026-09-13',
          cutoffAt: '2026-09-13T23:59:59Z',
          paymentDate: '2026-09-18',
          amount: 20,
          currency: 'USD',
          status: 'Paid',
          paidAt: '2026-09-18T10:00:00Z',
          paymentReference: '••••3456',
          earningCount: 2,
        },
      ]),
    ),
  'GET /me/earnings/summary': () => json(200, makeSummary()),
  'GET /me/profile': () =>
    json(200, {
      id: 'u1',
      email: 'ada@example.com',
      emailVerified: true,
      displayName: 'Ada Lovelace',
      countryCode: 'GB',
      languageCode: 'en',
      timeZone: 'Europe/London',
      interests: ['fashion'],
      marketingEmailOptIn: false,
      whatsAppNumber: null,
      whatsAppOptIn: false,
      tier: 'Standard',
      referralCode: 'K7QM2ZP4',
      createdAt: '2026-09-01T00:00:00Z',
    }),
  'GET /me/payout-profile': () =>
    json(200, {
      configured: true,
      method: 'BankTransfer',
      accountHolderName: 'Ada Lovelace',
      destinationHint: '••••5432',
      preferredCurrency: 'GBP',
      countryCode: 'GB',
      updatedAt: '2026-09-20T00:00:00Z',
    }),
  'GET /me/notification-preferences': () =>
    json(200, {
      channels: [
        { channel: 'InApp', available: true, reason: null },
        { channel: 'Email', available: true, reason: null },
        { channel: 'WhatsApp', available: false, reason: 'WhatsApp notifications are not available yet.' },
      ],
      types: [
        {
          type: 'submission.decision',
          label: 'Submission decisions',
          description: 'When a reviewer decides.',
          essential: false,
          marketing: false,
          channels: [
            { channel: 'InApp', enabled: true, locked: true, available: true },
            { channel: 'Email', enabled: true, locked: false, available: true },
            { channel: 'WhatsApp', enabled: true, locked: false, available: false },
          ],
        },
        {
          type: 'payout.paid',
          label: 'Payout sent',
          description: 'When we pay you.',
          essential: true,
          marketing: false,
          channels: [
            { channel: 'InApp', enabled: true, locked: true, available: true },
            { channel: 'Email', enabled: true, locked: true, available: true },
            { channel: 'WhatsApp', enabled: false, locked: true, available: false },
          ],
        },
      ],
    }),
};

const pages: [string, ReactElement, string, string, string][] = [
  [
    'campaign detail',
    <CampaignDetailPage key="c" />,
    '/app/campaigns/autumn-launch',
    '/app/campaigns/:slug',
    'Paid-content disclosure required',
  ],
  ['submissions', <SubmissionsPage key="s" />, '/app/submissions', '/app/submissions', 'Autumn launch'],
  [
    'notifications',
    <NotificationsPage key="n" />,
    '/app/notifications',
    '/app/notifications',
    'Your post was approved',
  ],
  ['support', <SupportPage key="t" />, '/app/support', '/app/support', 'Payout question'],
  ['ticket', <TicketDetailPage key="td" />, '/app/support/t1', '/app/support/:ticketId', 'On 2 October.'],
  ['referrals', <ReferralsPage key="r" />, '/app/referrals', '/app/referrals', 'Z***'],
  [
    'achievements',
    <AchievementsPage key="a" />,
    '/app/achievements',
    '/app/achievements',
    'Five approved posts',
  ],
  ['payouts', <PayoutsPage key="p" />, '/app/payouts', '/app/payouts', 'PB-2026-09-13'],
  ['profile', <ProfileDetailsPage key="pr" />, '/app/profile', '/app/profile', 'About you'],
  [
    'payout details',
    <PayoutDetailsPage key="pd" />,
    '/app/profile/payout-details',
    '/app/profile/payout-details',
    '••••5432',
  ],
  [
    'notification preferences',
    <NotificationPreferencesPage key="np" />,
    '/app/profile/notification-preferences',
    '/app/profile/notification-preferences',
    'Submission decisions',
  ],
];

describe('participant pages accessibility', () => {
  it.each(pages)('%s has no axe violations', async (_name, element, route, path, text) => {
    mockFetch(routes);
    const { container } = renderWithApp(element, { route, path });
    expect((await screen.findAllByText(text)).length).toBeGreaterThan(0);
    expect(await axeViolations(container)).toEqual([]);
  });

  it('renders the submissions list as cards on phones without violations', async () => {
    setViewportWidth(360);
    mockFetch(routes);
    const { container } = renderWithApp(<SubmissionsPage />, {
      route: '/app/submissions',
      path: '/app/submissions',
    });
    expect(await screen.findByText('Autumn launch')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('words ticket statuses for the participant with the shared status badge', async () => {
    mockFetch(routes);
    renderWithApp(<TicketDetailPage />, { route: '/app/support/t1', path: '/app/support/:ticketId' });
    expect((await screen.findAllByText('Awaiting your reply')).length).toBeGreaterThan(0);
    expect(screen.queryByText('Awaiting participant')).not.toBeInTheDocument();
  });

  it('hides the tracking section when the campaign has no tracking', async () => {
    const { calls } = mockFetch(routes);
    renderWithApp(<CampaignDetailPage />, {
      route: '/app/campaigns/autumn-launch',
      path: '/app/campaigns/:slug',
    });
    expect(await screen.findByRole('heading', { name: 'Reward terms' })).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 50));
    expect(screen.queryByRole('heading', { name: 'Your tracking link' })).not.toBeInTheDocument();
    expect(calls.some((c) => c.path.includes('tracking-link'))).toBe(false);
  });

  it('creates a tracking link only when the participant asks for it', async () => {
    const user = userEvent.setup();
    const link = {
      id: 't1',
      campaignId: 'c1',
      campaignTitle: 'Autumn launch',
      code: 'abc123',
      shortUrl: 'https://oa.test/t/abc123',
      destinationPreview: 'https://brand.example',
      utmSource: 'optimizeall',
      utmMedium: 'social',
      utmCampaign: 'autumn',
      utmContent: null,
      createdAt: '2026-09-20T00:00:00Z',
      stats: { clicks: 4, uniqueClicks: 3, verifiedConversions: 1 },
    };
    const { calls } = mockFetch({
      ...routes,
      'GET /campaigns/autumn-launch': () => json(200, makeCampaign({ trackingEnabled: true })),
      'GET /me/tracking-links': () => json(200, []),
      'POST /me/campaigns/c1/tracking-link': () => json(200, link),
    });
    const { container } = renderWithApp(<CampaignDetailPage />, {
      route: '/app/campaigns/autumn-launch',
      path: '/app/campaigns/:slug',
    });
    const button = await screen.findByRole('button', { name: 'Get my tracking link' });
    await waitFor(() => expect(button).toBeEnabled());
    const posts = () =>
      calls.filter((c) => c.method === 'POST' && c.path === '/me/campaigns/c1/tracking-link');
    expect(posts()).toHaveLength(0);
    expect(await axeViolations(container)).toEqual([]);

    await user.click(button);
    expect(await screen.findByDisplayValue('https://oa.test/t/abc123')).toBeInTheDocument();
    expect(posts()).toHaveLength(1);
  });

  it('shows an existing tracking link without creating one', async () => {
    const { calls } = mockFetch({
      ...routes,
      'GET /campaigns/autumn-launch': () => json(200, makeCampaign({ trackingEnabled: true })),
      'GET /me/tracking-links': () =>
        json(200, [
          {
            id: 't1',
            campaignId: 'c1',
            campaignTitle: 'Autumn launch',
            code: 'abc123',
            shortUrl: 'https://oa.test/t/existing',
            destinationPreview: 'https://brand.example',
            utmSource: 'optimizeall',
            utmMedium: 'social',
            utmCampaign: 'autumn',
            utmContent: null,
            createdAt: '2026-09-20T00:00:00Z',
            stats: { clicks: 0, uniqueClicks: 0, verifiedConversions: 0 },
          },
        ]),
    });
    renderWithApp(<CampaignDetailPage />, {
      route: '/app/campaigns/autumn-launch',
      path: '/app/campaigns/:slug',
    });
    expect(await screen.findByDisplayValue('https://oa.test/t/existing')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Get my tracking link' })).not.toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST' && c.path.includes('tracking-link'))).toBe(false);
  });
});
