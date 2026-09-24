import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { Onboarding, ProjectTemplate, Timesheet } from '../shared/deliveryTypes';
import { OnboardingTab } from './clientTabs';
import { TemplatesPage } from './TemplatesPage';
import { mockStaffApi, timeEntry } from './testData';
import { TimePage } from './TimePage';

function projectTemplate(overrides: Partial<ProjectTemplate & { builtIn: boolean }> = {}): ProjectTemplate & { builtIn: boolean } {
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
        json(200, [projectTemplate(), projectTemplate({ id: 'pt2', key: 'custom-sprint', name: 'Custom sprint', builtIn: false })]),
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
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/agency/templates/projects/pt2')).toBe(true));
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
      expect(calls.find((c) => c.path === '/agency/time/timesheets/w1/reopen')?.body).toEqual({ concurrencyStamp: 'ws1' }),
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
