import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import type {
  CampaignRates,
  PersonRates,
  RateAssignment,
  RateCard,
  RateCardListItem,
  RateGroup,
  RateGroupListItem,
  RateGroupMember,
  RateGroupMemberEvent,
} from './types';

/** Query keys (all under 'rates' so a mutation can invalidate everything rate-related at once). */
export const rk = {
  all: ['rates'] as const,
  cards: (params?: object) => ['rates', 'cards', params ?? {}] as const,
  card: (id: string) => ['rates', 'card', id] as const,
  groups: (params?: object) => ['rates', 'groups', params ?? {}] as const,
  group: (id: string) => ['rates', 'group', id] as const,
  members: (id: string, params?: object) => ['rates', 'group', id, 'members', params ?? {}] as const,
  history: (id: string, params?: object) => ['rates', 'group', id, 'history', params ?? {}] as const,
  assignments: (params?: object) => ['rates', 'assignments', params ?? {}] as const,
  person: (userId: string, campaignId?: string) => ['rates', 'person', userId, campaignId ?? ''] as const,
  campaign: (campaignId: string) => ['rates', 'campaign', campaignId] as const,
};

export interface CardListParams {
  search?: string;
  status?: string;
  currency?: string;
  page: number;
  pageSize: number;
}

export function useRateCards(params: CardListParams) {
  return useQuery({
    queryKey: rk.cards(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<RateCardListItem>>('/admin/rate-cards', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useRateCard(id: string) {
  return useQuery({
    queryKey: rk.card(id),
    queryFn: ({ signal }) => api.get<RateCard>(`/admin/rate-cards/${id}`, { signal }),
  });
}

export function useRateGroups(params: {
  search?: string;
  mode?: string;
  includeArchived?: boolean;
  page: number;
  pageSize: number;
}) {
  return useQuery({
    queryKey: rk.groups(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<RateGroupListItem>>('/admin/rate-groups', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useRateGroup(id: string) {
  return useQuery({
    queryKey: rk.group(id),
    queryFn: ({ signal }) => api.get<RateGroup>(`/admin/rate-groups/${id}`, { signal }),
  });
}

export function useGroupMembers(id: string, params: { search?: string; page: number; pageSize: number }) {
  return useQuery({
    queryKey: rk.members(id, params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<RateGroupMember>>(`/admin/rate-groups/${id}/members`, {
        query: { ...params },
        signal,
      }),
    placeholderData: keepPreviousData,
  });
}

export function useGroupHistory(id: string, params: { page: number; pageSize: number }) {
  return useQuery({
    queryKey: rk.history(id, params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<RateGroupMemberEvent>>(`/admin/rate-groups/${id}/history`, {
        query: { ...params },
        signal,
      }),
    placeholderData: keepPreviousData,
  });
}

export function useAssignments(
  params: Record<string, string | number | boolean | undefined>,
  enabled = true,
) {
  return useQuery({
    queryKey: rk.assignments(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<RateAssignment>>('/admin/rate-assignments', { query: { ...params }, signal }),
    enabled,
  });
}

export function usePersonRates(userId: string, campaignId?: string) {
  return useQuery({
    queryKey: rk.person(userId, campaignId),
    queryFn: ({ signal }) =>
      api.get<PersonRates>(`/admin/users/${userId}/rates`, { query: { campaignId }, signal }),
  });
}

export function useCampaignRates(campaignId: string, enabled = true) {
  return useQuery({
    queryKey: rk.campaign(campaignId),
    queryFn: ({ signal }) => api.get<CampaignRates>(`/admin/campaigns/${campaignId}/rates`, { signal }),
    enabled,
  });
}

/** Active standard cards for pickers. */
export function useActiveCardOptions(enabled = true) {
  return useQuery({
    queryKey: rk.cards({ status: 'Active', pageSize: 200, picker: true }),
    queryFn: ({ signal }) =>
      api.get<PagedResult<RateCardListItem>>('/admin/rate-cards', {
        query: { status: 'Active', pageSize: 200 },
        signal,
      }),
    enabled,
    staleTime: 30_000,
  });
}

/** Live groups for pickers. */
export function useGroupOptions(enabled = true) {
  return useQuery({
    queryKey: rk.groups({ pageSize: 200, picker: true }),
    queryFn: ({ signal }) =>
      api.get<PagedResult<RateGroupListItem>>('/admin/rate-groups', { query: { pageSize: 200 }, signal }),
    enabled,
    staleTime: 30_000,
  });
}
