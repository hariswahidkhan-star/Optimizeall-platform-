import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { setViewportWidth } from '@/test/viewport';
import { LandingPage } from '@/features/public/LandingPage';
import { PublicLayout } from './layouts/PublicLayout';
import { PortalLayout } from './layouts/PortalLayout';
import { getPortal } from './portals';

describe('LandingPage', () => {
  it('renders the value proposition inside the public layout with no axe violations', async () => {
    mockFetch({ 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') });
    const { container } = renderWithApp(<p>unused</p>, {
      route: '/',
      path: '/unused',
      routes: [
        { path: '/', element: <PublicLayout />, children: [{ index: true, element: <LandingPage /> }] },
      ],
    });
    expect(
      screen.getByRole('heading', { level: 1, name: /Get paid to share brands you believe in/ }),
    ).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /Get started|Create your free account/ })).not.toHaveLength(0);
    expect(screen.getByRole('img', { name: /Discover the world of solution/ })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('PortalLayout', () => {
  const participant = getPortal('participant');

  function renderPortal(user = makeUser({ emailVerified: false })) {
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(user)),
      'POST /auth/resend-verification': () => json(202, { message: 'On its way.' }),
    });
    const result = renderWithApp(<PortalLayout portal={participant} />, {
      route: '/app',
      path: '/app',
    });
    return { ...result, calls };
  }

  it('shows navigation, the unverified-email banner and has no axe violations', async () => {
    const user = userEvent.setup();
    const { container, calls } = renderPortal();
    const nav = await screen.findByRole('navigation', { name: 'Participant navigation' });
    for (const label of [
      'Home',
      'Campaigns',
      'My submissions',
      'Earnings',
      'Payouts',
      'Social accounts',
      'Referrals',
      'Achievements',
      'Notifications',
      'Support',
      'Profile',
    ]) {
      expect(within(nav).getByRole('link', { name: label })).toBeInTheDocument();
    }
    expect(screen.getByText('Verify your email address')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Resend email' }));
    expect(await screen.findByText('Verification email sent')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/auth/resend-verification')!.body).toEqual({
      email: 'ada@example.com',
    });
  });

  it('opens the navigation drawer and shows the bottom tab bar on phones', async () => {
    setViewportWidth(360);
    const user = userEvent.setup();
    renderPortal(makeUser());
    const bottom = await screen.findByRole('navigation', { name: 'Quick navigation' });
    expect(within(bottom).getByRole('link', { name: 'Submissions' })).toBeInTheDocument();
    await user.click(within(bottom).getByRole('button', { name: 'More' }));
    const drawer = screen.getByRole('dialog', { name: 'Participant menu' });
    expect(within(drawer).getByRole('link', { name: 'Referrals' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('offers a portal switcher for multi-portal users', async () => {
    const user = userEvent.setup();
    renderPortal(
      makeUser({
        roles: ['Reviewer', 'Participant'],
        permissions: ['participant.portal', 'submissions.review'],
      }),
    );
    const switcher = await screen.findByRole('button', { name: /Switch portal/ });
    await user.click(switcher);
    const menu = screen.getByRole('menu');
    expect(within(menu).getByRole('menuitem', { name: /Reviewer/ })).toHaveAttribute('href', '/review');
    expect(within(menu).getByRole('menuitem', { name: /Participant/ })).toHaveAttribute(
      'aria-current',
      'true',
    );
  });
});
