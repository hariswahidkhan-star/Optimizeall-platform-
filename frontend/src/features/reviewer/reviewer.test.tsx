import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { describeReviewError } from './api/errors';
import { ApiError } from '@/lib/api/errors';
import { isTypingTarget } from './hooks/useHotkeys';
import { LiveChecksPage } from './pages/LiveChecksPage';
import { OverviewPage } from './pages/OverviewPage';
import { SocialAccountPage } from './pages/SocialAccountPage';
import { parseQueueParams, withQueueParam } from './queueParams';
import { reviewerSession } from './test/fixtures';
import { riskLevel } from './components/risk';

describe('queue URL params', () => {
  it('parses valid filters and drops invalid ones', () => {
    expect(
      parseQueueParams(new URLSearchParams('status=Approved&minRisk=abc&flagged=maybe&sort=x&page=-2')),
    ).toEqual({
      status: undefined,
      campaignId: undefined,
      platform: undefined,
      minRisk: undefined,
      flagged: undefined,
      assignedToMe: false,
      claimedByMe: false,
      sort: 'oldest',
      page: 1,
      pageSize: 25,
    });
    expect(
      parseQueueParams(new URLSearchParams('status=UnderReview&mine=1&claimed=1&sort=risk&page=3')),
    ).toMatchObject({
      status: 'UnderReview',
      assignedToMe: true,
      claimedByMe: true,
      sort: 'risk',
      page: 3,
    });
  });

  it('resets the page on filter changes and drops the default sort', () => {
    const next = withQueueParam(new URLSearchParams('page=4&sort=risk'), 'sort', 'oldest');
    expect(next.toString()).toBe('');
    expect(withQueueParam(new URLSearchParams('status=Pending'), 'page', '2').toString()).toBe(
      'status=Pending&page=2',
    );
  });
});

describe('helpers', () => {
  it('treats text fields as typing targets but not buttons', () => {
    const input = document.createElement('input');
    const checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    expect(isTypingTarget(input)).toBe(true);
    expect(isTypingTarget(document.createElement('textarea'))).toBe(true);
    expect(isTypingTarget(checkbox)).toBe(false);
    expect(isTypingTarget(document.createElement('button'))).toBe(false);
  });

  it('bands risk scores', () => {
    expect(riskLevel(0)).toBe('low');
    expect(riskLevel(15)).toBe('medium');
    expect(riskLevel(40)).toBe('high');
  });

  it('maps error codes to human messages and falls back to the server title', () => {
    expect(
      describeReviewError(new ApiError({ status: 403, code: 'review.self_review', title: 'x' })).title,
    ).toMatch(/own submission/);
    const unknown = describeReviewError(
      new ApiError({ status: 409, code: 'new.code', title: 'Server says no' }),
    );
    expect(unknown.description).toBe('Server says no');
    expect(unknown.refresh).toBe(true);
  });
});

describe('OverviewPage', () => {
  it('shows workload stats and quick links', async () => {
    mockFetch({
      'POST /auth/refresh': reviewerSession(),
      'GET /review/stats': () =>
        json(200, {
          myDecisionsToday: 12,
          queueByStatus: { Pending: 30, UnderReview: 4, NeedsCorrection: 2 },
          oldestPendingSubmittedAt: '2026-09-23T05:00:00Z',
          oldestPendingAgeHours: 5.25,
          dueLiveChecks: 3,
          openAppeals: 1,
          claimedByMe: 1,
        }),
    });
    const { container } = renderWithApp(<OverviewPage />, { route: '/review', path: '/review' });
    expect(await screen.findByText('12')).toBeInTheDocument();
    expect(screen.getByText('5.3 h')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Review queue' })).toHaveAttribute('href', '/review/queue');
    expect(screen.getByText(/3 due/)).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('LiveChecksPage', () => {
  const item = {
    submissionId: 's1',
    campaign: { id: 'c1', title: 'Autumn Launch' },
    participant: { id: 'p1', displayName: 'Sara Khan' },
    platform: 'Instagram',
    postUrl: 'https://www.instagram.com/p/abc',
    postedAt: '2026-09-20T10:00:00Z',
    decidedAt: '2026-09-21T10:00:00Z',
    dueAt: '2026-09-22T10:00:00Z',
    isDue: true,
  };

  it('unlocks actions after the post is opened and requires a note to mark removed', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': reviewerSession(),
      'GET /review/live-checks': () =>
        json(200, { items: [item], total: 1, page: 1, pageSize: 25, totalPages: 1 }),
      'POST /review/submissions/s1/live-check': () =>
        json(200, { submissionId: 's1', status: 'Reversed', liveCheckStatus: 'Removed', earnings: [] }),
    });
    const { container } = renderWithApp(<LiveChecksPage />, {
      route: '/review/live-checks',
      path: '/review/live-checks',
    });
    const removed = await screen.findByRole('button', { name: /Mark removed: Autumn Launch/ });
    expect(removed).toBeDisabled();
    const link = screen.getByRole('link', { name: /Open post/ });
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'));
    link.addEventListener('click', (e) => e.preventDefault());
    await user.click(link);
    expect(removed).toBeEnabled();
    await user.click(removed);
    const dialog = await screen.findByRole('alertdialog');
    await user.click(screen.getByRole('button', { name: 'Mark removed & reverse' }));
    expect(await screen.findByText(/at least 5 characters/)).toBeInTheDocument();
    await user.type(screen.getByLabelText(/What did you find/), 'Post deleted');
    await user.click(screen.getByRole('button', { name: 'Mark removed & reverse' }));
    await screen.findByText('Marked as removed');
    expect(calls.find((c) => c.path === '/review/submissions/s1/live-check')?.body).toEqual({
      result: 'Removed',
      note: 'Post deleted',
    });
    expect(dialog).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('SocialAccountPage', () => {
  const detail = (ownerId = 'owner-1') => ({
    account: {
      id: 'sa1',
      platform: 'TikTok',
      handle: 'sara',
      profileUrl: 'https://www.tiktok.com/@sara',
      accountCreatedAt: '2021-03-01T00:00:00Z',
      accountAgeDays: 2000,
      followerCount: 1200,
      primaryLanguage: 'en',
      audienceCountryCode: 'PK',
      verificationStatus: 'PendingReview',
      verificationNote: null,
      verifiedAt: null,
      isActive: true,
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-02T00:00:00Z',
      owner: { id: ownerId, displayName: 'Sara Khan', email: 'sara@example.com', countryCode: 'PK' },
      concurrencyStamp: 'st',
    },
    qualifies: true,
    eligibleFrom: null,
    reasons: [],
    verifiedBy: null,
    ownerActiveAccountCount: 2,
    history: [],
  });

  it('requires a note to reject and maps social.not_pending', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': reviewerSession(),
      'GET /review/social-accounts/sa1': () => json(200, detail()),
      'POST /review/social-accounts/sa1/decision': () => problem(409, 'social.not_pending', 'Not pending'),
    });
    const { container } = renderWithApp(<SocialAccountPage />, {
      route: '/review/social-verification/sa1',
      path: '/review/social-verification/:accountId',
    });
    await user.click(await screen.findByRole('radio', { name: /Rejected/ }));
    await user.click(screen.getByRole('button', { name: 'Save decision' }));
    expect(await screen.findByText('Explain why the profile was rejected.')).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/decision'))).toBe(false);
    await user.type(screen.getByLabelText(/Note to the participant/), 'Profile is private');
    await user.click(screen.getByRole('button', { name: 'Save decision' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Profile is no longer waiting for review');
    expect(await axeViolations(container)).toEqual([]);
  });

  it('blocks verifying your own profile', async () => {
    mockFetch({
      'POST /auth/refresh': reviewerSession(),
      'GET /review/social-accounts/sa1': () => json(200, detail('rev-1')),
    });
    renderWithApp(<SocialAccountPage />, {
      route: '/review/social-verification/sa1',
      path: '/review/social-verification/:accountId',
    });
    expect(await screen.findByText('You can’t verify your own profile')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save decision' })).not.toBeInTheDocument();
  });
});
