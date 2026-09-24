import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { RegisterPage } from './RegisterPage';

const anonymous = { 'POST /auth/refresh': () => problem(401, 'auth.session_expired', 'Expired') };

async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Email'), 'grace@example.com');
  await user.type(screen.getByLabelText('Password'), 'Correct-Horse-42');
  await user.type(screen.getByLabelText('Display name'), 'Grace Hopper');
  await user.selectOptions(screen.getByLabelText('Country'), 'GB');
  await user.click(screen.getByRole('checkbox', { name: /accept the participant rules/i }));
}

function renderRegister(route = '/register') {
  return renderWithApp(<RegisterPage />, {
    route,
    path: '/register',
    routes: [{ path: '/check-email', element: <p>check email page</p> }],
  });
}

describe('RegisterPage', () => {
  it('validates on submit, focuses the first invalid field and shows password policy', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch(anonymous);
    renderRegister();

    await user.type(screen.getByLabelText('Email'), 'not-an-email');
    await user.type(screen.getByLabelText('Password'), 'password123');
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    const email = screen.getByLabelText('Email');
    expect(email).toHaveAttribute('aria-invalid', 'true');
    expect(email).toHaveAccessibleDescription(/valid email address/);
    expect(email).toHaveFocus();
    expect(screen.getByLabelText('Password')).toHaveAccessibleDescription(/too common/);
    expect(screen.getByText(/need to accept the participant rules/)).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/auth/register')).toBe(false);
  });

  it('submits the full contract including referral/invite codes and device id', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      ...anonymous,
      'POST /auth/register': () => json(202, { message: 'Check your inbox.' }),
    });
    renderRegister('/register?ref=FRIEND42&invite=VIP-2026');
    expect(screen.getByText('You were invited')).toBeInTheDocument();

    await fillValid(user);
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByText('check email page')).toBeInTheDocument();
    const body = calls.find((c) => c.path === '/auth/register')!.body as Record<string, unknown>;
    expect(body).toMatchObject({
      email: 'grace@example.com',
      password: 'Correct-Horse-42',
      displayName: 'Grace Hopper',
      countryCode: 'GB',
      acceptTerms: true,
      marketingEmailOptIn: false,
      referralCode: 'FRIEND42',
      inviteCode: 'VIP-2026',
    });
    expect(typeof body.deviceId).toBe('string');
    expect(typeof body.timeZone).toBe('string');
    expect(typeof body.languageCode).toBe('string');
  });

  it('maps server field errors onto the fields', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...anonymous,
      'POST /auth/register': () =>
        problem(400, 'auth.weak_password', 'Choose a stronger password.', {
          errors: { password: ['Don’t include your email address in your password.'] },
        }),
    });
    renderRegister();
    await fillValid(user);
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    const password = screen.getByLabelText('Password');
    await waitFor(() => expect(password).toHaveAttribute('aria-invalid', 'true'));
    expect(password).toHaveAccessibleDescription(/Don’t include your email address/);
    expect(password).toHaveFocus();
  });

  it('maps ASP.NET validation keys and the terms code', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...anonymous,
      'POST /auth/register': () =>
        json(400, {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { CountryCode: ['Use a two-letter country code.'] },
        }),
    });
    renderRegister();
    await fillValid(user);
    await user.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText('Use a two-letter country code.')).toBeInTheDocument();
    expect(screen.getByLabelText('Country')).toHaveAttribute('aria-invalid', 'true');
  });

  it('shows non-field errors in an alert', async () => {
    const user = userEvent.setup();
    mockFetch({
      ...anonymous,
      'POST /auth/register': () =>
        problem(429, 'rate_limited', 'Too many requests. Please wait a moment and try again.'),
    });
    renderRegister();
    await fillValid(user);
    await user.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Too many requests');
  });

  it('has no axe violations', async () => {
    mockFetch(anonymous);
    const { container } = renderRegister('/register?ref=FRIEND42');
    expect(await axeViolations(container)).toEqual([]);
    // axe walks the whole form (country and time-zone lists): ~9 s alone, far longer on a loaded runner.
  }, 120_000);
});
