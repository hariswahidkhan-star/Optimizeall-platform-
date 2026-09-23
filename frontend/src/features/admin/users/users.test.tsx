import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { problem } from '@/test/fetchMock';
import { axeViolations } from '@/test/render';
import type { AdminUserDetail } from '../api/types';
import { json, mockAdminApi, renderAdmin } from '../test/helpers';
import { UserDetailPage } from './UserDetailPage';
import { UsersPage } from './UsersPage';

function detail(
  overrides: Partial<AdminUserDetail['profile']> = {},
  roles = ['Participant'],
): AdminUserDetail {
  return {
    profile: {
      id: 'u-42',
      email: 'sara@example.com',
      displayName: 'Sara Khan',
      countryCode: 'PK',
      languageCode: 'en',
      timeZone: 'Asia/Karachi',
      interests: ['fitness'],
      status: 'Active',
      statusReason: null,
      statusChangedAt: null,
      tier: 'Standard',
      referralCode: 'SARA01',
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
      total: 3,
      pending: 1,
      underReview: 0,
      approved: 2,
      needsCorrection: 0,
      rejected: 0,
      reversed: 0,
    },
    earnings: [{ status: 'Approved', currency: 'USD', amount: 25, count: 5 }],
    activePayoutHolds: [],
    payoutProfile: null,
    recentAudit: [],
    concurrencyStamp: 'stamp',
  };
}

const renderDetail = (id = 'u-42') =>
  renderAdmin(<UserDetailPage />, `/admin/users/${id}`, '/admin/users/:userId');

describe('User detail — suspend', () => {
  it('requires a reason and the typed email before suspending, and explains session revocation', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({
      'GET /admin/users/u-42': () => json(200, detail()),
      'POST /admin/users/u-42/suspend': () =>
        json(200, detail({ status: 'Suspended', statusReason: 'Fraud' })),
    });
    renderDetail();
    await user.click(await screen.findByRole('button', { name: 'Suspend' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Suspend Sara Khan/ });
    expect(within(dialog).getByText('Sessions are revoked immediately')).toBeInTheDocument();

    const confirm = within(dialog).getByRole('button', { name: 'Suspend account' });
    expect(confirm).toBeDisabled(); // email not typed yet

    await user.type(within(dialog).getByLabelText(/Type/), 'sara@example.com');
    expect(confirm).toBeEnabled();
    await user.click(confirm);
    expect(await within(dialog).findByText(/Enter a reason/)).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/suspend'))).toBe(false);

    await user.type(within(dialog).getByLabelText(/Reason/), 'Confirmed fraud ring');
    await user.click(confirm);
    await waitFor(() =>
      expect(calls.find((c) => c.path.endsWith('/suspend'))?.body).toEqual({
        reason: 'Confirmed fraud ring',
        confirm: true,
      }),
    );
    expect(await screen.findByText('This account is suspended')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reactivate' })).toBeInTheDocument();
  });

  it('hides actions the user has no permission for', async () => {
    mockAdminApi({ 'GET /admin/users/u-42': () => json(200, detail()) }, ['users.view']);
    renderDetail();
    expect(await screen.findByRole('heading', { name: 'Sara Khan', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Suspend' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Change roles' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Change tier' })).not.toBeInTheDocument();
  });

  it('shows a no-access state when the server answers 403', async () => {
    mockAdminApi({ 'GET /admin/users/u-42': () => problem(403, 'auth.forbidden', 'Forbidden') });
    renderDetail();
    expect(await screen.findByText('You don’t have access to this')).toBeInTheDocument();
  });
});

describe('User detail — roles', () => {
  it('shows the server error when removing your own Admin role', async () => {
    const user = userEvent.setup();
    mockAdminApi({
      'GET /admin/users/admin-1': () =>
        json(200, {
          ...detail({ id: 'admin-1', email: 'admin@optimizeall.local', displayName: 'Platform Admin' }, [
            'Admin',
          ]),
        }),
      'PUT /admin/users/admin-1/roles': () =>
        problem(403, 'admin.cannot_remove_own_admin', 'You can’t remove your own Admin role.'),
    });
    renderDetail('admin-1');
    await user.click(await screen.findByRole('button', { name: 'Change roles' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('checkbox', { name: 'Admin' }));
    await user.click(within(dialog).getByRole('checkbox', { name: 'Reviewer' }));
    expect(within(dialog).getByText(/removing your own Admin role/)).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(/Reason/), 'Stepping down');
    await user.click(within(dialog).getByRole('button', { name: 'Save roles' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(/can’t remove your own Admin role/);
  });

  it('maps admin.last_admin and validation errors', async () => {
    const user = userEvent.setup();
    let attempt = 0;
    const { calls } = mockAdminApi({
      'GET /admin/users/u-42': () => json(200, detail({}, ['Admin'])),
      'PUT /admin/users/u-42/roles': () =>
        ++attempt === 1
          ? problem(409, 'admin.last_admin', 'At least one active administrator must remain.')
          : json(400, {
              title: 'One or more validation errors occurred.',
              errors: { Roles: ['Unknown role.'] },
            }),
    });
    renderDetail();
    await user.click(await screen.findByRole('button', { name: 'Change roles' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('checkbox', { name: 'Admin' }));
    await user.click(within(dialog).getByRole('checkbox', { name: 'Finance' }));
    await user.type(within(dialog).getByLabelText(/Reason/), 'Moving to finance');
    await user.click(within(dialog).getByRole('button', { name: 'Save roles' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      /At least one active administrator must remain/,
    );
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      roles: ['Finance'],
      reason: 'Moving to finance',
      confirm: true,
    });

    await user.click(within(dialog).getByRole('button', { name: 'Save roles' }));
    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('Unknown role.'));
  });

  it('requires at least one role', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({ 'GET /admin/users/u-42': () => json(200, detail()) });
    renderDetail();
    await user.click(await screen.findByRole('button', { name: 'Change roles' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('checkbox', { name: 'Participant' }));
    await user.type(within(dialog).getByLabelText(/Reason/), 'No roles');
    await user.click(within(dialog).getByRole('button', { name: 'Save roles' }));
    expect(await within(dialog).findAllByText('Choose at least one role.')).not.toHaveLength(0);
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);
  });
});

describe('Users list', () => {
  it('passes filters to the API and has no axe violations', async () => {
    const { calls } = mockAdminApi({
      'GET /admin/users': () =>
        json(200, {
          items: [
            {
              id: 'u-42',
              email: 'sara@example.com',
              displayName: 'Sara Khan',
              countryCode: 'PK',
              status: 'Active',
              tier: 'Gold',
              roles: ['Participant'],
              emailVerified: true,
              createdAt: '2026-08-01T00:00:00Z',
              lastActiveAt: null,
            },
          ],
          total: 1,
          page: 1,
          pageSize: 25,
          totalPages: 1,
        }),
    });
    const { baseElement } = renderAdmin(<UsersPage />, '/admin/users?role=Admin&tier=Gold', '/admin/users');
    expect(await screen.findByRole('link', { name: 'Sara Khan' })).toHaveAttribute(
      'href',
      '/admin/users/u-42',
    );
    const list = calls.find((c) => c.path === '/admin/users');
    expect(list).toBeDefined();
    const url = new URL(
      String(
        (globalThis.fetch as unknown as { mock: { calls: unknown[][] } }).mock.calls.find((c) =>
          String(c[0]).includes('/admin/users?'),
        )?.[0],
      ),
      'http://x',
    );
    expect(url.searchParams.get('role')).toBe('Admin');
    expect(url.searchParams.get('tier')).toBe('Gold');
    expect(screen.getByRole('button', { name: 'Invite staff' })).toBeInTheDocument();
    expect(await axeViolations(baseElement)).toEqual([]);
  });
});

describe('User detail — axe', () => {
  it('has no axe violations', async () => {
    mockAdminApi({ 'GET /admin/users/u-42': () => json(200, detail()) });
    const { baseElement } = renderDetail();
    await screen.findByRole('heading', { name: 'Sara Khan', level: 1 });
    expect(await axeViolations(baseElement)).toEqual([]);
  });
});
