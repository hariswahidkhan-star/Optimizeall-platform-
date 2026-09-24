import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { BrandKit, Onboarding, ProjectTemplate, TaskDetail, Timesheet } from '../shared/deliveryTypes';
import { BrandKitTab, OnboardingTab } from './clientTabs';
import { TaskDrawer } from './TaskDrawer';
import { TemplatesPage } from './TemplatesPage';
import { DESIGNER_PERMISSIONS, mockStaffApi, task, timeEntry } from './testData';
import { TimePage } from './TimePage';

function projectTemplate(
  overrides: Partial<ProjectTemplate & { builtIn: boolean }> = {},
): ProjectTemplate & { builtIn: boolean } {
  return {
    id: 'pt1',
    key: 'seo-retainer',
    name: 'SEO monthly retainer',
    description: null,
    projectType: 'SeoProgram',
    serviceLines: ['seo'],
    defaultBudgetHours: 20,
    durationDays: 30,
    milestones: [],
    tasks: [],
    recurring: [],
    isActive: true,
    concurrencyStamp: 's1',
    builtIn: true,
    ...overrides,
  };
}

describe('Templates page', () => {
  it('protects built-in templates and deletes custom ones after confirmation', async () => {
    const user = userEvent.setup();
    const { calls } = mockStaffApi({
      'GET /agency/templates/projects': () =>
        json(200, [
          projectTemplate(),
          projectTemplate({ id: 'pt2', key: 'custom-sprint', name: 'Custom sprint', builtIn: false }),
        ]),
      'GET /agency/templates/briefs': () => json(200, []),
      'GET /agency/templates/reports': () => json(200, []),
      'DELETE /agency/templates/projects/pt2': () => json(204),
    });
    const { container } = renderWithApp(<TemplatesPage />, { route: '/agency/templates' });
    const builtIn = await screen.findByRole('article', { name: 'SEO monthly retainer' });
    expect(within(builtIn).getByText('Built-in')).toBeInTheDocument();
    expect(within(builtIn).queryByRole('button', { name: /Delete/ })).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    const custom = screen.getByRole('article', { name: 'Custom sprint' });
    await user.click(within(custom).getByRole('button', { name: 'Delete Custom sprint' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Delete the template “Custom sprint”/ });
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() =>
      expect(calls.some((c) => c.method === 'DELETE' && c.path === '/agency/templates/projects/pt2')).toBe(
        true,
      ),
    );
  });
});

const onboarding: Onboarding = {
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
  ],
  done: 0,
  total: 1,
  percentComplete: 0,
};

describe('Client onboarding checklist', () => {
  it('edits a step’s details and removes a step with confirmation', async () => {
    const user = userEvent.setup();
    const { calls } = mockStaffApi({
      'GET /agency/clients/c1/onboarding': () => json(200, onboarding),
      'PUT /agency/clients/c1/onboarding/o1/details': () => json(200, onboarding),
      'DELETE /agency/clients/c1/onboarding/o1': () => json(200, { ...onboarding, items: [] }),
    });
    const { container } = renderWithApp(<OnboardingTab clientId="c1" />);
    await user.click(await screen.findByRole('button', { name: 'Edit Google Analytics 4 access' }));
    const dialog = await screen.findByRole('dialog', { name: 'Edit onboarding step' });
    const title = within(dialog).getByLabelText(/Title/);
    await user.clear(title);
    await user.type(title, 'GA4 editor access');
    expect(await axeViolations(dialog)).toEqual([]);
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
        title: 'GA4 editor access',
        description: 'Add the agency as an Editor.',
        category: 'Access',
        owner: 'Client',
      }),
    );

    await user.click(screen.getByRole('button', { name: 'Remove Google Analytics 4 access' }));
    const confirm = await screen.findByRole('alertdialog', { name: /Remove “Google Analytics 4 access”/ });
    await user.click(within(confirm).getByRole('button', { name: 'Remove step' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE')).toBe(true));
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Client-owned onboarding steps on the client’s behalf', () => {
  const agencyStep = {
    ...onboarding.items[0]!,
    id: 'o2',
    key: 'kickoff-call',
    title: 'Kickoff call held',
    owner: 'Agency' as const,
  };
  const withBoth: Onboarding = { ...onboarding, items: [onboarding.items[0]!, agencyStep], total: 2 };

  it('confirms before ticking a client step and records it on the client’s behalf; agency steps save directly', async () => {
    const user = userEvent.setup();
    const done: Onboarding = {
      ...withBoth,
      items: [
        {
          ...onboarding.items[0]!,
          status: 'Done',
          completedAt: '2026-09-20T10:00:00Z',
          completedBy: 'Amira Haddad',
          completedOnBehalfOfClient: true,
        },
        agencyStep,
      ],
      done: 1,
      percentComplete: 50,
    };
    const { calls } = mockStaffApi({
      'GET /agency/clients/c1/onboarding': () => json(200, withBoth),
      'POST /agency/clients/c1/onboarding/o1/on-behalf': () => json(200, done),
      'PUT /agency/clients/c1/onboarding/o2': () => json(200, done),
    });
    const { container } = renderWithApp(<OnboardingTab clientId="c1" />);
    await user.selectOptions(await screen.findByLabelText('Status of Google Analytics 4 access'), 'Done');
    const confirm = await screen.findByRole('alertdialog', { name: /done on the client’s behalf\?/ });
    expect(confirm).toHaveTextContent('The client will see that you completed it for them');
    expect(calls.some((c) => c.path.includes('/on-behalf'))).toBe(false);
    await user.click(within(confirm).getByRole('button', { name: 'Mark done for the client' }));
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/agency/clients/c1/onboarding/o1/on-behalf')?.body).toEqual({
        done: true,
      }),
    );
    expect(await screen.findByText(/by Amira Haddad on behalf of the client/)).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    await user.selectOptions(screen.getByLabelText('Status of Kickoff call held'), 'Done');
    await waitFor(() =>
      expect(
        calls.find((c) => c.method === 'PUT' && c.path === '/agency/clients/c1/onboarding/o2')?.body,
      ).toEqual({ status: 'Done' }),
    );
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('un-ticks a client step on the client’s behalf after confirmation', async () => {
    const user = userEvent.setup();
    const done: Onboarding = {
      ...onboarding,
      items: [
        {
          ...onboarding.items[0]!,
          status: 'Done',
          completedAt: '2026-09-20T10:00:00Z',
          completedBy: 'Amira Haddad',
          completedOnBehalfOfClient: true,
        },
      ],
      done: 1,
      percentComplete: 100,
    };
    const { calls } = mockStaffApi({
      'GET /agency/clients/c1/onboarding': () => json(200, done),
      'POST /agency/clients/c1/onboarding/o1/on-behalf': () => json(200, onboarding),
    });
    renderWithApp(<OnboardingTab clientId="c1" />);
    await user.selectOptions(await screen.findByLabelText('Status of Google Analytics 4 access'), 'Pending');
    const confirm = await screen.findByRole('alertdialog', { name: /not done again\?/ });
    await user.click(within(confirm).getByRole('button', { name: 'Mark not done' }));
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/agency/clients/c1/onboarding/o1/on-behalf')?.body).toEqual({
        done: false,
      }),
    );
  });
});

const brandKit: BrandKit = {
  clientAccountId: 'c1',
  colors: [],
  fonts: [],
  toneOfVoice: null,
  personas: [],
  competitors: [],
  dos: [],
  donts: [],
  keyMessages: [],
  assets: [
    {
      id: 'a1',
      kind: 'Guideline',
      label: 'Brand book',
      fileId: 'f1',
      fileName: 'brand-book.pdf',
      contentType: 'application/pdf',
      sizeBytes: 1200,
      staffUrl: '/api/v1/agency/files/f1',
      clientUrl: '/api/v1/client/orgs/c1/files/f1',
      createdAt: '2026-09-01T00:00:00Z',
    },
  ],
  updatedAt: '2026-09-01T00:00:00Z',
  concurrencyStamp: 'b1',
};

describe('Removing brand assets', () => {
  it('removes an asset after confirmation (clients.manage)', async () => {
    const user = userEvent.setup();
    const { calls } = mockStaffApi({
      'GET /agency/clients/c1/brand-kit': () => json(200, brandKit),
      'DELETE /agency/clients/c1/brand-kit/assets/a1': () => json(200, { ...brandKit, assets: [] }),
    });
    const { container } = renderWithApp(<BrandKitTab clientId="c1" />);
    await user.click(await screen.findByRole('button', { name: 'Remove Brand book' }));
    const confirm = await screen.findByRole('alertdialog', {
      name: /Remove “Brand book” from the brand kit\?/,
    });
    expect(await axeViolations(confirm)).toEqual([]);
    await user.click(within(confirm).getByRole('button', { name: 'Remove asset' }));
    expect(await screen.findByText('No brand assets yet')).toBeInTheDocument();
    expect(calls.filter((c) => c.method === 'DELETE')).toHaveLength(1);
    expect(await axeViolations(container)).toEqual([]);
  });

  it('shows the error when the asset was already removed by someone else', async () => {
    const user = userEvent.setup();
    mockStaffApi({
      'GET /agency/clients/c1/brand-kit': () => json(200, brandKit),
      'DELETE /agency/clients/c1/brand-kit/assets/a1': () =>
        json(404, {
          title: 'Not found',
          status: 404,
          code: 'not_found',
          detail: 'BrandAsset was not found.',
        }),
    });
    renderWithApp(<BrandKitTab clientId="c1" />);
    await user.click(await screen.findByRole('button', { name: 'Remove Brand book' }));
    const confirm = await screen.findByRole('alertdialog');
    await user.click(within(confirm).getByRole('button', { name: 'Remove asset' }));
    expect(await within(confirm).findByRole('alert')).toBeInTheDocument();
  });

  it('hides the remove action from staff without clients.manage', async () => {
    mockStaffApi({ 'GET /agency/clients/c1/brand-kit': () => json(200, brandKit) }, DESIGNER_PERMISSIONS);
    renderWithApp(<BrandKitTab clientId="c1" />);
    expect(await screen.findByText('Brand book')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove Brand book' })).not.toBeInTheDocument();
  });
});

describe('Removing task attachments', () => {
  it('removes an attachment after confirmation', async () => {
    const user = userEvent.setup();
    const file = {
      id: 'f9',
      fileName: 'keyword-gap.pdf',
      contentType: 'application/pdf',
      sizeBytes: 900,
      createdAt: '2026-09-01T00:00:00Z',
      staffUrl: '/api/v1/agency/files/f9',
      clientUrl: '/api/v1/client/orgs/c1/files/f9',
    };
    const detail: TaskDetail = {
      task: task(),
      description: null,
      checklist: [],
      comments: [],
      watchers: [],
      blockedBy: [],
      blocking: [],
      attachments: [
        {
          id: 'att1',
          file,
          addedBy: { id: 'am-1', displayName: 'Amira Haddad', email: '' },
          createdAt: '2026-09-01T00:00:00Z',
        },
      ],
      hoursLogged: 0,
      createdAt: '2026-09-01T00:00:00Z',
      completedAt: null,
      iWatch: false,
    };
    const { calls } = mockStaffApi({
      'GET /agency/tasks/t1': () => json(200, detail),
      'GET /agency/staff': () => json(200, []),
      'DELETE /agency/tasks/t1/attachments/att1': () => json(200, { ...detail, attachments: [] }),
    });
    renderWithApp(<TaskDrawer taskId="t1" projectId="p1" clientId="c1" onClose={() => {}} />);
    await user.click(await screen.findByRole('button', { name: 'Remove attachment keyword-gap.pdf' }));
    const confirm = await screen.findByRole('alertdialog', {
      name: /Remove “keyword-gap.pdf” from this task\?/,
    });
    await user.click(within(confirm).getByRole('button', { name: 'Remove attachment' }));
    await waitFor(() => expect(screen.queryByText('keyword-gap.pdf')).not.toBeInTheDocument());
    expect(
      calls.filter((c) => c.method === 'DELETE' && c.path === '/agency/tasks/t1/attachments/att1'),
    ).toHaveLength(1);
  });
});

function week(overrides: Partial<Timesheet> = {}): Timesheet {
  return {
    id: 'w1',
    userId: 'am-1',
    userName: 'Amira Haddad',
    weekStart: '2026-09-21',
    status: 'Submitted',
    totalMinutes: 120,
    billableMinutes: 120,
    days: [{ date: '2026-09-21', minutes: 120 }],
    entries: [timeEntry({ isRunning: false, minutes: 120, locked: true })],
    submittedAt: '2026-09-23T10:00:00Z',
    decidedAt: null,
    decidedBy: null,
    decisionComment: null,
    concurrencyStamp: 'ws1',
    ...overrides,
  };
}

describe('Timesheet recall', () => {
  it('lets the owner recall a submitted week with its stamp', async () => {
    const user = userEvent.setup();
    const { calls } = mockStaffApi({
      'GET /agency/time/timer': () => json(204),
      'GET /agency/time/timesheets/week': () => json(200, week()),
      'GET /agency/time/timesheets/pending': () => json(200, []),
      'GET /agency/projects': () => json(200, { items: [], total: 0, page: 1, pageSize: 200, totalPages: 0 }),
      'POST /agency/time/timesheets/w1/reopen': () => json(200, week({ status: 'Open' })),
    });
    const { container } = renderWithApp(<TimePage />, { route: '/agency/time' });
    await user.click(await screen.findByRole('button', { name: 'Recall week' }));
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/agency/time/timesheets/w1/reopen')?.body).toEqual({
        concurrencyStamp: 'ws1',
      }),
    );
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Timesheet approvals', () => {
  it('offers no decision on my own submitted week (someone else approves it) but does on a colleague’s', async () => {
    const user = userEvent.setup();
    mockStaffApi({
      'GET /agency/time/timer': () => json(204),
      'GET /agency/time/timesheets/week': () => json(200, week({ status: 'Open' })),
      'GET /agency/time/timesheets/pending': () =>
        json(200, [week({ id: 'mine' }), week({ id: 'theirs', userId: 'st-1', userName: 'Daniel Okafor' })]),
      'GET /agency/projects': () => json(200, { items: [], total: 0, page: 1, pageSize: 200, totalPages: 0 }),
      'GET /agency/staff': () => json(200, []),
    });
    renderWithApp(<TimePage />, { route: '/agency/time' });
    await user.click(await screen.findByRole('tab', { name: 'Approvals' }));
    const mine = await screen.findByRole('article', { name: /^Amira Haddad, week of/ });
    expect(within(mine).queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(within(mine).queryByRole('button', { name: 'Return for changes' })).not.toBeInTheDocument();
    expect(mine).toHaveTextContent('Someone else must approve your timesheet');
    const theirs = screen.getByRole('article', { name: /^Daniel Okafor, week of/ });
    expect(within(theirs).getByRole('button', { name: 'Approve' })).toBeInTheDocument();
  });
});
