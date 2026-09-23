import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { authRoutes, makeCard, paged } from '../test/fixtures';
import { filtersFromSearch, searchFromFilters } from './campaignFilters';
import { CampaignsPage } from './CampaignsPage';

function setup(route = '/app/campaigns') {
  const mock = mockFetch({
    ...authRoutes,
    'GET /campaign-categories': () =>
      json(200, [
        {
          id: 'cat1',
          name: 'Fashion',
          slug: 'fashion',
          description: null,
          icon: null,
          sortOrder: 1,
          isActive: true,
        },
      ]),
    'GET /campaigns': () =>
      json(
        200,
        paged([
          makeCard(),
          makeCard({
            id: 'c2',
            slug: 'b',
            title: 'Second',
            eligibility: { isEligible: false, reasons: [{ code: 'x', message: 'Your profile is too new.' }] },
          }),
        ]),
      ),
  });
  const result = renderWithApp(<CampaignsPage />, { route, path: '/app/campaigns' });
  return { ...result, urls: () => mock.fn.mock.calls.map((c) => String(c[0])) };
}

const lastCampaignQuery = (urls: () => string[]) =>
  new URL(
    urls()
      .filter((u) => u.startsWith('/api/v1/campaigns?'))
      .at(-1)!,
    'http://x',
  ).searchParams;

describe('CampaignsPage', () => {
  it('reflects filters in the URL and in the API query', async () => {
    const user = userEvent.setup();
    const { router, urls } = setup();
    expect(await screen.findByText('Autumn launch')).toBeInTheDocument();
    expect(screen.getByText('Your profile is too new.')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Platform'), 'TikTok');
    await waitFor(() => expect(router.state.location.search).toContain('platform=TikTok'));

    await user.selectOptions(screen.getByLabelText('Category'), 'cat1');
    await user.click(screen.getByLabelText('Only campaigns I’m eligible for'));
    await user.selectOptions(screen.getByLabelText('Sort by'), 'reward');
    await waitFor(() => {
      const search = new URLSearchParams(router.state.location.search);
      expect(search.get('category')).toBe('cat1');
      expect(search.get('eligible')).toBe('1');
      expect(search.get('sort')).toBe('reward');
    });

    await user.type(screen.getByLabelText('Search'), 'autumn');
    await waitFor(() => expect(new URLSearchParams(router.state.location.search).get('q')).toBe('autumn'), {
      timeout: 2000,
    });

    await waitFor(() => {
      const q = lastCampaignQuery(urls);
      expect(q.get('platform')).toBe('TikTok');
      expect(q.get('categoryId')).toBe('cat1');
      expect(q.get('eligibleOnly')).toBe('true');
      expect(q.get('sort')).toBe('reward');
      expect(q.get('search')).toBe('autumn');
    });
  });

  it('restores filters from the URL', async () => {
    setup('/app/campaigns?platform=Instagram&minReward=3&deadlineBefore=2026-12-01&topic=fitness&page=2');
    expect(await screen.findByText('Autumn launch')).toBeInTheDocument();
    expect(screen.getByLabelText('Platform')).toHaveValue('Instagram');
    expect(screen.getByLabelText('Minimum base reward')).toHaveValue(3);
    expect(screen.getByLabelText('Topic')).toHaveValue('fitness');
    expect(screen.getByLabelText('Deadline on or before')).toHaveValue('2026-12-01');
  });

  it('round-trips filter state', () => {
    const filters = filtersFromSearch(new URLSearchParams('q=a&platform=X&eligible=1&sort=newest&page=3'));
    expect(filters).toMatchObject({
      search: 'a',
      platform: 'X',
      eligibleOnly: true,
      sort: 'newest',
      page: 3,
    });
    expect(searchFromFilters(filters).toString()).toBe('q=a&platform=X&eligible=1&sort=newest&page=3');
  });

  it('has no axe violations', async () => {
    const { container } = setup();
    await screen.findByText('Autumn launch');
    expect(await axeViolations(container)).toEqual([]);
  });
});
