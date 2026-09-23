import { useQuery } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { api } from '@/lib/api/client';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { RequireAuth, RequirePermission } from './guards';
import { canOpenPath, defaultLandingPath } from './portals';
import { safeNextPath } from './redirects';

const Protected = () => (
  <RequireAuth>
    <RequirePermission anyOf={['submissions.review']}>
      <p>review queue</p>
    </RequirePermission>
  </RequireAuth>
);

describe('route guards', () => {
  it('sends anonymous users to /login with the original path as next', async () => {
    mockFetch({ 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') });
    const { router } = renderWithApp(<Protected />, {
      route: '/review/queue?page=2',
      path: '/review/*',
      routes: [{ path: '/login', element: <p>login page</p> }],
    });
    expect(await screen.findByText('login page')).toBeInTheDocument();
    expect(new URLSearchParams(router.state.location.search).get('next')).toBe('/review/queue?page=2');
  });

  it('shows a friendly 403 page when the permission is missing', async () => {
    mockFetch({ 'POST /auth/refresh': () => json(200, session(makeUser())) });
    renderWithApp(<Protected />, { route: '/review/queue', path: '/review/*' });
    expect(await screen.findByRole('heading', { name: /don’t have access/i })).toBeInTheDocument();
    expect(screen.queryByText('review queue')).not.toBeInTheDocument();
  });

  it('renders the page when the user has the permission', async () => {
    mockFetch({
      'POST /auth/refresh': () =>
        json(200, session(makeUser({ roles: ['Reviewer'], permissions: ['submissions.review'] }))),
    });
    renderWithApp(<Protected />, { route: '/review/queue', path: '/review/*' });
    expect(await screen.findByText('review queue')).toBeInTheDocument();
  });
});

describe('forced sign-out (AuthProvider + RequireAuth)', () => {
  function ProtectedPage() {
    const query = useQuery({ queryKey: ['home'], queryFn: () => api.get('/me/home') });
    return <p>{query.isSuccess ? 'home loaded' : 'protected page'}</p>;
  }

  it('keeps expired=1 on the guard redirect when the session cannot be renewed (e.g. suspension)', async () => {
    let refreshes = 0;
    mockFetch({
      // Bootstrap succeeds; the renewal after the 401 fails (the account was suspended meanwhile).
      'POST /auth/refresh': () =>
        ++refreshes === 1
          ? json(200, session(makeUser()))
          : problem(401, 'auth.session_revoked', 'Signed out'),
      'GET /me/home': () => problem(401, 'auth.unauthorized', 'Unauthorized'),
    });
    const { router } = renderWithApp(
      <RequireAuth>
        <ProtectedPage />
      </RequireAuth>,
      {
        route: '/app/earnings?page=2',
        path: '/app/*',
        routes: [{ path: '/login', element: <p>login page</p> }],
      },
    );

    expect(await screen.findByText('login page')).toBeInTheDocument();
    const params = new URLSearchParams(router.state.location.search);
    expect(params.get('expired')).toBe('1');
    expect(params.get('next')).toBe('/app/earnings?page=2');
  });

  it('does not mark an ordinary anonymous redirect as expired', async () => {
    mockFetch({ 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') });
    const { router } = renderWithApp(<Protected />, {
      route: '/review/queue',
      path: '/review/*',
      routes: [{ path: '/login', element: <p>login page</p> }],
    });
    expect(await screen.findByText('login page')).toBeInTheDocument();
    expect(new URLSearchParams(router.state.location.search).get('expired')).toBeNull();
  });
});

describe('post-login landing', () => {
  const admin = [
    'users.view',
    'settings.manage',
    'content.manage',
    'audit.view',
    'participant.portal',
    'payouts.view',
  ];

  it('prefers a permitted next path', () => {
    expect(defaultLandingPath(['participant.portal'], '/app/earnings')).toBe('/app/earnings');
  });

  it('ignores next paths the user cannot open', () => {
    expect(defaultLandingPath(['participant.portal'], '/admin/users')).toBe('/app');
  });

  it('uses portal priority', () => {
    expect(defaultLandingPath(admin)).toBe('/admin');
    expect(defaultLandingPath(['payouts.view', 'ledger.view', 'users.view', 'audit.view'])).toBe('/finance');
    // The Finance role also holds agency billing/client permissions; it still lands in /finance.
    expect(defaultLandingPath(['payouts.view', 'ledger.view', 'billing.view', 'billing.manage', 'clients.view'])).toBe('/finance');
    expect(defaultLandingPath(['crm.view', 'billing.view', 'clients.view'])).toBe('/agency');
    expect(defaultLandingPath(['campaigns.manage', 'users.view'])).toBe('/manage');
    expect(defaultLandingPath(['submissions.review', 'users.view'])).toBe('/review');
    expect(defaultLandingPath(['participant.portal'])).toBe('/app');
  });

  it('checks section permissions inside a portal', () => {
    expect(canOpenPath(['users.view'], '/admin/users')).toBe(true);
    expect(canOpenPath(['users.view'], '/admin/settings')).toBe(false);
  });

  it('rejects open-redirect targets', () => {
    expect(safeNextPath('//evil.example')).toBeNull();
    expect(safeNextPath('/\\evil.example')).toBeNull();
    expect(safeNextPath('https://evil.example')).toBeNull();
    expect(safeNextPath('/app/earnings?x=1#top')).toBe('/app/earnings?x=1#top');
  });
});
