import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type QueryParams } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import type {
  AgingReport,
  BillingOverview,
  BillingSettings,
  ClientOption,
  CollectionsReport,
  Contract,
  ContractRequest,
  ContractSummary,
  CreditNote,
  Invoice,
  InvoiceDraftRequest,
  InvoiceSummary,
  MrrReport,
  Payment,
  PaymentRecorded,
  PreviewResponse,
  PriceLineInput,
  RecordPaymentRequest,
  RevenueReport,
  Statement,
  TaxRate,
} from './types';

const BASE = '/agency/billing';

export const billingKeys = {
  all: ['billing'] as const,
  overview: () => ['billing', 'overview'] as const,
  invoices: (params: QueryParams) => ['billing', 'invoices', params] as const,
  invoice: (id: string) => ['billing', 'invoice', id] as const,
  payments: (params: QueryParams) => ['billing', 'payments', params] as const,
  creditNotes: (params: QueryParams) => ['billing', 'credit-notes', params] as const,
  contracts: (params: QueryParams) => ['billing', 'contracts', params] as const,
  contract: (id: string) => ['billing', 'contract', id] as const,
  clients: () => ['billing', 'clients'] as const,
  taxRates: (all: boolean) => ['billing', 'tax-rates', all] as const,
  settings: () => ['billing', 'settings'] as const,
  report: (name: string, params: QueryParams) => ['billing', 'report', name, params] as const,
  statement: (clientId: string, params: QueryParams) => ['billing', 'statement', clientId, params] as const,
};

// ---------------- Queries

export function useBillingOverview() {
  return useQuery({ queryKey: billingKeys.overview(), queryFn: ({ signal }) => api.get<BillingOverview>(`${BASE}/overview`, { signal }) });
}

export function useInvoices(params: QueryParams) {
  return useQuery({
    queryKey: billingKeys.invoices(params),
    queryFn: ({ signal }) => api.get<PagedResult<InvoiceSummary>>(`${BASE}/invoices`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useInvoice(id: string | undefined) {
  return useQuery({
    queryKey: billingKeys.invoice(id ?? ''),
    queryFn: ({ signal }) => api.get<Invoice>(`${BASE}/invoices/${id}`, { signal }),
    enabled: !!id,
  });
}

export function usePayments(params: QueryParams) {
  return useQuery({
    queryKey: billingKeys.payments(params),
    queryFn: ({ signal }) => api.get<PagedResult<Payment>>(`${BASE}/payments`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useCreditNotes(params: QueryParams) {
  return useQuery({
    queryKey: billingKeys.creditNotes(params),
    queryFn: ({ signal }) => api.get<PagedResult<CreditNote>>(`${BASE}/credit-notes`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useContracts(params: QueryParams) {
  return useQuery({
    queryKey: billingKeys.contracts(params),
    queryFn: ({ signal }) => api.get<PagedResult<ContractSummary>>('/agency/contracts', { query: params, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useContract(id: string | undefined) {
  return useQuery({
    queryKey: billingKeys.contract(id ?? ''),
    queryFn: ({ signal }) => api.get<Contract>(`/agency/contracts/${id}`, { signal }),
    enabled: !!id,
  });
}

export function useClientOptions(enabled = true) {
  return useQuery({
    queryKey: billingKeys.clients(),
    queryFn: ({ signal }) => api.get<ClientOption[]>(`${BASE}/clients`, { signal }),
    staleTime: 5 * 60_000,
    enabled,
  });
}

export function useTaxRates(includeInactive = false) {
  return useQuery({
    queryKey: billingKeys.taxRates(includeInactive),
    queryFn: ({ signal }) => api.get<TaxRate[]>(`${BASE}/tax-rates`, { query: { includeInactive }, signal }),
    staleTime: 5 * 60_000,
  });
}

export function useBillingSettings() {
  return useQuery({ queryKey: billingKeys.settings(), queryFn: ({ signal }) => api.get<BillingSettings>(`${BASE}/settings`, { signal }) });
}

export function useReport<T>(name: 'aging' | 'revenue' | 'mrr' | 'collections', params: QueryParams) {
  return useQuery({
    queryKey: billingKeys.report(name, params),
    queryFn: ({ signal }) => api.get<T>(`${BASE}/reports/${name}`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });
}

export const useAgingReport = (params: QueryParams) => useReport<AgingReport>('aging', params);
export const useRevenueReport = (params: QueryParams) => useReport<RevenueReport>('revenue', params);
export const useMrrReport = () => useReport<MrrReport>('mrr', {});
export const useCollectionsReport = (params: QueryParams) => useReport<CollectionsReport>('collections', params);

export function useStatement(clientId: string | undefined, params: QueryParams) {
  return useQuery({
    queryKey: billingKeys.statement(clientId ?? '', params),
    queryFn: ({ signal }) => api.get<Statement>(`${BASE}/clients/${clientId}/statement`, { query: params, signal }),
    enabled: !!clientId,
  });
}

/**
 * Server-side totals for an editor (debounced by the caller). Money is never computed in the browser: the preview API
 * validates and prices every line with the same rules as saving.
 */
export function usePricePreview(path: string, currency: string, lines: PriceLineInput[], enabled: boolean) {
  return useQuery({
    queryKey: ['billing', 'preview', path, currency, lines],
    queryFn: ({ signal }) => api.post<PreviewResponse>(path, { currency, lines }, { signal }),
    enabled: enabled && lines.length > 0,
    placeholderData: keepPreviousData,
    retry: false,
    staleTime: 60_000,
  });
}

// ---------------- Mutations

function useInvalidate() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: billingKeys.all });
}

export function useSaveInvoice(id?: string) {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (body: InvoiceDraftRequest) =>
      id ? api.put<Invoice>(`${BASE}/invoices/${id}`, body) : api.post<Invoice>(`${BASE}/invoices`, body),
    onSuccess: () => invalidate(),
  });
}

export function useInvoiceAction(id: string) {
  const invalidate = useInvalidate();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ action, body }: { action: 'issue' | 'send' | 'void' | 'write-off'; body?: unknown }) =>
      api.post<Invoice>(`${BASE}/invoices/${id}/${action}`, body ?? {}),
    onSuccess: (invoice) => {
      qc.setQueryData(billingKeys.invoice(id), invoice);
      void invalidate();
    },
  });
}

export function useDeleteInvoice() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: (id: string) => api.delete<void>(`${BASE}/invoices/${id}`), onSuccess: () => invalidate() });
}

export function useRecordPayment(invoiceId: string) {
  const invalidate = useInvalidate();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: RecordPaymentRequest) => api.post<PaymentRecorded>(`${BASE}/invoices/${invoiceId}/payments`, body),
    onSuccess: (result) => {
      qc.setQueryData(billingKeys.invoice(invoiceId), result.invoice);
      void invalidate();
    },
  });
}

export function useCreateCreditNote() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (body: { requestId: string; invoiceId?: string; clientAccountId?: string; amount: number; reason: string }) =>
      api.post<CreditNote>(`${BASE}/credit-notes`, body),
    onSuccess: () => invalidate(),
  });
}

export function useApplyCreditNote(id: string) {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (body: { invoiceId: string; amount: number }) => api.post<CreditNote>(`${BASE}/credit-notes/${id}/apply`, body),
    onSuccess: () => invalidate(),
  });
}

export function useSaveContract(id?: string) {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (body: ContractRequest) => (id ? api.put<Contract>(`/agency/contracts/${id}`, body) : api.post<Contract>('/agency/contracts', body)),
    onSuccess: () => invalidate(),
  });
}

export function useContractAction(id: string) {
  const invalidate = useInvalidate();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ action, body }: { action: 'activate' | 'pause' | 'resume' | 'cancel' | 'generate-invoices'; body?: unknown }) =>
      api.post<Contract>(`/agency/contracts/${id}/${action}`, body ?? {}),
    onSuccess: (contract) => {
      qc.setQueryData(billingKeys.contract(id), contract);
      void invalidate();
    },
  });
}

export function useSaveSettings() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (body: { settings: BillingSettings; reason: string; confirm: boolean }) => api.put<BillingSettings>(`${BASE}/settings`, body),
    onSuccess: () => invalidate(),
  });
}

export function useSaveTaxRate(id?: string) {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (body: Omit<TaxRate, 'id' | 'concurrencyStamp'> & { concurrencyStamp?: string }) =>
      id ? api.put<TaxRate>(`${BASE}/tax-rates/${id}`, body) : api.post<TaxRate>(`${BASE}/tax-rates`, body),
    onSuccess: () => invalidate(),
  });
}
