/** Person-level pricing contracts — mirror of backend `Modules/Rates/RateDtos.cs`. */
import type { IsoDateTime } from '@/lib/api/types';
import type { ParticipantTier, RewardQuote, SocialPlatform } from '@/features/campaigns/api/types';

export const CONTENT_FORMATS = ['Post', 'Story', 'ShortVideo', 'LongVideo', 'Carousel'] as const;
export type ContentFormat = (typeof CONTENT_FORMATS)[number];

export type RateCardStatus = 'Draft' | 'Active' | 'Archived';
export type RateCardKind = 'Standard' | 'Custom';
export type RateCardVersionStatus = 'Approved' | 'PendingApproval' | 'Rejected';
export type MembershipMode = 'Manual' | 'Automatic';
export type AssignmentTarget = 'Person' | 'Group';
export type RateOutcome = 'Won' | 'NotApplicable' | 'Outranked';

/** Precedence levels, highest first (lower rank wins). */
export const RATE_LEVELS = [
  'CampaignPersonalCustom',
  'CampaignPersonalCard',
  'CampaignGroup',
  'GlobalPersonalCustom',
  'GlobalPersonalCard',
  'GlobalGroup',
  'CampaignSegment',
  'GlobalSegment',
  'CampaignRules',
] as const;
export type RateSourceLevel = (typeof RATE_LEVELS)[number];

export interface UserRef {
  id: string;
  displayName: string;
}

export interface RateCardLine {
  id: string;
  platform: SocialPlatform | null;
  format: ContentFormat | null;
  countryCode: string | null;
  amount: number;
  label: string | null;
}

export interface RateCardLineInput {
  platform: SocialPlatform | null;
  format: ContentFormat | null;
  countryCode: string | null;
  amount: number;
  label: string | null;
}

export interface RateCardRatesInput {
  currency: string;
  dailyCapPerParticipant: number | null;
  weeklyCapPerParticipant: number | null;
  campaignCapPerParticipant: number | null;
  stackCampaignBonuses: boolean;
  lines: RateCardLineInput[];
}

export interface RateCardVersion {
  id: string;
  version: number;
  currency: string;
  dailyCapPerParticipant: number | null;
  weeklyCapPerParticipant: number | null;
  campaignCapPerParticipant: number | null;
  stackCampaignBonuses: boolean;
  effectiveFrom: IsoDateTime;
  createdAt: IsoDateTime;
  createdBy: UserRef | null;
  reason: string;
  status: RateCardVersionStatus;
  decidedBy: UserRef | null;
  decidedAt: IsoDateTime | null;
  decisionNote: string | null;
  maxIncreasePercent: number | null;
  isCurrent: boolean;
  lines: RateCardLine[];
}

export interface RateCardListItem {
  id: string;
  name: string;
  description: string | null;
  kind: RateCardKind;
  status: RateCardStatus;
  currency: string;
  currentVersion: number;
  lineCount: number;
  minAmount: number | null;
  maxAmount: number | null;
  activeAssignments: number;
  pendingApproval: boolean;
  updatedAt: IsoDateTime;
}

export interface RateCardRef {
  id: string;
  name: string;
  kind: RateCardKind;
  status: RateCardStatus;
  currency: string;
  currentVersion: number;
}

export interface RateGroupRef {
  id: string;
  name: string;
  membershipMode: MembershipMode;
  priority: number;
}

export interface RateAssignment {
  id: string;
  level: RateSourceLevel;
  levelLabel: string;
  target: AssignmentTarget;
  card: RateCardRef;
  person: UserRef | null;
  group: RateGroupRef | null;
  campaign: { id: string; title: string } | null;
  isCustom: boolean;
  validFrom: IsoDateTime | null;
  validTo: IsoDateTime | null;
  endedAt: IsoDateTime | null;
  endReason: string | null;
  note: string;
  isActive: boolean;
  createdAt: IsoDateTime;
  createdBy: UserRef | null;
  concurrencyStamp: string;
}

export interface RateCard {
  id: string;
  name: string;
  description: string | null;
  kind: RateCardKind;
  status: RateCardStatus;
  currency: string;
  currentVersion: number;
  owner: UserRef | null;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  archivedAt: IsoDateTime | null;
  archiveReason: string | null;
  concurrencyStamp: string;
  versions: RateCardVersion[];
  assignments: RateAssignment[];
  usedBySubmissions: number;
  fourEyesRequiredAbovePercent: boolean;
  fourEyesThresholdPercent: number;
}

export interface RateGroupListItem {
  id: string;
  name: string;
  description: string | null;
  priority: number;
  membershipMode: MembershipMode;
  autoRule: string | null;
  memberCount: number | null;
  cards: RateCardRef[];
  archivedAt: IsoDateTime | null;
  updatedAt: IsoDateTime;
}

export interface RateGroup {
  id: string;
  name: string;
  description: string | null;
  priority: number;
  membershipMode: MembershipMode;
  autoTiers: ParticipantTier[];
  autoMinFollowers: number | null;
  autoMaxFollowers: number | null;
  autoRequireVerified: boolean;
  autoRule: string | null;
  memberCount: number | null;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  archivedAt: IsoDateTime | null;
  archiveReason: string | null;
  concurrencyStamp: string;
  assignments: RateAssignment[];
}

export interface RateGroupInput {
  name: string;
  description: string | null;
  priority: number;
  membershipMode: MembershipMode;
  autoTiers: ParticipantTier[] | null;
  autoMinFollowers: number | null;
  autoMaxFollowers: number | null;
  autoRequireVerified: boolean;
}

export interface RateGroupMember {
  userId: string;
  displayName: string;
  email: string;
  countryCode: string;
  tier: ParticipantTier;
  status: string;
  isTestAccount: boolean;
  addedAt: IsoDateTime | null;
  addedBy: UserRef | null;
  note: string | null;
  followers: number | null;
}

export interface BulkIssue {
  row: number | null;
  value: string;
  userId: string | null;
  code: string;
  message: string;
}

export interface BulkMembersResult {
  requested: number;
  added: number;
  unchanged: number;
  removed: number;
  rejected: BulkIssue[];
  warnings: BulkIssue[];
}

export interface CsvImportResult {
  dryRun: boolean;
  rows: number;
  valid: number;
  added: number;
  alreadyMembers: number;
  rejected: BulkIssue[];
  warnings: BulkIssue[];
}

export interface RateGroupMemberEvent {
  userId: string;
  displayName: string;
  action: 'Added' | 'Removed';
  at: IsoDateTime;
  actor: UserRef | null;
  source: string;
  reason: string | null;
}

export interface PrecedenceLevel {
  rank: number;
  level: RateSourceLevel;
  label: string;
}

export interface PersonGroup {
  id: string;
  name: string;
  membershipMode: MembershipMode;
  priority: number;
  addedAt: IsoDateTime | null;
  matchedPlatforms: SocialPlatform[];
}

export interface EffectiveRate {
  platform: SocialPlatform;
  format: ContentFormat | null;
  level: RateSourceLevel;
  levelLabel: string;
  sourceLabel: string;
  amount: number;
  currency: string;
  assignmentId: string | null;
  rateCardId: string | null;
  rateCardVersion: number | null;
  validTo: IsoDateTime | null;
}

export interface PersonRates {
  user: UserRef;
  countryCode: string;
  tier: ParticipantTier;
  status: string;
  isTestAccount: boolean;
  campaign: { id: string; title: string } | null;
  groups: PersonGroup[];
  assignments: RateAssignment[];
  effective: EffectiveRate[];
  precedence: PrecedenceLevel[];
}

export interface ExplainCandidate {
  level: RateSourceLevel;
  levelLabel: string;
  outcome: RateOutcome;
  reason: string;
  assignmentId: string;
  rateCardId: string;
  cardName: string;
  version: number | null;
  groupId: string | null;
  groupName: string | null;
  priority: number;
  lineId: string | null;
  lineConditions: string | null;
  amount: number | null;
  currency: string | null;
  validFrom: IsoDateTime | null;
  validTo: IsoDateTime | null;
}

export interface RateExplanation {
  platform: SocialPlatform;
  format: ContentFormat | null;
  countryCode: string;
  tier: ParticipantTier;
  followers: number | null;
  evaluatedAt: IsoDateTime;
  campaign: { id: string; title: string } | null;
  campaignPolicy: 'Allowed' | 'CampaignRatesOnly' | null;
  maxMultiplier: number | null;
  winner: RateSourceLevel;
  summary: string;
  candidates: ExplainCandidate[];
  conversion: {
    fromCurrency: string;
    toCurrency: string;
    rate: number;
    exchangeRateId: string | null;
    cardAmount: number;
    amount: number;
  } | null;
  conversionError: string | null;
  quote: RewardQuote | null;
  precedence: PrecedenceLevel[];
}

export interface CampaignRates {
  campaignId: string;
  currency: string;
  personalRatesMode: 'Allowed' | 'CampaignRatesOnly';
  personalRateMaxMultiplier: number | null;
  campaignAssignments: RateAssignment[];
  globalAssignments: RateAssignment[];
  fxProblems: string[];
  peopleWithPersonalRates: number;
}
