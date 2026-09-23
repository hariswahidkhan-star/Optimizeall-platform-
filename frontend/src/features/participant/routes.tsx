import {
  Award,
  Banknote,
  Bell,
  FileCheck2,
  Home,
  LifeBuoy,
  Megaphone,
  Share2,
  UserRound,
  Users,
  Wallet,
} from 'lucide-react';
import { Navigate, type RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { AchievementsPage } from './achievements/AchievementsPage';
import { CampaignDetailPage } from './campaigns/CampaignDetailPage';
import { CampaignsPage } from './campaigns/CampaignsPage';
import { EarningsPage } from './earnings/EarningsPage';
import { HomePage } from './home/HomePage';
import { NotificationsPage } from './notifications/NotificationsPage';
import { PayoutDetailPage, PayoutsPage } from './payouts/PayoutsPage';
import { NotificationPreferencesPage } from './profile/NotificationPreferencesPage';
import { PayoutDetailsPage } from './profile/PayoutDetailsPage';
import { ProfileDetailsPage } from './profile/ProfileDetailsPage';
import { ProfileLayout } from './profile/ProfileLayout';
import { SecurityPage } from './profile/SecurityPage';
import { ReferralsPage } from './referrals/ReferralsPage';
import { SocialAccountsPage } from './social/SocialAccountsPage';
import { SubmissionDetailPage } from './submissions/SubmissionDetailPage';
import { SubmissionsPage } from './submissions/SubmissionsPage';
import { NewTicketPage, SupportPage, TicketDetailPage } from './support/SupportPages';

/** Portal entry. No section needs more than the participant portal permission. */
export const portalRequires: PermissionRequirement = { anyOf: [Permissions.ParticipantPortal] };

/** Participant portal (/app). Paths are relative to the portal base. */
export const nav: PortalNavItem[] = [
  { to: '', label: 'Home', icon: Home, mobilePrimary: true },
  {
    to: 'campaigns',
    label: 'Campaigns',
    icon: Megaphone,
    description: 'Browse campaigns you can join and the content to share.',
    mobilePrimary: true,
  },
  {
    to: 'submissions',
    label: 'My submissions',
    shortLabel: 'Submissions',
    icon: FileCheck2,
    description: 'Track the posts you submitted and their review status.',
    mobilePrimary: true,
  },
  {
    to: 'earnings',
    label: 'Earnings',
    icon: Wallet,
    description: 'Pending, approved and paid earnings.',
    mobilePrimary: true,
  },
  {
    to: 'payouts',
    label: 'Payouts',
    icon: Banknote,
    description: 'Biweekly payout history and upcoming payouts.',
  },
  {
    to: 'social-accounts',
    label: 'Social accounts',
    icon: Share2,
    description: 'Connect and verify the accounts you post from.',
  },
  {
    to: 'referrals',
    label: 'Referrals',
    icon: Users,
    description: 'Invite friends and track referral rewards.',
  },
  {
    to: 'achievements',
    label: 'Achievements',
    icon: Award,
    description: 'Milestones and badges you have earned.',
  },
  {
    to: 'notifications',
    label: 'Notifications',
    icon: Bell,
    description: 'Updates about your submissions and payouts.',
  },
  { to: 'support', label: 'Support', icon: LifeBuoy, description: 'Get help from the Optimize All team.' },
  {
    to: 'profile',
    label: 'Profile',
    icon: UserRound,
    description: 'Your details, payout details, preferences and security.',
  },
];

export const routes: RouteObject[] = [
  { index: true, element: <HomePage /> },
  { path: 'campaigns', element: <CampaignsPage /> },
  { path: 'campaigns/:slug', element: <CampaignDetailPage /> },
  { path: 'submissions', element: <SubmissionsPage /> },
  { path: 'submissions/:id', element: <SubmissionDetailPage /> },
  { path: 'earnings', element: <EarningsPage /> },
  { path: 'payouts', element: <PayoutsPage /> },
  { path: 'payouts/:itemId', element: <PayoutDetailPage /> },
  { path: 'social-accounts', element: <SocialAccountsPage /> },
  { path: 'referrals', element: <ReferralsPage /> },
  { path: 'achievements', element: <AchievementsPage /> },
  { path: 'notifications', element: <NotificationsPage /> },
  { path: 'support', element: <SupportPage /> },
  { path: 'support/new', element: <NewTicketPage /> },
  { path: 'support/:ticketId', element: <TicketDetailPage /> },
  // The seeded onboarding step links here.
  { path: 'payout-details', element: <PayoutDetailsRedirect /> },
  {
    path: 'profile',
    element: <ProfileLayout />,
    children: [
      { index: true, element: <ProfileDetailsPage /> },
      { path: 'payout-details', element: <PayoutDetailsPage /> },
      { path: 'notification-preferences', element: <NotificationPreferencesPage /> },
      { path: 'security', element: <SecurityPage /> },
    ],
  },
];

function PayoutDetailsRedirect() {
  return <Navigate to="/app/profile/payout-details" replace />;
}
