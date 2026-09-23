import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { AnalyticsOverview, MetricSection } from '../api/types';
import { managerSession } from '../test/fixtures';
import { AnalyticsPage } from './AnalyticsPage';
import { AnalyticsReport } from './AnalyticsReport';

const section = (
  key: string,
  title: string,
  measurement: MetricSection['measurement'],
  metrics: MetricSection['metrics'],
) => ({
  key,
  title,
  measurement,
  metrics,
});

const overview: AnalyticsOverview = {
  from: '2026-08-25T00:00:00Z',
  to: '2026-09-23T23:59:59Z',
  campaignId: null,
  platform: null,
  funnel: section('funnel', 'Funnel', 'counted', [
    {
      key: 'registrations',
      label: 'Registrations',
      value: 12,
      unit: 'count',
      measurement: 'counted',
      note: null,
    },
  ]),
  posts: section('posts', 'Posts', 'counted', [
    {
      key: 'approvalRate',
      label: 'Approval rate',
      value: 66.67,
      unit: 'percent',
      measurement: 'counted',
      note: null,
    },
  ]),
  spend: section('spend', 'Spend', 'counted', [
    {
      key: 'spend',
      label: 'Spend on posts',
      value: 11.5,
      unit: 'money',
      measurement: 'counted',
      note: null,
      currency: 'USD',
    },
    {
      key: 'costPerApprovedPost',
      label: 'Cost per approved post',
      value: null,
      unit: 'money',
      measurement: 'counted',
      note: 'No approved posts.',
      currency: 'USD',
    },
  ]),
  spendByCampaign: [{ campaignId: 'c1', title: 'Alpha', currency: 'USD', amount: 11.5 }],
  reach: section('reach', 'Reach', 'estimated', [
    {
      key: 'estimatedReach',
      label: 'Estimated reach (declared follower counts, not measured views)',
      value: 3000,
      unit: 'count',
      measurement: 'estimated',
      note: 'Sum of declared follower counts of accounts used in submitted posts.',
    },
  ]),
  traffic: section('traffic', 'Traffic', 'measured', [
    {
      key: 'trackedClicks',
      label: 'Tracked clicks',
      value: 40,
      unit: 'count',
      measurement: 'measured',
      note: 'Bots excluded.',
    },
  ]),
  conversions: section('conversions', 'Conversions', 'measured', [
    {
      key: 'verifiedConversions',
      label: 'Verified conversions',
      value: 2,
      unit: 'count',
      measurement: 'measured',
      note: null,
    },
  ]),
  timeseries: [
    { date: '2026-09-22', registrations: 1, submissions: 2, approvals: 1, clicks: 3 },
    { date: '2026-09-23', registrations: 2, submissions: 1, approvals: 1, clicks: 5 },
  ],
  campaigns: [
    {
      campaignId: 'c1',
      title: 'Alpha',
      status: 'Active',
      submitted: 3,
      approved: 2,
      approvalRate: 66.67,
      spend: [{ currency: 'USD', amount: 11.5 }],
      costPerApproved: [{ currency: 'USD', amount: 5.75 }],
      clicks: 3,
      uniqueClicks: 2,
      verifiedConversions: 1,
      estimatedReach: 3000,
    },
  ],
  platforms: null,
  timeBasis: 'Posts by submission time (UTC).',
};

function sectionByHeading(name: RegExp) {
  return screen.getByRole('region', { name });
}

describe('AnalyticsReport', () => {
  it('keeps estimated and measured figures in separate, labelled sections', async () => {
    const { container } = renderWithApp(<AnalyticsReport data={overview} />, { withAuth: false });
    const reach = await screen.findByRole('region', { name: /Reach \(estimated\)/ });
    expect(within(reach).getByText('Estimated — not measured')).toBeInTheDocument();
    expect(within(reach).getAllByText('Estimated').length).toBeGreaterThan(0);
    expect(within(reach).queryByText('Measured')).not.toBeInTheDocument();
    expect(within(reach).getByText(/Sum of declared follower counts/)).toBeInTheDocument();

    const traffic = sectionByHeading(/Traffic \(measured\)/);
    expect(within(traffic).getAllByText('Measured').length).toBeGreaterThan(0);
    expect(within(traffic).queryByText('Estimated')).not.toBeInTheDocument();

    const conversions = sectionByHeading(/Conversions \(measured, verified\)/);
    expect(within(conversions).queryByText('Estimated')).not.toBeInTheDocument();

    const spend = sectionByHeading(/^Spend/);
    expect(within(spend).getAllByText('$11.50').length).toBeGreaterThan(0);
    expect(within(spend).getByText('No approved posts.')).toBeInTheDocument();

    const table = screen.getByRole('table', { name: 'Performance by campaign' });
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((h) => h.textContent);
    expect(headers.find((h) => h?.startsWith('Reach'))).toMatch(/Estimated/);
    expect(headers.find((h) => h?.startsWith('Clicks'))).toMatch(/Measured/);
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('AnalyticsPage', () => {
  it('loads the overview with filters and exports CSV', async () => {
    const user = userEvent.setup();
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const { calls } = mockFetch({
      'POST /auth/refresh': managerSession(),
      'GET /analytics/overview': () => json(200, overview),
      'GET /admin/campaigns': () => json(200, { items: [], total: 0, page: 1, pageSize: 200, totalPages: 0 }),
      'GET /marketing/tracking/summary': () =>
        json(200, {
          from: '',
          to: '',
          campaignId: null,
          clicks: 0,
          uniqueClicks: 0,
          botClicksExcluded: 0,
          verifiedConversions: 0,
          conversionValue: [],
          topParticipants: [],
          note: 'Measured clicks.',
        }),
      'GET /marketing/retention/summary': () =>
        json(200, { from: '', to: '', total: 3, items: [{ kind: 'campaign.alert', sent: 3 }] }),
      'GET /analytics/overview/export.csv': () =>
        new Response('section,key\n', { status: 200, headers: { 'Content-Type': 'text/csv' } }),
    });
    renderWithApp(<AnalyticsPage />, { route: '/manage/analytics', path: '/manage/analytics' });
    expect(await screen.findByRole('region', { name: /Reach \(estimated\)/ })).toBeInTheDocument();
    expect(await screen.findByRole('img', { name: 'Retention messages by kind' })).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Platform'), 'TikTok');
    await user.click(screen.getByRole('button', { name: 'Export CSV' }));
    const exportCall = await vi.waitFor(() => {
      const call = calls.find((c) => c.path === '/analytics/overview/export.csv');
      if (!call) throw new Error('not yet');
      return call;
    });
    expect(exportCall).toBeTruthy();
    const url = vi.mocked(fetch).mock.calls.find(([u]) => String(u).includes('export.csv'))![0] as string;
    expect(url).toContain('platform=TikTok');
    expect(url).toMatch(/from=\d{4}-\d{2}-\d{2}T00%3A00%3A00Z/);
    await vi.waitFor(() => expect(click).toHaveBeenCalled());
    click.mockRestore();
  });
});
