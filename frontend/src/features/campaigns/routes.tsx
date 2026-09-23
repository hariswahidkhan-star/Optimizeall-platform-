import {
  Award,
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
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { AchievementsPage } from './achievements/AchievementsPage';
import { AnalyticsPage, CampaignAnalyticsPage } from './analytics/AnalyticsPage';
import { CalendarPage } from './calendar/CalendarPage';
import { CampaignEditorPage } from './editor/CampaignEditorPage';
import { ExperimentResultsPage } from './experiments/ExperimentResultsPage';
import { ExperimentsPage } from './experiments/ExperimentsPage';
import { InvitationsPage } from './invitations/InvitationsPage';
import { CampaignsListPage } from './list/CampaignsListPage';
import { OverviewPage } from './overview/OverviewPage';
import { ReferralsPage } from './referrals/ReferralsPage';
import { TemplatesPage } from './templates/TemplatesPage';

/** Portal entry (the campaign pages call campaigns.manage APIs). */
export const portalRequires: PermissionRequirement = { anyOf: [Permissions.CampaignsManage] };

/** Growth sections call marketing.manage APIs; within the portal that means campaigns.manage and marketing.manage. */
const marketing: PermissionRequirement = {
  allOf: [Permissions.CampaignsManage, Permissions.MarketingManage],
};
const analytics: PermissionRequirement = { allOf: [Permissions.CampaignsManage, Permissions.AnalyticsView] };

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
    requires: marketing,
  },
  {
    to: 'calendar',
    label: 'Content calendar',
    icon: CalendarDays,
    description: 'What goes live when, across campaigns.',
    requires: marketing,
  },
  {
    to: 'invitations',
    label: 'Invitations & landing pages',
    icon: Link2,
    description: 'Invite codes and campaign landing pages.',
    requires: marketing,
  },
  {
    to: 'experiments',
    label: 'Experiments',
    icon: FlaskConical,
    description: 'A/B tests on landing pages and messaging.',
    requires: marketing,
  },
  {
    to: 'referrals',
    label: 'Referrals',
    icon: Users,
    description: 'Referral programme rules and performance.',
    requires: marketing,
  },
  {
    to: 'analytics',
    label: 'Analytics',
    icon: BarChart3,
    description: 'Reach, participation and cost per approved post.',
    requires: analytics,
  },
  {
    to: 'achievements',
    label: 'Achievements',
    icon: Award,
    description: 'Badges participants earn for milestones.',
    requires: marketing,
  },
];

export const routes: RouteObject[] = [
  { index: true, element: <OverviewPage /> },
  {
    path: 'campaigns',
    children: [
      { index: true, element: <CampaignsListPage /> },
      { path: 'new', element: <CampaignEditorPage /> },
      { path: ':campaignId', element: <CampaignEditorPage /> },
    ],
  },
  { path: 'templates', handle: { requires: marketing }, element: <TemplatesPage /> },
  { path: 'calendar', handle: { requires: marketing }, element: <CalendarPage /> },
  { path: 'invitations', handle: { requires: marketing }, element: <InvitationsPage /> },
  {
    path: 'experiments',
    handle: { requires: marketing },
    children: [
      { index: true, element: <ExperimentsPage /> },
      { path: ':experimentId', element: <ExperimentResultsPage /> },
    ],
  },
  { path: 'referrals', handle: { requires: marketing }, element: <ReferralsPage /> },
  {
    path: 'analytics',
    handle: { requires: analytics },
    children: [
      { index: true, element: <AnalyticsPage /> },
      { path: 'campaigns/:campaignId', element: <CampaignAnalyticsPage /> },
    ],
  },
  { path: 'achievements', handle: { requires: marketing }, element: <AchievementsPage /> },
];
