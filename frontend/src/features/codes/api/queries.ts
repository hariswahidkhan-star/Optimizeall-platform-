import { keepPreviousData, useQuery, type QueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import type {
  CodeAssignment,
  CodeProgram,
  CodeProgramListItem,
  CodeReport,
  CodeSale,
  CodeSaleListItem,
  DiscountCode,
  DiscountCodeDetail,
  MyCode,
  MyCodeSale,
} from './types';

/** Query keys (all under 'codes' so a mutation can refresh everything code-related at once). */
export const ck = {
  all: ['codes'] as const,
  programs: (params?: object) => ['codes', 'programs', params ?? {}] as const,
  program: (id: string) => ['codes', 'program', id] as const,
  codes: (programId: string, params?: object) =>
    ['codes', 'program', programId, 'codes', params ?? {}] as const,
  code: (id: string) => ['codes', 'code', id] as const,
  assignments: (programId: string, params?: object) =>
    ['codes', 'program', programId, 'assignments', params ?? {}] as const,
  sales: (params?: object) => ['codes', 'sales', params ?? {}] as const,
  sale: (id: string) => ['codes', 'sale', id] as const,
  report: (params: object) => ['codes', 'report', params] as const,
  mine: ['codes', 'me'] as const,
  mySales: (params?: object) => ['codes', 'me', 'sales', params ?? {}] as const,
  mySale: (id: string) => ['codes', 'me', 'sale', id] as const,
};

export const invalidateCodes = (client: QueryClient) => client.invalidateQueries({ queryKey: ck.all });

export function useCodePrograms(params: {
  search?: string;
  status?: string;
  page: number;
  pageSize: number;
}) {
  return useQuery({
    queryKey: ck.programs(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<CodeProgramListItem>>('/admin/code-programs', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useCodeProgram(id: string) {
  return useQuery({
    queryKey: ck.program(id),
    queryFn: ({ signal }) => api.get<CodeProgram>(`/admin/code-programs/${id}`, { signal }),
  });
}

export function useProgramCodes(
  programId: string,
  params: { search?: string; status?: string; page: number; pageSize: number },
) {
  return useQuery({
    queryKey: ck.codes(programId, params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<DiscountCode>>(`/admin/code-programs/${programId}/codes`, {
        query: { ...params },
        signal,
      }),
    placeholderData: keepPreviousData,
  });
}

export function useDiscountCode(id: string | null) {
  return useQuery({
    queryKey: ck.code(id ?? ''),
    queryFn: ({ signal }) => api.get<DiscountCodeDetail>(`/admin/discount-codes/${id}`, { signal }),
    enabled: !!id,
  });
}

export function useProgramAssignments(programId: string, params: { page: number; pageSize: number }) {
  return useQuery({
    queryKey: ck.assignments(programId, params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<CodeAssignment>>(`/admin/code-programs/${programId}/assignments`, {
        query: { ...params },
        signal,
      }),
    placeholderData: keepPreviousData,
  });
}

export interface SaleListParams {
  programId?: string;
  status?: string;
  verification?: string;
  source?: string;
  userId?: string;
  search?: string;
  page: number;
  pageSize: number;
}

export function useCodeSales(params: SaleListParams) {
  return useQuery({
    queryKey: ck.sales(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<CodeSaleListItem>>('/admin/code-sales', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useCodeSale(id: string) {
  return useQuery({
    queryKey: ck.sale(id),
    queryFn: ({ signal }) => api.get<CodeSale>(`/admin/code-sales/${id}`, { signal }),
  });
}

export function useCodeReport(
  params: { programId?: string; groupBy: string; from?: string; to?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: ck.report(params),
    queryFn: ({ signal }) => api.get<CodeReport>('/admin/code-reports', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useMyCodes() {
  return useQuery({
    queryKey: ck.mine,
    queryFn: ({ signal }) => api.get<MyCode[]>('/me/codes', { signal }),
  });
}

export function useMyCodeSales(params: { status?: string; codeId?: string; page: number; pageSize: number }) {
  return useQuery({
    queryKey: ck.mySales(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<MyCodeSale>>('/me/code-sales', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useMyCodeSale(id: string) {
  return useQuery({
    queryKey: ck.mySale(id),
    queryFn: ({ signal }) => api.get<MyCodeSale>(`/me/code-sales/${id}`, { signal }),
  });
}
