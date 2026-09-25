import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { deliverableDetail, report } from '@/features/agency/delivery/testData';
import type { ClientHome, MyOrganization } from '@/features/agency/shared/deliveryTypes';
import { json, makeUser, mockFetch, problem, session, type MockRequest } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { setViewportWidth } from '@/test/viewport';
import { ApprovalDetailPage } from './ApprovalsPages';
import { ClientHomePage } from './ClientHomePage';
import { ClientMessagesPage, ClientReportPage } from './ClientPages';
import { ORG_STORAGE_KEY } from './useClientOrg';

const nimbus: MyOrganization = {
  clientId: 'org-n',
  name: 'Nimbus Fitness',
  slug: 'nimbus-fitness',
  status: 'Active',
  role: 'Approver',
  logoUrl: null,
  currency: 'USD',
  timeZone: 'UTC',
};
const aurora: MyOrganization = {
  clientId: 'org-a',
  name: 'Aurora Skincare',
  slug: 'aurora-skincare',
  status: 'Active',
  role: 'Viewer',
  logoUrl: null,
  currency: 'AED',
  timeZone: 'UTC',
};

type Handler = (req: MockRequest) => Response | Promise<Response>;

function mockClientApi(routes: Record<string, Handler>, orgs: MyOrganization[] = [nimbus, aurora]) {
  const user = makeUser({
    id: 'cu-1',
    displayName: 'Taylor Reed',
    roles: ['Client'],
    permissions: ['client.portal'],
  });
  return mockFetch({
    'POST /auth/refresh': () => json(200, session(user)),
    'GET /client/orgs': () => json(200, orgs),
    ...routes,
  });
}

function home(org: MyOrganization, overrides: Partial<ClientHome> = {}): ClientHome {
  return {
    organization: org,
    onboarding: {
      items: [
        {
          id: 'o1',
          key: 'ga4-access',
          title: 'Google Analytics 4 access',
          description: 'Add the agency as an Editor.',
          category: 'Access',
          owner: 'Client',
          sortOrder: 0,
          status: 'Pending',
          completedAt: null,
          completedBy: null,
          note: null,
          completedOnBehalfOfClient: false,
        },
        {
          id: 'o2',
          key: 'kickoff-call',
          title: 'Kickoff call held',
          description: null,
          category: 'Kickoff',
          owner: 'Agency',
          sortOrder: 1,
          status: 'Done',
          completedAt: '2026-09-01T10:00:00Z',
          completedBy: 'Amira',
          note: null,
          completedOnBehalfOfClient: false,
        },
      ],
      done: 1,
      total: 2,
      percentComplete: 50,
    },
    awaitingApproval: [deliverableDetail().deliverable],
    recentDeliverables: [],
    latestReport: null,
    upcomingMeetings: [],
    threads: [],
    team: [
      {
        userId: 'am-1',
        displayName: 'Amira Haddad',
        email: 'am@demo.optimizeall.app',
        roles: ['AccountManager'],
        isAccountManager: true,
        isPrimary: true,
      },
    ],
    projects: [],
    nps: { period: '2026-Q3', due: false, myScore: 9 },
    canApprove: org.role !== 'Viewer',
    ...overrides,
  };
}

describe('Client home and org switcher', () => {
  it('shows onboarding, approvals and the account team, and switches organization', async () => {
    const { calls } = mockClientApi({
      'GET /client/orgs/org-n/home': () => json(200, home(nimbus)),
      'GET /client/orgs/org-a/home': () => json(200, home(aurora, { awaitingApproval: [] })),
    });
    const { container } = renderWithApp(<ClientHomePage />, { route: '/client?org=org-n', path: '/client' });
    expect(
      await screen.findByRole('list', { name: 'Deliverables awaiting your approval' }),
    ).toHaveTextContent('Hero banner');
    expect(screen.getByText('Google Analytics 4 access')).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Account team' })).toHaveTextContent('Amira Haddad');
    expect(screen.getByRole('link', { name: /am@demo.optimizeall.app/ })).toHaveAttribute(
      'href',
      'mailto:am@demo.optimizeall.app',
    );
    expect(await axeViolations(container)).toEqual([]);

    const switcher = screen.getByRole('combobox', { name: 'Organization' });
    expect(switcher).toHaveValue('org-n');
    await userEvent.selectOptions(switcher, 'org-a');
    expect(await screen.findByText("You're all caught up")).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/client/orgs/org-a/home')).toBe(true);
    expect(window.localStorage.getItem(ORG_STORAGE_KEY)).toBe('org-a');
  });

  it('hides the switcher for a single organization and falls back to it without ?org', async () => {
    window.localStorage.removeItem(ORG_STORAGE_KEY);
    mockClientApi({ 'GET /client/orgs/org-n/home': () => json(200, home(nimbus)) }, [nimbus]);
    renderWithApp(<ClientHomePage />, { route: '/client', path: '/client' });
    await screen.findByRole('list', { name: 'Deliverables awaiting your approval' });
    expect(screen.queryByRole('combobox', { name: 'Organization' })).not.toBeInTheDocument();
  });

  it('asks for the quarterly NPS when due', async () => {
    const { calls } = mockClientApi(
      {
        'GET /client/orgs/org-n/home': () =>
          json(200, home(nimbus, { nps: { period: '2026-Q3', due: true, myScore: null } })),
        'POST /client/orgs/org-n/feedback/nps': () =>
          json(200, { period: '2026-Q3', due: false, myScore: 9 }),
      },
      [nimbus],
    );
    renderWithApp(<ClientHomePage />, { route: '/client', path: '/client' });
    const survey = await screen.findByRole('group', { name: /How likely are you to recommend us/ });
    await userEvent.click(within(survey).getByLabelText('9'));
    await userEvent.click(screen.getByRole('button', { name: 'Send feedback' }));
    expect(await screen.findByText('Thank you!')).toBeInTheDocument();
    expect(calls.find((c) => c.path === '/client/orgs/org-n/feedback/nps')?.body).toEqual({
      score: 9,
      comment: null,
    });
  });
});

describe('Client home: steps done on the client’s behalf', () => {
  it('names client steps the agency marked done for the client', async () => {
    const recently = new Date(Date.now() - 2 * 86_400_000).toISOString();
    const base = home(nimbus);
    mockClientApi(
      {
        'GET /client/orgs/org-n/home': () =>
          json(
            200,
            home(nimbus, {
              onboarding: {
                ...base.onboarding,
                items: [
                  {
                    ...base.onboarding.items[0]!,
                    status: 'Done',
                    completedAt: recently,
                    completedBy: 'Amira Haddad',
                    completedOnBehalfOfClient: true,
                  },
                  base.onboarding.items[1]!,
                ],
                done: 2,
                percentComplete: 100,
              },
            }),
          ),
      },
      [nimbus],
    );
    const { container } = renderWithApp(<ClientHomePage />, { route: '/client', path: '/client' });
    const list = await screen.findByRole('list', { name: 'Steps done on your behalf' });
    expect(list).toHaveTextContent('Google Analytics 4 access — marked done by Amira Haddad');
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Client messages by duty', () => {
  const threads = [
    {
      id: 'th1',
      clientId: 'org-a',
      subject: 'Launch plan',
      projectId: null,
      lastMessageAt: '2026-09-01T10:00:00Z',
      messageCount: 1,
      unreadCount: 0,
      lastMessagePreview: 'Hello',
      lastAuthor: 'Amira Haddad',
      isInternal: false,
    },
  ];

  it('is read-only for a Viewer: no composer, with an explanation', async () => {
    mockClientApi({ 'GET /client/orgs/org-a/threads/paged': () => json(200, { items: threads, total: threads.length, page: 1, pageSize: 50, totalPages: 1 }) }, [aurora]);
    renderWithApp(<ClientMessagesPage />, { route: '/client/messages', path: '/client/messages' });
    expect(await screen.findByText(/Your role is read-only here/)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Launch plan' })).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'New conversation' })).not.toBeInTheDocument();
  });

  it('lets an Approver start a conversation', async () => {
    mockClientApi({ 'GET /client/orgs/org-n/threads/paged': () => json(200, { items: [], total: 0, page: 1, pageSize: 50, totalPages: 0 }) }, [nimbus]);
    renderWithApp(<ClientMessagesPage />, { route: '/client/messages', path: '/client/messages' });
    expect(await screen.findByRole('form', { name: 'New conversation' })).toBeInTheDocument();
    expect(screen.queryByText(/Your role is read-only here/)).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /Internal/ })).not.toBeInTheDocument();
  });
});

const renderApproval = () =>
  renderWithApp(<ApprovalDetailPage />, {
    route: '/client/approvals/d1?org=org-n',
    path: '/client/approvals/:deliverableId',
  });

describe('Client approvals', () => {
  it('approves the reviewed version and can then rate it', async () => {
    const { calls } = mockClientApi({
      'GET /client/orgs/org-n/deliverables/d1': () => json(200, deliverableDetail()),
      'POST /client/orgs/org-n/deliverables/d1/approve': () =>
        json(
          200,
          deliverableDetail(
            {
              approvedVersion: 2,
              approvedByName: 'Taylor Reed',
              allowedActions: ['comment', 'rate'],
              history: [
                {
                  id: 'h1',
                  versionNumber: 2,
                  stage: 'Client',
                  decision: 'Approved',
                  userName: 'Taylor Reed',
                  comment: 'Great',
                  createdAt: '2026-09-23T10:00:00Z',
                },
              ],
            },
            { status: 'Approved', approvedAt: '2026-09-23T10:00:00Z' },
          ),
        ),
      'POST /client/orgs/org-n/deliverables/d1/csat': () => json(204),
    });
    const { container } = renderApproval();
    // Side-by-side compare: latest version and the previous one with its pinned client comment.
    expect(await screen.findByRole('region', { name: 'Showing: version 2' })).toBeInTheDocument();
    expect(
      within(screen.getByRole('region', { name: 'Compare with: version 1' })).getByText(
        'Use the lighter logo',
      ),
    ).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    await userEvent.type(screen.getByRole('textbox', { name: /^Comment/ }), 'Great');
    await userEvent.click(screen.getByRole('button', { name: 'Approve version 2' }));
    expect(await screen.findByRole('status', { name: 'Approved' })).toHaveTextContent(
      'Version 2 was approved by Taylor Reed',
    );
    expect(calls.find((c) => c.path.endsWith('/approve'))?.body).toEqual({ version: 2, comment: 'Great' });

    const rating = screen.getByRole('group', { name: /How satisfied are you/ });
    await userEvent.click(within(rating).getByLabelText('5'));
    await userEvent.click(screen.getByRole('button', { name: 'Send rating' }));
    expect(await screen.findByText('Thanks for rating this deliverable.')).toBeInTheDocument();
  });

  it('requires a comment to request changes', async () => {
    const { calls } = mockClientApi({
      'GET /client/orgs/org-n/deliverables/d1': () => json(200, deliverableDetail()),
      'POST /client/orgs/org-n/deliverables/d1/request-changes': () =>
        json(200, deliverableDetail({ allowedActions: ['comment'] }, { status: 'ChangesRequested' })),
    });
    renderApproval();
    const request = await screen.findByRole('button', { name: 'Request changes' });
    expect(request).toBeDisabled();
    await userEvent.type(screen.getByRole('textbox', { name: /^Comment/ }), 'Swap the photo');
    await userEvent.click(request);
    await waitFor(() =>
      expect(calls.find((c) => c.path.endsWith('/request-changes'))?.body).toEqual({
        version: 2,
        comment: 'Swap the photo',
      }),
    );
    expect(await screen.findByText('Changes in progress')).toBeInTheDocument();
  });

  it('reloads when the version is outdated', async () => {
    let fresh = false;
    mockClientApi({
      'GET /client/orgs/org-n/deliverables/d1': () =>
        json(200, fresh ? deliverableDetail({}, { currentVersion: 3 }) : deliverableDetail()),
      'POST /client/orgs/org-n/deliverables/d1/approve': () => {
        fresh = true;
        return problem(
          409,
          'deliverable.stale_version',
          'Version 2 is outdated; the latest version is 3. Reload to review it.',
        );
      },
    });
    renderApproval();
    await userEvent.click(await screen.findByRole('button', { name: 'Approve version 2' }));
    expect(await screen.findByText(/Version 2 is outdated/)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Approve version 3' })).toBeInTheDocument();
  });

  it('is read-only for Viewers', async () => {
    mockClientApi(
      {
        'GET /client/orgs/org-a/deliverables/d1': () => json(200, deliverableDetail({ allowedActions: [] })),
      },
      [aurora],
    );
    renderWithApp(<ApprovalDetailPage />, {
      route: '/client/approvals/d1',
      path: '/client/approvals/:deliverableId',
    });
    expect(await screen.findByRole('status', { name: 'View only' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Approve/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Request changes' })).not.toBeInTheDocument();
  });

  it('stacks the version compare and stays accessible at 360px', async () => {
    setViewportWidth(360);
    mockClientApi({ 'GET /client/orgs/org-n/deliverables/d1': () => json(200, deliverableDetail()) });
    const { container } = renderApproval();
    await screen.findByRole('button', { name: 'Approve version 2' });
    expect(container.querySelector('.dl-compare')).not.toBeNull();
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Client report', () => {
  it('shows measurement labels and sources and hides empty sections', async () => {
    mockClientApi({ 'GET /client/orgs/org-n/reports/r1': () => json(200, report()) });
    const { container } = renderWithApp(<ClientReportPage />, {
      route: '/client/reports/r1?org=org-n',
      path: '/client/reports/:reportId',
    });
    const seo = await screen.findByRole('list', { name: 'SEO KPIs' });
    expect(within(seo).getByText('Estimated')).toBeInTheDocument();
    expect(within(seo).getByText('Source: Meta Business Suite (modelled reach)')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Email marketing' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Print' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});
