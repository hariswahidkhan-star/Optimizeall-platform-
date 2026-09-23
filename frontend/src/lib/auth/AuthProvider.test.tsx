import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { api, tokenStore } from '@/lib/api/client';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { useAuth } from './useAuth';

function Probe() {
  const { status, user, hasPermission, logout } = useAuth();
  return (
    <div>
      <p>status:{status}</p>
      <p>user:{user?.displayName ?? 'none'}</p>
      <p>can-review:{String(hasPermission('submissions.review'))}</p>
      <button onClick={() => void api.get('/me/home').catch(() => undefined)}>load</button>
      <button onClick={() => void logout()}>sign out</button>
    </div>
  );
}

describe('AuthProvider', () => {
  it('restores the session from the refresh cookie on load', async () => {
    const { calls } = mockFetch({ 'POST /auth/refresh': () => json(200, session(makeUser())) });
    renderWithApp(<Probe />);

    expect(screen.getByText('status:loading')).toBeInTheDocument();
    expect(await screen.findByText('status:authenticated')).toBeInTheDocument();
    expect(screen.getByText('user:Ada Lovelace')).toBeInTheDocument();
    expect(screen.getByText('can-review:false')).toBeInTheDocument();
    expect(tokenStore.get()).toBe('access-1');
    expect(calls.filter((c) => c.path === '/auth/refresh')).toHaveLength(1);
  });

  it('becomes anonymous when there is no session', async () => {
    mockFetch({ 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') });
    renderWithApp(<Probe />);
    expect(await screen.findByText('status:anonymous')).toBeInTheDocument();
    expect(tokenStore.get()).toBeNull();
  });

  it('redirects to /login with expired=1 and next when the session cannot be renewed', async () => {
    let refreshes = 0;
    mockFetch({
      'POST /auth/refresh': () => {
        refreshes += 1;
        return refreshes === 1 ? json(200, session()) : problem(401, 'auth.session_expired', 'Expired');
      },
      'GET /me/home': () => problem(401, 'auth.unauthorized', 'No'),
    });
    const { router } = renderWithApp(<Probe />, {
      route: '/app/earnings?tab=paid',
      routes: [{ path: '/login', element: <p>login page</p> }],
    });
    await screen.findByText('status:authenticated');

    await userEvent.click(screen.getByRole('button', { name: 'load' }));

    expect(await screen.findByText('login page')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');
    const params = new URLSearchParams(router.state.location.search);
    expect(params.get('expired')).toBe('1');
    expect(params.get('next')).toBe('/app/earnings?tab=paid');
  });

  it('signs out through the API and clears the token', async () => {
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session()),
      'POST /auth/logout': () => new Response(null, { status: 204 }),
    });
    const { router } = renderWithApp(<Probe />, { routes: [{ path: '/login', element: <p>login page</p> }] });
    await screen.findByText('status:authenticated');

    await userEvent.click(screen.getByRole('button', { name: 'sign out' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'));
    expect(calls.some((c) => c.path === '/auth/logout')).toBe(true);
    expect(tokenStore.get()).toBeNull();
  });
});
