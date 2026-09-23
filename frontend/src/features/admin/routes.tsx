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
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { PendingSection } from '@/components/PendingSection';
import { PortalOverview } from '@/components/PortalOverview';
import { Permissions } from '@/lib/auth/permissions';

/**
 * Admin portal (/admin). The portal is open to several staff roles, so every section declares its own permission;
 * nav items are hidden and the router answers 403 on those routes without it (see app/router.tsx).
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
    requires: { anyOf: [Permissions.ContentManage, Permissions.SettingsManage] },
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

export const routes: RouteObject[] = [
  { index: true, element: <PortalOverview /> },
  { path: 'users', element: <PendingSection title="Users" description="Everyone on the platform." /> },
  { path: 'settings', element: <PendingSection title="Settings" description="Platform-wide settings." /> },
  {
    path: 'content',
    element: <PendingSection title="Content" description="Banners, announcements, FAQs and onboarding." />,
  },
  {
    path: 'categories',
    element: <PendingSection title="Campaign categories" description="Campaign categories." />,
  },
  {
    path: 'support',
    element: <PendingSection title="Support tickets" description="Open and recent tickets." />,
  },
  { path: 'audit', element: <PendingSection title="Audit log" description="Who changed what, and why." /> },
  {
    path: 'jobs',
    element: <PendingSection title="Jobs & notifications" description="Job runs and outbox status." />,
  },
  { path: 'analytics', element: <PendingSection title="Analytics" description="Platform analytics." /> },
];
