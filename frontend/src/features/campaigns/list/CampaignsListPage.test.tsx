import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { AdminCampaignListItem } from '../api/types';
import { MANAGER_PERMISSIONS, makeCampaign, managerSession } from '../test/fixtures';
import { CampaignsListPage } from './CampaignsListPage';

const item: AdminCampaignListItem = {
  id: 'c1',
  slug: 'spring-drop',
  title: 'Spring drop',
  status: 'Active',
  visibility: 'Public',
  category: null,
  platforms: ['Instagram'],
  startsAt: '2026-09-01T00:00:00Z',
  endsAt: '2026-10-01T00:00:00Z',
  submissionDeadline: '2026-10-04T00:00:00Z',
  submissions: { total: 5, pending: 2, approved: 2, rejected: 1 },
  currency: 'USD',
  spent: 250,
  budget: 1000,
  budgetRemaining: 750,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  publishedAt: '2026-09-01T00:00:00Z',
};

function setup(permissions = MANAGER_PERMISSIONS, extra: Parameters<typeof mockFetch>[0] = {}) {
  const mocks = mockFetch({
    'POST /auth/refresh': managerSession(permissions),
    'GET /admin/campaigns': () =>
      json(200, { items: [item], total: 1, page: 1, pageSize: 25, totalPages: 1 }),
    'GET /admin/campaign-categories': () => json(200, []),
    ...extra,
  });
  return {
    ...mocks,
    ...renderWithApp(<CampaignsListPage />, { route: '/manage/campaigns', path: '/manage/campaigns' }),
  };
}

describe('CampaignsListPage', () => {
  it('shows counts and spend vs budget', async () => {
    const { container } = setup();
    const table = await screen.findByRole('table', { name: 'Campaigns' });
    expect(within(table).getByText('5 submitted')).toBeInTheDocument();
    expect(within(table).getByText('2 pending')).toBeInTheDocument();
    expect(within(table).getByRole('progressbar', { name: /Spend for Spring drop/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'New campaign' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('pauses with a required reason and surfaces server errors', async () => {
    const user = userEvent.setup();
    let attempts = 0;
    const { calls } = setup(MANAGER_PERMISSIONS, {
      'POST /admin/campaigns/c1/pause': () =>
        ++attempts === 1
          ? problem(409, 'campaign.invalid_transition', 'Only active campaigns can be paused.')
          : json(200, makeCampaign({ status: 'Paused' })),
    });
    await user.click(await screen.findByRole('button', { name: 'Actions for Spring drop' }));
    await user.click(screen.getByRole('menuitem', { name: /Pause/ }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Pause campaign' }));
    expect(within(dialog).getByText(/Enter a reason/)).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(/Reason/), 'Brand asked to hold');
    await user.click(within(dialog).getByRole('button', { name: 'Pause campaign' }));
    expect(await within(dialog).findByText('Only active campaigns can be paused.')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Pause campaign' }));
    expect(await screen.findByText('Campaign paused')).toBeInTheDocument();
    expect(calls.filter((c) => c.path === '/admin/campaigns/c1/pause').at(-1)!.body).toEqual({
      reason: 'Brand asked to hold',
    });
  });

  it('hides "New campaign" without rewards.edit', async () => {
    setup(MANAGER_PERMISSIONS.filter((p) => p !== 'rewards.edit'));
    await screen.findByRole('table', { name: 'Campaigns' });
    expect(screen.queryByRole('link', { name: 'New campaign' })).not.toBeInTheDocument();
  });
});
