import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { ParticipantHome } from '../api/types';
import { authRoutes, makeCard, makeHome, makeSummary, paged } from '../test/fixtures';
import { HomePage } from './HomePage';

function renderHome(home: ParticipantHome, extra: Record<string, () => Response> = {}) {
  const mock = mockFetch({
    ...authRoutes,
    'GET /me/home': () => json(200, home),
    'GET /me/achievements': () => json(200, []),
    'GET /content/announcements': () => json(200, []),
    'GET /campaigns/recommended': () =>
      json(200, [{ campaign: makeCard(), score: 5, reason: 'Matches your interest in fashion' }]),
    'GET /me/earnings/summary': () => json(200, makeSummary()),
    'GET /me/submissions': () => json(200, paged([])),
    ...extra,
  });
  const result = renderWithApp(<HomePage />, { route: '/app', path: '/app' });
  return { ...result, ...mock };
}

describe('HomePage states', () => {
  it('VerifyEmail: shows the verification hero and resends the email', async () => {
    const user = userEvent.setup();
    const { calls } = renderHome(makeHome({ state: 'VerifyEmail' }), {
      'POST /auth/resend-verification': () => json(202, { message: 'On its way.' }),
    });
    expect(
      await screen.findByRole('heading', { name: 'Verify your email to get started' }),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Resend verification email' }));
    expect(await screen.findByText('Verification email sent')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/auth/resend-verification')).toBe(true);
    expect(calls.some((c) => c.path === '/campaigns/recommended')).toBe(false);
  });

  it('AddSocialAccount: explains the minimum account age with a CTA', async () => {
    renderHome(makeHome({ state: 'AddSocialAccount' }));
    expect(
      await screen.findByRole('heading', { name: 'Add the social profile you post from' }),
    ).toBeInTheDocument();
    expect(screen.getByText(/90 days old/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Add a social profile/ })).toHaveAttribute(
      'href',
      '/app/social-accounts?add=1',
    );
  });

  it('AwaitingEligibility: counts down to eligibleFrom', async () => {
    const eligibleFrom = new Date(Date.now() + (3 * 24 + 5) * 3600_000 + 30 * 60_000).toISOString();
    renderHome(makeHome({ state: 'AwaitingEligibility', eligibleFrom }));
    expect(await screen.findByRole('heading', { name: 'Your profile qualifies soon' })).toBeInTheDocument();
    const countdown = screen.getByRole('list', { name: 'Time until your profile qualifies' });
    expect(countdown).toHaveTextContent('3 days');
    expect(countdown).toHaveTextContent('5 hours');
  });

  it('Ready: puts recommendations front and centre with their reason', async () => {
    renderHome(makeHome({ state: 'Ready' }));
    expect(await screen.findByRole('heading', { name: /pick your first campaign/ })).toBeInTheDocument();
    expect(await screen.findByText('Matches your interest in fashion')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Your earnings' })).not.toBeInTheDocument();
  });

  it('Active: shows earnings, next payout and corrections first', async () => {
    const needs = {
      id: 'n1',
      campaign: { id: 'c1', slug: 'x', title: 'Needs fixing' },
      platform: 'Instagram',
      postUrl: 'https://instagram.com/p/1',
      status: 'NeedsCorrection',
      submittedAt: '2026-09-20T00:00:00Z',
      estimatedReward: 5,
      currency: 'USD',
      decisionReason: 'Add #ad',
    };
    const recent = {
      ...needs,
      id: 'r1',
      status: 'Approved',
      campaign: { id: 'c2', slug: 'y', title: 'Recent one' },
    };
    const { container } = renderHome(makeHome({ state: 'Active', unreadNotificationCount: 3 }), {
      'GET /me/submissions': () => json(200, paged([needs, recent])),
    });
    expect(await screen.findByRole('heading', { name: 'Your earnings' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Next payout' })).toBeInTheDocument();
    expect(await screen.findByText('Needs fixing')).toBeInTheDocument();
    expect(screen.getByText('Add #ad')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '3 unread notifications' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Mark as done' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});
