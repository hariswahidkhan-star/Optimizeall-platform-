import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { paged, queueItem, reviewerSession } from '../test/fixtures';
import { QueuePage } from './QueuePage';

function queueUrls(fn: ReturnType<typeof mockFetch>['fn']): URL[] {
  return fn.mock.calls
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .filter((u) => u.pathname === '/api/v1/review/queue' && u.searchParams.get('pageSize') !== '200');
}

function renderQueue(route = '/review/queue', extra: Parameters<typeof mockFetch>[0] = {}) {
  const mock = mockFetch({
    'POST /auth/refresh': reviewerSession(),
    'GET /review/queue': () =>
      json(
        200,
        paged([
          queueItem(),
          queueItem({
            id: 's2',
            riskScore: 5,
            flagTypes: [],
            participant: { id: 'p2', displayName: 'Ali Raza', countryCode: 'PK' },
          }),
        ]),
      ),
    'GET /campaigns/options': () =>
      json(200, [
        { id: 'c3', title: 'Winter Draft', status: 'Draft' },
        { id: 'c2', title: 'Summer Sale', status: 'Ended' },
        { id: 'c1', title: 'Autumn Launch', status: 'Active' },
      ]),
    ...extra,
  });
  const utils = renderWithApp(<QueuePage />, {
    route,
    path: '/review/queue',
    routes: [{ path: '/review/queue/:id', element: <p>workspace opened</p> }],
  });
  return { ...mock, ...utils };
}

describe('QueuePage', () => {
  it('reads filters from the URL and sends them to the API', async () => {
    const { fn } = renderQueue(
      '/review/queue?status=Pending&platform=TikTok&minRisk=40&flagged=true&mine=1&sort=risk',
    );
    await screen.findAllByText('Autumn Launch');
    const url = queueUrls(fn).at(-1)!;
    expect(url.searchParams.get('status')).toBe('Pending');
    expect(url.searchParams.get('platform')).toBe('TikTok');
    expect(url.searchParams.get('minRisk')).toBe('40');
    expect(url.searchParams.get('flagged')).toBe('true');
    expect(url.searchParams.get('assignedToMe')).toBe('true');
    expect(url.searchParams.get('sort')).toBe('risk');
    // Active filters are shown as removable chips.
    expect(screen.getByRole('list', { name: 'Active filters' })).toHaveTextContent('Platform: TikTok');
  });

  it('writes filter and sort changes into the URL and refetches', async () => {
    const user = userEvent.setup();
    const { fn, router } = renderQueue('/review/queue?page=2');
    await screen.findAllByText('Autumn Launch');

    await user.selectOptions(screen.getByLabelText('Status'), 'UnderReview');
    await waitFor(() => expect(router.state.location.search).toContain('status=UnderReview'));
    // A filter change returns to page 1.
    expect(router.state.location.search).not.toContain('page=');

    await user.selectOptions(screen.getByLabelText('Sort'), 'risk');
    await waitFor(() => expect(router.state.location.search).toContain('sort=risk'));
    await waitFor(() => {
      const url = queueUrls(fn).at(-1)!;
      expect(url.searchParams.get('status')).toBe('UnderReview');
      expect(url.searchParams.get('sort')).toBe('risk');
    });

    await user.click(screen.getByRole('button', { name: /Remove filter Status/ }));
    await waitFor(() => expect(router.state.location.search).not.toContain('status='));
  });

  it('offers campaigns from /campaigns/options and a "Claimed by me" filter', async () => {
    const user = userEvent.setup();
    const { fn, router } = renderQueue();
    await screen.findAllByText('Autumn Launch');
    const campaign = screen.getByLabelText('Campaign');
    await waitFor(() =>
      expect(within(campaign).getByRole('option', { name: 'Summer Sale' })).toBeInTheDocument(),
    );
    // Drafts never have submissions, so they are not offered.
    expect(within(campaign).queryByRole('option', { name: 'Winter Draft' })).not.toBeInTheDocument();

    await user.selectOptions(campaign, 'c2');
    await waitFor(() => expect(router.state.location.search).toContain('campaignId=c2'));
    await user.selectOptions(screen.getByLabelText('Claim'), '1');
    await waitFor(() => expect(router.state.location.search).toContain('claimed=1'));
    await waitFor(() => {
      const url = queueUrls(fn).at(-1)!;
      expect(url.searchParams.get('claimedByMe')).toBe('true');
      expect(url.searchParams.get('campaignId')).toBe('c2');
    });
    expect(screen.getByRole('list', { name: 'Active filters' })).toHaveTextContent('Claim: Claimed by me');
  });

  it('shows risk severity and flags for each row', async () => {
    renderQueue();
    await screen.findByRole('button', { name: 'Review Autumn Launch by Ali Raza' });
    const rows = screen.getAllByRole('row');
    const first = rows[1]!;
    expect(within(first).getByText(/High risk/)).toBeInTheDocument();
    expect(within(first).getByText('Duplicate screenshot')).toBeInTheDocument();
    expect(within(rows[2]!).getByText('No flags')).toBeInTheDocument();
  });

  it('claims and opens the workspace from "Review"', async () => {
    const user = userEvent.setup();
    const { calls } = renderQueue('/review/queue', {
      'POST /review/submissions/s1/claim': () =>
        json(200, {
          submissionId: 's1',
          status: 'UnderReview',
          claimedBy: { id: 'rev-1', displayName: 'Rita' },
          claimExpiresAt: '2026-09-23T11:00:00Z',
          concurrencyStamp: 'x',
        }),
    });
    await user.click(await screen.findByRole('button', { name: 'Review Autumn Launch by Sara Khan' }));
    expect(await screen.findByText('workspace opened')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST' && c.path === '/review/submissions/s1/claim')).toBe(true);
  });

  it('explains a claim conflict with who holds the submission and offers a refresh', async () => {
    const user = userEvent.setup();
    renderQueue('/review/queue', {
      'POST /review/submissions/s1/claim': () =>
        problem(409, 'review.claimed_by_other', 'Omar is reviewing', {
          errors: {
            claimedBy: ['Omar Reviewer'],
            claimedByUserId: ['rev-2'],
            claimExpiresAt: ['2026-09-23T11:00:00Z'],
          },
        }),
    });
    await user.click(await screen.findByRole('button', { name: 'Review Autumn Launch by Sara Khan' }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Someone else is reviewing this');
    expect(alert).toHaveTextContent('Omar Reviewer holds this submission until');
    expect(within(alert).getByRole('button', { name: 'Refresh' })).toBeInTheDocument();
    expect(screen.queryByText('workspace opened')).not.toBeInTheDocument();
  });

  it('offers bulk assignment only with review.assign', async () => {
    renderQueue();
    await screen.findAllByText('Autumn Launch');
    expect(screen.queryByRole('checkbox', { name: 'Select all rows' })).not.toBeInTheDocument();
  });

  it('assigns selected submissions to a reviewer', async () => {
    const user = userEvent.setup();
    const { calls } = (() => {
      const mock = mockFetch({
        'POST /auth/refresh': reviewerSession(['review.assign']),
        'GET /review/queue': () => json(200, paged([queueItem(), queueItem({ id: 's2' })])),
        'GET /review/reviewers': () =>
          json(200, [
            { id: 'rev-2', displayName: 'Omar Reviewer', email: 'o@x', assignedOpen: 1, decisionsToday: 2 },
          ]),
        'POST /review/assign': () => json(200, { updated: 2, skippedIds: [] }),
      });
      renderWithApp(<QueuePage />, { route: '/review/queue', path: '/review/queue' });
      return mock;
    })();
    await user.click(await screen.findByRole('checkbox', { name: 'Select all rows' }));
    await user.click(screen.getByRole('button', { name: 'Assign…' }));
    const dialog = await screen.findByRole('dialog');
    await user.selectOptions(await within(dialog).findByLabelText(/Reviewer/), 'rev-2');
    await user.click(within(dialog).getByRole('button', { name: 'Assign' }));
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/review/assign')?.body).toEqual({
        submissionIds: ['s1', 's2'],
        reviewerId: 'rev-2',
      }),
    );
  });

  it('has no axe violations', async () => {
    const { container } = renderQueue();
    await screen.findAllByText('Autumn Launch');
    expect(await axeViolations(container)).toEqual([]);
  });
});
