import { lazyPage } from '@/app/lazyPage';
import {
  Award,
  Banknote,
  Bell,
  FileCheck2,
  GraduationCap,
  Home,
  LifeBuoy,
  Megaphone,
  Share2,
  TicketPercent,
  UserRound,
  Users,
  Wallet,
} from 'lucide-react';
import { Navigate, type RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';

const AchievementsPage = lazyPage(() => import('./achievements/AchievementsPage'), 'AchievementsPage');
const CampaignDetailPage = lazyPage(() => import('./campaigns/CampaignDetailPage'), 'CampaignDetailPage');
const CampaignsPage = lazyPage(() => import('./campaigns/CampaignsPage'), 'CampaignsPage');
const EarningsPage = lazyPage(() => import('./earnings/EarningsPage'), 'EarningsPage');
const HomePage = lazyPage(() => import('./home/HomePage'), 'HomePage');
const NotificationsPage = lazyPage(() => import('./notifications/NotificationsPage'), 'NotificationsPage');
const PayoutDetailPage = lazyPage(() => import('./payouts/PayoutsPage'), 'PayoutDetailPage');
const PayoutsPage = lazyPage(() => import('./payouts/PayoutsPage'), 'PayoutsPage');
const NotificationPreferencesPage = lazyPage(
  () => import('./profile/NotificationPreferencesPage'),
  'NotificationPreferencesPage',
);
const PayoutDetailsPage = lazyPage(() => import('./profile/PayoutDetailsPage'), 'PayoutDetailsPage');
const ProfileDetailsPage = lazyPage(() => import('./profile/ProfileDetailsPage'), 'ProfileDetailsPage');
const ProfileLayout = lazyPage(() => import('./profile/ProfileLayout'), 'ProfileLayout');
const SecurityPage = lazyPage(() => import('./profile/SecurityPage'), 'SecurityPage');
const ReferralsPage = lazyPage(() => import('./referrals/ReferralsPage'), 'ReferralsPage');
const SocialAccountsPage = lazyPage(() => import('./social/SocialAccountsPage'), 'SocialAccountsPage');
const SubmissionDetailPage = lazyPage(
  () => import('./submissions/SubmissionDetailPage'),
  'SubmissionDetailPage',
);
const LearningHomePage = lazyPage(() => import('./learning/LearningHomePages'), 'LearningHomePage');
const LearningCatalogPage = lazyPage(() => import('./learning/LearningHomePages'), 'LearningCatalogPage');
const LearningCertificatePage = lazyPage(() => import('./learning/LearningHomePages'), 'LearningCertificatePage');
const LearningCoursePage = lazyPage(() => import('./learning/CoursePages'), 'LearningCoursePage');
const LearningLessonPage = lazyPage(() => import('./learning/CoursePages'), 'LearningLessonPage');
const LearningPathsPage = lazyPage(() => import('./learning/PathPages'), 'LearningPathsPage');
const LearningPathPage = lazyPage(() => import('./learning/PathPages'), 'LearningPathPage');
const ExamOverviewPage = lazyPage(() => import('./learning/ExamPages'), 'ExamOverviewPage');
const ExamAttemptPage = lazyPage(() => import('./learning/ExamPages'), 'ExamAttemptPage');
const SubmissionsPage = lazyPage(() => import('./submissions/SubmissionsPage'), 'SubmissionsPage');
const NewTicketPage = lazyPage(() => import('./support/SupportPages'), 'NewTicketPage');
const SupportPage = lazyPage(() => import('./support/SupportPages'), 'SupportPage');
const TicketDetailPage = lazyPage(() => import('./support/SupportPages'), 'TicketDetailPage');
const MyCodesPage = lazyPage(() => import('../codes/participant/MyCodesPage'), 'MyCodesPage');
const MyCodeSalePage = lazyPage(() => import('../codes/participant/MyCodeSalePage'), 'MyCodeSalePage');

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
    to: 'codes',
    label: 'My codes',
    icon: TicketPercent,
    description: 'Brand discount codes: share them, report sales and earn a commission.',
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
    to: 'learning',
    label: 'Learning',
    icon: GraduationCap,
    description: 'Free courses, your progress, exams and certificates.',
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
  { path: 'codes', element: <MyCodesPage /> },
  { path: 'codes/sales/:saleId', element: <MyCodeSalePage /> },
  { path: 'payouts', element: <PayoutsPage /> },
  { path: 'payouts/:itemId', element: <PayoutDetailPage /> },
  { path: 'social-accounts', element: <SocialAccountsPage /> },
  { path: 'referrals', element: <ReferralsPage /> },
  { path: 'achievements', element: <AchievementsPage /> },
  { path: 'learning', element: <LearningHomePage /> },
  { path: 'learning/catalog', element: <LearningCatalogPage /> },
  { path: 'learning/paths', element: <LearningPathsPage /> },
  { path: 'learning/paths/:pathSlug', element: <LearningPathPage /> },
  { path: 'learning/courses/:slug', element: <LearningCoursePage /> },
  { path: 'learning/courses/:slug/lessons/:lessonSlug', element: <LearningLessonPage /> },
  { path: 'learning/courses/:slug/exam', element: <ExamOverviewPage /> },
  { path: 'learning/attempts/:attemptId', element: <ExamAttemptPage /> },
  { path: 'learning/certificates/:id', element: <LearningCertificatePage /> },
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
