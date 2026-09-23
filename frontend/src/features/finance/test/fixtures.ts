import { json, makeUser, session } from '@/test/fetchMock';
import type {
  PayoutBatchDetail,
  PayoutBatchSummary,
  PayoutItem,
  PayoutScheduleResponse,
  Reconciliation,
} from '../api/types';

export const FINANCE_PERMISSIONS = [
  'ledger.view',
  'ledger.adjust',
  'payouts.view',
  'payouts.prepare',
  'payouts.finalize',
  'payouts.record_payment',
  'payouts.hold',
  'payouts.settings',
  'rewards.approve_bonus',
];

export const financeUser = makeUser({
  id: 'fin-me',
  displayName: 'Farah Finance',
  email: 'farah@optimizeall.local',
  roles: ['Finance'],
  permissions: FINANCE_PERMISSIONS,
  timeZone: 'UTC',
});

/** Route map entry answering the AuthProvider bootstrap with a signed-in finance user. */
export const signedIn = { 'POST /auth/refresh': () => json(200, session(financeUser)) };

export function batchSummary(overrides: Partial<PayoutBatchSummary> = {}): PayoutBatchSummary {
  return {
    id: 'b1',
    reference: 'PB-2026-09-20',
    periodKey: '2026-09-20',
    periodStart: '2026-09-06T23:59:59Z',
    cutoffAt: '2026-09-20T23:59:59Z',
    paymentDate: '2026-09-25',
    status: 'Draft',
    itemCount: 2,
    totalAmount: 47,
    currency: 'USD',
    paidCount: 0,
    paidAmount: 0,
    preparedBy: { id: 'someone-else', displayName: 'Pat Preparer', email: 'pat@optimizeall.local' },
    finalizedBy: null,
    createdAt: '2026-09-21T08:00:00Z',
    finalizedAt: null,
    completedAt: null,
    cancelledAt: null,
    ...overrides,
  };
}

export function payoutItem(overrides: Partial<PayoutItem> = {}): PayoutItem {
  return {
    itemId: '11111111-1111-4111-8111-111111111111',
    user: { id: 'p1', displayName: 'Sara Khan', email: 'sara@example.com', country: 'PK' },
    amount: 25,
    currency: 'USD',
    earningCount: 1,
    status: 'Pending',
    paymentProvider: 'manual',
    destinationHint: '••••6702',
    paymentReference: null,
    paidAt: null,
    holdReason: null,
    failureReason: null,
    concurrencyStamp: 'item-stamp',
    ...overrides,
  };
}

export function batchDetail(
  batch: Partial<PayoutBatchSummary> = {},
  items: PayoutItem[] = [
    payoutItem(),
    payoutItem({
      itemId: '22222222-2222-4222-8222-222222222222',
      user: { id: 'p2', displayName: 'Leo Martins', email: 'leo@example.com', country: 'BR' },
      amount: 22,
    }),
  ],
): PayoutBatchDetail {
  return {
    batch: batchSummary(batch),
    concurrencyStamp: 'batch-stamp-1',
    notes: null,
    cancelReason: null,
    totalsByStatus: [{ status: items[0]?.status ?? 'Pending', count: items.length, amount: 47 }],
    warnings: {
      missingPayoutDetails: [],
      openAppeals: [],
      openDisputes: [],
      highRiskSubmissions: [],
      highRiskThreshold: 50,
      exclusions: [
        {
          user: { id: 'p3', displayName: 'Mia Low', email: 'mia@example.com', country: 'GB' },
          reason: 'BelowMinimum',
          amount: 5,
          earningCount: 1,
        },
      ],
    },
    items: { items, total: items.length, page: 1, pageSize: 25, totalPages: 1 },
  };
}

export function scheduleResponse(): PayoutScheduleResponse {
  const current = {
    id: 's1',
    frequency: 'Biweekly' as const,
    anchorCutoffDate: '2026-01-04',
    cutoffLocalTime: '23:59:59',
    timeZone: 'UTC',
    paymentDelayDays: 5,
    minimumPayoutAmount: 10,
    settlementCurrency: 'USD',
    earningHoldDays: 3,
    autoPrepareBatches: true,
    effectiveFrom: '2026-01-01T00:00:00Z',
    createdAt: '2026-01-01T00:00:00Z',
    createdByUserId: null,
    changeReason: 'Initial schedule',
    isDefault: false,
  };
  const period = (key: string, start: string) => ({
    periodKey: key,
    periodStart: `${start}T23:59:59Z`,
    cutoffAt: `${key}T23:59:59Z`,
    cutoffLocalDate: key,
    paymentDate: key,
  });
  return {
    current,
    currentPeriod: period('2026-10-04', '2026-09-20'),
    lastCompletedPeriod: period('2026-09-20', '2026-09-06'),
    upcoming: [period('2026-10-04', '2026-09-20'), period('2026-10-18', '2026-10-04')],
    scheduledChanges: [],
    history: [current],
  };
}

export function reconciliation(overrides: Partial<Reconciliation> = {}): Reconciliation {
  return {
    batchId: 'b1',
    reference: 'PB-2026-09-20',
    periodKey: '2026-09-20',
    status: 'Finalized',
    currency: 'USD',
    expected: 47,
    recordedPaid: 25,
    awaiting: 22,
    failed: 0,
    held: 0,
    cancelled: 0,
    paidCount: 1,
    awaitingCount: 1,
    isBalanced: true,
    discrepancies: [],
    items: [
      {
        itemId: '11111111-1111-4111-8111-111111111111',
        user: { id: 'p1', displayName: 'Sara Khan', email: 'sara@example.com', country: 'PK' },
        status: 'Paid',
        amount: 25,
        earningsTotal: 25,
        earningCount: 1,
        linkedEarningCount: 1,
        paymentReference: 'TRX-1',
        paidAt: '2026-09-26T10:00:00Z',
        ok: true,
      },
    ],
    ...overrides,
  };
}
