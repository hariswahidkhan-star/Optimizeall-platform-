import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { authRoutes, makeSocial } from '../test/fixtures';
import { SocialAccountsPage } from './SocialAccountsPage';

const list = (items = [makeSocial()]) => ({
  minAccountAgeDays: 90,
  minFollowers: 0,
  maxActiveAccounts: 10,
  items,
});

function renderPage(extra: Record<string, () => Response> = {}, route = '/app/social-accounts') {
  const mock = mockFetch({ ...authRoutes, 'GET /me/social-accounts': () => json(200, list()), ...extra });
  return { ...renderWithApp(<SocialAccountsPage />, { route, path: '/app/social-accounts' }), ...mock };
}

async function fillAdd(user: ReturnType<typeof userEvent.setup>) {
  const dialog = await screen.findByRole('dialog', { name: 'Add a social profile' });
  await user.selectOptions(within(dialog).getByLabelText('Platform'), 'Instagram');
  await user.type(within(dialog).getByLabelText('Handle'), 'ada.new');
  await user.type(within(dialog).getByLabelText('Profile link'), 'https://www.instagram.com/ada.new');
  await user.type(within(dialog).getByLabelText('Account created on'), '2024-02-01');
  await user.type(within(dialog).getByLabelText('Followers'), '1200');
  return dialog;
}

describe('SocialAccountsPage', () => {
  it('shows qualification, verification, limits and no axe violations', async () => {
    const tooNew = makeSocial({
      id: 'sa2',
      handle: 'fresh',
      qualifies: false,
      verificationStatus: 'Unverified',
      eligibleFrom: new Date(Date.now() + 10 * 86_400_000).toISOString(),
      reasons: [{ code: 'social.account_too_new', message: 'Profiles must be at least 90 days old.' }],
    });
    const { container } = renderPage({
      'GET /me/social-accounts': () => json(200, list([makeSocial(), tooNew])),
    });
    expect(await screen.findByText('Profiles must be at least 90 days old.')).toBeInTheDocument();
    expect(screen.getByText(/10 days/)).toBeInTheDocument();
    expect(screen.getByText(/up to/)).toHaveTextContent('10 active profiles');
    expect(screen.getByRole('button', { name: 'Request verification' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('validates client-side and maps server field errors', async () => {
    const user = userEvent.setup();
    const { calls } = renderPage(
      {
        'POST /me/social-accounts': () =>
          problem(400, 'social.invalid', 'Some profile details are invalid.', {
            errors: { profileUrl: ['Use an instagram.com profile link.'] },
          }),
      },
      '/app/social-accounts?add=1',
    );
    const dialog = await screen.findByRole('dialog', { name: 'Add a social profile' });
    await user.click(screen.getByRole('button', { name: 'Add profile' }));
    expect(await within(dialog).findByText('Choose the platform.')).toBeInTheDocument();
    expect(within(dialog).getByText('Enter your handle.')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/me/social-accounts' && c.method === 'POST')).toBe(false);

    await fillAdd(user);
    await user.click(screen.getByRole('button', { name: 'Add profile' }));
    const url = within(dialog).getByLabelText('Profile link');
    await waitFor(() => expect(url).toHaveAttribute('aria-invalid', 'true'));
    expect(url).toHaveAccessibleDescription(expect.stringContaining('Use an instagram.com profile link.'));
    expect(within(dialog).getByLabelText('Handle')).toHaveValue('ada.new');
    const body = calls.find((c) => c.path === '/me/social-accounts' && c.method === 'POST')!.body as Record<
      string,
      unknown
    >;
    expect(body).toMatchObject({
      platform: 'Instagram',
      handle: 'ada.new',
      accountCreatedAt: '2024-02-01T00:00:00Z',
      followerCount: 1200,
    });
  });

  it('routes already-registered to the handle field', async () => {
    const user = userEvent.setup();
    renderPage(
      {
        'POST /me/social-accounts': () =>
          problem(409, 'social.already_registered', 'This profile is already registered to another account.'),
      },
      '/app/social-accounts?add=1',
    );
    const dialog = await fillAdd(user);
    await user.click(screen.getByRole('button', { name: 'Add profile' }));
    const handle = within(dialog).getByLabelText('Handle');
    await waitFor(() =>
      expect(handle).toHaveAccessibleDescription(expect.stringContaining('already registered')),
    );
  });

  it('shows the verification-reset message after an edit', async () => {
    const user = userEvent.setup();
    renderPage({
      'PUT /me/social-accounts/sa1': () =>
        json(200, {
          account: makeSocial({ followerCount: 3000, verificationStatus: 'Unverified' }),
          verificationReset: true,
          message: 'Profile updated. It needs to be verified again.',
        }),
    });
    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    const dialog = await screen.findByRole('dialog', { name: 'Edit @ada' });
    expect(within(dialog).getByText(/sends this profile back for verification/)).toBeInTheDocument();
    const followers = within(dialog).getByLabelText('Followers');
    await user.clear(followers);
    await user.type(followers, '3000');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findAllByText('Profile updated. It needs to be verified again.')).not.toHaveLength(0);
  });
});
