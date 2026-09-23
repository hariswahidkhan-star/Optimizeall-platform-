/** CRM & proposals API contracts — mirror of backend `Modules/Crm/CrmDtos.cs` and `ProposalDtos.cs`. */
import type { PriceLine, PriceLineInput, RecurringTotals, Totals } from '@/features/agency/billing/api/types';

export type CompanySize = 'Unknown' | 'Solo' | 'Micro' | 'Small' | 'Medium' | 'Large' | 'Enterprise';
export type LifecycleStage =
  | 'Subscriber'
  | 'Lead'
  | 'MarketingQualifiedLead'
  | 'SalesQualifiedLead'
  | 'Opportunity'
  | 'Customer'
  | 'Evangelist';
export type ConsentStatus = 'Unknown' | 'Subscribed' | 'Unsubscribed' | 'NotGiven';
export type DealSource = 'WebsiteInquiry' | 'Form' | 'Referral' | 'Outbound' | 'Event' | 'Other';
export type StageKind = 'Open' | 'Won' | 'Lost';
export type DealStatus = 'Open' | 'Won' | 'Lost';
export type ActivityType = 'Note' | 'Call' | 'Meeting' | 'Email' | 'Task';
export type ProposalStatus = 'Draft' | 'Sent' | 'Viewed' | 'Accepted' | 'Declined' | 'Expired' | 'Withdrawn';
export type ScoringCategory = 'Fit' | 'Engagement';

export interface UserRef {
  id: string;
  displayName: string;
  email: string;
}

export interface Utm {
  source: string | null;
  medium: string | null;
  campaign: string | null;
  at: string | null;
}

export interface CurrencyValue {
  currency: string;
  amount: number;
}

export interface Stage {
  id: string;
  name: string;
  position: number;
  winProbability: number;
  kind: StageKind;
  isActive: boolean;
  concurrencyStamp: string;
}

export interface DealSummary {
  id: string;
  title: string;
  stageId: string;
  stageName: string;
  status: DealStatus;
  value: number;
  currency: string;
  winProbability: number;
  weightedValue: number;
  expectedCloseDate: string | null;
  companyId: string | null;
  companyName: string | null;
  primaryContactId: string | null;
  contactName: string | null;
  owner: UserRef | null;
  source: DealSource;
  serviceSlugs: string[];
  score: number | null;
  stageChangedAt: string;
  createdAt: string;
  concurrencyStamp: string;
}

export interface BoardColumn {
  stage: Stage;
  count: number;
  totals: CurrencyValue[];
  deals: DealSummary[];
}

export interface Board {
  columns: BoardColumn[];
}

export interface DealContact {
  contactId: string;
  displayName: string;
  email: string | null;
  jobTitle: string | null;
  role: string | null;
  primary: boolean;
}

export interface DealProposal {
  id: string;
  number: string;
  title: string;
  status: ProposalStatus;
  currentVersion: number;
  total: number;
  currency: string;
  sentAt: string | null;
  acceptedAt: string | null;
}

export interface Deal extends Omit<DealSummary, 'contactName' | 'score'> {
  stageKind: StageKind;
  sourceDetail: string | null;
  budgetRange: string | null;
  firstTouch: Utm;
  lastTouch: Utm;
  lostReason: string | null;
  closedAt: string | null;
  clientAccountId: string | null;
  contacts: DealContact[];
  proposals: DealProposal[];
  updatedAt: string;
}

export interface DealRequest {
  title: string;
  companyId?: string | null;
  primaryContactId?: string | null;
  stageId?: string | null;
  value: number;
  currency: string;
  expectedCloseDate?: string | null;
  serviceSlugs?: string[];
  ownerUserId?: string | null;
  source: DealSource;
  sourceDetail?: string | null;
  budgetRange?: string | null;
  concurrencyStamp?: string;
}

export interface ContactSummary {
  id: string;
  firstName: string;
  lastName: string | null;
  displayName: string;
  email: string | null;
  phone: string | null;
  jobTitle: string | null;
  companyId: string | null;
  companyName: string | null;
  lifecycleStage: LifecycleStage;
  owner: UserRef | null;
  consentStatus: ConsentStatus;
  tags: string[];
  source: string | null;
  score: number;
  createdAt: string;
}

export interface Contact extends ContactSummary {
  consentChangedAt: string | null;
  budgetRange: string | null;
  scoreBreakdown: { rule: string; category: ScoringCategory; points: number }[];
  firstTouch: Utm;
  lastTouch: Utm;
  deals: DealSummary[];
  engagement: Record<string, number>;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface ContactRequest {
  firstName: string;
  lastName?: string | null;
  email?: string | null;
  phone?: string | null;
  jobTitle?: string | null;
  companyId?: string | null;
  lifecycleStage: LifecycleStage;
  ownerUserId?: string | null;
  consentStatus: ConsentStatus;
  tags?: string[];
  source?: string | null;
  budgetRange?: string | null;
  concurrencyStamp?: string;
}

export interface CompanySummary {
  id: string;
  name: string;
  domain: string | null;
  industry: string | null;
  size: CompanySize;
  countryCode: string | null;
  owner: UserRef | null;
  tags: string[];
  contacts: number;
  openDeals: number;
  clientAccountId: string | null;
  createdAt: string;
}

export interface Company extends Omit<CompanySummary, 'contacts' | 'openDeals'> {
  customFields: string;
  contacts: ContactSummary[];
  deals: DealSummary[];
  updatedAt: string;
  concurrencyStamp: string;
}

export interface CompanyRequest {
  name: string;
  domain?: string | null;
  industry?: string | null;
  size: CompanySize;
  countryCode?: string | null;
  ownerUserId?: string | null;
  tags?: string[];
  customFields?: string | null;
  concurrencyStamp?: string;
}

export interface Activity {
  id: string;
  type: ActivityType;
  subject: string;
  body: string | null;
  contactId: string | null;
  contactName: string | null;
  companyId: string | null;
  companyName: string | null;
  dealId: string | null;
  dealTitle: string | null;
  occursAt: string | null;
  durationMinutes: number | null;
  dueAt: string | null;
  remindAt: string | null;
  assignee: UserRef | null;
  completedAt: string | null;
  isOverdue: boolean;
  isSystem: boolean;
  createdBy: UserRef | null;
  createdAt: string;
  concurrencyStamp: string;
}

export interface ActivityRequest {
  type: ActivityType;
  subject: string;
  body?: string | null;
  contactId?: string | null;
  companyId?: string | null;
  dealId?: string | null;
  occursAt?: string | null;
  durationMinutes?: number | null;
  dueAt?: string | null;
  remindAt?: string | null;
  assigneeUserId?: string | null;
  concurrencyStamp?: string;
}

export interface ScoringRule {
  id: string;
  name: string;
  category: ScoringCategory;
  field: string;
  matchValue: string | null;
  points: number;
  maxOccurrences: number | null;
  isActive: boolean;
  concurrencyStamp: string;
}

export interface SavedView {
  id: string;
  name: string;
  entity: 'contacts' | 'companies' | 'deals';
  filters: Record<string, string>;
  shared: boolean;
  mine: boolean;
  createdAt: string;
}

export interface CrmDashboard {
  pipeline: { stageId: string; stageName: string; winProbability: number; count: number; value: CurrencyValue[]; weighted: CurrencyValue[] }[];
  openValue: CurrencyValue[];
  weightedForecast: CurrencyValue[];
  openDeals: number;
  wonLast90Days: number;
  lostLast90Days: number;
  winRate: number;
  leadSources: { source: DealSource; deals: number; won: number }[];
  dueToday: Activity[];
  overdueTasks: number;
  newLeadsLast30Days: number;
}

export interface ImportResult {
  dryRun: boolean;
  totalRows: number;
  created: number;
  updated: number;
  skipped: number;
  failed: number;
  rows: { row: number; status: 'created' | 'updated' | 'skipped' | 'error'; email: string | null; contactId: string | null; errors: string[] }[];
}

// ---------------- Proposals

export interface ProposalVersion {
  versionNumber: number;
  title: string;
  currency: string;
  validUntil: string;
  executiveSummary: string | null;
  goals: string | null;
  scope: string | null;
  deliverables: string | null;
  timeline: string | null;
  terms: string | null;
  lines: PriceLine[];
  totals: Totals;
  recurring: RecurringTotals;
  createdAt: string;
  sentAt: string | null;
  locked: boolean;
}

export interface ProposalSummary {
  id: string;
  number: string;
  title: string;
  status: ProposalStatus;
  dealId: string | null;
  dealTitle: string | null;
  clientAccountId: string | null;
  clientName: string | null;
  companyName: string | null;
  currency: string;
  total: number;
  monthlyRecurringValue: number;
  currentVersion: number;
  validUntil: string;
  sentAt: string | null;
  viewCount: number;
  acceptedAt: string | null;
  createdAt: string;
}

export interface Proposal {
  id: string;
  number: string;
  title: string;
  status: ProposalStatus;
  dealId: string | null;
  dealTitle: string | null;
  clientAccountId: string | null;
  clientName: string | null;
  companyId: string | null;
  companyName: string | null;
  contactId: string | null;
  contactName: string | null;
  currency: string;
  currentVersion: number;
  sentVersion: number | null;
  recipientName: string | null;
  recipientEmail: string | null;
  invoiceOnAcceptance: boolean | null;
  shareUrl: string | null;
  sentAt: string | null;
  viewCount: number;
  firstViewedAt: string | null;
  lastViewedAt: string | null;
  acceptedAt: string | null;
  acceptedVersion: number | null;
  signerName: string | null;
  signerTitle: string | null;
  signerEmail: string | null;
  declinedAt: string | null;
  declineReason: string | null;
  version: ProposalVersion;
  versions: { versionNumber: number; total: number; monthlyRecurringValue: number; currency: string; createdAt: string; sentAt: string | null; locked: boolean }[];
  contractIds: string[];
  invoiceIds: string[];
  createdAt: string;
  concurrencyStamp: string;
}

export interface ProposalRequest {
  title: string;
  dealId?: string | null;
  clientAccountId?: string | null;
  companyId?: string | null;
  contactId?: string | null;
  currency?: string;
  validUntil: string;
  executiveSummary?: string;
  goals?: string;
  scope?: string;
  deliverables?: string;
  timeline?: string;
  terms?: string;
  recipientName?: string;
  recipientEmail?: string;
  invoiceOnAcceptance?: boolean | null;
  lines: PriceLineInput[];
  concurrencyStamp?: string;
}

/** What the client sees (public `/p/:token` page and client portal). */
export interface PublicProposal {
  number: string;
  title: string;
  status: ProposalStatus;
  agencyName: string;
  preparedFor: string | null;
  recipientName: string | null;
  version: ProposalVersion;
  canRespond: boolean;
  expired: boolean;
  beingRevised: boolean;
  acceptedAt: string | null;
  signerName: string | null;
  signerTitle: string | null;
  declinedAt: string | null;
}

export interface AcceptProposalResponse {
  proposal: PublicProposal;
  clientAccountCreated: boolean;
  invitationSent: boolean;
  contractsCreated: number;
  invoiceCreated: boolean;
}
