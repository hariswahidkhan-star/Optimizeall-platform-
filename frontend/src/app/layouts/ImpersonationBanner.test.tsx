import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { tokenStore } from '@/lib/api/client';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { ImpersonationBanner } from './ImpersonationBanner';

const admin = makeUser({
  id: 'admin-1',
  displayName: 'Platform Admin',
  roles: ['Admin'],
  permissions: ['users.view', 'users.impersonate'],
});

const jane = makeUser({
  id: 'u-42',
  displayName: 'Jane Doe',
  isTestAccount: true,
  impersonatedBy: {
    id: 'admin-1',
    displayName: 'Platform Admin',
    email: 'admin@optimizeall.local',
    startedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 60 * 60_000).toISOString(),
  },
});

describe('ImpersonationBanner', () => {
  it('names the impersonated user, their role and the TEST label, and is accessible', async () => {
    mockFetch({ 'POST /auth/refresh': () => json(200, session(jane, 'jane-token')) });
    const { container } = renderWithApp(<ImpersonationBanner />);
    const banner = await screen.findByRole('region', { name: 'Impersonation' });
    expect(banner).toHaveTextContent(/You are viewing as Jane Doe \(participant\)/);
    expect(within(banner).getByText('TEST')).toBeInTheDocument();
    expect(banner).toHaveTextContent(/signed in as Platform Admin/);
    expect(within(banner).getByRole('button', { name: 'Exit' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('renders nothing for an ordinary session', async () => {
    mockFetch({ 'POST /auth/refresh': () => json(200, session(admin)) });
    renderWithApp(
      <>
        <p>page</p>
        <ImpersonationBanner />
      </>,
    );
    await screen.findByText('page');
    await waitFor(() => expect(tokenStore.get()).toBe('access-1'));
    expect(screen.queryByRole('region', { name: 'Impersonation' })).not.toBeInTheDocument();
  });

  it('Exit ends the impersonation and returns to the admin users page as the admin', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(jane, 'jane-token')),
      'POST /auth/impersonation/exit': () => json(200, session(admin, 'admin-token')),
    });
    const { router } = renderWithApp(<ImpersonationBanner />, {
      route: '/app',
      path: '/app',
      routes: [{ path: '/admin/users', element: <p>admin users page</p> }],
    });
    await user.click(await screen.findByRole('button', { name: 'Exit' }));
    expect(await screen.findByText('admin users page')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/admin/users');
    expect(tokenStore.get()).toBe('admin-token');
    const exit = calls.find((c) => c.path === '/auth/impersonation/exit');
    expect(exit?.headers.Authorization).toBe('Bearer jane-token');
    expect(screen.queryByRole('region', { name: 'Impersonation' })).not.toBeInTheDocument();
  });
});
