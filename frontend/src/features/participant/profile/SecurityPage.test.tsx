import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { SecurityPage } from './SecurityPage';

const signedIn = { 'POST /auth/refresh': () => json(200, session(makeUser({ email: 'amina@gmail.com' }))) };

function renderPage() {
  return renderWithApp(<SecurityPage />, {
    route: '/app/profile/security',
    path: '/app/profile/security',
  });
}

describe('SecurityPage', () => {
  it('offers a Google-only account a link to set a first password instead of "change password"', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...signedIn,
      'GET /auth/external-logins': () =>
        json(200, {
          hasPassword: false,
          googleEnabled: true,
          externalLogins: [
            {
              provider: 'google',
              email: 'amina@gmail.com',
              createdAt: '2026-09-01T10:00:00Z',
              lastUsedAt: null,
            },
          ],
        }),
      'POST /auth/forgot-password': () => json(202, { message: 'sent' }),
    });
    const { container } = renderPage();

    const send = await screen.findByRole('button', { name: 'Email me a link to set a password' });
    // Changing a password needs the current one, which this account doesn't have.
    expect(screen.queryByLabelText(/Current password/)).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    await user.click(send);
    expect(await screen.findByText('Check your email')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/auth/forgot-password')?.body).toEqual({ email: 'amina@gmail.com' });
  });

  it('keeps the change-password form for accounts with a password', async () => {
    const { calls } = mockFetch({
      ...signedIn,
      'GET /auth/external-logins': () =>
        json(200, { hasPassword: true, googleEnabled: false, externalLogins: [] }),
    });
    renderPage();
    expect(await screen.findByLabelText(/Current password/)).toBeInTheDocument();
    await waitFor(() => expect(calls.some((c) => c.path === '/auth/external-logins')).toBe(true));
    expect(screen.getByLabelText(/Current password/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /set a password/ })).not.toBeInTheDocument();
  });
});
