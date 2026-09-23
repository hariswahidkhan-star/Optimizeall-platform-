import { lazyPage } from '@/app/lazyPage';
import {
  Building2,
  Clock,
  FileBarChart,
  FileCheck2,
  FolderKanban,
  LayoutDashboard,
  LayoutTemplate,
  ListTodo,
} from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import './delivery.css';

const ClientDetailPage = lazyPage(() => import('./ClientDetailPage'), 'ClientDetailPage');
const ClientsPage = lazyPage(() => import('./ClientsPage'), 'ClientsPage');
const DashboardPage = lazyPage(() => import('./DashboardPage'), 'DashboardPage');
const DeliverableReviewPage = lazyPage(() => import('./DeliverablesPage'), 'DeliverableReviewPage');
const DeliverablesPage = lazyPage(() => import('./DeliverablesPage'), 'DeliverablesPage');
const MyTasksPage = lazyPage(() => import('./MyTasksPage'), 'MyTasksPage');
const ProjectDetailPage = lazyPage(() => import('./ProjectDetailPage'), 'ProjectDetailPage');
const ProjectsPage = lazyPage(() => import('./ProjectsPage'), 'ProjectsPage');
const ReportBuilderPage = lazyPage(() => import('./ReportsPages'), 'ReportBuilderPage');
const ReportsPage = lazyPage(() => import('./ReportsPages'), 'ReportsPage');
const TemplatesPage = lazyPage(() => import('./TemplatesPage'), 'TemplatesPage');
const TimePage = lazyPage(() => import('./TimePage'), 'TimePage');

/** Each section declares the permission of the API it reads (route `handle.requires` + nav item). */
const requires = {
  clients: { anyOf: [Permissions.ClientsView] },
  projects: { anyOf: [Permissions.ProjectsView] },
  time: { anyOf: [Permissions.TimeTrack] },
  reports: { anyOf: [Permissions.ReportsManage] },
} satisfies Record<string, PermissionRequirement>;

/** Agency portal area: Client delivery (dashboard, clients, projects, deliverables, time, reports). Paths are relative to /agency. */
export const nav: PortalNavItem[] = [
  { to: '', label: 'Home', icon: LayoutDashboard },
  {
    to: 'clients',
    label: 'Clients',
    icon: Building2,
    description: 'Client accounts, teams, onboarding and health.',
    requires: requires.clients,
  },
  {
    to: 'projects',
    label: 'Projects',
    icon: FolderKanban,
    description: 'Retainers, campaigns and builds with kanban boards.',
    requires: requires.projects,
  },
  {
    to: 'tasks',
    label: 'My tasks',
    icon: ListTodo,
    description: 'Everything assigned to you.',
    requires: requires.projects,
  },
  {
    to: 'deliverables',
    label: 'Deliverables',
    icon: FileCheck2,
    description: 'Internal review and client approvals.',
    requires: requires.projects,
  },
  {
    to: 'time',
    label: 'Time',
    icon: Clock,
    description: 'Timer, timesheets and utilization.',
    requires: requires.time,
  },
  {
    to: 'reports',
    label: 'Reports',
    icon: FileBarChart,
    description: 'Monthly client performance reports.',
    requires: requires.reports,
  },
  {
    to: 'templates',
    label: 'Templates',
    icon: LayoutTemplate,
    description: 'Service and brief templates.',
    requires: requires.projects,
  },
];

export const routes: RouteObject[] = [
  { index: true, element: <DashboardPage /> },
  {
    path: 'clients',
    handle: { requires: requires.clients },
    children: [
      { index: true, element: <ClientsPage /> },
      { path: ':clientId', element: <ClientDetailPage /> },
    ],
  },
  {
    path: 'projects',
    handle: { requires: requires.projects },
    children: [
      { index: true, element: <ProjectsPage /> },
      { path: ':projectId', element: <ProjectDetailPage /> },
    ],
  },
  { path: 'tasks', handle: { requires: requires.projects }, element: <MyTasksPage /> },
  {
    path: 'deliverables',
    handle: { requires: requires.projects },
    children: [
      { index: true, element: <DeliverablesPage /> },
      { path: ':deliverableId', element: <DeliverableReviewPage /> },
    ],
  },
  { path: 'time', handle: { requires: requires.time }, element: <TimePage /> },
  {
    path: 'reports',
    handle: { requires: requires.reports },
    children: [
      { index: true, element: <ReportsPage /> },
      { path: ':reportId', element: <ReportBuilderPage /> },
    ],
  },
  { path: 'templates', handle: { requires: requires.projects }, element: <TemplatesPage /> },
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [
  Permissions.ClientsView,
  Permissions.ProjectsView,
  Permissions.TimeTrack,
  Permissions.ReportsManage,
];
