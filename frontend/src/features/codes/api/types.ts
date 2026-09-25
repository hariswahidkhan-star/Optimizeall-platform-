/** Discount-code (affiliate) sales — mirrors backend Modules/Codes/CodeDtos.cs. */

export type CodeProgramStatus = 'Draft' | 'Active' | 'Paused' | 'Archived';
export type CodePayoutType = 'FlatPerSale' | 'PercentOfNet';
export type DiscountCodeStatus = 'Available' | 'Assigned' | 'Paused' | 'Expired' | 'Retired';
export type DiscountCodeSource = 'Manual' | 'Import' | 'Generated';
export type CodeAssignmentTarget = 'Person' | 'Group';
export type CodeSaleStatus =
  'Pending' | 'NeedsInfo' | 'Approved' | 'Rejected' | 'Withdrawn' | 'Cancelled' | 'Refunded';
export type CodeSaleSource = 'Participant' | 'Admin' | 'Import';
export type CodeSaleVerification = 'Unverified' | 'Matched' | 'Mismatch' | 'ReportedByBrand';
export type CodeReportGrouping = 'Program' | 'Code' | 'Person' | 'Group';
export type CodeSaleDecision = 'Approve' | 'Reject' | 'RequestInfo';

export interface UserRef {
  id: string;
  displayName: string;
}

export interface NamedRef {
  id: string;
  name: string;
}

export interface ProgramRef {
  id: string;
  name: string;
  brandName: string;
  currency: string;
}

export interface CodeTier {
  thresholdSales: number;
  flatAmount: number | null;
  percent: number | null;
  bonusAmount: number | null;
}

export interface CodePayoutOverride {
  id: string;
  target: CodeAssignmentTarget;
  person: UserRef | null;
  group: NamedRef | null;
  payoutType: CodePayoutType;
  flatAmount: number | null;
  percent: number | null;
  description: string;
  reason: string;
  createdAt: string;
  createdBy: UserRef | null;
  endedAt: string | null;
  endReason: string | null;
  isActive: boolean;
}

export interface CodeProgramStats {
  codes: number;
  availableCodes: number;
  assignedCodes: number;
  pendingSales: number;
  approvedSales: number;
  commissionApproved: number;
  commissionPaid: number;
  budgetRemaining: number | null;
}

export interface CodeProgramListItem {
  id: string;
  name: string;
  brandName: string;
  status: CodeProgramStatus;
  currency: string;
  startsAt: string;
  endsAt: string | null;
  payoutSummary: string;
  codes: number;
  assignedCodes: number;
  pendingSales: number;
  approvedSales: number;
  commissionApproved: number;
  updatedAt: string;
}

export interface CodeProgram {
  id: string;
  name: string;
  brandName: string;
  client: NamedRef | null;
  campaign: NamedRef | null;
  description: string | null;
  terms: string | null;
  storeUrl: string | null;
  discountLabel: string | null;
  currency: string;
  startsAt: string;
  endsAt: string | null;
  status: CodeProgramStatus;
  payoutType: CodePayoutType;
  flatAmount: number | null;
  percent: number | null;
  tiers: CodeTier[];
  dailyCapPerPerson: number | null;
  programCapPerPerson: number | null;
  budgetAmount: number | null;
  maxOrderAgeDays: number;
  requireProof: boolean;
  payoutVersion: number;
  payoutSummary: string;
  overrides: CodePayoutOverride[];
  stats: CodeProgramStats;
  createdAt: string;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface CodeAssignment {
  id: string;
  codeId: string;
  code: string;
  target: CodeAssignmentTarget;
  person: UserRef | null;
  group: NamedRef | null;
  validFrom: string;
  validTo: string | null;
  endedAt: string | null;
  endReason: string | null;
  reason: string;
  createdAt: string;
  createdBy: UserRef | null;
  isLive: boolean;
}

export interface DiscountCode {
  id: string;
  programId: string;
  code: string;
  status: DiscountCodeStatus;
  source: DiscountCodeSource;
  validFrom: string | null;
  validTo: string | null;
  note: string | null;
  assignment: CodeAssignment | null;
  sales: number;
  createdAt: string;
  concurrencyStamp: string;
}

export interface DiscountCodeDetail {
  code: DiscountCode;
  program: ProgramRef;
  history: CodeAssignment[];
}

export interface CodeIssue {
  row: number | null;
  value: string | null;
  code: string;
  message: string;
}

export interface CodeImportResult {
  dryRun: boolean;
  rows: number;
  valid: number;
  created: number;
  duplicates: number;
  rejected: CodeIssue[];
  warnings: CodeIssue[];
  sample: string[];
}

export interface AutoAssignResult {
  dryRun: boolean;
  members: number;
  assigned: number;
  alreadyHadCode: number;
  skipped: number;
  availableCodes: number;
  issues: CodeIssue[];
  assignments: CodeAssignment[];
}

export interface CodeSaleEvent {
  fromStatus: CodeSaleStatus | null;
  toStatus: CodeSaleStatus;
  action: string;
  actor: UserRef | null;
  reason: string | null;
  at: string;
}

export interface CodeSaleEarning {
  id: string;
  type: string;
  status: string;
  amount: number;
  currency: string;
  rateSourceLabel: string | null;
  createdAt: string;
}

export interface CodeSaleListItem {
  id: string;
  program: ProgramRef;
  code: NamedRef;
  person: UserRef;
  sharedCode: boolean;
  orderReference: string;
  orderDate: string;
  netAmount: number;
  discountAmount: number;
  currency: string;
  programNetAmount: number;
  status: CodeSaleStatus;
  source: CodeSaleSource;
  verification: CodeSaleVerification;
  submittedAt: string;
  estimatedCommission: number | null;
  commissionAmount: number | null;
  isTestAccount: boolean;
  userStatus: string;
  canDecide: boolean;
  concurrencyStamp: string;
}

export interface CodeSale {
  id: string;
  program: ProgramRef;
  code: NamedRef;
  person: UserRef;
  personEmail: string | null;
  group: NamedRef | null;
  orderReference: string;
  orderDate: string;
  netAmount: number;
  discountAmount: number;
  currency: string;
  exchangeRate: number;
  programNetAmount: number;
  programDiscountAmount: number;
  productNote: string | null;
  proofUrl: string | null;
  status: CodeSaleStatus;
  source: CodeSaleSource;
  createdBy: UserRef | null;
  submittedAt: string;
  estimatedCommission: number | null;
  commissionAmount: number | null;
  payoutSourceLabel: string | null;
  appliedCaps: string[];
  payoutVersion: number | null;
  verification: CodeSaleVerification;
  verificationNote: string | null;
  reportedNetAmount: number | null;
  reportedOrderDate: string | null;
  decidedAt: string | null;
  decidedBy: UserRef | null;
  decisionReason: string | null;
  refundedAt: string | null;
  refundReason: string | null;
  isTestAccount: boolean;
  userStatus: string;
  canDecide: boolean;
  cannotDecideReason: string | null;
  events: CodeSaleEvent[];
  earnings: CodeSaleEarning[];
  concurrencyStamp: string;
}

export interface BulkDecisionItem {
  saleId: string;
  succeeded: boolean;
  code: string | null;
  message: string | null;
}

export interface BulkDecisionResult {
  requested: number;
  approved: number;
  skipped: number;
  items: BulkDecisionItem[];
}

export interface SalesImportIssue {
  row: number;
  orderReference: string | null;
  code: string | null;
  outcome: string;
  message: string;
}

export interface SalesImportResult {
  dryRun: boolean;
  rows: number;
  matched: number;
  mismatched: number;
  created: number;
  cancelled: number;
  refunded: number;
  unchanged: number;
  rejected: number;
  issues: SalesImportIssue[];
}

export interface MyCodeStats {
  sales: number;
  pending: number;
  approved: number;
  grossSales: number;
  commissionPending: number;
  commissionApproved: number;
  commissionPaid: number;
  clicks: number | null;
}

export interface MyCode {
  codeId: string;
  code: string;
  programId: string;
  programName: string;
  brandName: string;
  discountLabel: string | null;
  description: string | null;
  terms: string | null;
  storeUrl: string | null;
  shareUrl: string | null;
  shared: boolean;
  assignedFrom: string;
  assignedUntil: string | null;
  isActive: boolean;
  inactiveReason: string | null;
  currency: string;
  yourRate: string;
  tierPerks: string[];
  requireProof: boolean;
  maxOrderAgeDays: number;
  programStartsAt: string;
  programEndsAt: string | null;
  stats: MyCodeStats;
}

export interface MyCodeSale {
  id: string;
  programId: string;
  programName: string;
  brandName: string;
  codeId: string;
  code: string;
  orderReference: string;
  orderDate: string;
  netAmount: number;
  discountAmount: number;
  currency: string;
  productNote: string | null;
  proofUrl: string | null;
  status: CodeSaleStatus;
  source: CodeSaleSource;
  submittedAt: string;
  decisionReason: string | null;
  estimatedCommission: number | null;
  commissionAmount: number | null;
  programCurrency: string;
  canEdit: boolean;
  canWithdraw: boolean;
  events: CodeSaleEvent[];
  concurrencyStamp: string;
}

export interface CodeReportRow {
  id: string | null;
  label: string;
  detail: string | null;
  uses: number;
  pending: number;
  approved: number;
  rejected: number;
  refunded: number;
  grossSales: number;
  discountGiven: number;
  netSales: number;
  commissionPending: number;
  commissionApproved: number;
  commissionPaid: number;
  commissionReversed: number;
  clicks: number | null;
  conversionRate: number | null;
}

export interface CodeReport {
  groupBy: CodeReportGrouping;
  program: ProgramRef | null;
  currency: string | null;
  from: string | null;
  to: string | null;
  totals: CodeReportRow;
  rows: CodeReportRow[];
  note: string;
}
