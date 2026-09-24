import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { GoogleConnectionCard } from './GoogleConnectionCard';
import { redirectToGoogle } from './redirect';

vi.mock('./redirect', () => ({ redirectToGoogle: vi.fn() }));

const signedIn = { 'POST /auth/refresh': () => json(200, session(makeUser())) };
const linked = {
  provider: 'google',
  email: 'ada@gmail.com',
  createdAt: '2026-09-01T10:00:00Z',
  lastUsedAt: null,
};

function renderCard() {
  return renderWithApp(<GoogleConnectionCard />, {
    route: '/app/profile/security',
    path: '/app/profile/security',
  });
}

describe('GoogleConnectionCard', () => {
  beforeEach(() => vi.mocked(redirectToGoogle).mockReset());

  it('connects Google from the profile and comes back to it', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /auth/external-logins': () =>
        json(200, { hasPassword: true, googleEnabled: true, externalLogins: [] }),
      'POST /auth/external-logins/google/start': () =>
        json(200, { authorizationUrl: 'https://accounts.google.com/o/oauth2/v2/auth?state=s' }),
    });
    renderCard();
    await user.click(await screen.findByRole('button', { name: 'Continue with Google' }));
    await waitFor(() => expect(redirectToGoogle).toHaveBeenCalled());
    expect(calls.find((c) => c.path === '/auth/external-logins/google/start')?.body).toEqual({
      returnTo: '/app/profile/security',
    });
  });

  it('renders nothing when Google is not configured and not connected', async () => {
    const { calls } = mockFetch({
      ...signedIn,
      'GET /auth/external-logins': () =>
        json(200, { hasPassword: true, googleEnabled: false, externalLogins: [] }),
    });
    const { container } = renderCard();
    await waitFor(() => expect(calls.some((c) => c.path === '/auth/external-logins')).toBe(true));
    expect(container).toBeEmptyDOMElement();
  });

  it('disconnects Google when the account has a password', async () => {
    const user = userEvent.setup();
    let logins = [linked];
    const { calls } = mockFetch({
      ...signedIn,
      'GET /auth/external-logins': () =>
        json(200, { hasPassword: true, googleEnabled: true, externalLogins: logins }),
      'DELETE /auth/external-logins/google': () => {
        logins = [];
        return new Response(null, { status: 204 });
      },
    });
    renderCard();
    expect(await screen.findByText('ada@gmail.com')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Disconnect Google' }));
    expect(await screen.findByRole('button', { name: 'Continue with Google' })).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'DELETE')).toBe(true);
  });

  it('does not offer to disconnect the only sign-in method', async () => {
    mockFetch({
      ...signedIn,
      'GET /auth/external-logins': () =>
        json(200, { hasPassword: false, googleEnabled: true, externalLogins: [linked] }),
    });
    renderCard();
    const button = await screen.findByRole('button', { name: 'Disconnect Google' });
    expect(button).toBeDisabled();
    expect(button).toHaveAccessibleDescription(/Google is currently your only way to sign in/);
  });
});
