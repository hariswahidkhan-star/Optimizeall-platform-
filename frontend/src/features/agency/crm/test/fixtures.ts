import type { PriceLine, Totals } from '@/features/agency/billing/api/types';
import { json, makeUser, session } from '@/test/fetchMock';
import type { BoardColumn, DealSummary, ProposalVersion, PublicProposal, Stage } from '../api/types';

export const SALES_PERMISSIONS = ['crm.view', 'crm.manage', 'proposals.manage', 'clients.view', 'billing.view'];

export const salesUser = makeUser({
  id: 'sales-1',
  displayName: 'Hassan Raza',
  email: 'sales@demo.optimizeall.app',
  roles: ['SalesRep'],
  permissions: SALES_PERMISSIONS,
  timeZone: 'UTC',
});

export const signedInSales = { 'POST /auth/refresh': () => json(200, session(salesUser)) };

export function stage(overrides: Partial<Stage> = {}): Stage {
  return { id: 's-new', name: 'New', position: 10, winProbability: 5, kind: 'Open', isActive: true, concurrencyStamp: 'st', ...overrides };
}

export function deal(overrides: Partial<DealSummary> = {}): DealSummary {
  return {
    id: 'd1',
    title: 'Brightline Dental',
    stageId: 's-new',
    stageName: 'New',
    status: 'Open',
    value: 36000,
    currency: 'USD',
    winProbability: 5,
    weightedValue: 1800,
    expectedCloseDate: null,
    companyId: 'c1',
    companyName: 'Brightline Dental Group',
    primaryContactId: null,
    contactName: 'Rachel Nguyen',
    owner: { id: 'sales-1', displayName: 'Hassan Raza', email: 'sales@demo.optimizeall.app' },
    source: 'WebsiteInquiry',
    serviceSlugs: ['seo'],
    score: 58,
    stageChangedAt: '2026-09-20T10:00:00Z',
    createdAt: '2026-09-01T10:00:00Z',
    concurrencyStamp: 'stamp-d1',
    ...overrides,
  };
}

export function board(): { columns: BoardColumn[] } {
  const stages = [
    stage(),
    stage({ id: 's-qual', name: 'Qualified', position: 20, winProbability: 25 }),
    stage({ id: 's-won', name: 'Won', position: 1000, winProbability: 100, kind: 'Won' }),
    stage({ id: 's-lost', name: 'Lost', position: 1001, winProbability: 0, kind: 'Lost' }),
  ];
  return {
    columns: stages.map((s) => ({
      stage: s,
      count: s.id === 's-new' ? 1 : 0,
      totals: s.id === 's-new' ? [{ currency: 'USD', amount: 36000 }] : [],
      deals: s.id === 's-new' ? [deal()] : [],
    })),
  };
}

export const emptyTotals = (currency = 'USD'): Totals => ({
  currency,
  grossTotal: 0,
  discountTotal: 0,
  subtotal: 0,
  taxTotal: 0,
  total: 0,
  taxes: [],
});

export function priceLine(overrides: Partial<PriceLine> = {}): PriceLine {
  return {
    id: 'l1',
    position: 1,
    description: 'SEO retainer',
    serviceSlug: 'seo',
    packageSlug: null,
    quantity: 1,
    unitPrice: 2500,
    discountType: 'None',
    discountValue: 0,
    taxRateId: null,
    taxName: null,
    taxPercent: 0,
    taxInclusive: false,
    recurrence: 'Monthly',
    discountAmount: 0,
    subtotal: 2500,
    taxAmount: 0,
    total: 2500,
    ...overrides,
  };
}

export function proposalVersion(overrides: Partial<ProposalVersion> = {}): ProposalVersion {
  return {
    versionNumber: 2,
    title: 'Growth retainer',
    currency: 'USD',
    validUntil: '2030-01-31',
    executiveSummary: 'Grow qualified demand.',
    goals: 'More leads',
    scope: null,
    deliverables: null,
    timeline: null,
    terms: '30 days notice',
    lines: [priceLine()],
    totals: { ...emptyTotals(), grossTotal: 2500, subtotal: 2500, total: 2500 },
    recurring: { oneTimeTotal: 0, monthlyTotal: 2500, quarterlyTotal: 0, annualTotal: 0, monthlyRecurringValue: 2500, firstInvoiceTotal: 2500, firstYearValue: 30000 },
    createdAt: '2026-09-20T10:00:00Z',
    sentAt: '2026-09-20T11:00:00Z',
    locked: false,
    ...overrides,
  };
}

export function publicProposal(overrides: Partial<PublicProposal> = {}): PublicProposal {
  return {
    number: 'PR-2026-0007',
    title: 'Growth retainer',
    status: 'Sent',
    agencyName: 'Optimize All',
    preparedFor: 'Brightline Dental Group',
    recipientName: 'Rachel Nguyen',
    version: proposalVersion(),
    canRespond: true,
    expired: false,
    beingRevised: false,
    acceptedAt: null,
    signerName: null,
    signerTitle: null,
    declinedAt: null,
    ...overrides,
  };
}

export const TOKEN = 'a'.repeat(43);
