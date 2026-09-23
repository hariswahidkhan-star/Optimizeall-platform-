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
  Trophy,
} from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { AnalyticsPage } from './AnalyticsPage';
import { CalendarPage } from './CalendarPage';
import { ComposerPage } from './ComposerPage';
import { CompetitorsPage, InboxPage, ListeningPage } from './EngagementPages';
import { LibraryPage } from './LibraryPage';
import { ConnectCallbackPage, ProfilesPage } from './ProfilesPage';
import { ApprovalsPage, PublishingPage } from './WorkflowPages';

/** Every social page calls social.manage APIs; approve/schedule/publish actions additionally need social.publish (checked per action). */
const social: PermissionRequirement = { anyOf: [Permissions.SocialManage] };

/** Agency portal area: Social media management. Paths are relative to /agency. */
export const nav: PortalNavItem[] = [
  { to: 'social', label: 'Social calendar', icon: CalendarDays, description: 'Plan, approve and schedule posts across clients.', requires: social },
  { to: 'social/compose', label: 'Compose', icon: PenSquare, description: 'Write a post with per-network variants and previews.', requires: social },
  { to: 'social/approvals', label: 'Social approvals', icon: ClipboardCheck, description: 'Posts awaiting internal or client approval.', requires: social },
  { to: 'social/publishing', label: 'Publishing log', icon: Send, description: 'Scheduled, published and failed posts.', requires: social },
  { to: 'social/profiles', label: 'Profiles & connections', icon: Plug, description: 'Brand profiles, OAuth connections and queue slots.', requires: social },
  { to: 'social/library', label: 'Social library', icon: Images, description: 'Media, hashtag sets, snippets and UTM campaigns.', requires: social },
  { to: 'social/analytics', label: 'Social analytics', icon: BarChart3, description: 'KPIs, top posts and best times, with sources.', requires: social },
  { to: 'social/listening', label: 'Listening', icon: Ear, description: 'Keywords, hashtags and competitor mentions.', requires: social },
  { to: 'social/inbox', label: 'Social inbox', icon: Inbox, description: 'Comments and messages to answer.', requires: social },
  { to: 'social/competitors', label: 'Competitors', icon: Trophy, description: 'Benchmark competitor profiles.', requires: social },
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
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [Permissions.SocialManage];
