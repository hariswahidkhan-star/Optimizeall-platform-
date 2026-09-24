import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { GoogleCallbackPage } from './GoogleCallbackPage';

const anonymous = { 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') };
const callbackRoute = '/auth/google/callback?code=4%2Fabc&state=signed-state';

function renderCallback(route = callbackRoute) {
  return renderWithApp(<GoogleCallbackPage />, {
    route,
    path: '/auth/google/callback',
    routes: [
      { path: '/app/*', element: <p>participant portal</p> },
      { path: '/login', element: <p>login page</p> },
    ],
  });
}

const callbackResponse = (overrides: Record<string, unknown>) => ({
  status: 'signedIn',
  auth: null,
  ticket: null,
  email: null,
  displayName: null,
  returnTo: null,
  ...overrides,
});

describe('GoogleCallbackPage', () => {
  beforeEach(() => sessionStorage.clear());

  it('posts the code and state once and lands where the sign-in started', async () => {
    const { calls } = mockFetch({
      ...anonymous,
      'POST /auth/google/callback': () =>
        json(200, callbackResponse({ auth: session(makeUser()), returnTo: '/app/earnings' })),
    });
    const { router } = renderCallback();
    expect(await screen.findByText('participant portal')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/app/earnings');
    const posts = calls.filter((c) => c.path === '/auth/google/callback');
    expect(posts).toHaveLength(1);
    expect(posts[0]!.body).toEqual({ code: '4/abc', state: 'signed-state' });
  });

  it('ignores an unsafe return path', async () => {
    mockFetch({
      ...anonymous,
      'POST /auth/google/callback': () =>
        json(200, callbackResponse({ auth: session(makeUser()), returnTo: '//evil.example.com' })),
    });
    const { router } = renderCallback();
    expect(await screen.findByText('participant portal')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/app');
  });

  it('asks a new user to accept the terms before creating the account', async () => {
    const user = userEvent.setup();
    sessionStorage.setItem('oa.google.signupCodes', JSON.stringify({ referralCode: 'FRIEND42' }));
    const { calls } = mockFetch({
      ...anonymous,
      'POST /auth/google/callback': () =>
        json(
          200,
          callbackResponse({
            status: 'needsTerms',
            ticket: 'signed-ticket',
            email: 'amina@gmail.com',
            displayName: 'Amina Siddiqui',
          }),
        ),
      'POST /auth/google/complete': () => json(200, session(makeUser({ email: 'amina@gmail.com' }))),
    });
    const { container, router } = renderCallback();

    expect(await screen.findByRole('heading', { name: 'Finish creating your account' })).toBeInTheDocument();
    expect(screen.getByText('amina@gmail.com')).toBeInTheDocument();
    expect(screen.getByLabelText(/Display name/)).toHaveValue('Amina Siddiqui');
    expect(await axeViolations(container)).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Create account' }));
    expect(
      await screen.findByText('You need to accept the participant rules to create an account.'),
    ).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/auth/google/complete')).toBe(false);

    await user.click(screen.getByRole('checkbox', { name: /I accept the participant rules/ }));
    await user.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText('participant portal')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/app');

    const complete = calls.find((c) => c.path === '/auth/google/complete')!.body as Record<string, unknown>;
    expect(complete).toMatchObject({
      ticket: 'signed-ticket',
      acceptTerms: true,
      displayName: 'Amina Siddiqui',
      referralCode: 'FRIEND42',
    });
    expect(complete.countryCode).toMatch(/^[A-Z]{2}$/);
    expect(sessionStorage.getItem('oa.google.signupCodes')).toBeNull();
  });

  it('explains a refused sign-in', async () => {
    mockFetch({
      ...anonymous,
      'POST /auth/google/callback': () =>
        problem(
          409,
          'auth.google_link_unverified',
          'An account with this email already exists, but its email address hasn’t been verified.',
        ),
    });
    renderCallback();
    expect(await screen.findByRole('alert')).toHaveTextContent('its email address hasn’t been verified');
    expect(screen.getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', '/login');
  });

  it('does not call the API when the user cancelled at Google', async () => {
    const { calls } = mockFetch(anonymous);
    renderCallback('/auth/google/callback?error=access_denied&state=signed-state');
    expect(await screen.findByRole('alert')).toHaveTextContent('You cancelled signing in with Google');
    await waitFor(() => expect(calls.some((c) => c.path === '/auth/refresh')).toBe(true));
    expect(calls.some((c) => c.path === '/auth/google/callback')).toBe(false);
  });

  it('returns to the profile after linking', async () => {
    mockFetch({
      'POST /auth/refresh': () => json(200, session(makeUser())),
      'POST /auth/google/callback': () =>
        json(200, callbackResponse({ status: 'linked', returnTo: '/app/profile/security' })),
    });
    const { router } = renderCallback();
    await waitFor(() => expect(router.state.location.pathname).toBe('/app/profile/security'));
  });
});
