import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import type { ClientDetail, Meeting } from '../shared/deliveryTypes';
import { ClientDetailPage } from './ClientDetailPage';
import { MeetingsTab } from './clientTabs';
import { AM_PERMISSIONS, mockStaffApi } from './testData';

/** The demo Finance role: clients.view and time.view_all, but neither projects.view nor time.track. */
const FINANCE_PERMISSIONS = ['billing.view', 'billing.manage', 'clients.view', 'time.view_all'];
/** The demo Sales role: clients.view only (no projects, no time). */
const SALES_PERMISSIONS = ['billing.view', 'clients.view', 'crm.view', 'crm.manage', 'proposals.manage'];

const client: ClientDetail = {
  id: 'c1',
  name: 'Nimbus Fitness',
  slug: 'nimbus-fitness',
  summary: null,
  industry: 'Fitness',
  website: null,
  countryCode: 'US',
  timeZone: 'UTC',
  currency: 'USD',
  status: 'Active',
  statusReason: null,
  statusChangedAt: null,
  accountManager: null,
  logoFileId: null,
  logoUrl: null,
  billingContactName: null,
  billingEmail: null,
  billingAddress: null,
  taxId: null,
  notes: null,
  approvalSlaDays: 3,
  autoApproveAfterDays: null,
  lastInvoicePaidAt: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  concurrencyStamp: 's1',
};

function renderClient(permissions: string[], tab = 'overview') {
  const mocks = mockStaffApi({ 'GET /agency/clients/c1': () => json(200, client) }, permissions);
  renderWithApp(<ClientDetailPage />, {
    route: `/agency/clients/c1?tab=${tab}`,
    path: '/agency/clients/:clientId',
  });
  return mocks;
}

describe('Client detail tabs', () => {
  it.each([
    ['finance', FINANCE_PERMISSIONS],
    ['sales', SALES_PERMISSIONS],
  ])(
    'hides the Projects and Time tabs from %s, whose API calls there would be refused',
    async (_, permissions) => {
      const { calls } = renderClient(permissions, 'time');
      const tabs = await screen.findByRole('tablist', { name: 'Client sections' });
      expect(tabs).toHaveTextContent('Overview');
      expect(screen.queryByRole('tab', { name: 'Projects' })).not.toBeInTheDocument();
      expect(screen.queryByRole('tab', { name: 'Time' })).not.toBeInTheDocument();
      // ?tab=time falls back to the overview instead of a hidden tab.
      expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
      expect(
        calls.some((c) => c.path.startsWith('/agency/projects') || c.path.startsWith('/agency/time')),
      ).toBe(false);
    },
  );

  it('shows the Projects and Time tabs to an account manager', async () => {
    renderClient(AM_PERMISSIONS);
    expect(await screen.findByRole('tab', { name: 'Projects' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Time' })).toBeInTheDocument();
  });
});

const meeting: Meeting = {
  id: 'm1',
  clientId: 'c1',
  clientName: 'Nimbus Fitness',
  projectId: null,
  title: 'September review',
  kind: 'MonthlyReview',
  startsAt: '2026-09-01T10:00:00Z',
  durationMinutes: 30,
  location: null,
  agenda: null,
  notes: null,
  status: 'Held',
  attendees: [],
  actionItems: [{ id: 'a1', text: 'Send the Q4 plan', assigneeUserId: null, dueDate: null, taskId: null }],
  concurrencyStamp: 's1',
};

describe('Client meetings tab', () => {
  function renderMeetings(permissions: string[]) {
    const mocks = mockStaffApi(
      {
        'GET /agency/meetings': () => json(200, [meeting]),
        'GET /agency/projects': () => json(200, { items: [], page: 1, pageSize: 200, totalCount: 0 }),
      },
      permissions,
    );
    renderWithApp(<MeetingsTab clientId="c1" />);
    return mocks;
  }

  it('is read-only for finance: no schedule, edit or create-task controls the API would refuse', async () => {
    const { calls } = renderMeetings(FINANCE_PERMISSIONS);
    expect(await screen.findByRole('article', { name: 'September review' })).toBeInTheDocument();
    expect(screen.getByText('Send the Q4 plan')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Schedule meeting' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Edit/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Create task/ })).not.toBeInTheDocument();
    expect(calls.some((c) => c.path.startsWith('/agency/projects'))).toBe(false);
  });

  it('lets an account manager schedule, edit and turn action items into tasks', async () => {
    renderMeetings(AM_PERMISSIONS);
    expect(await screen.findByRole('button', { name: 'Schedule meeting' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit September review' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create task for Send the Q4 plan' })).toBeInTheDocument();
  });
});
