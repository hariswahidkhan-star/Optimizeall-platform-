/**
 * Participant API contracts. Mirrors the backend DTOs in `backend/src/OptimizeAll.Api/Modules/**` (camelCase JSON,
 * string enums, UTC ISO-8601 timestamps, money as a number with its ISO currency beside it).
 */
import type { IsoDateTime, PagedResult } from '@/lib/api/types';

export type { PagedResult };

/** `DateOnly` values travel as "yyyy-MM-dd". */
export type IsoDate = string;

export const SOCIAL_PLATFORMS = [
  'Instagram',
  'TikTok',
  'X',
  'Facebook',
  'LinkedIn',
  'YouTube',
  'Threads',
  'Pinterest',
  'Snapchat',
] as const;
export type SocialPlatform = (typeof SOCIAL_PLATFORMS)[number];

export type ParticipantTier = 'Standard' | 'Silver' | 'Gold' | 'Platinum';

export interface Reason {
  code: string;
  message: string;
}

// ---------------------------------------------------------------- Accounts / home

export type HomeState = 'VerifyEmail' | 'AddSocialAccount' | 'AwaitingEligibility' | 'Ready' | 'Active';

export interface OnboardingStep {
  id: string;
  key: string;
  title: string;
  description: string;
  actionLabel: string | null;
  actionUrl: string | null;
  completionRule: string;
  isManual: boolean;
  completed: boolean;
}

export interface HomeBanner {
  id: string;
  title: string;
  body: string | null;
  imageUrl: string | null;
  ctaLabel: string | null;
  ctaUrl: string | null;
}

export type AnnouncementSeverity = 'Info' | 'Success' | 'Warning' | 'Critical';

export interface Announcement {
  id: string;
  title: string;
  body: string;
  severity: AnnouncementSeverity;
  publishAt: IsoDateTime;
  expiresAt: IsoDateTime | null;
}

export interface SubmissionCounts {
  total: number;
  pending: number;
  underReview: number;
  approved: number;
  needsCorrection: number;
  rejected: number;
  reversed: number;
}

export interface ParticipantHome {
  user: {
    id: string;
    displayName: string;
    emailVerified: boolean;
    tier: ParticipantTier;
    timeZone: string;
    countryCode: string;
    languageCode: string;
  };
  state: HomeState;
  eligibleFrom: IsoDateTime | null;
  onboarding: {
    steps: OnboardingStep[];
    completedCount: number;
    totalCount: number;
    progressPercent: number;
  };
  banners: HomeBanner[];
  announcements: Announcement[];
  submissions: SubmissionCounts;
  unreadNotificationCount: number;
  openSupportTicketCount: number;
  socialAccounts: {
    total: number;
    eligible: number;
    soonestEligibleFrom: IsoDateTime | null;
    minAccountAgeDays: number;
  };
}

export interface Profile {
  id: string;
  email: string;
  emailVerified: boolean;
  displayName: string;
  countryCode: string;
  languageCode: string;
  timeZone: string;
  interests: string[];
  marketingEmailOptIn: boolean;
  whatsAppNumber: string | null;
  whatsAppOptIn: boolean;
  tier: ParticipantTier;
  referralCode: string;
  createdAt: IsoDateTime;
}

export interface UpdateProfileRequest {
  displayName: string;
  countryCode: string;
  languageCode: string;
  timeZone: string;
  interests: string[];
  marketingEmailOptIn: boolean;
  whatsAppNumber: string | null;
  whatsAppOptIn: boolean;
}

export type PayoutMethod = 'BankTransfer' | 'PayPal' | 'MobileWallet' | 'Other';

export interface PayoutProfile {
  configured: boolean;
  method: PayoutMethod | null;
  accountHolderName: string | null;
  destinationHint: string | null;
  preferredCurrency: string | null;
  countryCode: string | null;
  updatedAt: IsoDateTime | null;
}

export interface UpdatePayoutProfileRequest {
  method: PayoutMethod;
  accountHolderName: string;
  destination: string;
  preferredCurrency: string;
  countryCode: string | null;
}

// ---------------------------------------------------------------- Social accounts

export type VerificationStatus = 'Unverified' | 'PendingReview' | 'Verified' | 'Rejected';

export interface SocialAccount {
  id: string;
  platform: SocialPlatform;
  handle: string;
  profileUrl: string;
  accountCreatedAt: IsoDateTime;
  accountAgeDays: number;
  followerCount: number;
  primaryLanguage: string | null;
  audienceCountryCode: string | null;
  verificationStatus: VerificationStatus;
  verificationNote: string | null;
  isActive: boolean;
  qualifies: boolean;
  eligibleFrom: IsoDateTime | null;
  reasons: Reason[];
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface SocialAccountList {
  minAccountAgeDays: number;
  minFollowers: number;
  maxActiveAccounts: number;
  items: SocialAccount[];
}

export interface SocialAccountInput {
  handle: string;
  profileUrl: string;
  accountCreatedAt: IsoDateTime;
  followerCount: number;
  primaryLanguage: string | null;
  audienceCountryCode: string | null;
}

export interface SocialAccountChangeResponse {
  account: SocialAccount;
  verificationReset: boolean;
  message: string;
}

// ---------------------------------------------------------------- Campaigns

export interface CategoryRef {
  id: string;
  name: string;
  slug: string;
}

export interface CampaignCategory extends CategoryRef {
  description: string | null;
  icon: string | null;
  sortOrder: number;
  isActive: boolean;
}

export type CampaignStatus = 'Draft' | 'Scheduled' | 'Active' | 'Paused' | 'Ended' | 'Archived';

export interface CardReward {
  currency: string;
  baseAmount: number;
  maxAmount: number;
  hasBonuses: boolean;
}

export interface CampaignCard {
  id: string;
  slug: string;
  title: string;
  summary: string;
  category: CategoryRef | null;
  topics: string[];
  platforms: SocialPlatform[];
  status: CampaignStatus;
  upcoming: boolean;
  startsAt: IsoDateTime;
  endsAt: IsoDateTime;
  submissionDeadline: IsoDateTime;
  heroImageUrl: string | null;
  reward: CardReward | null;
  eligibility: { isEligible: boolean; reasons: Reason[] };
  mySubmissionCount: number;
  remainingSubmissions: number;
}

export interface RecommendedCampaign {
  campaign: CampaignCard;
  score: number;
  reason: string;
}

export type CampaignAssetType = 'Image' | 'Video' | 'Caption' | 'Link' | 'Document';

export interface CampaignAsset {
  id: string;
  type: CampaignAssetType;
  title: string;
  url: string | null;
  fileId: string | null;
  body: string | null;
  platform: SocialPlatform | null;
  templateId: string | null;
  sortOrder: number;
}

export interface RewardOverrideTerm {
  platform: SocialPlatform | null;
  countryCode: string | null;
  tier: ParticipantTier | null;
  amount: number;
  validFrom: IsoDateTime | null;
  validTo: IsoDateTime | null;
  label: string | null;
}

export interface RewardBonusTerm {
  type: string;
  amount: number;
  approvalMode: string;
  validFrom: IsoDateTime | null;
  validTo: IsoDateTime | null;
  label: string | null;
}

export interface RewardTerms {
  currency: string;
  baseAmount: number;
  overrides: RewardOverrideTerm[];
  bonuses: RewardBonusTerm[];
  dailyCap: number | null;
  weeklyCap: number | null;
  campaignCap: number | null;
  minPostLiveHours: number;
  maxSubmissionsPerParticipant: number;
  requireScreenshot: boolean;
  ruleSetVersion: number;
}

export interface AccountEligibility {
  socialAccountId: string;
  platform: SocialPlatform;
  handle: string;
  isEligible: boolean;
  eligibleFrom: IsoDateTime | null;
  reasons: Reason[];
}

export interface CampaignDetail {
  id: string;
  slug: string;
  title: string;
  summary: string;
  category: CategoryRef | null;
  topics: string[];
  platforms: SocialPlatform[];
  status: CampaignStatus;
  visibility: 'Public' | 'InviteOnly';
  upcoming: boolean;
  isOpenForSubmissions: boolean;
  startsAt: IsoDateTime;
  endsAt: IsoDateTime;
  submissionDeadline: IsoDateTime;
  timeZone: string;
  heroImageUrl: string | null;
  landingHeadline: string | null;
  landingBody: string | null;
  reward: CardReward | null;
  description: string;
  postingInstructions: string;
  requiredHashtags: string | null;
  requiredMentions: string | null;
  assets: CampaignAsset[];
  disclosures: { platform: SocialPlatform; text: string }[];
  rewardTerms: RewardTerms | null;
  eligibility: { isEligible: boolean; reasons: Reason[]; accounts: AccountEligibility[] };
  mySubmissions: { id: string; status: SubmissionStatus; submittedAt: IsoDateTime }[];
  mySubmissionCount: number;
  remainingSubmissions: number;
}

export type CampaignSort = 'deadline' | 'reward' | 'newest';

export interface CampaignFilters {
  platform?: string;
  categoryId?: string;
  topic?: string;
  minReward?: string;
  deadlineBefore?: string;
  eligibleOnly?: boolean;
  search?: string;
  sort?: CampaignSort;
  page: number;
}

export type ExperimentElement = 'Title' | 'CreativeAsset' | 'Instructions' | 'LandingPage';

export interface ExperimentVariant {
  experimentId: string;
  element: ExperimentElement;
  variantId: string;
  key: string;
  title: string | null;
  instructions: string | null;
  asset: { id: string; url: string | null; title: string; type: CampaignAssetType } | null;
  landingHeadline: string | null;
  landingBody: string | null;
}

export interface TrackingLink {
  id: string;
  campaignId: string;
  campaignTitle: string;
  code: string;
  shortUrl: string;
  destinationPreview: string;
  utmSource: string;
  utmMedium: string;
  utmCampaign: string;
  utmContent: string | null;
  createdAt: IsoDateTime;
  stats: { clicks: number; uniqueClicks: number; verifiedConversions: number };
}

// ---------------------------------------------------------------- Submissions

export type SubmissionStatus =
  'Pending' | 'UnderReview' | 'Approved' | 'NeedsCorrection' | 'Rejected' | 'Reversed';

export type LiveCheckStatus = 'NotRequired' | 'Pending' | 'ConfirmedLive' | 'Removed';

export type AppealStatus = 'Open' | 'Upheld' | 'Overturned' | 'Withdrawn';

export interface SubmissionListItem {
  id: string;
  campaign: { id: string; slug: string; title: string };
  platform: SocialPlatform;
  postUrl: string;
  status: SubmissionStatus;
  submittedAt: IsoDateTime;
  estimatedReward: number;
  currency: string;
  decisionReason: string | null;
}

export interface SubmissionTimelineEntry {
  action: string;
  fromStatus: SubmissionStatus | null;
  toStatus: SubmissionStatus;
  reason: string | null;
  actor: string;
  at: IsoDateTime;
}

export interface SubmissionEarning {
  id: string;
  type: string;
  amount: number;
  currency: string;
  status: EarningStatus;
  createdAt: IsoDateTime;
}

export interface SubmissionAppeal {
  id: string;
  status: AppealStatus;
  decisionAppealed: SubmissionStatus;
  reason: string;
  resolutionNote: string | null;
  createdAt: IsoDateTime;
  resolvedAt: IsoDateTime | null;
}

export interface SubmissionDetail {
  id: string;
  campaign: { id: string; slug: string; title: string };
  platform: SocialPlatform;
  socialAccount: { id: string; platform: SocialPlatform; handle: string };
  postUrl: string;
  postedAt: IsoDateTime;
  captionText: string | null;
  screenshotUrl: string | null;
  status: SubmissionStatus;
  submittedAt: IsoDateTime;
  decidedAt: IsoDateTime | null;
  decisionReason: string | null;
  correctionCount: number;
  estimatedReward: number;
  currency: string;
  rewardRuleSetVersion: number;
  liveCheck: { status: LiveCheckStatus; dueAt: IsoDateTime | null; checkedAt: IsoDateTime | null };
  timeline: SubmissionTimelineEntry[];
  earnings: SubmissionEarning[];
  appeal: SubmissionAppeal | null;
  canEdit: boolean;
  canAppeal: boolean;
  appealDeadline: IsoDateTime | null;
}

// ---------------------------------------------------------------- Ledger & payouts

export type EarningStatus = 'PendingApproval' | 'Approved' | 'Scheduled' | 'Paid' | 'Reversed' | 'Declined';

export const EARNING_TYPES = [
  'PostReward',
  'FirstPostBonus',
  'TimeLimitedBonus',
  'QualityBonus',
  'ReferralReward',
  'Adjustment',
  'Reversal',
] as const;
export type EarningType = (typeof EARNING_TYPES)[number];

export interface EarningsSummary {
  currency: string;
  pending: number;
  approved: number;
  onHold: number;
  scheduled: number;
  paid: number;
  reversed: number;
  availableForNextPayout: number;
  lifetimeEarned: number;
  nextPayout: {
    periodKey: string;
    cutoffAt: IsoDateTime;
    paymentDate: IsoDate;
    minimumPayoutAmount: number;
    meetsMinimum: boolean;
    estimatedAmount: number;
  };
  activeHold: boolean;
  holdMessage: string | null;
  pendingByCurrency: { currency: string; amount: number; converted: boolean }[];
  byCurrency: {
    currency: string;
    pendingApproval: number;
    approved: number;
    scheduled: number;
    paid: number;
    reversed: number;
  }[];
}

export interface Earning {
  id: string;
  createdAt: IsoDateTime;
  type: EarningType;
  status: EarningStatus;
  description: string;
  campaign: { id: string; title: string } | null;
  submissionId: string | null;
  originalAmount: number;
  originalCurrency: string;
  exchangeRate: number;
  settlementAmount: number;
  settlementCurrency: string;
  ruleSetVersion: number | null;
  availableAt: IsoDateTime | null;
  paidAt: IsoDateTime | null;
  reason: string | null;
}

export type PayoutItemStatus = 'AwaitingPayment' | 'Paid' | 'Failed';

export interface MyPayout {
  itemId: string;
  batchReference: string;
  periodKey: string;
  cutoffAt: IsoDateTime;
  paymentDate: IsoDate;
  amount: number;
  currency: string;
  status: PayoutItemStatus;
  paidAt: IsoDateTime | null;
  paymentReference: string | null;
  earningCount: number;
}

export interface MyPayoutDetail {
  payout: MyPayout;
  earnings: Earning[];
}

// ---------------------------------------------------------------- Marketing

export interface Referrals {
  code: string;
  link: string;
  program: {
    enabled: boolean;
    rewardAmount: number;
    currency: string;
    qualifyingAction: string;
    qualifyWithinDays: number;
  };
  stats: {
    registered: number;
    qualified: number;
    rewarded: number;
    pendingReward: number;
    expired: number;
    rejected: number;
  };
  items: {
    id: string;
    maskedName: string;
    status: 'Registered' | 'Qualified' | 'Rejected' | 'Expired';
    registeredAt: IsoDateTime;
    qualifiedAt: IsoDateTime | null;
    qualifyBy: IsoDateTime;
    rewardStatus: EarningStatus | null;
  }[];
}

export interface Achievement {
  key: string;
  name: string;
  description: string;
  icon: string | null;
  criterion: string;
  threshold: number;
  progress: number;
  awardedAt: IsoDateTime | null;
}

// ---------------------------------------------------------------- Notifications

export interface NotificationItem {
  id: string;
  type: string;
  title: string;
  body: string;
  linkUrl: string | null;
  createdAt: IsoDateTime;
  readAt: IsoDateTime | null;
  isRead: boolean;
}

export type NotificationChannel = 'InApp' | 'Email' | 'WhatsApp';

export interface NotificationPreferences {
  channels: { channel: NotificationChannel; available: boolean; reason: string | null }[];
  types: {
    type: string;
    label: string;
    description: string;
    essential: boolean;
    marketing: boolean;
    channels: { channel: NotificationChannel; enabled: boolean; locked: boolean; available: boolean }[];
  }[];
}

// ---------------------------------------------------------------- Support

export type TicketStatus = 'Open' | 'AwaitingParticipant' | 'AwaitingStaff' | 'Resolved' | 'Closed';
export const TICKET_CATEGORIES = [
  'General',
  'Account',
  'SocialProfile',
  'Submission',
  'Payout',
  'Technical',
  'Dispute',
] as const;
export type TicketCategory = (typeof TICKET_CATEGORIES)[number];

export interface TicketSummary {
  id: string;
  reference: string;
  subject: string;
  category: TicketCategory;
  status: TicketStatus;
  priority: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface Ticket extends TicketSummary {
  submissionId: string | null;
  payoutItemId: string | null;
  resolvedAt: IsoDateTime | null;
  canReply: boolean;
  messages: { id: string; body: string; fromStaff: boolean; authorName: string; createdAt: IsoDateTime }[];
}

export interface CreateTicketRequest {
  subject: string;
  category: TicketCategory;
  body: string;
  submissionId: string | null;
  payoutItemId: string | null;
}
