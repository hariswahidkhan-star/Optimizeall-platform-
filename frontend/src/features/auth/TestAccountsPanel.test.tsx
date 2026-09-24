import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { LoginPage } from './LoginPage';

const accounts = [
  {
    id: 't-1',
    email: 'test+qa-abc123@test.optimizeall.app',
    displayName: 'QA Finance',
    roles: ['Finance'],
    isTestAccount: true,
  },
  {
    id: 'd-1',
    email: 'sara.participant@demo.optimizeall.app',
    displayName: 'Sara Participant',
    roles: ['Participant'],
    isTestAccount: false,
  },
];

describe('Login page — test accounts (non-production)', () => {
  it('lists test and demo accounts and signs in with one click', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired'),
      'GET /dev/test-accounts': () => json(200, accounts),
      'POST /dev/test-login': () =>
        json(
          200,
          session(
            makeUser({
              id: 't-1',
              displayName: 'QA Finance',
              isTestAccount: true,
              permissions: ['participant.portal'],
            }),
          ),
        ),
    });
    const { router } = renderWithApp(<LoginPage />, {
      route: '/login',
      path: '/login',
      routes: [{ path: '/app/*', element: <p>participant home</p> }],
    });

    const panel = await screen.findByRole('region', { name: /Test accounts/ });
    expect(within(panel).getByRole('heading', { name: 'Test accounts', level: 3 })).toBeInTheDocument();
    expect(within(panel).getByRole('heading', { name: 'Demo accounts', level: 3 })).toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: /Sign in as QA Finance \(Finance\)/ }));

    await waitFor(() =>
      expect(calls.find((c) => c.path === '/dev/test-login')?.body).toEqual({ userId: 't-1' }),
    );
    expect(await screen.findByText('participant home')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/app');
  });

  it('shows nothing when the API does not offer test login (production)', async () => {
    const { calls } = mockFetch({
      'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired'),
      'GET /dev/test-accounts': () => problem(404, 'http_404', 'Not found'),
    });
    renderWithApp(<LoginPage />, { route: '/login', path: '/login' });
    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument();
    await waitFor(() => expect(calls.some((c) => c.path === '/dev/test-accounts')).toBe(true));
    expect(screen.queryByRole('region', { name: /Test accounts/ })).not.toBeInTheDocument();
  });
});
