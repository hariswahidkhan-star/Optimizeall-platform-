import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type QueryParams } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import type {
  AdjustmentRequest,
  AdjustmentResult,
  AdminUserListItem,
  BulkPaymentLine,
  BulkPaymentResult,
  CreateHoldResponse,
  EarningsSummary,
  ExchangeRate,
  FinalizeResponse,
  LedgerRow,
  PaymentRecorded,
  PayoutBatchDetail,
  PayoutBatchSummary,
  PayoutHold,
  PayoutItem,
  PayoutItemDetail,
  PayoutScheduleResponse,
  PendingEarning,
  PrepareBatchResponse,
  Reconciliation,
  ReversalResult,
  UpdateScheduleRequest,
} from './types';

/** Query keys. Everything lives under ['finance'] so a broad invalidation refreshes the portal. */
export const financeKeys = {
  all: ['finance'] as const,
  schedule: () => ['finance', 'schedule'] as const,
  batches: (params?: QueryParams) => ['finance', 'batches', params ?? {}] as const,
  batchesRoot: () => ['finance', 'batches'] as const,
  batch: (id: string, params?: QueryParams) => ['finance', 'batch', id, params ?? {}] as const,
  batchRoot: (id: string) => ['finance', 'batch', id] as const,
  item: (batchId: string, itemId: string) => ['finance', 'batch', batchId, 'item', itemId] as const,
  reconciliation: (batchId: string) => ['finance', 'batch', batchId, 'reconciliation'] as const,
  ledger: (params?: QueryParams) => ['finance', 'ledger', params ?? {}] as const,
  balance: (userId: string) => ['finance', 'balance', userId] as const,
  pending: (params?: QueryParams) => ['finance', 'pending', params ?? {}] as const,
  holds: (params?: QueryParams) => ['finance', 'holds', params ?? {}] as const,
  rates: (params?: QueryParams) => ['finance', 'rates', params ?? {}] as const,
  users: (search: string) => ['finance', 'users', search] as const,
};

const BATCHES = '/finance/payout-batches';

// ---------- Queries ----------

export function useSchedule(enabled = true) {
  return useQuery({
    queryKey: financeKeys.schedule(),
    queryFn: ({ signal }) => api.get<PayoutScheduleResponse>('/finance/payout-schedule', { signal }),
    enabled,
  });
}

export function useBatches(params: QueryParams, enabled = true) {
  return useQuery({
    queryKey: financeKeys.batches(params),
    queryFn: ({ signal }) => api.get<PagedResult<PayoutBatchSummary>>(BATCHES, { query: params, signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useBatch(id: string, params: QueryParams, enabled = true) {
  return useQuery({
    queryKey: financeKeys.batch(id, params),
    queryFn: ({ signal }) => api.get<PayoutBatchDetail>(`${BATCHES}/${id}`, { query: params, signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useBatchItem(batchId: string, itemId: string | null) {
  return useQuery({
    queryKey: financeKeys.item(batchId, itemId ?? ''),
    queryFn: ({ signal }) => api.get<PayoutItemDetail>(`${BATCHES}/${batchId}/items/${itemId}`, { signal }),
    enabled: !!itemId,
  });
}

export function useReconciliation(batchId: string, enabled = true) {
  return useQuery({
    queryKey: financeKeys.reconciliation(batchId),
    queryFn: ({ signal }) => api.get<Reconciliation>(`${BATCHES}/${batchId}/reconciliation`, { signal }),
    enabled,
  });
}

export function useLedger(params: QueryParams) {
  return useQuery({
    queryKey: financeKeys.ledger(params),
    queryFn: ({ signal }) => api.get<PagedResult<LedgerRow>>('/finance/ledger', { query: params, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useUserBalance(userId: string) {
  return useQuery({
    queryKey: financeKeys.balance(userId),
    queryFn: ({ signal }) => api.get<EarningsSummary>(`/finance/users/${userId}/balance`, { signal }),
  });
}

export function usePendingEarnings(params: QueryParams, enabled = true) {
  return useQuery({
    queryKey: financeKeys.pending(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<PendingEarning>>('/finance/pending-earnings', { query: params, signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useHolds(params: QueryParams, enabled = true) {
  return useQuery({
    queryKey: financeKeys.holds(params),
    queryFn: ({ signal }) => api.get<PagedResult<PayoutHold>>('/finance/holds', { query: params, signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useExchangeRates(params: QueryParams) {
  return useQuery({
    queryKey: financeKeys.rates(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<ExchangeRate>>('/finance/exchange-rates', { query: params, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useUserSearch(search: string, enabled: boolean) {
  return useQuery({
    queryKey: financeKeys.users(search),
    queryFn: ({ signal }) =>
      api.get<PagedResult<AdminUserListItem>>('/admin/users', {
        query: { search, pageSize: 8 },
        signal,
      }),
    enabled: enabled && search.trim().length >= 2,
    staleTime: 60_000,
  });
}

// ---------- Mutations ----------

/** Invalidates everything that shows batch data (list, detail, items, reconciliation, overview). */
function useInvalidateBatch() {
  const client = useQueryClient();
  return (batchId?: string) =>
    Promise.all([
      client.invalidateQueries({ queryKey: financeKeys.batchesRoot() }),
      batchId ? client.invalidateQueries({ queryKey: financeKeys.batchRoot(batchId) }) : Promise.resolve(),
      client.invalidateQueries({ queryKey: ['finance', 'ledger'] }),
    ]);
}

export function usePrepareBatch() {
  const invalidate = useInvalidateBatch();
  return useMutation({
    mutationFn: (body: { periodKey?: string; note?: string }) =>
      api.post<PrepareBatchResponse>(`${BATCHES}/prepare`, body),
    onSuccess: (result) => invalidate(result.batch.id),
  });
}

export function useHoldItem(batchId: string) {
  const invalidate = useInvalidateBatch();
  return useMutation({
    mutationFn: ({ itemId, reason }: { itemId: string; reason: string }) =>
      api.post<PayoutItem>(`${BATCHES}/${batchId}/items/${itemId}/hold`, { reason }),
    onSettled: () => invalidate(batchId),
  });
}

export function useUnholdItem(batchId: string) {
  const invalidate = useInvalidateBatch();
  return useMutation({
    mutationFn: ({ itemId, note }: { itemId: string; note?: string }) =>
      api.post<PayoutItem>(`${BATCHES}/${batchId}/items/${itemId}/unhold`, { note: note || undefined }),
    onSettled: () => invalidate(batchId),
  });
}

export function useRegenerateBatch(batchId: string) {
  const invalidate = useInvalidateBatch();
  return useMutation({
    mutationFn: (reason: string) =>
      api.post<PayoutBatchSummary>(`${BATCHES}/${batchId}/regenerate`, { reason }),
    onSettled: () => invalidate(batchId),
  });
}

export function useCancelBatch(batchId: string) {
  const invalidate = useInvalidateBatch();
  return useMutation({
    mutationFn: (reason: string) =>
      api.post<PayoutBatchSummary>(`${BATCHES}/${batchId}/cancel`, { reason, confirm: true }),
    onSettled: () => invalidate(batchId),
  });
}

export function useFinalizeBatch(batchId: string) {
  const invalidate = useInvalidateBatch();
  return useMutation({
    mutationFn: (body: { concurrencyStamp: string; reason?: string }) =>
      api.post<FinalizeResponse>(`${BATCHES}/${batchId}/finalize`, {
        confirm: true,
        concurrencyStamp: body.concurrencyStamp,
        reason: body.reason || undefined,
      }),
    onSettled: () => invalidate(batchId),
  });
}

export function useRecordPayment(batchId: string) {
  const invalidate = useInvalidateBatch();
  return useMutation({
    mutationFn: ({
      itemId,
      ...body
    }: {
      itemId: string;
      paymentReference: string;
      paidAt: string;
      note?: string;
    }) => api.post<PaymentRecorded>(`${BATCHES}/${batchId}/items/${itemId}/record-payment`, body),
    onSettled: () => invalidate(batchId),
  });
}

export function useRecordPayments(batchId: string) {
  const invalidate = useInvalidateBatch();
  return useMutation({
    mutationFn: (lines: BulkPaymentLine[]) =>
      api.post<BulkPaymentResult[]>(`${BATCHES}/${batchId}/record-payments`, lines),
    onSettled: () => invalidate(batchId),
  });
}

export function useMarkFailed(batchId: string) {
  const invalidate = useInvalidateBatch();
  return useMutation({
    mutationFn: ({ itemId, reason }: { itemId: string; reason: string }) =>
      api.post<PaymentRecorded>(`${BATCHES}/${batchId}/items/${itemId}/mark-failed`, { reason }),
    onSettled: () => invalidate(batchId),
  });
}

export function useCreateAdjustment() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: AdjustmentRequest) => api.post<AdjustmentResult>('/finance/adjustments', body),
    onSuccess: () => client.invalidateQueries({ queryKey: financeKeys.all }),
  });
}

export function useReverseEarning() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      api.post<ReversalResult>(`/finance/earnings/${id}/reverse`, { reason, confirm: true }),
    onSuccess: () => client.invalidateQueries({ queryKey: financeKeys.all }),
  });
}

export function useDecidePending() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      decision,
      concurrencyStamp,
      reason,
    }: {
      id: string;
      decision: 'approve' | 'decline';
      concurrencyStamp: string;
      reason?: string;
    }) =>
      api.post<LedgerRow>(
        `/finance/pending-earnings/${id}/${decision}`,
        decision === 'approve' ? { concurrencyStamp } : { concurrencyStamp, reason },
      ),
    onSettled: () => client.invalidateQueries({ queryKey: financeKeys.all }),
  });
}

export function useCreateHold() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { userId: string; reason: string }) =>
      api.post<CreateHoldResponse>('/finance/holds', body),
    onSuccess: () => client.invalidateQueries({ queryKey: financeKeys.all }),
  });
}

export function useReleaseHold() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, note }: { id: string; note?: string }) =>
      api.post<PayoutHold>(`/finance/holds/${id}/release`, { note: note || undefined }),
    onSuccess: () => client.invalidateQueries({ queryKey: financeKeys.all }),
  });
}

export function useCreateExchangeRate() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      baseCurrency: string;
      quoteCurrency: string;
      rate: number;
      effectiveAt: string;
      source: string;
      reason: string;
    }) => api.post<ExchangeRate>('/finance/exchange-rates', { ...body, confirm: true }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['finance', 'rates'] }),
  });
}

export function useUpdateSchedule() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateScheduleRequest) =>
      api.put<PayoutScheduleResponse>('/finance/payout-schedule', body),
    onSuccess: (data) => client.setQueryData(financeKeys.schedule(), data),
  });
}
