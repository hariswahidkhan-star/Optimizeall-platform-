/** Email & SMS marketing API contract (mirrors backend `Modules/EmailMarketing` DTOs). */
import type { IsoDateTime, PagedResult } from '@/lib/api/types';

export type { PagedResult };

export type MessageChannel = 'Email' | 'Sms' | 'WhatsApp';
export type SubscriberStatus = 'Subscribed' | 'Unsubscribed' | 'Bounced' | 'Complained' | 'Cleaned';
export type ConsentStatus = 'Unknown' | 'Pending' | 'Granted' | 'Withdrawn';
export type MembershipStatus = 'Pending' | 'Subscribed' | 'Unsubscribed';
export type SuppressionReason = 'Unsubscribed' | 'HardBounce' | 'Complaint' | 'StopKeyword' | 'InvalidAddress' | 'Manual';
export type EngagementTier = 'Active' | 'Warm' | 'Cold' | 'New';
export type CampaignStatus = 'Draft' | 'Scheduled' | 'Sending' | 'Paused' | 'Sent' | 'Cancelled';
export type CampaignType = 'Regular' | 'AbTest';
export type ScheduleMode = 'Immediate' | 'FixedTime' | 'RecipientTimeZone';
export type AbWinnerMetric = 'OpenRate' | 'ClickRate';
export type ApprovalStatus = 'NotRequired' | 'Pending' | 'Approved' | 'Rejected';
export type CheckStatus = 'Pass' | 'Warning' | 'Fail' | 'Info';
export type AutomationStatus = 'Draft' | 'Active' | 'Paused' | 'Archived';
export type AutomationTrigger =
  | 'ListSubscribed'
  | 'TagAdded'
  | 'FormSubmitted'
  | 'NewsletterConfirmed'
  | 'DateAnniversary'
  | 'CustomEvent';
export type AutomationStepType =
  | 'SendEmail'
  | 'SendSms'
  | 'Wait'
  | 'Condition'
  | 'AddTag'
  | 'RemoveTag'
  | 'NotifyStaff'
  | 'Exit';
export type EnrollmentStatus = 'Active' | 'Completed' | 'Exited' | 'Failed';
export type ImportStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed';

export interface Workspace {
  key: string;
  clientAccountId: string | null;
  name: string;
  slug: string | null;
  timeZone: string;
  currency: string;
}

export interface EmailList {
  id: string;
  clientAccountId: string | null;
  name: string;
  description: string | null;
  doubleOptIn: boolean;
  showInPreferenceCenter: boolean;
  publicKey: string;
  signupUrl: string;
  consentText: string;
  consentTextVersion: string;
  subscribed: number;
  pending: number;
  unsubscribed: number;
  isArchived: boolean;
  createdAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface SubscriberListItem {
  id: string;
  email: string | null;
  phone: string | null;
  firstName: string | null;
  lastName: string | null;
  status: SubscriberStatus;
  emailConsent: ConsentStatus;
  smsConsent: ConsentStatus;
  countryCode: string | null;
  source: string;
  createdAt: IsoDateTime;
  lastOpenAt: IsoDateTime | null;
  lastClickAt: IsoDateTime | null;
  tags: string[];
  tier: EngagementTier;
}

export interface SubscriberDetail {
  id: string;
  clientAccountId: string | null;
  email: string | null;
  phone: string | null;
  firstName: string | null;
  lastName: string | null;
  language: string | null;
  countryCode: string | null;
  timeZone: string | null;
  source: string;
  status: SubscriberStatus;
  emailConsent: ConsentStatus;
  emailConsentAt: IsoDateTime | null;
  smsConsent: ConsentStatus;
  smsConsentAt: IsoDateTime | null;
  whatsAppConsent: ConsentStatus;
  whatsAppConsentAt: IsoDateTime | null;
  frequency: 'Any' | 'Weekly' | 'Monthly';
  tags: string[];
  customFields: Record<string, string>;
  lists: { listId: string; listName: string; status: MembershipStatus; subscribedAt: IsoDateTime | null; unsubscribedAt: IsoDateTime | null; source: string }[];
  consentHistory: {
    channel: MessageChannel;
    status: ConsentStatus;
    recordedAt: IsoDateTime;
    source: string;
    consentTextVersion: string | null;
    hasIpHash: boolean;
    note: string | null;
  }[];
  activity: { type: string; occurredAt: IsoDateTime; isMachine: boolean; detail: string | null; campaignId: string | null; campaignName: string | null }[];
  emailSuppressed: boolean;
  smsSuppressed: boolean;
  tier: EngagementTier;
  createdAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface Suppression {
  id: string;
  channel: MessageChannel;
  value: string;
  reason: SuppressionReason;
  source: string;
  note: string | null;
  createdAt: IsoDateTime;
}

export interface ImportPreview {
  headers: string[];
  sampleRows: string[][];
  totalRows: number;
  suggestedMapping: Record<string, string>;
  targets: string[];
}

export interface ImportResult {
  id: string;
  listId: string;
  status: ImportStatus;
  fileName: string;
  totalRows: number;
  processedRows: number;
  created: number;
  updated: number;
  skipped: number;
  failed: number;
  errors: { row: number; message: string }[];
  createdAt: IsoDateTime;
  completedAt: IsoDateTime | null;
}

// ---------- Segments ----------

export type SegmentConditionKind = 'field' | 'custom' | 'tag' | 'list' | 'engagement' | 'purchase' | 'consent' | 'event';

export interface SegmentCondition {
  kind: SegmentConditionKind;
  field?: string;
  op?: string;
  value?: string;
  values?: string[];
  event?: string;
  withinDays?: number;
  campaignId?: string;
  channel?: string;
}

export interface SegmentDefinition {
  match: 'all' | 'any';
  conditions: SegmentCondition[];
  groups: SegmentDefinition[];
}

export interface Segment {
  id: string;
  clientAccountId: string | null;
  name: string;
  definition: SegmentDefinition;
  lastCount: number | null;
  lastCountedAt: IsoDateTime | null;
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface SegmentPreview {
  count: number;
  total: number;
  sample: { id: string; email: string | null; firstName: string | null; lastName: string | null; countryCode: string | null }[];
}

// ---------- Templates ----------

export type BlockType = 'header' | 'text' | 'image' | 'button' | 'divider' | 'spacer' | 'columns' | 'social' | 'footer';

export interface DesignBlock {
  type: BlockType;
  id?: string;
  logoUrl?: string;
  logoAlt?: string;
  title?: string;
  subtitle?: string;
  html?: string;
  src?: string;
  alt?: string;
  width?: number;
  text?: string;
  href?: string;
  color?: string;
  textColor?: string;
  align?: 'left' | 'center' | 'right';
  backgroundColor?: string;
  height?: number;
  columns?: { blocks: DesignBlock[] }[];
  links?: { network: string; url: string }[];
  showPreferencesLink?: boolean;
}

export interface EmailDesign {
  settings?: {
    backgroundColor?: string;
    contentBackgroundColor?: string;
    contentWidth?: number;
    fontFamily?: string;
    textColor?: string;
    linkColor?: string;
  };
  blocks: DesignBlock[];
}

export interface TemplateListItem {
  id: string;
  clientAccountId: string | null;
  name: string;
  category: string;
  subject: string;
  isGlobal: boolean;
  isArchived: boolean;
  updatedAt: IsoDateTime;
}

export interface Template {
  id: string;
  clientAccountId: string | null;
  name: string;
  category: string;
  subject: string;
  previewText: string | null;
  design: EmailDesign;
  isGlobal: boolean;
  isArchived: boolean;
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface RenderResult {
  subject: string;
  html: string;
  text: string;
  sizeBytes: number;
  errors: string[];
  warnings: string[];
}

export interface TestSendResult {
  sent: boolean;
  providerMessageId: string | null;
  error: string | null;
}

// ---------- Campaigns ----------

export interface CampaignListItem {
  id: string;
  clientAccountId: string | null;
  name: string;
  channel: MessageChannel;
  type: CampaignType;
  status: CampaignStatus;
  approvalStatus: ApprovalStatus;
  subject: string | null;
  scheduleMode: ScheduleMode;
  scheduledAt: IsoDateTime | null;
  scheduledLocalTime: string | null;
  recipientCount: number;
  sent: number;
  uniqueOpens: number;
  uniqueClicks: number;
  completedAt: IsoDateTime | null;
  updatedAt: IsoDateTime;
}

export interface CampaignVariant {
  key: string;
  subject: string | null;
  previewText: string | null;
  senderProfileId: string | null;
  design: EmailDesign | null;
}

export interface Campaign {
  id: string;
  clientAccountId: string | null;
  name: string;
  channel: MessageChannel;
  type: CampaignType;
  status: CampaignStatus;
  listId: string | null;
  segmentId: string | null;
  templateId: string | null;
  senderProfileId: string | null;
  subject: string;
  previewText: string | null;
  design: EmailDesign;
  topic: string | null;
  smsBody: string | null;
  whatsAppTemplateName: string | null;
  whatsAppTemplateLanguage: string | null;
  whatsAppParameters: string[];
  scheduleMode: ScheduleMode;
  scheduledAt: IsoDateTime | null;
  scheduledLocalTime: string | null;
  sendWindowStartHour: number | null;
  sendWindowEndHour: number | null;
  throttlePerMinute: number;
  abTestPercent: number;
  abWinnerMetric: AbWinnerMetric;
  abWaitHours: number;
  abWinnerVariant: string | null;
  abDecidedAt: IsoDateTime | null;
  variants: CampaignVariant[];
  approvalStatus: ApprovalStatus;
  approvalNote: string | null;
  approvalDecidedAt: IsoDateTime | null;
  sendConfirmedAt: IsoDateTime | null;
  sendStartedAt: IsoDateTime | null;
  completedAt: IsoDateTime | null;
  pausedAt: IsoDateTime | null;
  pauseReason: string | null;
  cancelledAt: IsoDateTime | null;
  recipientCount: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface ChecklistItem {
  id: string;
  label: string;
  status: CheckStatus;
  detail: string | null;
}

export interface Checklist {
  items: ChecklistItem[];
  canSend: boolean;
  audienceCount: number;
  smsSegments: number | null;
  smsEncoding: string | null;
  estimatedCost: number | null;
  costCurrency: string | null;
  requiresClientApproval: boolean;
}

export interface MoneyTotal {
  currency: string;
  amount: number;
}

export interface CampaignReport {
  id: string;
  clientAccountId: string | null;
  name: string;
  channel: MessageChannel;
  type: CampaignType;
  status: CampaignStatus;
  subject: string | null;
  sendStartedAt: IsoDateTime | null;
  completedAt: IsoDateTime | null;
  recipients: number;
  sent: number;
  pending: number;
  failed: number;
  skipped: number;
  cancelled: number;
  delivered: number;
  deliveredIsEstimated: boolean;
  hardBounces: number;
  softBounces: number;
  uniqueOpens: number;
  totalOpens: number;
  machineOpens: number;
  machineOnlyOpeners: number;
  uniqueClicks: number;
  totalClicks: number;
  unsubscribes: number;
  complaints: number;
  conversions: number;
  revenue: MoneyTotal[];
  openRate: number;
  clickRate: number;
  clickToOpenRate: number;
  bounceRate: number;
  unsubscribeRate: number;
  complaintRate: number;
  smsSegments: number;
  cost: number;
  costCurrency: string | null;
  links: { linkId: string; url: string; position: number; uniqueClicks: number; totalClicks: number }[];
  devices: { key: string; opens: number; clicks: number }[];
  mailClients: { key: string; opens: number; clicks: number }[];
  variants: { key: string; subject: string | null; sent: number; uniqueOpens: number; uniqueClicks: number; openRate: number; clickRate: number; winner: boolean }[];
  timeline: { hour: IsoDateTime; opens: number; clicks: number }[];
}

export interface EmailKpis {
  clientAccountId: string | null;
  from: IsoDateTime;
  to: IsoDateTime;
  campaignsSent: number;
  emailsSent: number;
  delivered: number;
  uniqueOpens: number;
  uniqueClicks: number;
  openRate: number;
  clickRate: number;
  unsubscribes: number;
  complaints: number;
  hardBounces: number;
  conversions: number;
  revenue: MoneyTotal[];
  smsSent: number;
  smsCost: number;
  newSubscribers: number;
  activeSubscribers: number;
  automationEmailsSent: number;
}

export interface Overview {
  contacts: number;
  subscribed: number;
  lists: number;
  activeAutomations: number;
  scheduledCampaigns: number;
  last30Days: EmailKpis;
  recentCampaigns: CampaignListItem[];
}

export interface ListHealth {
  listId: string;
  name: string;
  subscribed: number;
  pending: number;
  unsubscribed: number;
  bounced: number;
  complained: number;
  cleaned: number;
  active: number;
  warm: number;
  cold: number;
  new: number;
  emailConsentGranted: number;
  growth: { day: string; subscribed: number; unsubscribed: number }[];
  netGrowth: number;
}

// ---------- Automations ----------

export interface StepConfig {
  templateId?: string | null;
  subject?: string | null;
  body?: string | null;
  minutes?: number | null;
  hours?: number | null;
  days?: number | null;
  untilTime?: string | null;
  check?: string | null;
  stepKey?: string | null;
  tag?: string | null;
  field?: string | null;
  value?: string | null;
  eventName?: string | null;
  userIds?: string[] | null;
  message?: string | null;
}

export interface StepDefinition {
  key: string;
  type: AutomationStepType;
  config: StepConfig;
  next?: string | null;
  altNext?: string | null;
}

export interface AutomationListItem {
  id: string;
  clientAccountId: string | null;
  name: string;
  status: AutomationStatus;
  trigger: AutomationTrigger;
  active: number;
  completed: number;
  exited: number;
  updatedAt: IsoDateTime;
}

export interface TriggerConfig {
  listId?: string | null;
  tag?: string | null;
  formId?: string | null;
  dateField?: string | null;
  eventName?: string | null;
}

export interface Automation {
  id: string;
  clientAccountId: string | null;
  name: string;
  description: string | null;
  status: AutomationStatus;
  trigger: AutomationTrigger;
  triggerConfig: TriggerConfig;
  reentry: 'Never' | 'AfterExit';
  reentryCooldownDays: number;
  goal: { kind: string; tag?: string | null; eventName?: string | null } | null;
  senderProfileId: string | null;
  entryStepKey: string | null;
  steps: (StepDefinition & { stats: { runs: number; sent: number; opened: number; clicked: number; skipped: number; failed: number } })[];
  active: number;
  completed: number;
  exited: number;
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
}

// ---------- Settings ----------

export interface SenderProfile {
  id: string;
  clientAccountId: string | null;
  fromName: string;
  fromEmail: string;
  replyTo: string | null;
  isDefault: boolean;
  verified: boolean;
  verifiedAt: IsoDateTime | null;
  verificationSentAt: IsoDateTime | null;
  concurrencyStamp: string;
}

export interface WorkspaceSettings {
  clientAccountId: string | null;
  organizationName: string;
  physicalAddress: string;
  requireClientApproval: boolean;
  emailProvider: string;
  defaultThrottlePerMinute: number;
  quietHoursStart: number;
  quietHoursEnd: number;
  defaultTimeZone: string;
  smsCostPerSegment: number;
  whatsAppCostPerMessage: number;
  costCurrency: string;
  providers: { channel: string; ready: boolean; detail: string }[];
  webhooks: { sendGrid: string; mailgun: string; twilioInbound: string; twilioStatus: string; conversions: string; events: string };
  availableProviders: string[];
  concurrencyStamp: string;
}

// ---------- Client portal ----------

export interface ClientCampaignItem {
  id: string;
  clientAccountId: string;
  clientName: string;
  name: string;
  channel: MessageChannel;
  status: CampaignStatus;
  approvalStatus: ApprovalStatus;
  subject: string | null;
  scheduleMode: ScheduleMode;
  scheduledAt: IsoDateTime | null;
  scheduledLocalTime: string | null;
  completedAt: IsoDateTime | null;
  sent: number;
  uniqueOpens: number;
  uniqueClicks: number;
  canApprove: boolean;
}

// ---------- Public pages ----------

export interface Preferences {
  workspace: string;
  maskedEmail: string;
  unsubscribedFromAll: boolean;
  frequency: 'Any' | 'Weekly' | 'Monthly';
  topics: { listId: string; name: string; description: string | null; subscribed: boolean }[];
}

export interface SignupForm {
  listName: string;
  workspace: string;
  consentText: string;
  doubleOptIn: boolean;
}
