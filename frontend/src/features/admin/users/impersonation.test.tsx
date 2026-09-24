import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { makeUser, problem, session } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import type { AdminUserDetail, AdminUserListItem } from '../api/types';
import { ADMIN_PERMISSIONS, json, mockAdminApi } from '../test/helpers';
import { UserDetailPage } from './UserDetailPage';
import { UsersPage } from './UsersPage';

const IMPERSONATOR_PERMISSIONS = [...ADMIN_PERMISSIONS, 'users.impersonate'];

function detail(
  overrides: Partial<AdminUserDetail['profile']> = {},
  roles = ['Participant'],
): AdminUserDetail {
  return {
    profile: {
      id: 'u-42',
      email: 'jane@example.com',
      displayName: 'Jane Doe',
      countryCode: 'PK',
      languageCode: 'en',
      timeZone: 'UTC',
      interests: [],
      status: 'Active',
      statusReason: null,
      statusChangedAt: null,
      tier: 'Standard',
      referralCode: 'JANE01',
      emailVerified: true,
      emailVerifiedAt: '2026-09-01T00:00:00Z',
      marketingEmailOptIn: false,
      whatsAppOptIn: false,
      whatsAppNumberHint: null,
      lastLoginAt: null,
      lastActiveAt: null,
      createdAt: '2026-08-01T00:00:00Z',
      ...overrides,
    },
    roles,
    statusHistory: [],
    socialAccounts: [],
    submissionCounts: {
      total: 0,
      pending: 0,
      underReview: 0,
      approved: 0,
      needsCorrection: 0,
      rejected: 0,
      reversed: 0,
    },
    earnings: [],
    activePayoutHolds: [],
    payoutProfile: null,
    recentAudit: [],
    concurrencyStamp: 'stamp',
  };
}

const jane = makeUser({
  id: 'u-42',
  displayName: 'Jane Doe',
  email: 'jane@example.com',
  impersonatedBy: {
    id: 'admin-1',
    displayName: 'Platform Admin',
    email: 'admin@optimizeall.local',
    startedAt: '2026-09-24T10:00:00Z',
    expiresAt: '2026-09-24T11:00:00Z',
  },
});

function renderDetail() {
  return renderWithApp(<UserDetailPage />, {
    route: '/admin/users/u-42',
    path: '/admin/users/:userId',
    routes: [{ path: '/app/*', element: <p>participant home</p> }],
  });
}

describe('Log in as (impersonation)', () => {
  it('requires a reason and the typed email, then switches to the user’s portal', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi(
      {
        'GET /admin/users/u-42': () => json(200, detail()),
        'POST /admin/users/u-42/impersonate': () => json(200, session(jane, 'jane-token')),
      },
      IMPERSONATOR_PERMISSIONS,
    );
    const { router } = renderDetail();

    await user.click(await screen.findByRole('button', { name: 'Log in as' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Log in as Jane Doe/ });
    expect(within(dialog).getByText(/Some actions are blocked/)).toBeInTheDocument();
    const confirm = within(dialog).getByRole('button', { name: 'Log in as user' });
    expect(confirm).toBeDisabled();

    await user.type(within(dialog).getByLabelText(/Type/), 'jane@example.com');
    await user.click(confirm);
    expect(await within(dialog).findByText(/Enter a reason/)).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/impersonate'))).toBe(false);

    await user.type(within(dialog).getByLabelText(/Why do you need/), 'Ticket 4211 earnings page');
    await user.click(confirm);
    await waitFor(() =>
      expect(calls.find((c) => c.path.endsWith('/impersonate'))?.body).toEqual({
        reason: 'Ticket 4211 earnings page',
        confirm: true,
      }),
    );
    expect(await screen.findByText('participant home')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/app');
  });

  it('shows the server refusal inline', async () => {
    const user = userEvent.setup();
    mockAdminApi(
      {
        'GET /admin/users/u-42': () => json(200, detail()),
        'POST /admin/users/u-42/impersonate': () =>
          problem(
            409,
            'admin.impersonation_target_inactive',
            'Suspended or deactivated accounts can’t be impersonated.',
          ),
      },
      IMPERSONATOR_PERMISSIONS,
    );
    renderDetail();
    await user.click(await screen.findByRole('button', { name: 'Log in as' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.type(within(dialog).getByLabelText(/Type/), 'jane@example.com');
    await user.type(within(dialog).getByLabelText(/Why do you need/), 'Ticket 4211 earnings page');
    await user.click(within(dialog).getByRole('button', { name: 'Log in as user' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(/can’t be impersonated/);
  });

  it('is offered only with users.impersonate and never for admins or suspended accounts', async () => {
    mockAdminApi({ 'GET /admin/users/u-42': () => json(200, detail()) });
    const first = renderDetail();
    expect(await screen.findByRole('heading', { name: 'Jane Doe', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Log in as' })).not.toBeInTheDocument();
    first.unmount();

    mockAdminApi(
      { 'GET /admin/users/u-42': () => json(200, detail({}, ['Admin'])) },
      IMPERSONATOR_PERMISSIONS,
    );
    const second = renderDetail();
    expect(await screen.findByRole('heading', { name: 'Jane Doe', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Log in as' })).not.toBeInTheDocument();
    second.unmount();

    mockAdminApi(
      { 'GET /admin/users/u-42': () => json(200, detail({ status: 'Suspended' })) },
      IMPERSONATOR_PERMISSIONS,
    );
    renderDetail();
    expect(await screen.findByRole('heading', { name: 'Jane Doe', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Log in as' })).not.toBeInTheDocument();
  });
});

const listItem = (overrides: Partial<AdminUserListItem>): AdminUserListItem => ({
  id: 'u-1',
  email: 'real@example.com',
  displayName: 'Real Person',
  countryCode: 'PK',
  status: 'Active',
  tier: 'Standard',
  roles: ['Participant'],
  emailVerified: true,
  createdAt: '2026-08-01T00:00:00Z',
  lastActiveAt: null,
  ...overrides,
});

describe('Test users', () => {
  it('labels test accounts, filters by account type and offers Log in as per row', async () => {
    const { calls } = mockAdminApi(
      {
        'GET /admin/users': () =>
          json(200, {
            items: [
              listItem({}),
              listItem({
                id: 'u-2',
                displayName: 'QA Tester',
                email: 'test+qa@test.optimizeall.app',
                isTestAccount: true,
              }),
              listItem({ id: 'u-3', displayName: 'Other Admin', roles: ['Admin'] }),
            ],
            total: 3,
            page: 1,
            pageSize: 25,
          }),
      },
      IMPERSONATOR_PERMISSIONS,
    );
    const user = userEvent.setup();
    renderWithApp(<UsersPage />, { route: '/admin/users', path: '/admin/users' });

    const row = (await screen.findAllByText('QA Tester'))[0]!.closest('tr, li, [role="row"]') as HTMLElement;
    expect(within(row).getByText('TEST')).toBeInTheDocument();
    expect(screen.getAllByText('TEST')).toHaveLength(1);
    expect(
      screen.getAllByRole('button', { name: /^Log in as (Real Person|QA Tester)$/ }).length,
    ).toBeGreaterThanOrEqual(2);
    expect(screen.queryByRole('button', { name: 'Log in as Other Admin' })).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Account type'), 'true');
    await waitFor(() =>
      expect(calls.some((c) => c.path === '/admin/users' && c.method === 'GET')).toBe(true),
    );
  });

  it('creates a test user and shows the generated password once', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi(
      {
        'GET /admin/users': () => json(200, { items: [], total: 0, page: 1, pageSize: 25 }),
        'POST /admin/test-users': () =>
          json(201, {
            id: 'u-9',
            email: 'test+finance-ab12cd@test.optimizeall.app',
            displayName: 'Test Finance',
            roles: ['Finance'],
            password: 'Gen3rated-Pass-xyz',
            clientAccountId: null,
          }),
      },
      IMPERSONATOR_PERMISSIONS,
    );
    renderWithApp(<UsersPage />, { route: '/admin/users', path: '/admin/users' });

    await user.click(await screen.findByRole('button', { name: 'Create test user' }));
    const dialog = await screen.findByRole('dialog', { name: 'Create a test user' });
    await user.click(within(dialog).getByRole('checkbox', { name: 'Participant' }));
    await user.click(within(dialog).getByRole('button', { name: 'Create test user' }));
    expect(await within(dialog).findByText('Choose at least one role.')).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/admin/test-users')).toBe(false);

    await user.click(within(dialog).getByRole('checkbox', { name: 'Finance' }));
    await user.click(within(dialog).getByRole('button', { name: 'Create test user' }));
    const done = await screen.findByRole('dialog', { name: 'Test user created' });
    expect(within(done).getByTestId('test-user-password')).toHaveTextContent('Gen3rated-Pass-xyz');
    expect(within(done).getByText('test+finance-ab12cd@test.optimizeall.app')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/admin/test-users')?.body).toEqual({ roles: ['Finance'] });
  });
});
