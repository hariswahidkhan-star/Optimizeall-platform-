import { BadgeCheck, Inbox, LayoutDashboard, Radar, Scale } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { AppealDetailPage } from './pages/AppealDetailPage';
import { AppealsPage } from './pages/AppealsPage';
import { LiveChecksPage } from './pages/LiveChecksPage';
import { OverviewPage } from './pages/OverviewPage';
import { QueuePage } from './pages/QueuePage';
import { SocialAccountPage } from './pages/SocialAccountPage';
import { SocialVerificationPage } from './pages/SocialVerificationPage';
import { WorkspacePage } from './pages/WorkspacePage';
import './reviewer.css';

/** Portal entry (the queue and live checks call submissions.review APIs). */
export const portalRequires: PermissionRequirement = { anyOf: [Permissions.SubmissionsReview] };

const appeals: PermissionRequirement = { allOf: [Permissions.SubmissionsReview, Permissions.AppealsResolve] };
const socialVerification: PermissionRequirement = {
  allOf: [Permissions.SubmissionsReview, Permissions.SocialAccountsVerify],
};

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
    requires: appeals,
  },
  {
    to: 'social-verification',
    label: 'Social verification',
    icon: BadgeCheck,
    description: 'Verify that connected social accounts are established and genuine.',
    requires: socialVerification,
  },
];

export const routes: RouteObject[] = [
  { index: true, element: <OverviewPage /> },
  {
    path: 'queue',
    children: [
      { index: true, element: <QueuePage /> },
      { path: ':submissionId', element: <WorkspacePage /> },
    ],
  },
  { path: 'live-checks', element: <LiveChecksPage /> },
  {
    path: 'appeals',
    handle: { requires: appeals },
    children: [
      { index: true, element: <AppealsPage /> },
      { path: ':appealId', element: <AppealDetailPage /> },
    ],
  },
  {
    path: 'social-verification',
    handle: { requires: socialVerification },
    children: [
      { index: true, element: <SocialVerificationPage /> },
      { path: ':accountId', element: <SocialAccountPage /> },
    ],
  },
];
