/**
 * Campaign-manager API contracts. Mirrors the backend DTOs in `Modules/Campaigns`, `Modules/Rewards`,
 * `Modules/Marketing/**`, `Modules/Analytics` and `Modules/Retention` (camelCase JSON, string enums, UTC ISO dates).
 */
import type { IsoDateTime } from '@/lib/api/types';

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

export const PARTICIPANT_TIERS = ['Standard', 'Silver', 'Gold', 'Platinum'] as const;
export type ParticipantTier = (typeof PARTICIPANT_TIERS)[number];

export const CAMPAIGN_STATUSES = ['Draft', 'Scheduled', 'Active', 'Paused', 'Ended', 'Archived'] as const;
export type CampaignStatus = (typeof CAMPAIGN_STATUSES)[number];

export type CampaignVisibility = 'Public' | 'InviteOnly';

export const ASSET_TYPES = ['Image', 'Video', 'Caption', 'Link', 'Document'] as const;
export type CampaignAssetType = (typeof ASSET_TYPES)[number];

export const REWARD_RULE_TYPES = [
  'BaseRate',
  'RateOverride',
  'TimeLimitedBonus',
  'FirstPostBonus',
  'QualityBonus',
] as const;
export type RewardRuleType = (typeof REWARD_RULE_TYPES)[number];
export type BonusApprovalMode = 'Automatic' | 'ManualApproval';

export interface CategoryRef {
  id: string;
  name: string;
  slug: string;
}

export interface AdminCategory extends CategoryRef {
  description: string | null;
  icon: string | null;
  sortOrder: number;
  isActive: boolean;
  campaignCount: number;
}

export interface SubmissionCounts {
  total: number;
  pending: number;
  approved: number;
  rejected: number;
}

export interface AdminCampaignListItem {
  id: string;
  slug: string;
  title: string;
  status: CampaignStatus;
  visibility: CampaignVisibility;
  category: CategoryRef | null;
  platforms: SocialPlatform[];
  startsAt: IsoDateTime;
  endsAt: IsoDateTime;
  submissionDeadline: IsoDateTime;
  submissions: SubmissionCounts;
  currency: string;
  spent: number;
  budget: number | null;
  budgetRemaining: number | null;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  publishedAt: IsoDateTime | null;
}

export interface Eligibility {
  minAccountAgeDays: number | null;
  minFollowers: number;
  requireVerifiedAccount: boolean;
  countries: string[];
  languages: string[];
  interests: string[];
  tiers: ParticipantTier[];
}

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

export interface AssetInput {
  type: CampaignAssetType;
  title: string;
  url?: string | null;
  fileId?: string | null;
  body?: string | null;
  platform?: SocialPlatform | null;
  sortOrder?: number | null;
  templateId?: string | null;
}

export interface Disclosure {
  id: string;
  platform: SocialPlatform | null;
  countryCode: string | null;
  text: string;
}

export interface DisclosureInput {
  platform: SocialPlatform | null;
  countryCode: string | null;
  text: string;
}

export interface RewardRule {
  id: string;
  type: RewardRuleType;
  amount: number;
  platform: SocialPlatform | null;
  countryCode: string | null;
  tier: ParticipantTier | null;
  validFrom: IsoDateTime | null;
  validTo: IsoDateTime | null;
  approvalMode: BonusApprovalMode;
  priority: number;
  label: string | null;
}

export interface RewardRuleSet {
  id: string;
  version: number;
  currency: string;
  dailyCapPerParticipant: number | null;
  weeklyCapPerParticipant: number | null;
  campaignCapPerParticipant: number | null;
  effectiveFrom: IsoDateTime;
  createdAt: IsoDateTime;
  createdBy: { id: string; displayName: string } | null;
  reason: string;
  summary: string;
  isCurrent: boolean;
  inUseBySubmissions: number;
  rules: RewardRule[];
  personalRatesMode?: PersonalRatesMode;
  personalRateMaxMultiplier?: number | null;
}

/** Campaign policy for person-level rates (rate cards, rate groups, negotiated deals). */
export type PersonalRatesMode = 'Allowed' | 'CampaignRatesOnly';

export interface RewardRuleInput {
  type: RewardRuleType;
  amount: number;
  platform?: SocialPlatform | null;
  countryCode?: string | null;
  tier?: ParticipantTier | null;
  validFrom?: IsoDateTime | null;
  validTo?: IsoDateTime | null;
  approvalMode?: BonusApprovalMode;
  priority?: number;
  label?: string | null;
}

export interface RewardRuleSetInput {
  currency: string;
  dailyCapPerParticipant: number | null;
  weeklyCapPerParticipant: number | null;
  campaignCapPerParticipant: number | null;
  personalRatesMode?: PersonalRatesMode;
  personalRateMaxMultiplier?: number | null;
  rules: RewardRuleInput[];
}

export interface RewardPreviewRequest {
  platform: SocialPlatform;
  countryCode: string;
  tier: ParticipantTier;
  postedAt?: IsoDateTime | null;
  isFirstApprovedPost: boolean;
  earnedToday: number;
  earnedThisWeek: number;
  earnedInCampaign: number;
  qualityBonusRequested?: number | null;
  ruleSetVersion?: number | null;
  draft?: RewardRuleSetInput | null;
  campaignBudgetRemaining?: number | null;
}

export interface RewardLine {
  type: string;
  ruleId: string;
  amount: number;
  uncappedAmount: number;
  requiresApproval: boolean;
  label: string;
  fromPersonalRate?: boolean;
}

/** Where a post rate came from (campaign rules or a person-level rate), as returned with quotes. */
export interface RateSource {
  level: string;
  levelLabel: string;
  label: string;
  campaignRateAmount: number;
  personalAmount: number | null;
  limited: boolean;
  ignoredReason: string | null;
  cardAmount: number | null;
  cardCurrency: string | null;
  exchangeRate: number | null;
  validTo: IsoDateTime | null;
  rateCardId: string | null;
  rateCardVersion: number | null;
  rateGroupId: string | null;
  rateAssignmentId: string | null;
}

export interface RewardQuote {
  ruleSetId: string | null;
  ruleSetVersion: number | null;
  currency: string;
  lines: RewardLine[];
  total: number;
  appliedCaps: string[];
  ruleSetSummary: string;
  rateSource?: RateSource | null;
}

/** Fields shared by create and update. */
export interface CampaignFieldsInput {
  title: string;
  slug?: string | null;
  summary: string;
  description: string;
  categoryId: string | null;
  topics: string[];
  visibility: CampaignVisibility;
  startsAt: IsoDateTime;
  endsAt: IsoDateTime;
  submissionDeadline: IsoDateTime | null;
  timeZone: string;
  postingInstructions: string;
  defaultDisclosureText: string;
  requiredHashtags: string | null;
  requiredMentions: string | null;
  budgetAmount: number | null;
  budgetCurrency: string | null;
  maxSubmissionsPerParticipant: number;
  minPostLiveHours: number;
  requireScreenshot: boolean;
  eligibility: Eligibility;
  platforms: SocialPlatform[];
  landingHeadline: string | null;
  landingBody: string | null;
  heroImageUrl: string | null;
  trackingDestinationUrl: string | null;
  utmCampaign: string | null;
}

export interface CreateCampaignRequest extends CampaignFieldsInput {
  rewardRules: RewardRuleSetInput;
}

export interface UpdateCampaignRequest extends CampaignFieldsInput {
  concurrencyStamp: string;
  confirm?: boolean;
  reason?: string | null;
}

export interface AdminCampaign {
  id: string;
  slug: string;
  title: string;
  summary: string;
  description: string;
  category: CategoryRef | null;
  topics: string[];
  status: CampaignStatus;
  visibility: CampaignVisibility;
  startsAt: IsoDateTime;
  endsAt: IsoDateTime;
  submissionDeadline: IsoDateTime;
  timeZone: string;
  postingInstructions: string;
  defaultDisclosureText: string;
  requiredHashtags: string | null;
  requiredMentions: string | null;
  budgetAmount: number | null;
  budgetCurrency: string;
  spent: number;
  budgetRemaining: number | null;
  maxSubmissionsPerParticipant: number;
  minPostLiveHours: number;
  requireScreenshot: boolean;
  eligibility: Eligibility;
  platforms: SocialPlatform[];
  landingHeadline: string | null;
  landingBody: string | null;
  heroImageUrl: string | null;
  trackingDestinationUrl: string | null;
  utmCampaign: string | null;
  assets: CampaignAsset[];
  disclosures: Disclosure[];
  currentRuleSet: RewardRuleSet | null;
  submissions: SubmissionCounts;
  createdByUserId: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  publishedAt: IsoDateTime | null;
  concurrencyStamp: string;
  /** App-relative public landing page (`/c/{slug}`); reachable while the campaign is Public and Scheduled/Active. */
  publicLandingPath: string;
}

export interface UploadedFile {
  id: string;
  url: string;
  contentType: string;
  sizeBytes: number;
  width: number;
  height: number;
  sha256: string;
  originalFileName: string;
  purpose: string;
  isPublic: boolean;
  createdAt: IsoDateTime;
}

// ---------------------------------------------------------------- marketing

export interface PostTemplate {
  id: string;
  name: string;
  platform: SocialPlatform | null;
  body: string;
  hashtags: string | null;
  languageCode: string | null;
  isArchived: boolean;
  usageCount: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface TemplateInput {
  name: string;
  platform: SocialPlatform | null;
  body: string;
  hashtags: string | null;
  languageCode: string | null;
  isArchived: boolean;
}

export const CALENDAR_STATUSES = ['Planned', 'Scheduled', 'Published', 'Cancelled'] as const;
export type CalendarEntryStatus = (typeof CALENDAR_STATUSES)[number];

export interface CalendarEntry {
  id: string;
  title: string;
  campaignId: string | null;
  campaignTitle: string | null;
  templateId: string | null;
  templateName: string | null;
  platform: SocialPlatform | null;
  scheduledFor: IsoDateTime;
  notes: string | null;
  status: CalendarEntryStatus;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface CalendarEntryInput {
  title: string;
  campaignId: string | null;
  templateId: string | null;
  platform: SocialPlatform | null;
  scheduledFor: IsoDateTime;
  notes: string | null;
  status: CalendarEntryStatus;
}

export interface CalendarResponse {
  from: IsoDateTime;
  to: IsoDateTime;
  items: CalendarEntry[];
}

export interface Invitation {
  id: string;
  code: string;
  url: string;
  name: string;
  campaignId: string | null;
  campaignTitle: string | null;
  utmSource: string | null;
  utmMedium: string | null;
  utmCampaign: string | null;
  expiresAt: IsoDateTime | null;
  maxUses: number | null;
  isActive: boolean;
  isUsable: boolean;
  stats: { visits: number; registrations: number; remainingUses: number | null };
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface InvitationInput {
  name: string;
  campaignId: string | null;
  utmSource: string | null;
  utmMedium: string | null;
  utmCampaign: string | null;
  expiresAt: IsoDateTime | null;
  maxUses: number | null;
  isActive: boolean;
}

export interface PublicInvitationLanding {
  code: string;
  type: 'campaign' | 'platform';
  headline: string;
  body: string;
  heroImageUrl: string | null;
  campaign: {
    slug: string;
    title: string;
    summary: string;
    platforms: SocialPlatform[];
    reward: { currency: string; baseAmount: number } | null;
    startsAt: IsoDateTime;
    endsAt: IsoDateTime;
    category: { name: string; slug: string } | null;
  } | null;
  utm: { source: string | null; medium: string | null; campaign: string | null } | null;
  experiment: { experimentId: string; variantId: string; key: string } | null;
}

export const EXPERIMENT_ELEMENTS = ['Title', 'CreativeAsset', 'Instructions', 'LandingPage'] as const;
export type ExperimentElement = (typeof EXPERIMENT_ELEMENTS)[number];
export const EXPERIMENT_STATUSES = ['Draft', 'Running', 'Paused', 'Completed'] as const;
export type ExperimentStatus = (typeof EXPERIMENT_STATUSES)[number];

export interface ExperimentVariant {
  id: string;
  key: string;
  name: string;
  weight: number;
  title: string | null;
  instructions: string | null;
  assetId: string | null;
  landingHeadline: string | null;
  landingBody: string | null;
}

export interface Experiment {
  id: string;
  campaignId: string;
  campaignTitle: string;
  name: string;
  hypothesis: string | null;
  element: ExperimentElement;
  status: ExperimentStatus;
  startedAt: IsoDateTime | null;
  endedAt: IsoDateTime | null;
  winningVariantId: string | null;
  variants: ExperimentVariant[];
  concurrencyStamp: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface VariantInput {
  key: string;
  name: string;
  weight: number;
  title?: string | null;
  instructions?: string | null;
  assetId?: string | null;
  landingHeadline?: string | null;
  landingBody?: string | null;
}

export interface ExperimentInput {
  campaignId: string;
  name: string;
  hypothesis: string | null;
  element: ExperimentElement;
  variants: VariantInput[];
  concurrencyStamp?: string;
}

export interface VariantResult {
  variantId: string;
  key: string;
  name: string;
  weight: number;
  assigned: number;
  submissions: number;
  approved: number;
  totalSubmissions: number;
  totalApproved: number;
  submissionRate: number | null;
  approvalRate: number | null;
}

export interface VariantComparison {
  variantKey: string;
  controlKey: string;
  absoluteLift: number | null;
  relativeLift: number | null;
  zScore: number | null;
  pValue: number | null;
  significant: boolean;
  note: string;
}

export interface ExperimentResults {
  experimentId: string;
  status: ExperimentStatus;
  metric: string;
  measurement: string;
  variants: VariantResult[];
  comparisons: VariantComparison[];
  method: string;
}

export const REFERRAL_STATUSES = ['Registered', 'Qualified', 'Rejected', 'Expired'] as const;
export type ReferralStatus = (typeof REFERRAL_STATUSES)[number];

export interface ReferralAdmin {
  id: string;
  referrer: { id: string; displayName: string; email: string };
  referred: { id: string; displayName: string; email: string };
  codeUsed: string;
  status: ReferralStatus;
  qualifyingAction: string;
  registeredAt: IsoDateTime;
  qualifyBy: IsoDateTime;
  qualifiedAt: IsoDateTime | null;
  fraudSignals: string[];
  rejectionReason: string | null;
  earningEntryId: string | null;
  rewardStatus: string | null;
  rewardAmount: number | null;
  rewardCurrency: string | null;
}

export const ACHIEVEMENT_CRITERIA = [
  'ApprovedSubmissions',
  'CampaignsCompleted',
  'TotalEarnedSettlement',
  'QualifiedReferrals',
  'PlatformsUsed',
] as const;
export type AchievementCriterion = (typeof ACHIEVEMENT_CRITERIA)[number];

export interface Achievement {
  id: string;
  key: string;
  name: string;
  description: string;
  icon: string | null;
  criterion: AchievementCriterion;
  threshold: number;
  sortOrder: number;
  isActive: boolean;
  awardedCount: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface AchievementInput {
  key: string;
  name: string;
  description: string;
  icon: string | null;
  criterion: AchievementCriterion;
  threshold: number;
  sortOrder: number;
  isActive: boolean;
}

// ---------------------------------------------------------------- analytics

export type MetricMeasurement = 'counted' | 'measured' | 'estimated';
export type MetricUnit = 'count' | 'percent' | 'money';

export interface Metric {
  key: string;
  label: string;
  value: number | null;
  unit: MetricUnit;
  measurement: MetricMeasurement;
  note: string | null;
  currency?: string | null;
}

export interface MetricSection {
  key: string;
  title: string;
  measurement: MetricMeasurement;
  metrics: Metric[];
}

export interface CurrencyAmount {
  currency: string;
  amount: number;
}

export interface CampaignAnalyticsRow {
  campaignId: string;
  title: string;
  status: CampaignStatus;
  submitted: number;
  approved: number;
  approvalRate: number | null;
  spend: CurrencyAmount[];
  costPerApproved: CurrencyAmount[];
  clicks: number;
  uniqueClicks: number;
  verifiedConversions: number;
  estimatedReach: number;
}

export interface PlatformAnalyticsRow {
  platform: SocialPlatform;
  submitted: number;
  approved: number;
  approvalRate: number | null;
  spend: CurrencyAmount[];
  costPerApproved: CurrencyAmount[];
  estimatedReach: number;
}

export interface AnalyticsOverview {
  from: IsoDateTime;
  to: IsoDateTime;
  campaignId: string | null;
  platform: SocialPlatform | null;
  funnel: MetricSection;
  posts: MetricSection;
  spend: MetricSection;
  spendByCampaign: { campaignId: string; title: string; currency: string; amount: number }[];
  reach: MetricSection;
  traffic: MetricSection;
  conversions: MetricSection;
  timeseries: {
    date: string;
    registrations: number;
    submissions: number;
    approvals: number;
    clicks: number;
  }[];
  campaigns: CampaignAnalyticsRow[];
  platforms: PlatformAnalyticsRow[] | null;
  timeBasis: string;
}

export interface RetentionSummary {
  from: IsoDateTime;
  to: IsoDateTime;
  total: number;
  items: { kind: string; sent: number }[];
}

export interface TrackingSummary {
  from: IsoDateTime;
  to: IsoDateTime;
  campaignId: string | null;
  clicks: number;
  uniqueClicks: number;
  botClicksExcluded: number;
  verifiedConversions: number;
  conversionValue: CurrencyAmount[];
  topParticipants: {
    userId: string | null;
    displayName: string | null;
    clicks: number;
    uniqueClicks: number;
    verifiedConversions: number;
  }[];
  note: string;
}

export interface SettingEntry {
  key: string;
  value: unknown;
  defaultValue: unknown;
  isDefault: boolean;
  valueType: string;
  description: string;
}
