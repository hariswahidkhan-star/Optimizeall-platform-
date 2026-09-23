import { BadgeCheck, BarChart3, Inbox, LayoutDashboard, Radar, Scale } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { PendingSection } from '@/components/PendingSection';
import { PortalOverview } from '@/components/PortalOverview';
import { Permissions } from '@/lib/auth/permissions';

/** Reviewer portal (/review). Paths are relative to the portal base. */
export const nav: PortalNavItem[] = [
  { to: '', label: 'Overview', icon: LayoutDashboard },
  { to: 'queue', label: 'Queue', icon: Inbox, description: 'Claim and review submitted posts in order.' },
  {
    to: 'live-checks',
    label: 'Live checks',
    icon: Radar,
    description: 'Re-check approved posts are still live and unchanged.',
  },
  {
    to: 'appeals',
    label: 'Appeals',
    icon: Scale,
    description: 'Resolve participant appeals against review decisions.',
    requires: { anyOf: [Permissions.AppealsResolve] },
  },
  {
    to: 'social-verification',
    label: 'Social verification',
    icon: BadgeCheck,
    description: 'Verify that connected social accounts are established and genuine.',
    requires: { anyOf: [Permissions.SocialAccountsVerify] },
  },
  {
    to: 'stats',
    label: 'My stats',
    icon: BarChart3,
    description: 'Your review volume, turnaround and accuracy.',
  },
];

export const routes: RouteObject[] = [
  { index: true, element: <PortalOverview /> },
  {
    path: 'queue',
    element: (
      <PendingSection
        title="Review queue"
        description="Submitted posts waiting for a decision, oldest first."
      />
    ),
  },
  {
    path: 'live-checks',
    element: <PendingSection title="Live checks" description="Approved posts due for a follow-up check." />,
  },
  {
    path: 'appeals',
    element: (
      <PendingSection title="Appeals" description="Participant appeals against rejections and reversals." />
    ),
  },
  {
    path: 'social-verification',
    element: <PendingSection title="Social verification" description="Accounts waiting for verification." />,
  },
  {
    path: 'stats',
    element: <PendingSection title="My stats" description="Your review activity over time." />,
  },
];
