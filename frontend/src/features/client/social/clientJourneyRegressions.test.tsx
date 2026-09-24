import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { summary } from '@/features/agency/social/test/fixtures';
import { ClientApprovalsPage, ClientPerformancePage } from './ClientSocialPages';

/** Regressions found by the social + ads E2E journey (frontend/e2e/j-social). */
const totals = {
  impressions: 0,
  reach: 0,
  engagements: 0,
  clicks: 0,
  videoViews: 0,
  engagementRate: null,
  followersStart: null,
  followersEnd: null,
  followersGrowth: null,
};

const clientSession = () =>
  json(
    200,
    session(
      makeUser({
        roles: ['Client'],
        permissions: ['client.portal'],
        displayName: 'Casey Client',
        timeZone: 'UTC',
      }),
    ),
  );

describe('ClientApprovalsPage', () => {
  it('does not fetch a post again after sending it back for changes (it is a draft now, which clients cannot open)', async () => {
    const user = userEvent.setup();
    let status = 'ClientApproval';
    const post = {
      ...summary({ status: 'ClientApproval' }),
      campaignId: null,
      autoAppendUtm: false,
      evergreenIntervalDays: 30,
      evergreenMaxRepeats: 3,
      evergreenRepeatCount: 0,
      recycledFromPostId: null,
      recycleNumber: null,
      requiresClientApproval: true,
      isValid: true,
      allowedActions: ['clientApprove', 'clientRequestChanges'],
      variants: [],
      comments: [],
      createdByName: 'Sofia',
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-01T00:00:00Z',
    };
    const { calls } = mockFetch({
      'POST /auth/refresh': clientSession,
      'GET /client/social/organizations': () =>
        json(200, [
          {
            id: 'c1',
            name: 'Nimbus Fitness',
            currency: 'USD',
            timeZone: 'UTC',
            role: 'Approver',
            canApprove: true,
          },
        ]),
      'GET /client/social/approvals': () =>
        json(200, status === 'ClientApproval' ? [summary({ status: 'ClientApproval' })] : []),
      'GET /client/social/posts/post1': () =>
        status === 'ClientApproval' ? json(200, post) : problem(404, 'not_found', 'Post not found.'),
      'GET /client/social/media': () =>
        status === 'ClientApproval' ? json(200, []) : problem(404, 'not_found', 'Post not found.'),
      'POST /client/social/posts/post1/request-changes': () => {
        status = 'Draft';
        return json(200, { ...post, status: 'Draft', allowedActions: [] });
      },
    });
    renderWithApp(<ClientApprovalsPage />, {
      route: '/client/social/approvals',
      path: '/client/social/approvals',
    });
    await user.click(await screen.findByRole('button', { name: 'Review' }, { timeout: 5000 }));
    const dialog = await screen.findByRole('dialog', { name: 'Launch day' }, { timeout: 5000 });
    await user.type(
      await within(dialog).findByRole('textbox', { name: /^Comment/ }, { timeout: 5000 }),
      'Not this week',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Request changes' }));
    expect(await screen.findByText("You're all caught up", {}, { timeout: 5000 })).toBeInTheDocument();
    const decided = calls.findIndex((c) => c.path === '/client/social/posts/post1/request-changes');
    await waitFor(() =>
      expect(calls.slice(decided).some((c) => c.path === '/client/social/approvals')).toBe(true),
    );
    expect(
      calls
        .slice(decided + 1)
        .filter((c) => c.path === '/client/social/posts/post1' || c.path === '/client/social/media'),
    ).toEqual([]);
  });
});

describe('ClientPerformancePage', () => {
  it('names ad platforms the way people read them (Google Ads, not GoogleAds)', async () => {
    mockFetch({
      'POST /auth/refresh': () =>
        json(
          200,
          session(
            makeUser({
              roles: ['Client'],
              permissions: ['client.portal'],
              displayName: 'Casey Client',
              timeZone: 'UTC',
            }),
          ),
        ),
      'GET /client/social/organizations': () =>
        json(200, [
          {
            id: 'c1',
            name: 'Helio Coffee',
            currency: 'EUR',
            timeZone: 'Europe/Berlin',
            role: 'Viewer',
            canApprove: false,
          },
        ]),
      'GET /client/social/performance': () =>
        json(200, {
          social: {
            clientAccountId: 'c1',
            from: '2026-09-01',
            to: '2026-09-23',
            totals,
            postsPublished: 0,
            profiles: [],
            sourceLabel: 'No data',
            sources: [],
            definitions: '',
          },
          socialSeries: [],
          topPosts: [],
          ads: {
            reportingCurrency: 'EUR',
            totals: {
              spend: 1337.94,
              impressions: 51210,
              clicks: 2050,
              conversions: 102,
              conversionValue: 9380,
            },
            kpis: { cpa: 13.12, roas: 7.01, ctr: 0.04 },
            platforms: [
              {
                platform: 'GoogleAds',
                totals: { spend: 1337.94, conversions: 102 },
                kpis: { roas: 7.01, cpa: 13.12 },
                sourceLabel: 'Measured',
              },
            ],
            fxMissing: [],
            sourceLabel: 'Measured',
          },
        }),
    });
    renderWithApp(<ClientPerformancePage />, {
      route: '/client/social/performance',
      path: '/client/social/performance',
    });
    expect(await screen.findByText(/^Google Ads: €1,337\.94/, {}, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByText(/GoogleAds/)).not.toBeInTheDocument();
  });
});
