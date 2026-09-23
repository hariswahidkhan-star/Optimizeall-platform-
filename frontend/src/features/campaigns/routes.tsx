import {
  BarChart3,
  CalendarDays,
  FlaskConical,
  LayoutDashboard,
  LayoutTemplate,
  Link2,
  Megaphone,
  Users,
} from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { PendingSection } from '@/components/PendingSection';
import { PortalOverview } from '@/components/PortalOverview';
import { Permissions } from '@/lib/auth/permissions';

/** Campaign manager portal (/manage). Paths are relative to the portal base. */
export const nav: PortalNavItem[] = [
  { to: '', label: 'Overview', icon: LayoutDashboard },
  {
    to: 'campaigns',
    label: 'Campaigns',
    icon: Megaphone,
    description: 'Create, schedule and run sharing campaigns.',
  },
  {
    to: 'templates',
    label: 'Templates',
    icon: LayoutTemplate,
    description: 'Reusable approved content and captions.',
  },
  {
    to: 'calendar',
    label: 'Content calendar',
    icon: CalendarDays,
    description: 'What goes live when, across campaigns.',
  },
  {
    to: 'invitations',
    label: 'Invitations & landing pages',
    icon: Link2,
    description: 'Invite codes and campaign landing pages.',
    requires: { anyOf: [Permissions.MarketingManage] },
  },
  {
    to: 'experiments',
    label: 'Experiments',
    icon: FlaskConical,
    description: 'A/B tests on landing pages and messaging.',
    requires: { anyOf: [Permissions.MarketingManage] },
  },
  {
    to: 'referrals',
    label: 'Referrals',
    icon: Users,
    description: 'Referral programme rules and performance.',
    requires: { anyOf: [Permissions.MarketingManage] },
  },
  {
    to: 'analytics',
    label: 'Analytics',
    icon: BarChart3,
    description: 'Reach, participation and cost per approved post.',
    requires: { anyOf: [Permissions.AnalyticsView] },
  },
];

export const routes: RouteObject[] = [
  { index: true, element: <PortalOverview /> },
  {
    path: 'campaigns',
    element: <PendingSection title="Campaigns" description="All campaigns and their status." />,
  },
  {
    path: 'templates',
    element: <PendingSection title="Templates" description="Approved content templates." />,
  },
  {
    path: 'calendar',
    element: <PendingSection title="Content calendar" description="Scheduled campaign content by date." />,
  },
  {
    path: 'invitations',
    element: (
      <PendingSection title="Invitations & landing pages" description="Invite codes and landing pages." />
    ),
  },
  {
    path: 'experiments',
    element: <PendingSection title="Experiments" description="Running and completed experiments." />,
  },
  {
    path: 'referrals',
    element: <PendingSection title="Referrals" description="Referral programme settings and results." />,
  },
  {
    path: 'analytics',
    element: <PendingSection title="Analytics" description="Campaign performance." />,
  },
];
