import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { ClientCampaignItem } from '@/features/agency/email/api/types';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { ClientCampaignPage, ClientEmailPage } from './ClientEmailPages';

const member = makeUser({ id: 'client-1', roles: ['Client'], permissions: ['client.portal'], timeZone: 'UTC' });

const pending: ClientCampaignItem = {
  id: 'c9',
  clientAccountId: 'org-1',
  clientName: 'Nimbus Fitness',
  name: 'Spring launch',
  channel: 'Email',
  status: 'Scheduled',
  approvalStatus: 'Pending',
  subject: 'New classes this spring',
  scheduleMode: 'FixedTime',
  scheduledAt: '2026-10-01T09:00:00Z',
  scheduledLocalTime: null,
  completedAt: null,
  sent: 0,
  uniqueOpens: 0,
  uniqueClicks: 0,
  canApprove: true,
};

const kpis = {
  clientAccountId: 'org-1',
  from: '2026-08-24T00:00:00Z',
  to: '2026-09-23T00:00:00Z',
  campaignsSent: 2,
  emailsSent: 1800,
  delivered: 1790,
  uniqueOpens: 700,
  uniqueClicks: 120,
  openRate: 0.3911,
  clickRate: 0.067,
  unsubscribes: 4,
  complaints: 0,
  hardBounces: 3,
  conversions: 9,
  revenue: [{ currency: 'USD', amount: 810 }],
  smsSent: 0,
  smsCost: 0,
  newSubscribers: 55,
  activeSubscribers: 900,
  automationEmailsSent: 60,
};

describe('Client email portal', () => {
  it('shows KPIs, campaigns and pending approvals for the member organization', async () => {
    mockFetch({
      'POST /auth/refresh': () => json(200, session(member)),
      'GET /client/email/clients': () => json(200, [{ id: 'org-1', name: 'Nimbus Fitness' }]),
      'GET /client/email/kpis': () => json(200, kpis),
      'GET /client/email/campaigns': () => json(200, { items: [pending], total: 1, page: 1, pageSize: 20 }),
      'GET /client/email/approvals': () => json(200, [pending]),
    });
    const { container } = renderWithApp(<ClientEmailPage />, { route: '/client/email' });
    expect(await screen.findByText('39.1%')).toBeInTheDocument();
    const approvals = await screen.findByRole('table', { name: 'Campaigns awaiting approval' });
    expect(within(approvals).getByRole('link', { name: 'Spring launch' })).toHaveAttribute('href', '/client/email/campaigns/c9');
    expect(await axeViolations(container)).toEqual([]);
  });

  it('lets an approver request changes with a note', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(member)),
      'GET /client/email/campaigns/c9/report': () =>
        json(200, { id: 'c9', name: 'Spring launch', channel: 'Email', status: 'Scheduled', subject: 'New classes this spring', sent: 0, recipients: 0 }),
      'GET /client/email/campaigns/c9/preview': () => json(200, { subject: 'New classes this spring', html: '<p>Hi</p>', text: 'Hi', sizeBytes: 10, errors: [], warnings: [] }),
      'GET /client/email/approvals': () => json(200, [pending]),
      'POST /client/email/campaigns/c9/approval': () => json(200, {}),
    });
    renderWithApp(<ClientCampaignPage />, {
      route: '/client/email/campaigns/c9',
      path: '/client/email/campaigns/:id',
      routes: [{ path: '/client/email', element: <h1>Email marketing</h1> }],
    });
    await user.click(await screen.findByRole('button', { name: 'Request changes' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.type(within(dialog).getByLabelText(/What should change/), 'Use the new logo');
    await user.click(within(dialog).getByRole('button', { name: 'Request changes' }));
    await waitFor(() => expect(calls.some((c) => c.path === '/client/email/campaigns/c9/approval')).toBe(true));
    expect(calls.find((c) => c.path === '/client/email/campaigns/c9/approval')!.body).toEqual({ approve: false, note: 'Use the new logo' });
    // The campaign is a draft again (hidden from the portal): back to the list, and it is not fetched again (a 404).
    expect(await screen.findByRole('heading', { name: 'Email marketing' })).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(calls.filter((c) => c.method === 'GET' && c.path === '/client/email/campaigns/c9/report')).toHaveLength(1);
  });
});
