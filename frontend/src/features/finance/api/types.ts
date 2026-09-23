/**
 * Finance API contract (docs/api/ledger-payouts.md). Mirrors backend DTOs in
 * `Modules/Ledger/LedgerDtos.cs` and `Modules/Payouts/PayoutDtos.cs`. Money is always a number with its currency.
 */
import type { IsoDateTime, PagedResult } from '@/lib/api/types';

/** `yyyy-MM-dd` (DateOnly). */
export type DateOnly = string;

export interface CampaignRef {
  id: string;
  title: string;
}

export interface UserRef {
  id: string;
  displayName: string;
  email: string;
}

export interface PayoutUser extends UserRef {
  country: string;
}

export type EarningType =
  | 'PostReward'
  | 'FirstPostBonus'
  | 'TimeLimitedBonus'
  | 'QualityBonus'
  | 'ReferralReward'
  | 'Adjustment'
  | 'Reversal';

export type EarningStatus = 'PendingApproval' | 'Approved' | 'Scheduled' | 'Paid' | 'Reversed' | 'Declined';

export const EARNING_TYPES: EarningType[] = [
  'PostReward',
  'FirstPostBonus',
  'TimeLimitedBonus',
  'QualityBonus',
  'ReferralReward',
  'Adjustment',
  'Reversal',
];

export const EARNING_STATUSES: EarningStatus[] = [
  'PendingApproval',
  'Approved',
  'Scheduled',
  'Paid',
  'Reversed',
  'Declined',
];

export interface LedgerRow {
  id: string;
  createdAt: IsoDateTime;
  user: UserRef;
  type: EarningType;
  status: EarningStatus;
  description: string;
  campaign: CampaignRef | null;
  submissionId: string | null;
  originalAmount: number;
  originalCurrency: string;
  exchangeRate: number;
  exchangeRateId: string | null;
  settlementAmount: number;
  settlementCurrency: string;
  ruleSetVersion: number | null;
  availableAt: IsoDateTime | null;
  approvedAt: IsoDateTime | null;
  approvedByUserId: string | null;
  createdByUserId: string | null;
  payoutItemId: string | null;
  paidAt: IsoDateTime | null;
  reversedAt: IsoDateTime | null;
  reversesEntryId: string | null;
  reversedByEntryId: string | null;
  reason: string | null;
  concurrencyStamp: string;
}

export interface NextPayout {
  periodKey: string;
  cutoffAt: IsoDateTime;
  paymentDate: DateOnly;
  minimumPayoutAmount: number;
  meetsMinimum: boolean;
  estimatedAmount: number;
}

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
  nextPayout: NextPayout;
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

export interface AdjustmentRequest {
  requestId: string;
  userId: string;
  amount: number;
  currency: string;
  reason: string;
  submissionId?: string | null;
  supportTicketId?: string | null;
  confirm: true;
}

export interface AdjustmentResult {
  created: boolean;
  earning: LedgerRow;
}

export interface ReversalResult {
  original: LedgerRow;
  reversal: LedgerRow;
}

export interface PendingEarning {
  id: string;
  createdAt: IsoDateTime;
  type: EarningType;
  description: string;
  user: UserRef;
  campaign: CampaignRef | null;
  submissionId: string | null;
  referralId: string | null;
  originalAmount: number;
  originalCurrency: string;
  settlementAmount: number;
  settlementCurrency: string;
  createdByUserId: string | null;
  awaitingLiveCheck: boolean;
  liveCheckDueAt: IsoDateTime | null;
  submissionRiskScore: number | null;
  concurrencyStamp: string;
}

export interface ExchangeRate {
  id: string;
  baseCurrency: string;
  quoteCurrency: string;
  rate: number;
  effectiveAt: IsoDateTime;
  source: string;
  createdAt: IsoDateTime;
  createdByUserId: string | null;
}

export type PayoutFrequency = 'Weekly' | 'Biweekly' | 'Monthly';

export interface PayoutSchedule {
  id: string | null;
  frequency: PayoutFrequency;
  anchorCutoffDate: DateOnly;
  cutoffLocalTime: string;
  timeZone: string;
  paymentDelayDays: number;
  minimumPayoutAmount: number;
  settlementCurrency: string;
  earningHoldDays: number;
  autoPrepareBatches: boolean;
  effectiveFrom: IsoDateTime | null;
  createdAt: IsoDateTime | null;
  createdByUserId: string | null;
  changeReason: string | null;
  isDefault: boolean;
}

export interface PayoutPeriod {
  periodKey: string;
  periodStart: IsoDateTime;
  cutoffAt: IsoDateTime;
  cutoffLocalDate: DateOnly;
  paymentDate: DateOnly;
}

export interface PayoutScheduleResponse {
  current: PayoutSchedule;
  currentPeriod: PayoutPeriod;
  lastCompletedPeriod: PayoutPeriod;
  upcoming: PayoutPeriod[];
  scheduledChanges: PayoutSchedule[];
  history: PayoutSchedule[];
}

export interface UpdateScheduleRequest {
  frequency: PayoutFrequency;
  anchorCutoffDate: DateOnly;
  cutoffLocalTime: string;
  timeZone: string;
  paymentDelayDays: number;
  minimumPayoutAmount: number;
  settlementCurrency: string;
  earningHoldDays: number;
  autoPrepareBatches: boolean;
  effectiveFrom: IsoDateTime;
  reason: string;
  confirm: true;
}

export interface PayoutHold {
  id: string;
  user: UserRef;
  reason: string;
  createdAt: IsoDateTime;
  createdByUserId: string;
  isActive: boolean;
  releasedAt: IsoDateTime | null;
  releasedByUserId: string | null;
  releaseNote: string | null;
}

export interface CreateHoldResponse {
  hold: PayoutHold;
  heldDraftItemIds: string[];
  awaitingPaymentItemIds: string[];
}

export type PayoutBatchStatus = 'Draft' | 'Finalized' | 'Completed' | 'Cancelled';
export type PayoutItemStatus = 'Pending' | 'Held' | 'AwaitingPayment' | 'Paid' | 'Failed' | 'Cancelled';
export type PaymentAttemptStatus = 'Created' | 'Submitted' | 'Succeeded' | 'Failed' | 'RequiresManualAction';
export type PayoutExclusionReason = 'PayoutHold' | 'AccountInactive' | 'NonPositiveBalance' | 'BelowMinimum';

export const BATCH_STATUSES: PayoutBatchStatus[] = ['Draft', 'Finalized', 'Completed', 'Cancelled'];
export const ITEM_STATUSES: PayoutItemStatus[] = [
  'Pending',
  'Held',
  'AwaitingPayment',
  'Paid',
  'Failed',
  'Cancelled',
];

export interface PayoutBatchSummary {
  id: string;
  reference: string;
  periodKey: string;
  periodStart: IsoDateTime;
  cutoffAt: IsoDateTime;
  paymentDate: DateOnly;
  status: PayoutBatchStatus;
  itemCount: number;
  totalAmount: number;
  currency: string;
  paidCount: number;
  paidAmount: number;
  preparedBy: UserRef | null;
  finalizedBy: UserRef | null;
  createdAt: IsoDateTime;
  finalizedAt: IsoDateTime | null;
  completedAt: IsoDateTime | null;
  cancelledAt: IsoDateTime | null;
}

export interface PayoutExclusion {
  user: PayoutUser;
  reason: PayoutExclusionReason;
  amount: number;
  earningCount: number;
}

export interface PrepareBatchResponse {
  created: boolean;
  batch: PayoutBatchSummary;
  exclusions: PayoutExclusion[];
}

export interface PayoutItem {
  itemId: string;
  user: PayoutUser;
  amount: number;
  currency: string;
  earningCount: number;
  status: PayoutItemStatus;
  paymentProvider: string;
  destinationHint: string | null;
  paymentReference: string | null;
  paidAt: IsoDateTime | null;
  holdReason: string | null;
  failureReason: string | null;
  concurrencyStamp: string;
}

export interface UserWarning {
  user: PayoutUser;
  itemId: string | null;
  detail: string;
}

export interface BatchWarnings {
  missingPayoutDetails: UserWarning[];
  openAppeals: UserWarning[];
  openDisputes: UserWarning[];
  highRiskSubmissions: UserWarning[];
  highRiskThreshold: number;
  exclusions: PayoutExclusion[];
}

export interface StatusTotal {
  status: PayoutItemStatus;
  count: number;
  amount: number;
}

export interface PayoutBatchDetail {
  batch: PayoutBatchSummary;
  concurrencyStamp: string;
  notes: string | null;
  cancelReason: string | null;
  totalsByStatus: StatusTotal[];
  warnings: BatchWarnings;
  items: PagedResult<PayoutItem>;
}

export interface ItemEarning {
  id: string;
  type: EarningType;
  status: EarningStatus;
  description: string;
  campaign: CampaignRef | null;
  submissionId: string | null;
  originalAmount: number;
  originalCurrency: string;
  exchangeRate: number;
  settlementAmount: number;
  settlementCurrency: string;
  createdAt: IsoDateTime;
  availableAt: IsoDateTime | null;
}

export interface PaymentAttempt {
  id: string;
  provider: string;
  idempotencyKey: string;
  status: PaymentAttemptStatus;
  providerReference: string | null;
  message: string | null;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime | null;
}

export interface PayoutItemDetail {
  batchId: string;
  batchReference: string;
  periodKey: string;
  batchStatus: PayoutBatchStatus;
  item: PayoutItem;
  earningsTotal: number;
  earnings: ItemEarning[];
  paymentAttempts: PaymentAttempt[];
}

export interface DispatchResult {
  itemId: string;
  status: PaymentAttemptStatus;
  providerReference: string | null;
  message: string;
  reused: boolean;
}

export interface FinalizeResponse {
  batch: PayoutBatchSummary;
  dispatch: DispatchResult[];
}

export interface PaymentRecorded {
  item: PayoutItem;
  batchStatus: PayoutBatchStatus;
}

export interface BulkPaymentLine {
  itemId: string;
  paymentReference: string;
  paidAt: IsoDateTime;
}

export type BulkPaymentStatus = 'recorded' | 'already_recorded' | 'invalid';

export interface BulkPaymentResult {
  itemId: string;
  status: BulkPaymentStatus;
  message: string;
}

export interface ReconciliationDiscrepancy {
  type: string;
  severity: 'error' | 'warning';
  itemId: string | null;
  userId: string | null;
  earningId: string | null;
  message: string;
}

export interface ReconciliationItem {
  itemId: string;
  user: PayoutUser;
  status: PayoutItemStatus;
  amount: number;
  earningsTotal: number;
  earningCount: number;
  linkedEarningCount: number;
  paymentReference: string | null;
  paidAt: IsoDateTime | null;
  ok: boolean;
}

export interface Reconciliation {
  batchId: string;
  reference: string;
  periodKey: string;
  status: PayoutBatchStatus;
  currency: string;
  expected: number;
  recordedPaid: number;
  awaiting: number;
  failed: number;
  held: number;
  cancelled: number;
  paidCount: number;
  awaitingCount: number;
  isBalanced: boolean;
  discrepancies: ReconciliationDiscrepancy[];
  items: ReconciliationItem[];
}

/** `GET /admin/users` list row (subset used by the participant picker). */
export interface AdminUserListItem {
  id: string;
  email: string;
  displayName: string;
  countryCode: string;
  status: string;
  roles: string[];
}
