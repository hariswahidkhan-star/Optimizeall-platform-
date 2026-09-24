import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { NotificationPreferences } from '@/features/participant/api/types';
import { NotificationSettingsDialog } from './NotificationSettingsDialog';

const cells = (enabled = true) => [
  { channel: 'InApp' as const, enabled: true, locked: true, available: true },
  { channel: 'Email' as const, enabled, locked: false, available: true },
  { channel: 'WhatsApp' as const, enabled: true, locked: false, available: false },
];

const prefs: NotificationPreferences = {
  channels: [
    { channel: 'InApp', available: true, reason: null },
    { channel: 'Email', available: true, reason: null },
    { channel: 'WhatsApp', available: false, reason: 'WhatsApp notifications are not available yet.' },
  ],
  types: [
    {
      type: 'account.status_changed',
      label: 'Account status',
      description: 'When your account is suspended or reactivated.',
      essential: true,
      marketing: false,
      group: 'Account',
      channels: cells().map((c) => ({ ...c, locked: true })),
    },
    {
      type: 'billing.payment_claimed',
      label: 'Client payment reports (staff)',
      description: 'When a client reports that they paid an invoice (payments hub).',
      essential: false,
      marketing: false,
      group: 'Sales, billing and payments',
      channels: cells(),
    },
  ],
};

describe('NotificationSettingsDialog', () => {
  it('shows grouped staff kinds, saves a change and has no axe violations', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'GET /me/notification-preferences': () => json(200, prefs),
      'PUT /me/notification-preferences': () => json(200, prefs),
    });
    const { baseElement } = renderWithApp(<NotificationSettingsDialog open onClose={() => {}} />, { withAuth: false });

    const dialog = await screen.findByRole('dialog', { name: 'Notification settings' });
    const table = await within(dialog).findByRole('table');
    expect(within(table).getByRole('columnheader', { name: 'Sales, billing and payments' })).toBeInTheDocument();
    expect(within(table).getByText('Client payment reports (staff)')).toBeInTheDocument();
    // No participant profile link for staff.
    expect(within(dialog).queryByRole('link', { name: /WhatsApp number/ })).not.toBeInTheDocument();
    expect(await axeViolations(baseElement)).toEqual([]);

    await user.click(within(dialog).getByRole('checkbox', { name: 'Email for Client payment reports (staff)' }));
    await user.click(within(dialog).getByRole('button', { name: 'Save preferences' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true));
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      preferences: [{ type: 'billing.payment_claimed', channel: 'Email', enabled: false }],
    });
  });
});
