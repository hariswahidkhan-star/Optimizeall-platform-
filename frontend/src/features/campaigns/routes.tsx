import { lazyPage } from '@/app/lazyPage';
import {
  Award,
  BarChart3,
  CalendarDays,
  CreditCard,
  FlaskConical,
  LayoutDashboard,
  LayoutTemplate,
  Link2,
  Megaphone,
  Users,
  UsersRound,
} from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';

const AchievementsPage = lazyPage(() => import('./achievements/AchievementsPage'), 'AchievementsPage');
const AnalyticsPage = lazyPage(() => import('./analytics/AnalyticsPage'), 'AnalyticsPage');
const CampaignAnalyticsPage = lazyPage(() => import('./analytics/AnalyticsPage'), 'CampaignAnalyticsPage');
const CalendarPage = lazyPage(() => import('./calendar/CalendarPage'), 'CalendarPage');
const CampaignEditorPage = lazyPage(() => import('./editor/CampaignEditorPage'), 'CampaignEditorPage');
const ExperimentResultsPage = lazyPage(
  () => import('./experiments/ExperimentResultsPage'),
  'ExperimentResultsPage',
);
const ExperimentsPage = lazyPage(() => import('./experiments/ExperimentsPage'), 'ExperimentsPage');
const InvitationsPage = lazyPage(() => import('./invitations/InvitationsPage'), 'InvitationsPage');
const CampaignsListPage = lazyPage(() => import('./list/CampaignsListPage'), 'CampaignsListPage');
const OverviewPage = lazyPage(() => import('./overview/OverviewPage'), 'OverviewPage');
const ReferralsPage = lazyPage(() => import('./referrals/ReferralsPage'), 'ReferralsPage');
const TemplatesPage = lazyPage(() => import('./templates/TemplatesPage'), 'TemplatesPage');
const RateCardsPage = lazyPage(() => import('../rates/cards/RateCardsPage'), 'RateCardsPage');
const RateCardDetailPage = lazyPage(() => import('../rates/cards/RateCardDetailPage'), 'RateCardDetailPage');
const RateGroupsPage = lazyPage(() => import('../rates/groups/RateGroupsPage'), 'RateGroupsPage');
const RateGroupDetailPage = lazyPage(() => import('../rates/groups/RateGroupDetailPage'), 'RateGroupDetailPage');

/** Portal entry (the campaign pages call campaigns.manage APIs). */
export const portalRequires: PermissionRequirement = { anyOf: [Permissions.CampaignsManage] };

/** Growth sections call marketing.manage APIs; within the portal that means campaigns.manage and marketing.manage. */
const marketing: PermissionRequirement = {
  allOf: [Permissions.CampaignsManage, Permissions.MarketingManage],
};
const analytics: PermissionRequirement = { allOf: [Permissions.CampaignsManage, Permissions.AnalyticsView] };
/** Person-level pricing (rate cards and groups) calls rates.view APIs. */
const rates: PermissionRequirement = { allOf: [Permissions.CampaignsManage, Permissions.RatesView] };

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
    to: 'rate-cards',
    label: 'Rate cards',
    icon: CreditCard,
    description: 'Reusable per-post rates for people and groups.',
    requires: rates,
  },
  {
    to: 'rate-groups',
    label: 'Rate groups',
    icon: UsersRound,
    description: 'Macro, micro, nano… people who share a rate.',
    requires: rates,
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
  {
    path: 'rate-cards',
    handle: { requires: rates },
    children: [
      { index: true, element: <RateCardsPage /> },
      { path: ':cardId', element: <RateCardDetailPage /> },
    ],
  },
  {
    path: 'rate-groups',
    handle: { requires: rates },
    children: [
      { index: true, element: <RateGroupsPage /> },
      { path: ':groupId', element: <RateGroupDetailPage /> },
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
