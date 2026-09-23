import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { LoginPage } from './LoginPage';

const anonymous = { 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') };

function renderLogin(route = '/login') {
  return renderWithApp(<LoginPage />, {
    route,
    path: '/login',
    routes: [
      { path: '/app/*', element: <p>participant portal</p> },
      { path: '/review/*', element: <p>review portal</p> },
    ],
  });
}

async function signIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Email'), 'ada@example.com');
  await user.type(screen.getByLabelText('Password'), 'secret-password');
  await user.click(screen.getByRole('button', { name: 'Sign in' }));
}

describe('LoginPage', () => {
  it('shows the expired-session notice', () => {
    mockFetch(anonymous);
    renderLogin('/login?expired=1');
    expect(screen.getByText('Your session has expired')).toBeInTheDocument();
  });

  it('signs in and goes to the permitted next path', async () => {
    const user = userEvent.setup();
    mockFetch({ ...anonymous, 'POST /auth/login': () => json(200, session(makeUser())) });
    const { router } = renderLogin('/login?next=%2Fapp%2Fearnings');
    await signIn(user);
    expect(await screen.findByText('participant portal')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/app/earnings');
  });

  it('ignores a next path the user cannot open and lands on their portal', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...anonymous,
      'POST /auth/login': () =>
        json(
          200,
          session(makeUser({ roles: ['Reviewer'], permissions: ['submissions.review', 'users.view'] })),
        ),
    });
    const { router } = renderLogin('/login?next=%2Fadmin%2Fsettings');
    await signIn(user);
    expect(await screen.findByText('review portal')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/review');
  });

  it('explains invalid credentials, lockout and suspension', async () => {
    const user = userEvent.setup();
    let response = problem(401, 'auth.invalid_credentials', 'The email or password is incorrect.');
    mockFetch({ ...anonymous, 'POST /auth/login': () => response });
    renderLogin();

    await signIn(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('The email or password is incorrect.');
    expect(screen.getByLabelText('Password')).toHaveValue('');

    response = problem(
      429,
      'auth.locked_out',
      'Too many failed sign-in attempts. Try again in a few minutes or reset your password.',
    );
    await user.type(screen.getByLabelText('Password'), 'again-password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('Too many sign-in attempts')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Reset your password' })).toHaveAttribute(
      'href',
      '/forgot-password',
    );

    response = problem(403, 'account.suspended', 'Your account is suspended.');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('This account can’t sign in')).toBeInTheDocument();
  });

  it('has no axe violations', async () => {
    mockFetch(anonymous);
    const { container } = renderLogin('/login?expired=1');
    expect(await axeViolations(container)).toEqual([]);
  });
});
