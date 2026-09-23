import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type QueryParams } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import type {
  Activity,
  ActivityRequest,
  Board,
  Company,
  CompanyRequest,
  CompanySummary,
  Contact,
  ContactRequest,
  ContactSummary,
  CrmDashboard,
  Deal,
  DealRequest,
  DealSummary,
  ImportResult,
  Proposal,
  ProposalRequest,
  ProposalSummary,
  SavedView,
  ScoringRule,
  Stage,
  UserRef,
} from './types';

const CRM = '/agency/crm';

export const crmKeys = {
  all: ['crm'] as const,
  dashboard: () => ['crm', 'dashboard'] as const,
  board: (params: QueryParams) => ['crm', 'board', params] as const,
  deals: (params: QueryParams) => ['crm', 'deals', params] as const,
  deal: (id: string) => ['crm', 'deal', id] as const,
  contacts: (params: QueryParams) => ['crm', 'contacts', params] as const,
  contact: (id: string) => ['crm', 'contact', id] as const,
  companies: (params: QueryParams) => ['crm', 'companies', params] as const,
  company: (id: string) => ['crm', 'company', id] as const,
  activities: (params: QueryParams) => ['crm', 'activities', params] as const,
  myTasks: (params: QueryParams) => ['crm', 'my-tasks', params] as const,
  stages: () => ['crm', 'stages'] as const,
  rules: () => ['crm', 'rules'] as const,
  views: (entity: string) => ['crm', 'views', entity] as const,
  assignees: () => ['crm', 'assignees'] as const,
  proposals: (params: QueryParams) => ['crm', 'proposals', params] as const,
  proposal: (id: string, version?: number) => ['crm', 'proposal', id, version ?? 'current'] as const,
};

// ---------------- Queries

export const useCrmDashboard = () =>
  useQuery({ queryKey: crmKeys.dashboard(), queryFn: ({ signal }) => api.get<CrmDashboard>(`${CRM}/dashboard`, { signal }) });

export const useBoard = (params: QueryParams) =>
  useQuery({
    queryKey: crmKeys.board(params),
    queryFn: ({ signal }) => api.get<Board>(`${CRM}/deals/board`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });

export const useDeals = (params: QueryParams) =>
  useQuery({
    queryKey: crmKeys.deals(params),
    queryFn: ({ signal }) => api.get<PagedResult<DealSummary>>(`${CRM}/deals`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });

export const useDeal = (id: string) =>
  useQuery({ queryKey: crmKeys.deal(id), queryFn: ({ signal }) => api.get<Deal>(`${CRM}/deals/${id}`, { signal }), enabled: !!id });

export const useContacts = (params: QueryParams) =>
  useQuery({
    queryKey: crmKeys.contacts(params),
    queryFn: ({ signal }) => api.get<PagedResult<ContactSummary>>(`${CRM}/contacts`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });

export const useContact = (id: string) =>
  useQuery({ queryKey: crmKeys.contact(id), queryFn: ({ signal }) => api.get<Contact>(`${CRM}/contacts/${id}`, { signal }), enabled: !!id });

export const useCompanies = (params: QueryParams) =>
  useQuery({
    queryKey: crmKeys.companies(params),
    queryFn: ({ signal }) => api.get<PagedResult<CompanySummary>>(`${CRM}/companies`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });

export const useCompany = (id: string) =>
  useQuery({ queryKey: crmKeys.company(id), queryFn: ({ signal }) => api.get<Company>(`${CRM}/companies/${id}`, { signal }), enabled: !!id });

export const useActivities = (params: QueryParams, enabled = true) =>
  useQuery({
    queryKey: crmKeys.activities(params),
    queryFn: ({ signal }) => api.get<PagedResult<Activity>>(`${CRM}/activities`, { query: params, signal }),
    enabled,
  });

export const useMyTasks = (params: QueryParams) =>
  useQuery({
    queryKey: crmKeys.myTasks(params),
    queryFn: ({ signal }) => api.get<PagedResult<Activity>>(`${CRM}/tasks/mine`, { query: params, signal }),
    placeholderData: keepPreviousData,
  });

export const useStages = () =>
  useQuery({ queryKey: crmKeys.stages(), queryFn: ({ signal }) => api.get<Stage[]>(`${CRM}/stages`, { signal }), staleTime: 60_000 });

export const useScoringRules = () =>
  useQuery({ queryKey: crmKeys.rules(), queryFn: ({ signal }) => api.get<ScoringRule[]>(`${CRM}/scoring/rules`, { signal }) });

export const useSavedViews = (entity: string) =>
  useQuery({ queryKey: crmKeys.views(entity), queryFn: ({ signal }) => api.get<SavedView[]>(`${CRM}/views`, { query: { entity }, signal }) });

export const useAssignees = () =>
  useQuery({ queryKey: crmKeys.assignees(), queryFn: ({ signal }) => api.get<UserRef[]>(`${CRM}/assignees`, { signal }), staleTime: 5 * 60_000 });

export const useProposals = (params: QueryParams) =>
  useQuery({
    queryKey: crmKeys.proposals(params),
    queryFn: ({ signal }) => api.get<PagedResult<ProposalSummary>>('/agency/proposals', { query: params, signal }),
    placeholderData: keepPreviousData,
  });

export const useProposal = (id: string | undefined, version?: number) =>
  useQuery({
    queryKey: crmKeys.proposal(id ?? '', version),
    queryFn: ({ signal }) =>
      api.get<Proposal>(version ? `/agency/proposals/${id}/versions/${version}` : `/agency/proposals/${id}`, { signal }),
    enabled: !!id,
  });

// ---------------- Mutations

function useInvalidateCrm() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: crmKeys.all });
}

export function useSaveDeal(id?: string) {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: (body: DealRequest) => (id ? api.put<Deal>(`${CRM}/deals/${id}`, body) : api.post<Deal>(`${CRM}/deals`, body)),
    onSuccess: () => invalidate(),
  });
}

/** Moves a deal to another stage (drag and drop or keyboard). */
export function useMoveDeal() {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: ({ dealId, stageId, lostReason, concurrencyStamp }: { dealId: string; stageId: string; lostReason?: string; concurrencyStamp: string }) =>
      api.post<Deal>(`${CRM}/deals/${dealId}/move`, { stageId, lostReason, concurrencyStamp }),
    onSettled: () => invalidate(),
  });
}

export function useSaveContact(id?: string) {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: (body: ContactRequest) => (id ? api.put<Contact>(`${CRM}/contacts/${id}`, body) : api.post<Contact>(`${CRM}/contacts`, body)),
    onSuccess: () => invalidate(),
  });
}

export function useSaveCompany(id?: string) {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: (body: CompanyRequest) => (id ? api.put<Company>(`${CRM}/companies/${id}`, body) : api.post<Company>(`${CRM}/companies`, body)),
    onSuccess: () => invalidate(),
  });
}

export function useSaveActivity(id?: string) {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: (body: ActivityRequest) => (id ? api.put<Activity>(`${CRM}/activities/${id}`, body) : api.post<Activity>(`${CRM}/activities`, body)),
    onSuccess: () => invalidate(),
  });
}

export function useCompleteActivity() {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: ({ id, completed, concurrencyStamp }: { id: string; completed: boolean; concurrencyStamp: string }) =>
      api.post<Activity>(`${CRM}/activities/${id}/${completed ? 'complete' : 'reopen'}`, { concurrencyStamp }),
    onSuccess: () => invalidate(),
  });
}

export function useImportContacts() {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: ({ file, dryRun, updateExisting }: { file: File; dryRun: boolean; updateExisting: boolean }) => {
      const form = new FormData();
      form.append('file', file);
      return api.upload<ImportResult>(`${CRM}/contacts/import`, form, { query: { dryRun, updateExisting } });
    },
    onSuccess: (result) => {
      if (!result.dryRun) void invalidate();
    },
  });
}

export function useSaveStages() {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: (stages: { id?: string | null; name: string; winProbability: number; kind: string; isActive: boolean }[]) =>
      api.put<Stage[]>(`${CRM}/stages`, { stages }),
    onSuccess: () => invalidate(),
  });
}

export function useSaveRule(id?: string) {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: (body: Omit<ScoringRule, 'id' | 'concurrencyStamp'> & { concurrencyStamp?: string }) =>
      id ? api.put<ScoringRule>(`${CRM}/scoring/rules/${id}`, body) : api.post<ScoringRule>(`${CRM}/scoring/rules`, body),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteRule() {
  const invalidate = useInvalidateCrm();
  return useMutation({ mutationFn: (id: string) => api.delete<void>(`${CRM}/scoring/rules/${id}`), onSuccess: () => invalidate() });
}

export function useRecomputeScores() {
  const invalidate = useInvalidateCrm();
  return useMutation({ mutationFn: () => api.post<{ contacts: number }>(`${CRM}/scoring/recompute`, {}), onSuccess: () => invalidate() });
}

export function useSaveView() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; entity: string; filters: Record<string, string>; shared: boolean }) => api.post<SavedView>(`${CRM}/views`, body),
    onSuccess: (view) => qc.invalidateQueries({ queryKey: crmKeys.views(view.entity) }),
  });
}

export function useDeleteView(entity: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.delete<void>(`${CRM}/views/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: crmKeys.views(entity) }),
  });
}

export function useSaveProposal(id?: string) {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: (body: ProposalRequest) => (id ? api.put<Proposal>(`/agency/proposals/${id}`, body) : api.post<Proposal>('/agency/proposals', body)),
    onSuccess: () => invalidate(),
  });
}

export function useSendProposal(id: string) {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: (body: { concurrencyStamp: string; message?: string; email: boolean }) =>
      api.post<{ proposal: Proposal; shareUrl: string; emailed: boolean }>(`/agency/proposals/${id}/send`, body),
    onSuccess: () => invalidate(),
  });
}

export function useWithdrawProposal(id: string) {
  const invalidate = useInvalidateCrm();
  return useMutation({
    mutationFn: (body: { concurrencyStamp: string; reason?: string }) => api.post<Proposal>(`/agency/proposals/${id}/withdraw`, body),
    onSuccess: () => invalidate(),
  });
}
