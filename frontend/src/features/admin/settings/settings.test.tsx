import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { problem } from '@/test/fetchMock';
import { axeViolations } from '@/test/render';
import type { Setting } from '../api/types';
import { json, mockAdminApi, renderAdmin } from '../test/helpers';
import { splitReferralErrors } from './settingsMeta';
import { SettingsPage } from './SettingsPage';

const minAge: Setting = {
  key: 'eligibility.minAccountAgeDays',
  value: 90,
  defaultValue: 90,
  isDefault: true,
  valueType: 'integer',
  description: 'Minimum age (days) of a social profile before it qualifies for campaigns.',
  updatedAt: null,
  updatedBy: null,
};
const retention: Setting = {
  key: 'retention.enabled',
  value: true,
  defaultValue: true,
  isDefault: true,
  valueType: 'boolean',
  description: 'Whether retention automations run.',
  updatedAt: '2026-09-20T10:00:00Z',
  updatedBy: { id: 'a', displayName: 'Ops Admin' },
};
const referral: Setting = {
  key: 'referral.program',
  value: {
    enabled: true,
    referrerRewardAmount: 5,
    currency: 'USD',
    qualifyingAction: 'FirstApprovedSubmission',
    qualifyWithinDays: 60,
    requireManualApproval: true,
    maxRewardedReferralsPerUser: 50,
  },
  defaultValue: null,
  isDefault: true,
  valueType: 'object',
  description: 'Referral program.',
  updatedAt: null,
  updatedBy: null,
};

describe('Settings', () => {
  it('shows value, default, last update and explanatory copy', async () => {
    mockAdminApi({ 'GET /admin/settings': () => json(200, [minAge, retention]) });
    renderAdmin(<SettingsPage />);
    const card = await screen.findByRole('region', { name: 'Minimum social account age' });
    expect(within(card).getByText(/makes newer accounts ineligible/)).toBeInTheDocument();
    expect(within(card).getByText('Allowed: 0–3,650 days.')).toBeInTheDocument();
    const retentionCard = screen.getByRole('region', { name: 'Retention automations' });
    expect(within(retentionCard).getByText(/Ops Admin/)).toBeInTheDocument();
    expect(within(retentionCard).getByRole('switch', { name: 'Retention automations' })).toBeChecked();
  });

  it('validates the range client-side, then needs a reason in a confirmation dialog', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({
      'GET /admin/settings': () => json(200, [minAge]),
      'PUT /admin/settings/eligibility.minAccountAgeDays': () =>
        json(200, { ...minAge, value: 120, isDefault: false, updatedAt: '2026-09-23T00:00:00Z' }),
    });
    renderAdmin(<SettingsPage />);
    const card = await screen.findByRole('region', { name: 'Minimum social account age' });
    const input = within(card).getByLabelText('New value');

    await user.clear(input);
    await user.type(input, '5000');
    await user.click(within(card).getByRole('button', { name: 'Save…' }));
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(within(card).getByText('Use a whole number from 0 to 3,650.')).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    await user.clear(input);
    await user.type(input, '120');
    await user.click(within(card).getByRole('button', { name: 'Save…' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Change Minimum social account age/ });
    expect(within(dialog).getByText('90 days')).toBeInTheDocument();
    expect(within(dialog).getByText('120 days')).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'Save setting' }));
    expect(await within(dialog).findByText(/Enter a reason/)).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);

    await user.type(within(dialog).getByLabelText(/Reason/), 'Fraud wave from new accounts');
    await user.click(within(dialog).getByRole('button', { name: 'Save setting' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
        value: 120,
        reason: 'Fraud wave from new accounts',
        confirm: true,
      }),
    );
    expect(await screen.findByText('Setting saved')).toBeInTheDocument();
  });

  it('maps a server validation error (PascalCase key) to the control', async () => {
    const user = userEvent.setup();
    mockAdminApi({
      'GET /admin/settings': () => json(200, [minAge]),
      'PUT /admin/settings/eligibility.minAccountAgeDays': () =>
        problem(400, 'settings.invalid_value', 'Some of the information needs attention.', {
          errors: { Value: ['Use a whole number from 0 to 3650.'] },
        }),
    });
    renderAdmin(<SettingsPage />);
    const card = await screen.findByRole('region', { name: 'Minimum social account age' });
    const input = within(card).getByLabelText('New value');
    await user.clear(input);
    await user.type(input, '100');
    await user.click(within(card).getByRole('button', { name: 'Save…' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.type(within(dialog).getByLabelText(/Reason/), 'Testing the limit');
    await user.click(within(dialog).getByRole('button', { name: 'Save setting' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(within(card).getByText('Use a whole number from 0 to 3650.')).toBeInTheDocument();
    expect(input).toHaveAttribute('aria-invalid', 'true');
  });

  it('edits the referral program as a form and sends the full object', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({
      'GET /admin/settings': () => json(200, [referral]),
      'PUT /admin/settings/referral.program': () =>
        problem(400, 'settings.invalid_value', 'Some of the information needs attention.', {
          errors: { value: ['qualifyWithinDays must be from 1 to 365. currency must be one of USD, EUR.'] },
        }),
    });
    renderAdmin(<SettingsPage />);
    const card = await screen.findByRole('region', { name: 'Referral program' });
    await user.clear(within(card).getByLabelText('Referrer reward'));
    await user.type(within(card).getByLabelText('Referrer reward'), '7.5');
    await user.click(within(card).getByRole('button', { name: 'Save…' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.type(within(dialog).getByLabelText(/Reason/), 'Q4 growth push');
    await user.click(within(dialog).getByRole('button', { name: 'Save setting' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
        value: {
          enabled: true,
          referrerRewardAmount: 7.5,
          currency: 'USD',
          qualifyingAction: 'FirstApprovedSubmission',
          qualifyWithinDays: 60,
          requireManualApproval: true,
          maxRewardedReferralsPerUser: 50,
        },
      }),
    );
    expect(await within(card).findByText('qualifyWithinDays must be from 1 to 365.')).toBeInTheDocument();
    expect(within(card).getByText('currency must be one of USD, EUR.')).toBeInTheDocument();
  });

  it('splits referral error sentences by field', () => {
    expect(
      splitReferralErrors(['referrerRewardAmount must be between 0 and 100,000. Other problem.']),
    ).toEqual({
      fields: { referrerRewardAmount: 'referrerRewardAmount must be between 0 and 100,000. Other problem.' },
      rest: [],
    });
  });

  it('has no axe violations', async () => {
    mockAdminApi({ 'GET /admin/settings': () => json(200, [minAge, retention, referral]) });
    const { baseElement } = renderAdmin(<SettingsPage />);
    await screen.findByRole('region', { name: 'Referral program' });
    expect(await axeViolations(baseElement)).toEqual([]);
  });
});
