import { lazyPage } from '@/app/lazyPage';
import { BadgeCheck, Inbox, LayoutDashboard, Radar, Scale, TicketPercent } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import './reviewer.css';

const AppealDetailPage = lazyPage(() => import('./pages/AppealDetailPage'), 'AppealDetailPage');
const AppealsPage = lazyPage(() => import('./pages/AppealsPage'), 'AppealsPage');
const LiveChecksPage = lazyPage(() => import('./pages/LiveChecksPage'), 'LiveChecksPage');
const OverviewPage = lazyPage(() => import('./pages/OverviewPage'), 'OverviewPage');
const QueuePage = lazyPage(() => import('./pages/QueuePage'), 'QueuePage');
const SocialAccountPage = lazyPage(() => import('./pages/SocialAccountPage'), 'SocialAccountPage');
const SocialVerificationPage = lazyPage(
  () => import('./pages/SocialVerificationPage'),
  'SocialVerificationPage',
);
const WorkspacePage = lazyPage(() => import('./pages/WorkspacePage'), 'WorkspacePage');
const CodeSalesReviewQueuePage = lazyPage(
  () => import('../codes/staff/CodeSalesQueuePage'),
  'CodeSalesReviewQueuePage',
);
const ReviewCodeSaleDetailPage = lazyPage(
  () => import('../codes/staff/CodeSalesQueuePage'),
  'ReviewCodeSaleDetailPage',
);

/** Portal entry (the queue and live checks call submissions.review APIs). */
export const portalRequires: PermissionRequirement = { anyOf: [Permissions.SubmissionsReview] };

const appeals: PermissionRequirement = { allOf: [Permissions.SubmissionsReview, Permissions.AppealsResolve] };
/** Discount-code sales review (sales.review). */
const codeSales: PermissionRequirement = { allOf: [Permissions.SubmissionsReview, Permissions.SalesReview] };
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
    to: 'code-sales',
    label: 'Code sales',
    icon: TicketPercent,
    description: 'Approve sales participants reported with brand discount codes.',
    requires: codeSales,
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
    path: 'code-sales',
    handle: { requires: codeSales },
    children: [
      { index: true, element: <CodeSalesReviewQueuePage /> },
      { path: ':saleId', element: <ReviewCodeSaleDetailPage /> },
    ],
  },
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
