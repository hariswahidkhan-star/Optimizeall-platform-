import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { tokenStore } from '@/lib/api/client';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { BANNER_OFFSET_VAR, ImpersonationBanner } from './ImpersonationBanner';

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

  it('stays visible: publishes its height for the sticky headers below it and clears it on exit', async () => {
    const user = userEvent.setup();
    mockFetch({
      'POST /auth/refresh': () => json(200, session(jane, 'jane-token')),
      'POST /auth/impersonation/exit': () => json(200, session(admin, 'admin-token')),
    });
    renderWithApp(<ImpersonationBanner />, {
      route: '/app',
      path: '/app',
      routes: [{ path: '/admin/users', element: <p>admin users page</p> }],
    });
    const banner = await screen.findByRole('region', { name: 'Impersonation' });
    expect(document.documentElement.style.getPropertyValue(BANNER_OFFSET_VAR)).toMatch(/px$/);
    // Screen readers hear it when it appears, and the Exit button is described by the notice.
    const status = within(banner).getByRole('status');
    expect(status).toHaveTextContent(/You are viewing as Jane Doe/);
    expect(within(banner).getByRole('button', { name: 'Exit' })).toHaveAttribute('aria-describedby', status.id);

    await user.click(within(banner).getByRole('button', { name: 'Exit' }));
    await screen.findByText('admin users page');
    expect(document.documentElement.style.getPropertyValue(BANNER_OFFSET_VAR)).toBe('');
  });

  it('names a custom role when the user has no built-in staff role', async () => {
    const lead = makeUser({ ...jane, roles: ['Participant'], customRoles: ['Support lead'], isTestAccount: false });
    mockFetch({ 'POST /auth/refresh': () => json(200, session(lead, 'lead-token')) });
    renderWithApp(<ImpersonationBanner />);
    const banner = await screen.findByRole('region', { name: 'Impersonation' });
    expect(banner).toHaveTextContent(/You are viewing as Jane Doe \(Support lead\)/);
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
