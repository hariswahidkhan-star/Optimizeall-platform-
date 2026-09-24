/**
 * Admin API contract types. Mirrors the backend DTOs in `Modules/{Admin,Content,Support,Notifications,Campaigns,
 * Analytics}` (camelCase JSON, string enums, UTC ISO timestamps).
 */
import type { IsoDateTime } from '@/lib/api/types';

// ---------- Enums ----------

export const ROLES = ['Participant', 'Reviewer', 'CampaignManager', 'Finance', 'Admin'] as const;
export type Role = (typeof ROLES)[number];

export const USER_STATUSES = ['Active', 'Suspended', 'Deactivated'] as const;
export const TIERS = ['Standard', 'Silver', 'Gold', 'Platinum'] as const;
export type Tier = (typeof TIERS)[number];

export const AUDIENCES = ['Everyone', 'Onboarding', 'Eligible', 'ActiveEarners', 'Inactive'] as const;
export type Audience = (typeof AUDIENCES)[number];

export const SEVERITIES = ['Info', 'Success', 'Warning', 'Critical'] as const;
export type Severity = (typeof SEVERITIES)[number];

export const COMPLETION_RULES = [
  'Manual',
  'EmailVerified',
  'ProfileCompleted',
  'SocialAccountAdded',
  'EligibleSocialAccount',
  'PayoutProfileAdded',
  'FirstSubmission',
  'FirstApprovedSubmission',
] as const;
export type CompletionRule = (typeof COMPLETION_RULES)[number];

export const TICKET_STATUSES = [
  'Open',
  'AwaitingParticipant',
  'AwaitingStaff',
  'Resolved',
  'Closed',
] as const;
export type TicketStatus = (typeof TICKET_STATUSES)[number];
export const TICKET_PRIORITIES = ['Low', 'Normal', 'High', 'Urgent'] as const;
export type TicketPriority = (typeof TICKET_PRIORITIES)[number];
export const TICKET_CATEGORIES = [
  'General',
  'Account',
  'SocialProfile',
  'Submission',
  'Payout',
  'Technical',
  'Dispute',
] as const;

export const DELIVERY_STATUSES = ['Pending', 'Sending', 'Sent', 'Failed', 'Skipped'] as const;
export const CHANNELS = ['InApp', 'Email', 'WhatsApp'] as const;
export const JOB_RUN_STATUSES = ['Running', 'Succeeded', 'Failed'] as const;

// ---------- Users ----------

export interface AdminUserListItem {
  id: string;
  email: string;
  displayName: string;
  countryCode: string;
  status: string;
  tier: string;
  roles: string[];
  emailVerified: boolean;
  createdAt: IsoDateTime;
  lastActiveAt: IsoDateTime | null;
  /** QA/demo account: never paid, left out of KPIs. */
  isTestAccount?: boolean;
}

export interface AdminUserProfile {
  id: string;
  email: string;
  displayName: string;
  countryCode: string;
  languageCode: string;
  timeZone: string;
  interests: string[];
  status: string;
  statusReason: string | null;
  statusChangedAt: IsoDateTime | null;
  tier: string;
  referralCode: string;
  emailVerified: boolean;
  emailVerifiedAt: IsoDateTime | null;
  marketingEmailOptIn: boolean;
  whatsAppOptIn: boolean;
  whatsAppNumberHint: string | null;
  lastLoginAt: IsoDateTime | null;
  lastActiveAt: IsoDateTime | null;
  createdAt: IsoDateTime;
  isTestAccount?: boolean;
}

export interface StatusHistoryEntry {
  at: IsoDateTime;
  action: string;
  actorUserId: string | null;
  actorDisplayName: string | null;
  reason: string | null;
}

export interface AdminSocialAccount {
  id: string;
  platform: string;
  handle: string;
  profileUrl: string;
  accountCreatedAt: IsoDateTime;
  accountAgeDays: number;
  followerCount: number;
  verificationStatus: string;
  isActive: boolean;
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

export interface EarningTotal {
  status: string;
  currency: string;
  amount: number;
  count: number;
}

export interface PayoutHold {
  id: string;
  reason: string;
  createdAt: IsoDateTime;
  createdByUserId: string;
}

export interface AdminUserDetail {
  profile: AdminUserProfile;
  roles: string[];
  statusHistory: StatusHistoryEntry[];
  socialAccounts: AdminSocialAccount[];
  submissionCounts: SubmissionCounts;
  earnings: EarningTotal[];
  activePayoutHolds: PayoutHold[];
  payoutProfile: {
    method: string;
    destinationHint: string;
    preferredCurrency: string;
    updatedAt: IsoDateTime;
  } | null;
  recentAudit: AuditLogEntry[];
  concurrencyStamp: string;
  /** Admin-defined roles assigned to the user (see features/admin/roles). */
  customRoles: AssignedCustomRole[];
}

export interface AssignedCustomRole {
  id: string;
  name: string;
  assignedAt: IsoDateTime;
}

// ---------- Settings ----------

export type SettingValue = number | boolean | Record<string, unknown> | string | null;

export interface Setting {
  key: string;
  value: SettingValue;
  defaultValue: SettingValue;
  isDefault: boolean;
  valueType: 'integer' | 'boolean' | 'object' | (string & {});
  description: string;
  updatedAt: IsoDateTime | null;
  updatedBy: { id: string; displayName: string } | null;
}

export interface ReferralProgram {
  enabled: boolean;
  referrerRewardAmount: number;
  currency: string;
  qualifyingAction: string;
  qualifyWithinDays: number;
  requireManualApproval: boolean;
  maxRewardedReferralsPerUser: number;
}

// ---------- Audit ----------

export interface AuditLogEntry {
  id: number;
  createdAt: IsoDateTime;
  actorUserId: string | null;
  actorEmail: string | null;
  actorDisplayName: string | null;
  actorType: string;
  action: string;
  entityType: string;
  entityId: string;
  before: unknown;
  after: unknown;
  reason: string | null;
  ipAddress: string | null;
  correlationId: string | null;
  /** Set when the action was taken while impersonating: read the actor as "impersonator as actor". */
  impersonatorUserId?: string | null;
  impersonatorDisplayName?: string | null;
}

// ---------- Jobs & notifications ----------

export interface JobRun {
  id: string;
  jobName: string;
  runKey: string;
  status: string;
  attempt: number;
  startedAt: IsoDateTime;
  finishedAt: IsoDateTime | null;
  summary: string | null;
  error: string | null;
  instanceId: string | null;
}

export interface Job {
  name: string;
  jobName: string;
  intervalSeconds: number;
  lastRun: JobRun | null;
}

export interface Delivery {
  id: string;
  notificationId: string;
  userId: string;
  userEmail: string | null;
  type: string | null;
  title: string | null;
  channel: string;
  status: string;
  attempts: number;
  nextAttemptAt: IsoDateTime;
  lockedUntil: IsoDateTime | null;
  lastError: string | null;
  providerMessageId: string | null;
  createdAt: IsoDateTime;
  sentAt: IsoDateTime | null;
}

// ---------- Content ----------

interface Stamped {
  id: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface Banner extends Stamped {
  title: string;
  body: string | null;
  imageUrl: string | null;
  ctaLabel: string | null;
  ctaUrl: string | null;
  audience: string;
  countryCode: string | null;
  languageCode: string | null;
  startsAt: IsoDateTime | null;
  endsAt: IsoDateTime | null;
  sortOrder: number;
  isActive: boolean;
}

export interface Announcement extends Stamped {
  title: string;
  body: string;
  severity: string;
  audience: string;
  publishAt: IsoDateTime;
  expiresAt: IsoDateTime | null;
  isActive: boolean;
}

export interface Faq extends Stamped {
  question: string;
  answer: string;
  category: string;
  sortOrder: number;
  isPublished: boolean;
}

export interface OnboardingStep extends Stamped {
  key: string;
  title: string;
  description: string;
  actionLabel: string | null;
  actionUrl: string | null;
  completionRule: string;
  sortOrder: number;
  isActive: boolean;
}

// ---------- Campaign categories ----------

export interface Category {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  icon: string | null;
  sortOrder: number;
  isActive: boolean;
  campaignCount: number;
}

export interface CategoryDeleteResult {
  deleted: boolean;
  deactivated: boolean;
  campaignCount: number;
}

// ---------- Support ----------

export interface TicketPerson {
  id: string;
  displayName: string;
  email: string;
}

export interface StaffTicketSummary {
  id: string;
  reference: string;
  subject: string;
  category: string;
  status: string;
  priority: string;
  requester: TicketPerson;
  assignedTo: TicketPerson | null;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface StaffMessage {
  id: string;
  body: string;
  isInternalNote: boolean;
  fromStaff: boolean;
  authorUserId: string;
  authorName: string;
  createdAt: IsoDateTime;
}

export interface StaffTicket {
  id: string;
  reference: string;
  subject: string;
  category: string;
  status: string;
  priority: string;
  submissionId: string | null;
  payoutItemId: string | null;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  resolvedAt: IsoDateTime | null;
  requester: {
    id: string;
    email: string;
    displayName: string;
    countryCode: string;
    status: string;
    tier: string;
    createdAt: IsoDateTime;
    openTicketCount: number;
    totalTicketCount: number;
  };
  assignedTo: TicketPerson | null;
  messages: StaffMessage[];
  concurrencyStamp: string;
}

// ---------- Analytics ----------

export type MetricMeasurement = 'counted' | 'measured' | 'estimated' | (string & {});

export interface Metric {
  key: string;
  label: string;
  value: number | null;
  unit: 'count' | 'percent' | 'money' | (string & {});
  measurement: MetricMeasurement;
  note: string | null;
  currency: string | null;
}

export interface MetricSection {
  key: string;
  title: string;
  measurement: MetricMeasurement;
  metrics: Metric[];
}

export interface AnalyticsOverview {
  from: IsoDateTime;
  to: IsoDateTime;
  campaignId: string | null;
  platform: string | null;
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
  campaigns: {
    campaignId: string;
    title: string;
    status: string;
    submitted: number;
    approved: number;
    approvalRate: number | null;
    spend: { currency: string; amount: number }[];
    costPerApproved: { currency: string; amount: number }[];
    clicks: number;
    uniqueClicks: number;
    verifiedConversions: number;
    estimatedReach: number;
  }[];
  timeBasis: string;
}
