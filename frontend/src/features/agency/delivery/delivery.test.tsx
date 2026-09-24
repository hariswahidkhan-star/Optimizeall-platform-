import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClientProvider } from '@tanstack/react-query';
import { matchRoutes } from 'react-router-dom';
import { afterEach, describe, expect, it } from 'vitest';
import { routes as appRoutes } from '@/app/router';
import { json } from '@/test/fetchMock';
import { axeViolations, renderWithApp, testQueryClient } from '@/test/render';
import { setViewportWidth } from '@/test/viewport';
import { registerDashboardTile, unregisterDashboardTile } from '../shared/dashboardTiles';
import fixture from '../shared/deliveryLinks.fixture.json';
import { ReportView } from '../shared/ReportView';
import { DashboardPage } from './DashboardPage';
import { Kanban } from './Kanban';
import { dashboard, DESIGNER_PERMISSIONS, mockStaffApi, report, task, timeEntry } from './testData';
import { TaskDrawer } from './TaskDrawer';
import { TimerWidget } from './TimerWidget';

describe('delivery links (notifications) resolve to real routes', () => {
  it.each(fixture.links.map((l) => [l.name, l.path] as const))('%s', (_, pattern) => {
    const path = pattern.split('?')[0]!.replace(/:\w+/g, '0f8fad5b-d9cb-469f-a165-70867728950e');
    const matches = matchRoutes(appRoutes, path);
    expect(matches, path).not.toBeNull();
    expect(matches![matches!.length - 1]!.route.path, `${path} falls through to NotFound`).not.toBe('*');
  });
});

describe('Agency dashboard', () => {
  afterEach(() => unregisterDashboardTile('test.pipeline'));

  it('shows my work, the account-manager panel and tiles registered by other areas', async () => {
    mockStaffApi({ 'GET /agency/dashboard': () => json(200, dashboard()), 'GET /agency/time/timer': () => json(204) });
    registerDashboardTile({ id: 'test.pipeline', title: 'Pipeline value', order: 10, Component: () => <p>$120k open</p> });
    const { container } = renderWithApp(<DashboardPage />, { route: '/agency', path: '/agency' });
    expect(await screen.findByRole('list', { name: 'My tasks due soon' })).toHaveTextContent('Write two blog posts');
    expect(screen.getByRole('list', { name: 'Deliverables awaiting my review' })).toHaveTextContent('Retargeting creatives');
    expect(screen.getByRole('list', { name: 'Client health board' })).toHaveTextContent('6 overdue tasks');
    expect(screen.getByText('Red · 40')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Pipeline value' })).toHaveTextContent('$120k open');
    expect(screen.queryByRole('region', { name: 'Agency snapshot' })).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('hides account-manager sections for delivery staff and respects tile permissions', async () => {
    mockStaffApi({ 'GET /agency/dashboard': () => json(200, dashboard({ accountManager: null })), 'GET /agency/time/timer': () => json(204) }, DESIGNER_PERMISSIONS);
    registerDashboardTile({ id: 'test.pipeline', title: 'Pipeline value', requires: { anyOf: ['crm.view'] }, Component: () => <p>secret</p> });
    renderWithApp(<DashboardPage />, { route: '/agency', path: '/agency' });
    await screen.findByRole('list', { name: 'My tasks due soon' });
    expect(screen.queryByRole('list', { name: 'Client health board' })).not.toBeInTheDocument();
    expect(screen.queryByText('secret')).not.toBeInTheDocument();
  });
});

function renderKanban(tasks = [task({ id: 't1', title: 'Crawl the site' }), task({ id: 't2', title: 'Fix redirects', sortOrder: 2000 }), task({ id: 't3', title: 'Keyword review', status: 'InProgress' })]) {
  const client = testQueryClient();
  client.setQueryData(['delivery', 'project', 'p1', 'tasks'], tasks);
  return render(
    <QueryClientProvider client={client}>
      <Kanban projectId="p1" tasks={tasks} canEdit onOpen={() => undefined} />
    </QueryClientProvider>,
  );
}

describe('Kanban', () => {
  it('moves a task to the next column with the keyboard and announces it', async () => {
    const { calls } = mockStaffApi({
      'POST /agency/tasks/t1/move': (req) => json(200, task({ id: 't1', status: (req.body as { status: 'InProgress' }).status, sortOrder: 5000 })),
      'GET /agency/projects/p1/tasks': () => json(200, []),
    });
    renderKanban();
    const card = screen.getByRole('button', { name: 'Crawl the site' });
    card.focus();
    fireEvent.keyDown(card, { key: 'ArrowRight' });
    await waitFor(() => expect(calls.some((c) => c.path === '/agency/tasks/t1/move')).toBe(true));
    const move = calls.find((c) => c.path === '/agency/tasks/t1/move')!;
    expect(move.body).toEqual({ status: 'InProgress', afterTaskId: 't3', concurrencyStamp: 'stamp-1' });
    expect(await screen.findByText(/Moved Crawl the site to In progress/)).toBeInTheDocument();
  });

  it('reorders within a column with arrow up/down', async () => {
    const { calls } = mockStaffApi({ 'POST /agency/tasks/t2/move': () => json(200, task({ id: 't2', sortOrder: 500 })) });
    renderKanban();
    const card = screen.getByRole('button', { name: 'Fix redirects' });
    fireEvent.keyDown(card, { key: 'ArrowUp' });
    await waitFor(() => expect(calls.find((c) => c.path === '/agency/tasks/t2/move')?.body).toEqual({ status: 'Todo', afterTaskId: null, concurrencyStamp: 'stamp-1' }));
  });

  it('shows one column with a column picker and per-card move select at 360px', async () => {
    setViewportWidth(360);
    mockStaffApi({});
    const { container } = renderKanban();
    expect(screen.getByLabelText('Column')).toBeInTheDocument();
    expect(container.querySelector('[data-layout="single"]')).not.toBeNull();
    expect(screen.getAllByRole('region')).toHaveLength(1);
    expect(screen.getByRole('combobox', { name: 'Move Crawl the site to' })).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Column'), 'InProgress');
    expect(screen.getByRole('button', { name: 'Keyword review' })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Timer', () => {
  it('starts a timer for a project, shows it running and stops it', async () => {
    let running: ReturnType<typeof timeEntry> | null = null;
    const { calls } = mockStaffApi({
      'GET /agency/time/timer': () => (running ? json(200, running) : json(204)),
      'GET /agency/projects': () =>
        json(200, { items: [{ id: 'p1', name: 'SEO retainer', clientName: 'Nimbus Fitness', status: 'Active' }], total: 1, page: 1, pageSize: 200, totalPages: 1 }),
      'POST /agency/time/timer/start': () => {
        running = timeEntry();
        return json(200, running);
      },
      'POST /agency/time/timer/stop': () => {
        const stopped = { ...running!, isRunning: false, minutes: 1 };
        running = null;
        return json(200, stopped);
      },
    });
    renderWithApp(<TimerWidget />, { route: '/agency/time', path: '/agency/time' });
    const project = await screen.findByLabelText(/Project/);
    await screen.findByRole('option', { name: 'Nimbus Fitness — SEO retainer' });
    await userEvent.selectOptions(project, 'p1');
    await userEvent.type(screen.getByLabelText(/What are you working on/), 'Keyword research');
    await userEvent.click(screen.getByRole('button', { name: 'Start timer' }));
    expect(await screen.findByRole('timer', { name: 'Elapsed time' })).toHaveTextContent(/0:01:0\d/);
    expect(calls.find((c) => c.path === '/agency/time/timer/start')?.body).toEqual({ projectId: 'p1', note: 'Keyword research', billable: true });
    await userEvent.click(screen.getByRole('button', { name: 'Stop timer' }));
    expect(await screen.findByRole('button', { name: 'Start timer' })).toBeInTheDocument();
  });

  it('shows the API error when a timer is already running elsewhere', async () => {
    mockStaffApi({
      'GET /agency/time/timer': () => json(204),
      'GET /agency/projects': () => json(200, { items: [{ id: 'p1', name: 'SEO', clientName: 'Nimbus', status: 'Active' }], total: 1, page: 1, pageSize: 200, totalPages: 1 }),
      'POST /agency/time/timer/start': () => json(409, { status: 409, code: 'time.timer_running', title: 'You already have a timer running. Stop it before starting another.' }),
    });
    renderWithApp(<TimerWidget />, { route: '/agency/time', path: '/agency/time' });
    await screen.findByRole('option', { name: 'Nimbus — SEO' });
    await userEvent.selectOptions(screen.getByLabelText(/Project/), 'p1');
    await userEvent.click(screen.getByRole('button', { name: 'Start timer' }));
    expect(await screen.findByText(/already have a timer running/)).toBeInTheDocument();
  });
});

describe('Report preview', () => {
  it('labels every KPI with its measurement and source, and explains empty data sources to staff only', async () => {
    const { container, rerender } = render(<ReportView report={report()} audience="staff" />);
    const kpis = within(screen.getByRole('list', { name: 'SEO KPIs' })).getAllByRole('listitem');
    expect(kpis).toHaveLength(3);
    expect(within(kpis[0]!).getByText('Measured')).toBeInTheDocument();
    expect(within(kpis[0]!).getByText('Source: Google Analytics 4')).toBeInTheDocument();
    expect(within(kpis[0]!).getByText(/▲.*vs\. last period/)).toBeInTheDocument();
    expect(within(kpis[1]!).getByText('Estimated')).toBeInTheDocument();
    expect(within(kpis[2]!).getByText('Manual')).toBeInTheDocument();
    expect(within(kpis[2]!).getByText('41%')).toBeInTheDocument();
    expect(screen.getByText('No data source is connected for this section yet.')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    rerender(<ReportView report={report()} audience="client" />);
    expect(screen.queryByRole('heading', { name: 'Email marketing' })).not.toBeInTheDocument();
    expect(screen.queryByText(/No data source/)).not.toBeInTheDocument();
  });
});

describe('dashboard tile registry', () => {
  it('orders tiles and replaces by id', async () => {
    const { dashboardTiles } = await import('../shared/dashboardTiles');
    act(() => {
      registerDashboardTile({ id: 'a', title: 'B tile', order: 5, Component: () => null });
      registerDashboardTile({ id: 'b', title: 'A tile', order: 1, Component: () => null });
      registerDashboardTile({ id: 'a', title: 'B tile v2', order: 5, Component: () => null });
    });
    expect(dashboardTiles().map((t) => t.title)).toEqual(['A tile', 'B tile v2']);
    unregisterDashboardTile('a');
    unregisterDashboardTile('b');
  });
});

describe('Task drawer', () => {
  it('confirms a saved task', async () => {
    const detail = {
      task: task(),
      description: null,
      checklist: [],
      comments: [],
      watchers: [],
      blockedBy: [],
      blocking: [],
      attachments: [],
      hoursLogged: 0,
      createdAt: '2026-09-01T00:00:00Z',
      completedAt: null,
      iWatch: false,
    };
    const { calls } = mockStaffApi({
      'GET /agency/tasks/t1': () => json(200, detail),
      'GET /agency/staff': () => json(200, []),
      'PUT /agency/tasks/t1': () => json(200, { ...detail, task: task({ title: 'Renamed task' }) }),
    });
    renderWithApp(<TaskDrawer taskId="t1" projectId="p1" clientId="c1" onClose={() => {}} />);
    const title = await screen.findByLabelText(/^Title/);
    await userEvent.clear(title);
    await userEvent.type(title, 'Renamed task');
    await userEvent.click(screen.getByRole('button', { name: /^Save/ }));
    expect(await screen.findByText('Task saved')).toBeInTheDocument();
    expect(calls.filter((c) => c.method === 'PUT' && c.path === '/agency/tasks/t1')).toHaveLength(1);
  });
});
