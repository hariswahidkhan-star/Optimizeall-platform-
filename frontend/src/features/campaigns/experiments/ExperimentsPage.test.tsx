import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import type { Experiment } from '../api/types';
import { managerSession } from '../test/fixtures';
import { ExperimentsPage } from './ExperimentsPage';

const experiment: Experiment = {
  id: 'e1',
  campaignId: 'old-campaign',
  campaignTitle: 'Legacy launch from 2024',
  name: 'Title test',
  hypothesis: null,
  element: 'Title',
  status: 'Draft',
  startedAt: null,
  endedAt: null,
  winningVariantId: null,
  variants: [
    {
      id: 'va',
      key: 'A',
      name: 'Control',
      weight: 50,
      title: 'Share',
      instructions: null,
      assetId: null,
      landingHeadline: null,
      landingBody: null,
    },
  ],
  concurrencyStamp: 's1',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
};

describe('ExperimentsPage', () => {
  it('shows the campaign title from the experiment and filters by /campaigns/options', async () => {
    const user = userEvent.setup();
    const { calls, fn } = mockFetch({
      'POST /auth/refresh': managerSession(),
      'GET /marketing/experiments': () =>
        json(200, { items: [experiment], total: 1, page: 1, pageSize: 25, totalPages: 1 }),
      'GET /campaigns/options': () =>
        json(200, [
          { id: 'c2', title: 'Winter push', status: 'Active' },
          { id: 'c1', title: 'Spring drop', status: 'Ended' },
        ]),
    });
    renderWithApp(<ExperimentsPage />, { route: '/manage/experiments', path: '/manage/experiments' });

    const table = await screen.findByRole('table', { name: /Experiments/ });
    expect(await within(table).findByText('Legacy launch from 2024')).toBeInTheDocument();
    // No title lookups in the first page of campaigns any more.
    expect(calls.some((c) => c.path === '/admin/campaigns')).toBe(false);

    const filter = screen.getByLabelText('Campaign');
    await waitFor(() =>
      expect(within(filter).getByRole('option', { name: 'Spring drop' })).toBeInTheDocument(),
    );
    await user.selectOptions(filter, 'c1');
    await waitFor(() => {
      const url = fn.mock.calls
        .map(([input]) => new URL(String(input), 'http://localhost'))
        .filter((u) => u.pathname === '/api/v1/marketing/experiments')
        .at(-1);
      expect(url?.searchParams.get('campaignId')).toBe('c1');
    });
  });
});
