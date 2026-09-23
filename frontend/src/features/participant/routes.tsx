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
import type { PortalNavItem } from '@/app/portalTypes';
import { PendingSection } from '@/components/PendingSection';
import type { RouteObject } from 'react-router-dom';
import { HomePage } from './HomePage';
import { ProfileLayout } from './profile/ProfileLayout';
import { SecurityPage } from './profile/SecurityPage';

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
  {
    path: 'campaigns',
    element: (
      <PendingSection
        title="Campaigns"
        description="Campaigns you can join, with the approved content to share."
      />
    ),
  },
  {
    path: 'submissions',
    element: (
      <PendingSection
        title="My submissions"
        description="Every post you submitted, with its review status and history."
      />
    ),
  },
  {
    path: 'earnings',
    element: (
      <PendingSection
        title="Earnings"
        description="What you have earned, what is pending approval and what has been paid."
      />
    ),
  },
  {
    path: 'payouts',
    element: (
      <PendingSection title="Payouts" description="Your biweekly payouts and the next scheduled payout." />
    ),
  },
  {
    path: 'social-accounts',
    element: (
      <PendingSection
        title="Social accounts"
        description="Connect the established accounts you share from and get them verified."
      />
    ),
  },
  {
    path: 'referrals',
    element: (
      <PendingSection title="Referrals" description="Your referral link and the people you have invited." />
    ),
  },
  {
    path: 'achievements',
    element: (
      <PendingSection title="Achievements" description="Milestones you have reached on Optimize All." />
    ),
  },
  {
    path: 'notifications',
    element: (
      <PendingSection title="Notifications" description="Updates about reviews, earnings and payouts." />
    ),
  },
  {
    path: 'support',
    element: <PendingSection title="Support" description="Open a ticket or follow up on an existing one." />,
  },
  {
    path: 'profile',
    element: <ProfileLayout />,
    children: [
      { index: true, element: <PendingSection embedded title="Profile details" /> },
      { path: 'payout-details', element: <PendingSection embedded title="Payout details" /> },
      {
        path: 'notification-preferences',
        element: <PendingSection embedded title="Notification preferences" />,
      },
      { path: 'security', element: <SecurityPage /> },
    ],
  },
];
