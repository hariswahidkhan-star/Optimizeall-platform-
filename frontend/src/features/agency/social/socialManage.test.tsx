import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { AdminPreset, AwarenessDayAdmin, SocialCampaign } from './api';
import { CompetitorDialog } from './EngagementPages';
import { CampaignDialog } from './LibraryDialogs';
import { EditProfileDialog } from './ProfilesPage';
import { AwarenessDayDialog, PresetDialog } from './SocialSettingsPage';
import { presets, profile } from './test/fixtures';

const campaign: SocialCampaign = {
  id: 'cmp1',
  clientAccountId: 'c1',
  name: 'Spring',
  utmCampaign: 'spring',
  utmSource: null,
  utmMedium: null,
  utmContent: null,
  utmTerm: null,
  concurrencyStamp: 'st1',
  isArchived: false,
};

describe('social edit dialogs', () => {
  it('creates a campaign with POST and edits one with PUT and its stamp', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    const { calls } = mockFetch({
      'POST /agency/social/clients/c1/campaigns': () => json(200, campaign),
      'PUT /agency/social/campaigns/cmp1': () => json(200, campaign),
    });
    const { container, unmount } = renderWithApp(
      <CampaignDialog clientId="c1" campaign={null} onClose={() => {}} onSaved={onSaved} />,
      { withAuth: false },
    );
    let dialog = await screen.findByRole('dialog', { name: 'New campaign' });
    await user.type(within(dialog).getByLabelText(/Campaign name/), 'Spring');
    await user.type(within(dialog).getByLabelText(/^utm_campaign/), 'spring');
    expect(await axeViolations(container)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      name: 'Spring',
      utmCampaign: 'spring',
      utmSource: null,
    });
    unmount();

    renderWithApp(<CampaignDialog clientId="c1" campaign={campaign} onClose={() => {}} onSaved={onSaved} />, {
      withAuth: false,
    });
    dialog = await screen.findByRole('dialog', { name: 'Edit campaign' });
    await user.type(within(dialog).getByLabelText(/^utm_medium/), 'paid-social');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
        utmMedium: 'paid-social',
        concurrencyStamp: 'st1',
      }),
    );
  });

  it('shows the server message when a competitor was changed meanwhile', async () => {
    const user = userEvent.setup();
    mockFetch({
      'PUT /agency/social/competitors/k1': () =>
        problem(
          409,
          'concurrency.conflict',
          'This competitor was changed by someone else. Reload and try again.',
        ),
    });
    const { container } = renderWithApp(
      <CompetitorDialog
        competitor={{
          id: 'k1',
          name: 'Rival',
          network: 'Instagram',
          handle: 'rival',
          profileUrl: null,
          snapshots: [],
          concurrencyStamp: 'x',
        }}
        onClose={() => {}}
        onSaved={() => {}}
      />,
      { withAuth: false },
    );
    const dialog = await screen.findByRole('dialog', { name: 'Edit competitor' });
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(
      await within(dialog).findByText('This competitor was changed by someone else. Reload and try again.'),
    ).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('edits a profile without changing its network', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({ 'PUT /agency/social/profiles/p-x': () => json(200, profile()) });
    renderWithApp(<EditProfileDialog profile={profile()} onClose={() => {}} onSaved={() => {}} />, {
      withAuth: false,
    });
    const dialog = await screen.findByRole('dialog');
    const name = within(dialog).getByLabelText(/Display name/);
    await user.clear(name);
    await user.type(name, 'Nimbus Gyms');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
        network: 'X',
        displayName: 'Nimbus Gyms',
        concurrencyStamp: 's1',
      }),
    );
  });
});

describe('social settings dialogs', () => {
  it('saves preset limits and posting times', async () => {
    const user = userEvent.setup();
    const preset: AdminPreset = {
      preset: presets[0]!,
      isCustomized: false,
      updatedAt: null,
      concurrencyStamp: 'ps',
    };
    const onSaved = vi.fn();
    const { calls } = mockFetch({
      'PUT /agency/social/admin/presets/X': () => json(200, { ...preset, isCustomized: true }),
    });
    const { container } = renderWithApp(
      <PresetDialog preset={preset} onClose={() => {}} onSaved={onSaved} />,
      { withAuth: false },
    );
    const dialog = await screen.findByRole('dialog');
    const max = within(dialog).getByLabelText(/Max text length/);
    await user.clear(max);
    await user.type(max, '300');
    const times = within(dialog).getByLabelText(/Recommended posting times/);
    await user.clear(times);
    await user.type(times, 'Mon 08:00{enter}Fri 17:30');
    expect(await axeViolations(container)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(calls.find((c) => c.method === 'PUT')?.body).toMatchObject({
      maxTextLength: 300,
      recommendedTimes: ['Mon 08:00', 'Fri 17:30'],
      concurrencyStamp: 'ps',
    });
  });

  it('adds an awareness day for one year only', async () => {
    const user = userEvent.setup();
    const day: AwarenessDayAdmin = {
      id: 'd1',
      month: 5,
      day: 12,
      year: 2027,
      name: 'Agency day',
      countries: ['GB'],
      sourceUrl: 'https://agency.example/',
      isActive: true,
      isBuiltIn: false,
      concurrencyStamp: 'd',
    };
    const { calls } = mockFetch({ 'POST /agency/social/admin/awareness-days': () => json(200, day) });
    const { container } = renderWithApp(
      <AwarenessDayDialog day={null} onClose={() => {}} onSaved={() => {}} />,
      { withAuth: false },
    );
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText(/^Name/), 'Agency day');
    await user.selectOptions(within(dialog).getByLabelText(/Month/), '5');
    const d = within(dialog).getByLabelText(/^Day/);
    await user.clear(d);
    await user.type(d, '12');
    await user.type(within(dialog).getByLabelText(/Only in year/), '2027');
    await user.type(within(dialog).getByLabelText(/Countries/), 'GB');
    const source = within(dialog).getByLabelText(/Source URL/);
    await user.clear(source);
    await user.type(source, 'https://agency.example/');
    expect(await axeViolations(container)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
        name: 'Agency day',
        month: 5,
        day: 12,
        year: 2027,
        countries: ['GB'],
      }),
    );
  });
});
