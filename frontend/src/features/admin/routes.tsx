import { lazyPage } from '@/app/lazyPage';
import {
  BarChart3,
  Cog,
  FileText,
  FolderTree,
  GraduationCap,
  KeyRound,
  LayoutDashboard,
  LifeBuoy,
  ScrollText,
  Timer,
  Users,
} from 'lucide-react';
import type { ReactElement } from 'react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import './admin.css';

const AnalyticsPage = lazyPage(() => import('./analytics/AnalyticsPage'), 'AnalyticsPage');
const AuditLogPage = lazyPage(() => import('./audit/AuditLogPage'), 'AuditLogPage');
const CategoriesPage = lazyPage(() => import('./categories/CategoriesPage'), 'CategoriesPage');
const ContentPage = lazyPage(() => import('./content/ContentPage'), 'ContentPage');
const LearningCoursesPage = lazyPage(() => import('./learning/LearningCoursesPage'), 'LearningCoursesPage');
const LearningCertificatesPage = lazyPage(
  () => import('./learning/LearningCoursesPage'),
  'LearningCertificatesPage',
);
const LearningCourseDetailPage = lazyPage(
  () => import('./learning/LearningCourseDetailPage'),
  'LearningCourseDetailPage',
);
const NewCoursePage = lazyPage(() => import('./learning/CourseEditorPage'), 'NewCoursePage');
const EditCoursePage = lazyPage(() => import('./learning/CourseEditorPage'), 'EditCoursePage');
const JobsPage = lazyPage(() => import('./jobs/JobsPage'), 'JobsPage');
const RolesPage = lazyPage(() => import('./roles/RolesPage'), 'RolesPage');
const OverviewPage = lazyPage(() => import('./OverviewPage'), 'OverviewPage');
const SettingsPage = lazyPage(() => import('./settings/SettingsPage'), 'SettingsPage');
const TicketDetailPage = lazyPage(() => import('./support/TicketDetailPage'), 'TicketDetailPage');
const TicketsPage = lazyPage(() => import('./support/TicketsPage'), 'TicketsPage');
const UserDetailPage = lazyPage(() => import('./users/UserDetailPage'), 'UserDetailPage');
const UsersPage = lazyPage(() => import('./users/UsersPage'), 'UsersPage');

/**
 * Admin portal (/admin). The portal is open to several staff roles, so every section declares its own permission
 * (matching the API controller's) on its route (`handle.requires`, which also guards detail pages) and nav item; the
 * router answers 403 on those routes without it (see app/router.tsx). Actions inside a page are gated by their own
 * permission too, and server 403s render a "no access" state.
 */
const requires = {
  users: { anyOf: [Permissions.UsersView] },
  roles: { anyOf: [Permissions.RolesManage] },
  settings: { anyOf: [Permissions.SettingsManage] },
  content: { anyOf: [Permissions.ContentManage] },
  // The API (/admin/campaign-categories) authorizes with campaigns.manage.
  categories: { anyOf: [Permissions.CampaignsManage] },
  support: { anyOf: [Permissions.SupportManage] },
  learning: { anyOf: [Permissions.LearningView] },
  learningManage: { anyOf: [Permissions.LearningView], allOf: [Permissions.LearningManage] },
  audit: { anyOf: [Permissions.AuditView] },
  jobs: { anyOf: [Permissions.JobsView] },
  analytics: { anyOf: [Permissions.AnalyticsView] },
} satisfies Record<string, PermissionRequirement>;

/** Portal entry: any permission that opens one of its sections. */
export const portalRequires: PermissionRequirement = {
  anyOf: [...new Set(Object.values(requires).flatMap((r) => r.anyOf))],
};

export const nav: PortalNavItem[] = [
  { to: '', label: 'Overview', icon: LayoutDashboard },
  {
    to: 'users',
    label: 'Users',
    icon: Users,
    description: 'Find people, manage roles and suspensions.',
    requires: requires.users,
  },
  {
    to: 'roles',
    label: 'Roles & permissions',
    icon: KeyRound,
    description: 'Built-in roles and custom roles with exactly the permissions you choose.',
    requires: requires.roles,
  },
  {
    to: 'settings',
    label: 'Settings',
    icon: Cog,
    description: 'Platform settings and policies.',
    requires: requires.settings,
  },
  {
    to: 'content',
    label: 'Content',
    icon: FileText,
    description: 'Banners, announcements, FAQs and onboarding copy.',
    requires: requires.content,
  },
  {
    to: 'categories',
    label: 'Campaign categories',
    icon: FolderTree,
    description: 'Categories used to organise campaigns.',
    requires: requires.categories,
  },
  {
    to: 'learning',
    label: 'Learning',
    icon: GraduationCap,
    description: 'Courses, pass rates, question analytics, learners and certificates.',
    requires: requires.learning,
  },
  {
    to: 'support',
    label: 'Support tickets',
    icon: LifeBuoy,
    description: 'Participant tickets and replies.',
    requires: requires.support,
  },
  {
    to: 'audit',
    label: 'Audit log',
    icon: ScrollText,
    description: 'Append-only record of sensitive actions.',
    requires: requires.audit,
  },
  {
    to: 'jobs',
    label: 'Jobs & notifications',
    icon: Timer,
    description: 'Background jobs and the notification outbox.',
    requires: requires.jobs,
  },
  {
    to: 'analytics',
    label: 'Analytics',
    icon: BarChart3,
    description: 'Platform-wide growth and quality metrics.',
    requires: requires.analytics,
  },
];

/** Every admin page stacks its header, callouts and cards with consistent spacing. */
function page(element: ReactElement): ReactElement {
  return <div className="admin-page">{element}</div>;
}

export const routes: RouteObject[] = [
  { index: true, element: page(<OverviewPage />) },
  {
    path: 'users',
    handle: { requires: requires.users },
    children: [
      { index: true, element: page(<UsersPage />) },
      { path: ':userId', element: page(<UserDetailPage />) },
    ],
  },
  { path: 'roles', handle: { requires: requires.roles }, element: page(<RolesPage />) },
  { path: 'settings', handle: { requires: requires.settings }, element: page(<SettingsPage />) },
  { path: 'content', handle: { requires: requires.content }, element: page(<ContentPage />) },
  { path: 'categories', handle: { requires: requires.categories }, element: page(<CategoriesPage />) },
  {
    path: 'support',
    handle: { requires: requires.support },
    children: [
      { index: true, element: page(<TicketsPage />) },
      { path: ':ticketId', element: page(<TicketDetailPage />) },
    ],
  },
  {
    path: 'learning',
    handle: { requires: requires.learning },
    children: [
      { index: true, element: page(<LearningCoursesPage />) },
      { path: 'certificates', element: page(<LearningCertificatesPage />) },
      { path: 'new', handle: { requires: requires.learningManage }, element: page(<NewCoursePage />) },
      { path: 'courses/:courseId', element: page(<LearningCourseDetailPage />) },
      {
        path: 'courses/:courseId/edit',
        handle: { requires: requires.learningManage },
        element: page(<EditCoursePage />),
      },
    ],
  },
  { path: 'audit', handle: { requires: requires.audit }, element: page(<AuditLogPage />) },
  { path: 'jobs', handle: { requires: requires.jobs }, element: page(<JobsPage />) },
  { path: 'analytics', handle: { requires: requires.analytics }, element: page(<AnalyticsPage />) },
];
