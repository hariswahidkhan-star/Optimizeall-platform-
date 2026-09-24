import type {
  AgencyDashboard,
  DeliverableDetail,
  DeliverableSummary,
  Report,
  TaskSummary,
  TimeEntry,
} from '../shared/deliveryTypes';
import { json, makeUser, mockFetch, session, type MockRequest } from '@/test/fetchMock';

export const AM_PERMISSIONS = [
  'clients.view',
  'clients.manage',
  'projects.view',
  'projects.manage',
  'deliverables.submit',
  'reports.manage',
  'time.track',
  'time.view_all',
];

export const DESIGNER_PERMISSIONS = ['clients.view', 'projects.view', 'deliverables.submit', 'time.track'];

type Handler = (req: MockRequest) => Response | Promise<Response>;

export function mockStaffApi(routes: Record<string, Handler>, permissions = AM_PERMISSIONS) {
  const user = makeUser({ id: 'am-1', displayName: 'Amira Haddad', roles: ['AccountManager'], permissions, timeZone: 'UTC' });
  return mockFetch({ 'POST /auth/refresh': () => json(200, session(user)), ...routes });
}

export function task(overrides: Partial<TaskSummary> = {}): TaskSummary {
  return {
    id: 't1',
    projectId: 'p1',
    projectName: 'SEO retainer',
    clientId: 'c1',
    clientName: 'Nimbus Fitness',
    milestoneId: null,
    title: 'Crawl the site',
    status: 'Todo',
    priority: 'Normal',
    dueDate: '2026-09-30',
    estimateHours: 4,
    labels: [],
    clientVisible: false,
    sortOrder: 1000,
    assignees: [],
    checklistDone: 0,
    checklistTotal: 0,
    commentCount: 0,
    isBlocked: false,
    isOverdue: false,
    concurrencyStamp: 'stamp-1',
    ...overrides,
  };
}

export function deliverable(overrides: Partial<DeliverableSummary> = {}): DeliverableSummary {
  return {
    id: 'd1',
    clientId: 'c1',
    clientName: 'Nimbus Fitness',
    projectId: 'p1',
    projectName: 'Social content',
    taskId: null,
    title: 'Hero banner',
    type: 'Design',
    status: 'ClientReview',
    currentVersion: 2,
    owner: null,
    reviewer: null,
    sentToClientAt: '2026-09-20T10:00:00Z',
    clientDueAt: '2026-09-25T10:00:00Z',
    isOverdue: false,
    approvedAt: null,
    updatedAt: '2026-09-22T10:00:00Z',
    concurrencyStamp: 'd-stamp',
    ...overrides,
  };
}

export function deliverableDetail(overrides: Partial<DeliverableDetail> = {}, summary: Partial<DeliverableSummary> = {}): DeliverableDetail {
  const person = { id: 'u9', displayName: 'Lucas Moreau', email: '' };
  return {
    deliverable: deliverable(summary),
    description: 'Homepage hero for the autumn campaign.',
    versions: [
      { id: 'v2', number: 2, file: null, linkUrl: 'https://example.com/v2', body: null, notes: 'Lighter logo', createdBy: person, createdAt: '2026-09-21T10:00:00Z' },
      { id: 'v1', number: 1, file: null, linkUrl: 'https://example.com/v1', body: null, notes: null, createdBy: person, createdAt: '2026-09-19T10:00:00Z' },
    ],
    comments: [
      { id: 'cm1', versionNumber: 1, author: { id: 'u5', displayName: 'Taylor Reed', email: '' }, fromClient: true, isInternal: false, body: 'Use the lighter logo', createdAt: '2026-09-20T09:00:00Z' },
    ],
    history: [],
    approvedVersion: null,
    approvedByName: null,
    autoApproved: false,
    allowedActions: ['comment', 'approve', 'requestChanges'],
    ...overrides,
  };
}

export function timeEntry(overrides: Partial<TimeEntry> = {}): TimeEntry {
  return {
    id: 'e1',
    userId: 'am-1',
    userName: 'Amira Haddad',
    clientId: 'c1',
    clientName: 'Nimbus Fitness',
    projectId: 'p1',
    projectName: 'SEO retainer',
    taskId: null,
    taskTitle: null,
    date: '2026-09-23',
    minutes: 0,
    billable: true,
    note: 'Keyword research',
    startedAt: new Date(Date.now() - 65_000).toISOString(),
    isRunning: true,
    locked: false,
    concurrencyStamp: 's',
    ...overrides,
  };
}

export function report(overrides: Partial<Report> = {}): Report {
  return {
    id: 'r1',
    clientId: 'c1',
    clientName: 'Nimbus Fitness',
    projectId: null,
    title: 'Nimbus Fitness — August 2026 performance report',
    periodStart: '2026-08-01',
    periodEnd: '2026-08-31',
    status: 'Published',
    templateKey: 'monthly-performance',
    sections: [
      { key: 'summary', kind: 'summary', title: 'Executive summary', body: 'Organic traffic grew 14%.', kpis: [] },
      {
        key: 'seo',
        kind: 'channel',
        title: 'SEO',
        body: null,
        providerKey: 'seo',
        kpis: [
          { key: 'sessions', label: 'Organic sessions', value: 18420, unit: null, previousValue: 16150, source: 'Google Analytics 4', measurement: 'Measured' },
          { key: 'reach', label: 'Reach', value: 214000, unit: null, previousValue: null, source: 'Meta Business Suite (modelled reach)', measurement: 'Estimated' },
          { key: 'open_rate', label: 'Newsletter open rate', value: 41, unit: '%', previousValue: 39, source: 'Klaviyo export', measurement: 'Manual' },
        ],
      },
      { key: 'email', kind: 'channel', title: 'Email marketing', body: null, kpis: [], providerKey: 'email', providerNote: 'No data source is connected for this section yet.' },
    ],
    publishedAt: '2026-09-02T10:00:00Z',
    publishedBy: 'Amira Haddad',
    availableProviders: [],
    updatedAt: '2026-09-02T10:00:00Z',
    concurrencyStamp: 'r-stamp',
    ...overrides,
  };
}

export function dashboard(overrides: Partial<AgencyDashboard> = {}): AgencyDashboard {
  return {
    myTasks: [task({ title: 'Write two blog posts', isOverdue: true, dueDate: '2026-09-20' })],
    myTaskCounts: { open: 5, overdue: 1, dueToday: 2 },
    reviewQueue: [deliverable({ id: 'd2', title: 'Retargeting creatives', status: 'InternalReview' })],
    reviewQueueTotal: 1,
    pendingClientApprovals: [deliverable()],
    todaysMeetings: [],
    timer: null,
    myMinutesThisWeek: 375,
    accountManager: {
      healthBoard: [
        {
          clientId: 'c2',
          clientName: 'Wanderly Travel',
          status: 'Active',
          score: 40,
          level: 'Red',
          reasons: [{ code: 'overdue_tasks', level: 'Red', message: '6 overdue tasks', penalty: 30 }],
          overdueTasks: 6,
          pendingApprovals: 0,
          accountManager: null,
        },
      ],
      overdueByClient: [{ clientId: 'c2', clientName: 'Wanderly Travel', overdueTasks: 6, overdueApprovals: 0 }],
      utilization: { from: '2026-09-21', to: '2026-09-27', rows: [], totalMinutes: 0, billableMinutes: 0 },
    },
    admin: null,
    ...overrides,
  };
}
