import {
  BarChart3,
  Cog,
  FileText,
  FolderTree,
  LayoutDashboard,
  LifeBuoy,
  ScrollText,
  Timer,
  Users,
} from 'lucide-react';
import type { ReactElement } from 'react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { Permissions } from '@/lib/auth/permissions';
import { AnalyticsPage } from './analytics/AnalyticsPage';
import { AuditLogPage } from './audit/AuditLogPage';
import { CategoriesPage } from './categories/CategoriesPage';
import { ContentPage } from './content/ContentPage';
import { JobsPage } from './jobs/JobsPage';
import { OverviewPage } from './OverviewPage';
import { SettingsPage } from './settings/SettingsPage';
import { TicketDetailPage } from './support/TicketDetailPage';
import { TicketsPage } from './support/TicketsPage';
import { UserDetailPage } from './users/UserDetailPage';
import { UsersPage } from './users/UsersPage';
import './admin.css';

/**
 * Admin portal (/admin). The portal is open to several staff roles, so every section declares its own permission;
 * nav items are hidden and the router answers 403 on those routes without it (see app/router.tsx). Actions inside a
 * page are gated by their own permission too, and server 403s render a "no access" state.
 */
export const nav: PortalNavItem[] = [
  { to: '', label: 'Overview', icon: LayoutDashboard },
  {
    to: 'users',
    label: 'Users',
    icon: Users,
    description: 'Find people, manage roles and suspensions.',
    requires: { anyOf: [Permissions.UsersView] },
  },
  {
    to: 'settings',
    label: 'Settings',
    icon: Cog,
    description: 'Platform settings and policies.',
    requires: { anyOf: [Permissions.SettingsManage] },
  },
  {
    to: 'content',
    label: 'Content',
    icon: FileText,
    description: 'Banners, announcements, FAQs and onboarding copy.',
    requires: { anyOf: [Permissions.ContentManage] },
  },
  {
    to: 'categories',
    label: 'Campaign categories',
    icon: FolderTree,
    description: 'Categories used to organise campaigns.',
    // The API (/admin/campaign-categories) authorizes with campaigns.manage.
    requires: { anyOf: [Permissions.CampaignsManage] },
  },
  {
    to: 'support',
    label: 'Support tickets',
    icon: LifeBuoy,
    description: 'Participant tickets and replies.',
    requires: { anyOf: [Permissions.SupportManage] },
  },
  {
    to: 'audit',
    label: 'Audit log',
    icon: ScrollText,
    description: 'Append-only record of sensitive actions.',
    requires: { anyOf: [Permissions.AuditView] },
  },
  {
    to: 'jobs',
    label: 'Jobs & notifications',
    icon: Timer,
    description: 'Background jobs and the notification outbox.',
    requires: { anyOf: [Permissions.JobsView] },
  },
  {
    to: 'analytics',
    label: 'Analytics',
    icon: BarChart3,
    description: 'Platform-wide growth and quality metrics.',
    requires: { anyOf: [Permissions.AnalyticsView] },
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
    children: [
      { index: true, element: page(<UsersPage />) },
      { path: ':userId', element: page(<UserDetailPage />) },
    ],
  },
  { path: 'settings', element: page(<SettingsPage />) },
  { path: 'content', element: page(<ContentPage />) },
  { path: 'categories', element: page(<CategoriesPage />) },
  {
    path: 'support',
    children: [
      { index: true, element: page(<TicketsPage />) },
      { path: ':ticketId', element: page(<TicketDetailPage />) },
    ],
  },
  { path: 'audit', element: page(<AuditLogPage />) },
  { path: 'jobs', element: page(<JobsPage />) },
  { path: 'analytics', element: page(<AnalyticsPage />) },
];
