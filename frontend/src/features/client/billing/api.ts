import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ContractSummary, CurrencyAmount, InvoiceSummary, PublicInvoice, Statement } from '@/features/agency/billing/api/types';
import type { AcceptProposalResponse, ProposalStatus, PublicProposal } from '@/features/agency/crm/api/types';
import { api, type QueryParams } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';

const BASE = '/client/billing';

export interface ClientBillingSummary {
  organizations: { clientAccountId: string; name: string; currency: string; role: string }[];
  outstanding: CurrencyAmount[];
  overdue: CurrencyAmount[];
  openInvoices: number;
  proposalsAwaitingResponse: number;
}

export interface ClientProposalSummary {
  id: string;
  number: string;
  title: string;
  status: ProposalStatus;
  currency: string;
  total: number;
  monthlyRecurringValue: number;
  validUntil: string;
  sentAt: string | null;
  acceptedAt: string | null;
}

export const clientBillingKeys = {
  all: ['client-billing'] as const,
  summary: () => ['client-billing', 'summary'] as const,
  invoices: (p: QueryParams) => ['client-billing', 'invoices', p] as const,
  invoice: (id: string) => ['client-billing', 'invoice', id] as const,
  proposals: () => ['client-billing', 'proposals'] as const,
  proposal: (id: string) => ['client-billing', 'proposal', id] as const,
  contracts: () => ['client-billing', 'contracts'] as const,
  statement: (p: QueryParams) => ['client-billing', 'statement', p] as const,
};

export const useClientBillingSummary = () =>
  useQuery({ queryKey: clientBillingKeys.summary(), queryFn: ({ signal }) => api.get<ClientBillingSummary>(`${BASE}/summary`, { signal }) });

export const useClientInvoices = (params: QueryParams) =>
  useQuery({
    queryKey: clientBillingKeys.invoices(params),
    queryFn: ({ signal }) => api.get<PagedResult<InvoiceSummary>>(`${BASE}/invoices`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });

export const useClientInvoice = (id: string) =>
  useQuery({ queryKey: clientBillingKeys.invoice(id), queryFn: ({ signal }) => api.get<PublicInvoice>(`${BASE}/invoices/${id}`, { signal }), enabled: !!id });

export const useClientProposals = () =>
  useQuery({ queryKey: clientBillingKeys.proposals(), queryFn: ({ signal }) => api.get<ClientProposalSummary[]>(`${BASE}/proposals`, { signal }) });

export const useClientProposal = (id: string) =>
  useQuery({ queryKey: clientBillingKeys.proposal(id), queryFn: ({ signal }) => api.get<PublicProposal>(`${BASE}/proposals/${id}`, { signal }), enabled: !!id });

export const useClientContracts = () =>
  useQuery({ queryKey: clientBillingKeys.contracts(), queryFn: ({ signal }) => api.get<ContractSummary[]>(`${BASE}/contracts`, { signal }) });

export const useClientStatement = (params: QueryParams, enabled: boolean) =>
  useQuery({
    queryKey: clientBillingKeys.statement(params),
    queryFn: ({ signal }) => api.get<Statement>(`${BASE}/statement`, { query: params, signal }),
    enabled,
  });

export function useRespondToProposal(id: string) {
  const qc = useQueryClient();
  return {
    accept: useMutation({
      mutationFn: (body: { version: number; fullName: string; title: string; agreeToTerms: boolean }) =>
        api.post<AcceptProposalResponse>(`${BASE}/proposals/${id}/accept`, body),
      onSuccess: (result) => {
        qc.setQueryData(clientBillingKeys.proposal(id), result.proposal);
        void qc.invalidateQueries({ queryKey: clientBillingKeys.all });
      },
    }),
    decline: useMutation({
      mutationFn: (body: { version: number; reason: string }) => api.post<PublicProposal>(`${BASE}/proposals/${id}/decline`, body),
      onSuccess: (result) => {
        qc.setQueryData(clientBillingKeys.proposal(id), result);
        void qc.invalidateQueries({ queryKey: clientBillingKeys.all });
      },
    }),
  };
}
