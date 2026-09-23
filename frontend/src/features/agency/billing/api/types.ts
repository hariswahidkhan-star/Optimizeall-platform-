/** Billing API contracts — mirror of backend `Modules/Billing/BillingDtos.cs` (camelCase, string enums, DateOnly = yyyy-MM-dd). */

export type DiscountType = 'None' | 'Percent' | 'Amount';
export type Recurrence = 'OneTime' | 'Monthly' | 'Quarterly' | 'Annually';
export type BillingFrequency = 'Monthly' | 'Quarterly' | 'Annually';
export type InvoiceStatus = 'Draft' | 'Issued' | 'PartiallyPaid' | 'Paid' | 'Overdue' | 'Void' | 'WrittenOff';
export type PaymentMethod = 'BankTransfer' | 'Card' | 'Cash' | 'Cheque' | 'PayPal' | 'Stripe' | 'Other';
export type ContractStatus = 'Draft' | 'Active' | 'Paused' | 'Cancelled' | 'Ended';
export type CreditNoteStatus = 'Open' | 'Applied';

/** Editable line as sent to the API; the server validates and prices it (the UI never computes money). */
export interface PriceLineInput {
  description: string;
  serviceSlug?: string | null;
  packageSlug?: string | null;
  quantity: number;
  unitPrice: number;
  discountType: DiscountType;
  discountValue: number;
  taxRateId?: string | null;
  recurrence?: Recurrence;
}

export interface PriceLine {
  id: string;
  position: number;
  description: string;
  serviceSlug: string | null;
  packageSlug: string | null;
  quantity: number;
  unitPrice: number;
  discountType: DiscountType;
  discountValue: number;
  taxRateId: string | null;
  taxName: string | null;
  taxPercent: number;
  taxInclusive: boolean;
  recurrence: Recurrence;
  discountAmount: number;
  subtotal: number;
  taxAmount: number;
  total: number;
}

export interface TaxBreakdown {
  name: string;
  ratePercent: number;
  inclusive: boolean;
  taxableAmount: number;
  taxAmount: number;
}

export interface Totals {
  currency: string;
  grossTotal: number;
  discountTotal: number;
  subtotal: number;
  taxTotal: number;
  total: number;
  taxes: TaxBreakdown[];
}

export interface RecurringTotals {
  oneTimeTotal: number;
  monthlyTotal: number;
  quarterlyTotal: number;
  annualTotal: number;
  monthlyRecurringValue: number;
  firstInvoiceTotal: number;
  firstYearValue: number;
}

export interface PreviewResponse {
  lines: PriceLine[];
  totals: Totals;
  recurring: RecurringTotals;
}

export interface InvoiceSummary {
  id: string;
  number: string | null;
  clientAccountId: string;
  clientName: string;
  status: InvoiceStatus;
  currency: string;
  issueDate: string | null;
  dueDate: string | null;
  total: number;
  amountPaid: number;
  balance: number;
  daysOverdue: number;
  createdAt: string;
  contractId: string | null;
}

export interface Payment {
  id: string;
  invoiceId: string;
  invoiceNumber: string | null;
  clientAccountId: string;
  clientName: string | null;
  amount: number;
  currency: string;
  method: PaymentMethod;
  reference: string;
  paidOn: string;
  notes: string | null;
  requestId: string;
  recordedBy: string | null;
  createdAt: string;
}

export interface CreditApplication {
  creditNoteId: string;
  creditNoteNumber: string;
  invoiceId: string;
  invoiceNumber: string | null;
  amount: number;
  appliedAt: string;
}

export interface Invoice {
  id: string;
  number: string | null;
  clientAccountId: string;
  clientName: string;
  clientBillingEmail: string | null;
  status: InvoiceStatus;
  currency: string;
  issueDate: string | null;
  dueDate: string | null;
  paymentTermsDays: number;
  totals: Totals;
  amountPaid: number;
  amountCredited: number;
  amountWrittenOff: number;
  balance: number;
  daysOverdue: number;
  notes: string | null;
  reference: string | null;
  contractId: string | null;
  proposalId: string | null;
  periodStart: string | null;
  periodEnd: string | null;
  lines: PriceLine[];
  payments: Payment[];
  credits: CreditApplication[];
  reminders: { kind: string; sentAt: string }[];
  publicUrl: string | null;
  issuedAt: string | null;
  sentAt: string | null;
  paidAt: string | null;
  voidedAt: string | null;
  voidReason: string | null;
  writtenOffAt: string | null;
  writeOffReason: string | null;
  issuedByUserId: string | null;
  createdAt: string;
  concurrencyStamp: string;
}

export interface InvoiceDraftRequest {
  clientAccountId: string;
  currency?: string;
  paymentTermsDays?: number;
  notes?: string;
  reference?: string;
  lines: PriceLineInput[];
  concurrencyStamp?: string;
}

export interface RecordPaymentRequest {
  requestId: string;
  amount: number;
  method: PaymentMethod;
  reference: string;
  paidOn: string;
  notes?: string;
  concurrencyStamp: string;
}

export interface PaymentRecorded {
  payment: Payment;
  invoice: Invoice;
  replayed: boolean;
}

export interface CreditNote {
  id: string;
  number: string;
  clientAccountId: string;
  clientName: string;
  invoiceId: string | null;
  invoiceNumber: string | null;
  currency: string;
  amount: number;
  amountApplied: number;
  remaining: number;
  status: CreditNoteStatus;
  reason: string;
  issueDate: string;
  applications: CreditApplication[];
  createdAt: string;
}

export interface ContractSummary {
  id: string;
  number: string;
  title: string;
  clientAccountId: string;
  clientName: string;
  status: ContractStatus;
  currency: string;
  billingFrequency: BillingFrequency;
  startDate: string;
  endDate: string | null;
  amountPerPeriod: number;
  monthlyValue: number;
  nextInvoiceDate: string | null;
  autoRenew: boolean;
}

export interface Contract {
  id: string;
  number: string;
  title: string;
  clientAccountId: string;
  clientName: string;
  status: ContractStatus;
  currency: string;
  startDate: string;
  endDate: string | null;
  billingFrequency: BillingFrequency;
  autoRenew: boolean;
  renewalTermMonths: number;
  noticePeriodDays: number;
  paymentTermsDays: number;
  autoIssueInvoices: boolean | null;
  nextPeriodIndex: number;
  nextInvoiceDate: string | null;
  proposalId: string | null;
  proposalVersion: number | null;
  proposalNumber: string | null;
  notes: string | null;
  lines: PriceLine[];
  totalsPerPeriod: Totals;
  monthlyValue: number;
  invoices: InvoiceSummary[];
  activatedAt: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
  createdAt: string;
  concurrencyStamp: string;
}

export interface ContractRequest {
  clientAccountId: string;
  title: string;
  currency?: string;
  startDate: string;
  endDate?: string | null;
  billingFrequency: BillingFrequency;
  autoRenew: boolean;
  renewalTermMonths: number;
  noticePeriodDays: number;
  paymentTermsDays?: number;
  autoIssueInvoices?: boolean | null;
  notes?: string;
  lines: PriceLineInput[];
  concurrencyStamp?: string;
}

export interface TaxRate {
  id: string;
  name: string;
  ratePercent: number;
  inclusive: boolean;
  countryCode: string | null;
  isActive: boolean;
  needsReview: boolean;
  notes: string | null;
  concurrencyStamp: string;
}

export interface BillingSettings {
  invoicePrefix: string;
  creditNotePrefix: string;
  contractPrefix: string;
  proposalPrefix: string;
  numberPadding: number;
  paymentTermsDays: number;
  defaultCurrency: string;
  invoiceOnAcceptance: boolean;
  autoIssueInvoices: boolean;
  remindersEnabled: boolean;
  reminderOffsetsDays: number[];
  companyName: string;
  companyAddress: string | null;
  companyTaxId: string | null;
  companyEmail: string | null;
  bankDetails: string | null;
  paymentLinkText: string | null;
  paymentInstructions: string | null;
  invoiceFooter: string | null;
  defaultTaxRateId: string | null;
}

export interface ClientOption {
  id: string;
  name: string;
  slug: string;
  currency: string;
  countryCode: string;
  status: string;
  billingEmail: string | null;
}

export interface CurrencyAmount {
  currency: string;
  amount: number;
}

export interface AgingRow {
  clientAccountId: string;
  clientName: string;
  currency: string;
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  over90: number;
  total: number;
}

export interface AgingReport {
  asOf: string;
  rows: AgingRow[];
  totals: AgingRow[];
}

export interface RevenueRow {
  key: string;
  label: string;
  currency: string;
  invoiced: number;
  collected: number;
}

export interface RevenueReport {
  groupBy: 'month' | 'service' | 'client';
  from: string;
  to: string;
  rows: RevenueRow[];
}

export interface MrrReport {
  rows: { clientAccountId: string; clientName: string; currency: string; activeContracts: number; mrr: number; arr: number }[];
  mrrByCurrency: CurrencyAmount[];
  arrByCurrency: CurrencyAmount[];
}

export interface CollectionsReport {
  from: string;
  to: string;
  rows: { month: string; currency: string; method: PaymentMethod; payments: number; amount: number }[];
}

export interface BillingOverview {
  outstanding: CurrencyAmount[];
  overdue: CurrencyAmount[];
  mrr: CurrencyAmount[];
  collectedLast30Days: CurrencyAmount[];
  draftInvoices: number;
  overdueInvoices: number;
  activeContracts: number;
  recentlyOverdue: InvoiceSummary[];
  recentPayments: Payment[];
}

export interface PaymentInstructions {
  companyName: string;
  companyAddress: string | null;
  companyTaxId: string | null;
  companyEmail: string | null;
  bankDetails: string | null;
  paymentLinkText: string | null;
  paymentInstructions: string | null;
  invoiceFooter: string | null;
  onlinePaymentAvailable: boolean;
}

/** Invoice as the client sees it (client portal and the public `/i/:token` page). */
export interface PublicInvoice {
  number: string | null;
  clientName: string;
  clientAddress: string | null;
  clientTaxId: string | null;
  status: InvoiceStatus;
  currency: string;
  issueDate: string | null;
  dueDate: string | null;
  totals: Totals;
  amountPaid: number;
  amountCredited: number;
  balance: number;
  notes: string | null;
  periodStart: string | null;
  periodEnd: string | null;
  lines: PriceLine[];
  payment: PaymentInstructions;
}

export interface Statement {
  clientAccountId: string;
  clientName: string;
  currency: string;
  from: string;
  to: string;
  openingBalance: number;
  lines: { date: string; type: string; reference: string; description: string; debit: number; credit: number; balance: number }[];
  closingBalance: number;
}
