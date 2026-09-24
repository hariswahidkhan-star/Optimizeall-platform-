import { lazyPage } from '@/app/lazyPage';
import {
  BarChart3,
  CalendarDays,
  ClipboardCheck,
  Ear,
  Images,
  Inbox,
  PenSquare,
  Plug,
  Send,
  Settings2,
  Trophy,
} from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';

const AnalyticsPage = lazyPage(() => import('./AnalyticsPage'), 'AnalyticsPage');
const CalendarPage = lazyPage(() => import('./CalendarPage'), 'CalendarPage');
const ComposerPage = lazyPage(() => import('./ComposerPage'), 'ComposerPage');
const CompetitorsPage = lazyPage(() => import('./EngagementPages'), 'CompetitorsPage');
const InboxPage = lazyPage(() => import('./EngagementPages'), 'InboxPage');
const ListeningPage = lazyPage(() => import('./EngagementPages'), 'ListeningPage');
const LibraryPage = lazyPage(() => import('./LibraryPage'), 'LibraryPage');
const ConnectCallbackPage = lazyPage(() => import('./ProfilesPage'), 'ConnectCallbackPage');
const ProfilesPage = lazyPage(() => import('./ProfilesPage'), 'ProfilesPage');
const ApprovalsPage = lazyPage(() => import('./WorkflowPages'), 'ApprovalsPage');
const PublishingPage = lazyPage(() => import('./WorkflowPages'), 'PublishingPage');
const SocialSettingsPage = lazyPage(() => import('./SocialSettingsPage'), 'SocialSettingsPage');

/** Every social page calls social.manage APIs; approve/schedule/publish actions additionally need social.publish (checked per action). */
const social: PermissionRequirement = { anyOf: [Permissions.SocialManage] };

/** Agency portal area: Social media management. Paths are relative to /agency. */
export const nav: PortalNavItem[] = [
  {
    to: 'social',
    label: 'Social calendar',
    icon: CalendarDays,
    description: 'Plan, approve and schedule posts across clients.',
    requires: social,
  },
  {
    to: 'social/compose',
    label: 'Compose',
    icon: PenSquare,
    description: 'Write a post with per-network variants and previews.',
    requires: social,
  },
  {
    to: 'social/approvals',
    label: 'Social approvals',
    icon: ClipboardCheck,
    description: 'Posts awaiting internal or client approval.',
    requires: social,
  },
  {
    to: 'social/publishing',
    label: 'Publishing log',
    icon: Send,
    description: 'Scheduled, published and failed posts.',
    requires: social,
  },
  {
    to: 'social/profiles',
    label: 'Profiles & connections',
    icon: Plug,
    description: 'Brand profiles, OAuth connections and queue slots.',
    requires: social,
  },
  {
    to: 'social/library',
    label: 'Social library',
    icon: Images,
    description: 'Media, hashtag sets, snippets and UTM campaigns.',
    requires: social,
  },
  {
    to: 'social/analytics',
    label: 'Social analytics',
    icon: BarChart3,
    description: 'KPIs, top posts and best times, with sources.',
    requires: social,
  },
  {
    to: 'social/listening',
    label: 'Listening',
    icon: Ear,
    description: 'Keywords, hashtags and competitor mentions.',
    requires: social,
  },
  {
    to: 'social/inbox',
    label: 'Social inbox',
    icon: Inbox,
    description: 'Comments and messages to answer.',
    requires: social,
  },
  {
    to: 'social/competitors',
    label: 'Competitors',
    icon: Trophy,
    description: 'Benchmark competitor profiles.',
    requires: social,
  },
  {
    to: 'social/settings',
    label: 'Social settings',
    icon: Settings2,
    description: 'Network limits, best posting times and awareness days.',
    requires: social,
  },
];

export const routes: RouteObject[] = [
  { path: 'social', element: <CalendarPage />, handle: { requires: social } },
  { path: 'social/compose', element: <ComposerPage />, handle: { requires: social } },
  { path: 'social/posts/:id', element: <ComposerPage />, handle: { requires: social } },
  { path: 'social/approvals', element: <ApprovalsPage />, handle: { requires: social } },
  { path: 'social/publishing', element: <PublishingPage />, handle: { requires: social } },
  { path: 'social/profiles', element: <ProfilesPage />, handle: { requires: social } },
  { path: 'social/connect/callback', element: <ConnectCallbackPage />, handle: { requires: social } },
  { path: 'social/library', element: <LibraryPage />, handle: { requires: social } },
  { path: 'social/analytics', element: <AnalyticsPage />, handle: { requires: social } },
  { path: 'social/listening', element: <ListeningPage />, handle: { requires: social } },
  { path: 'social/inbox', element: <InboxPage />, handle: { requires: social } },
  { path: 'social/competitors', element: <CompetitorsPage />, handle: { requires: social } },
  { path: 'social/settings', element: <SocialSettingsPage />, handle: { requires: social } },
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [Permissions.SocialManage];
