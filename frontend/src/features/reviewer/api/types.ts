/**
 * Reviewer API contract types. Mirrors `backend/src/OptimizeAll.Api/Modules/Review/ReviewDtos.cs`,
 * `Modules/Rewards/RewardDtos.cs` and `Modules/Social/SocialDtos.cs` (camelCase JSON, string enums, UTC ISO strings).
 */
import type { IsoDateTime } from '@/lib/api/types';

export type SocialPlatform =
  'Instagram' | 'TikTok' | 'X' | 'Facebook' | 'LinkedIn' | 'YouTube' | 'Threads' | 'Pinterest' | 'Snapchat';

export const SOCIAL_PLATFORMS: SocialPlatform[] = [
  'Instagram',
  'TikTok',
  'X',
  'Facebook',
  'LinkedIn',
  'YouTube',
  'Threads',
  'Pinterest',
  'Snapchat',
];

export type SubmissionStatus =
  'Pending' | 'UnderReview' | 'Approved' | 'NeedsCorrection' | 'Rejected' | 'Reversed' | 'Withdrawn';

export type LiveCheckStatus = 'NotRequired' | 'Pending' | 'ConfirmedLive' | 'Removed';
export type AppealStatus = 'Open' | 'Upheld' | 'Overturned' | 'Withdrawn';
export type VerificationStatus = 'Unverified' | 'PendingReview' | 'Verified' | 'Rejected';
export type ReviewDecision = 'Approve' | 'RequestCorrection' | 'Reject';
export type UserStatus = 'Active' | 'Suspended' | 'Deactivated';

export type FlagType =
  | 'DuplicateScreenshot'
  | 'RepeatedContent'
  | 'OutsideCampaignWindow'
  | 'AfterSubmissionDeadline'
  | 'AccountBelowMinimumAge'
  | 'AccountNotVerified'
  | 'PlatformMismatch'
  | 'UrlHostMismatch'
  | 'HighSubmissionVelocity'
  | 'NewParticipant'
  | 'SharedDeviceOrIp'
  | (string & {});

export interface PersonRef {
  id: string;
  displayName: string;
}

export interface CampaignRef {
  id: string;
  title: string;
}

export interface ReviewQueueItem {
  id: string;
  campaign: CampaignRef;
  participant: { id: string; displayName: string; countryCode: string };
  platform: SocialPlatform;
  handle: string;
  status: SubmissionStatus;
  submittedAt: IsoDateTime;
  riskScore: number;
  flagTypes: FlagType[];
  claimedBy: PersonRef | null;
  claimExpiresAt: IsoDateTime | null;
  assignedReviewer: PersonRef | null;
  correctionCount: number;
}

export interface Claim {
  submissionId: string;
  status: SubmissionStatus;
  claimedBy: PersonRef;
  claimExpiresAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface Requirements {
  campaignId: string;
  campaignTitle: string;
  postingInstructions: string;
  requiredHashtags: string | null;
  requiredMentions: string | null;
  disclosureText: string;
  allowedPlatforms: SocialPlatform[];
  startsAt: IsoDateTime;
  endsAt: IsoDateTime;
  submissionDeadline: IsoDateTime;
  minPostLiveHours: number;
  requireScreenshot: boolean;
}

export interface ReviewSubmission {
  id: string;
  postUrl: string;
  normalizedPostUrl: string;
  platform: SocialPlatform;
  postedAt: IsoDateTime;
  captionText: string | null;
  screenshotUrl: string | null;
  screenshotSha256: string | null;
  submittedAt: IsoDateTime;
  status: SubmissionStatus;
  correctionCount: number;
  riskScore: number;
  rewardRuleSetVersion: number;
  estimatedReward: number;
  currency: string;
  decidedAt: IsoDateTime | null;
  decidedBy: PersonRef | null;
  decisionReason: string | null;
  liveCheckStatus: LiveCheckStatus;
  liveCheckDueAt: IsoDateTime | null;
  assignedReviewer: PersonRef | null;
  concurrencyStamp: string;
  claim: {
    claimedBy: PersonRef | null;
    claimExpiresAt: IsoDateTime | null;
    isMine: boolean;
    isActive: boolean;
  };
}

export interface ReviewAccount {
  id: string;
  handle: string;
  profileUrl: string;
  platform: SocialPlatform;
  accountCreatedAt: IsoDateTime;
  accountAgeDays: number;
  followerCount: number;
  verificationStatus: VerificationStatus;
  isActive: boolean;
}

export interface ReviewParticipant {
  id: string;
  displayName: string;
  email: string;
  countryCode: string;
  tier: string;
  joinedAt: IsoDateTime;
  approvedCount: number;
  rejectedCount: number;
  reversedCount: number;
  /** Only Active participants can be approved (the server answers 409 participant.not_active otherwise). */
  status: UserStatus;
}

export interface HistoryItem {
  id: string;
  campaign: CampaignRef;
  status: SubmissionStatus;
  submittedAt: IsoDateTime;
  postUrl: string;
}

export interface Flag {
  id: string;
  type: FlagType;
  detail: string;
  weight: number;
  createdAt: IsoDateTime;
  resolved: boolean;
  resolvedAt: IsoDateTime | null;
  resolutionNote: string | null;
}

export interface RelatedSubmission {
  id: string;
  match: 'screenshot' | 'content' | 'screenshot_and_content' | (string & {});
  campaign: CampaignRef;
  participant: PersonRef;
  status: SubmissionStatus;
  submittedAt: IsoDateTime;
}

export interface ReviewEvent {
  action: string;
  fromStatus: SubmissionStatus | null;
  toStatus: SubmissionStatus;
  reason: string | null;
  actor: PersonRef | null;
  at: IsoDateTime;
}

export interface RewardLine {
  type: string;
  ruleId: string;
  amount: number;
  uncappedAmount: number;
  requiresApproval: boolean;
  label: string;
}

export interface RewardQuote {
  ruleSetId: string | null;
  ruleSetVersion: number | null;
  currency: string;
  lines: RewardLine[];
  total: number;
  appliedCaps: string[];
  ruleSetSummary: string;
}

export interface ReviewEarning {
  id: string;
  type: string;
  amount: number;
  currency: string;
  status: string;
  createdAt: IsoDateTime;
  rewardRuleSetVersion: number | null;
}

export interface ReviewAppeal {
  id: string;
  status: AppealStatus;
  decisionAppealed: SubmissionStatus;
  reason: string;
  createdAt: IsoDateTime;
  resolvedBy: PersonRef | null;
  resolutionNote: string | null;
  resolvedAt: IsoDateTime | null;
  concurrencyStamp: string;
}

export interface ReviewDetail {
  requirements: Requirements;
  submission: ReviewSubmission;
  account: ReviewAccount;
  participant: ReviewParticipant;
  history: HistoryItem[];
  flags: Flag[];
  relatedSubmissions: RelatedSubmission[];
  events: ReviewEvent[];
  rewardQuote: RewardQuote | null;
  earnings: ReviewEarning[];
  appeals: ReviewAppeal[];
  /** Ceiling of the recorded rule set's QualityBonus rule (rule-set currency); null = no quality bonus. */
  qualityBonusMax: number | null;
}

export interface DecisionRequest {
  decision: ReviewDecision;
  reason?: string;
  qualityBonusAmount?: number;
  concurrencyStamp: string;
}

export interface DecisionResult {
  submissionId: string;
  status: SubmissionStatus;
  decidedAt: IsoDateTime;
  decisionReason: string | null;
  concurrencyStamp: string;
  reward: RewardQuote | null;
  earnings: ReviewEarning[];
  liveCheckStatus: LiveCheckStatus;
  liveCheckDueAt: IsoDateTime | null;
}

export interface LiveCheckItem {
  submissionId: string;
  campaign: CampaignRef;
  participant: PersonRef;
  platform: SocialPlatform;
  postUrl: string;
  postedAt: IsoDateTime;
  decidedAt: IsoDateTime | null;
  dueAt: IsoDateTime | null;
  isDue: boolean;
}

export interface LiveCheckResultDto {
  submissionId: string;
  status: SubmissionStatus;
  liveCheckStatus: LiveCheckStatus;
  earnings: ReviewEarning[];
}

export interface ReverseResult {
  submissionId: string;
  status: SubmissionStatus;
  earnings: ReviewEarning[];
}

export interface AppealListItem {
  id: string;
  submissionId: string;
  campaign: CampaignRef;
  participant: PersonRef;
  status: AppealStatus;
  decisionAppealed: SubmissionStatus;
  reason: string;
  createdAt: IsoDateTime;
  originalDecidedBy: PersonRef | null;
  concurrencyStamp: string;
}

export interface AppealDetail {
  appeal: ReviewAppeal;
  originalDecidedBy: PersonRef | null;
  review: ReviewDetail;
}

export interface AppealResolution {
  appeal: ReviewAppeal;
  submissionStatus: SubmissionStatus;
  earnings: ReviewEarning[];
}

export interface Reviewer {
  id: string;
  displayName: string;
  email: string;
  assignedOpen: number;
  decisionsToday: number;
}

export interface AssignResult {
  updated: number;
  skippedIds: string[];
}

export interface ReviewStats {
  myDecisionsToday: number;
  queueByStatus: Record<string, number>;
  oldestPendingSubmittedAt: IsoDateTime | null;
  oldestPendingAgeHours: number | null;
  dueLiveChecks: number;
  openAppeals: number;
  claimedByMe: number;
}

// ---------- Social verification ----------

export interface ReviewSocialAccount {
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
  verifiedAt: IsoDateTime | null;
  isActive: boolean;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  owner: { id: string; displayName: string; email: string; countryCode: string };
  concurrencyStamp: string;
}

export interface ReviewSocialAccountDetail {
  account: ReviewSocialAccount;
  qualifies: boolean;
  eligibleFrom: IsoDateTime | null;
  reasons: { code: string; message: string }[];
  verifiedBy: PersonRef | null;
  ownerActiveAccountCount: number;
  history: {
    at: IsoDateTime;
    action: string;
    actorUserId: string | null;
    actorDisplayName: string | null;
    reason: string | null;
  }[];
}

export interface SocialDecisionRequest {
  decision: 'Verified' | 'Rejected';
  note?: string;
  verifiedAccountCreatedAt?: string;
  verifiedFollowerCount?: number;
  concurrencyStamp: string;
}
