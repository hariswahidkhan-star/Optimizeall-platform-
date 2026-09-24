import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { LoginPage } from '../LoginPage';
import { RegisterPage } from '../RegisterPage';
import { redirectToGoogle } from './redirect';

vi.mock('./redirect', () => ({ redirectToGoogle: vi.fn() }));

const anonymous = { 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') };
const enabled = { 'GET /auth/providers': () => json(200, { google: { enabled: true } }) };
const googleUrl = 'https://accounts.google.com/o/oauth2/v2/auth?client_id=abc&state=xyz';

describe('Continue with Google', () => {
  beforeEach(() => {
    vi.mocked(redirectToGoogle).mockReset();
    sessionStorage.clear();
  });

  it('is hidden when the API reports Google sign-in as not configured', async () => {
    const { calls } = mockFetch({
      ...anonymous,
      'GET /auth/providers': () => json(200, { google: { enabled: false } }),
    });
    renderWithApp(<LoginPage />, { route: '/login', path: '/login' });
    await waitFor(() => expect(calls.some((c) => c.path === '/auth/providers')).toBe(true));
    await screen.findByRole('button', { name: 'Sign in' });
    expect(screen.queryByRole('button', { name: 'Continue with Google' })).not.toBeInTheDocument();
  });

  it('is hidden when the provider lookup fails', async () => {
    const { calls } = mockFetch({
      ...anonymous,
      'GET /auth/providers': () => problem(500, 'server', 'Boom'),
    });
    renderWithApp(<LoginPage />, { route: '/login', path: '/login' });
    await waitFor(() => expect(calls.some((c) => c.path === '/auth/providers')).toBe(true));
    expect(screen.queryByRole('button', { name: 'Continue with Google' })).not.toBeInTheDocument();
  });

  it('starts the flow on the login page with the next path and goes to Google', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...anonymous,
      ...enabled,
      'POST /auth/google/start': () => json(200, { authorizationUrl: googleUrl }),
    });
    const { container } = renderWithApp(<LoginPage />, {
      route: '/login?next=%2Fapp%2Fearnings',
      path: '/login',
    });

    const button = await screen.findByRole('button', { name: 'Continue with Google' });
    expect(button.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
    expect(await axeViolations(container)).toEqual([]);

    await user.click(button);
    await waitFor(() => expect(redirectToGoogle).toHaveBeenCalledWith(googleUrl));
    expect(calls.find((c) => c.path === '/auth/google/start')?.body).toEqual({ returnTo: '/app/earnings' });
  });

  it('shows why the flow could not start', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...anonymous,
      ...enabled,
      'POST /auth/google/start': () =>
        problem(404, 'auth.google_disabled', 'Sign-in with Google isn’t available.'),
    });
    renderWithApp(<LoginPage />, { route: '/login', path: '/login' });
    await user.click(await screen.findByRole('button', { name: 'Continue with Google' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Sign-in with Google isn’t available.');
    expect(redirectToGoogle).not.toHaveBeenCalled();
  });

  it('is offered on the registration page and keeps the referral code for the terms step', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...anonymous,
      ...enabled,
      'POST /auth/google/start': () => json(200, { authorizationUrl: googleUrl }),
    });
    renderWithApp(<RegisterPage />, { route: '/register?ref=FRIEND42', path: '/register' });
    await user.click(await screen.findByRole('button', { name: 'Continue with Google' }));
    await waitFor(() => expect(redirectToGoogle).toHaveBeenCalledWith(googleUrl));
    expect(JSON.parse(sessionStorage.getItem('oa.google.signupCodes') ?? '{}')).toEqual({
      referralCode: 'FRIEND42',
    });
  });
});
