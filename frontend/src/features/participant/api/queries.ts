import { keepPreviousData, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type {
  Achievement,
  Announcement,
  CampaignCard,
  CampaignCategory,
  CampaignDetail,
  CampaignFilters,
  Earning,
  EarningsSummary,
  ExperimentVariant,
  MyPayout,
  MyPayoutDetail,
  NotificationItem,
  NotificationPreferences,
  PagedResult,
  ParticipantHome,
  PayoutProfile,
  Profile,
  RecommendedCampaign,
  Referrals,
  SocialAccountList,
  SubmissionDetail,
  SubmissionListItem,
  Ticket,
  TicketSummary,
  TrackingLink,
} from './types';

/** Query keys. Everything participant-scoped lives under `me` so a sign-out / role change can drop it at once. */
export const qk = {
  home: ['me', 'home'] as const,
  profile: ['me', 'profile'] as const,
  payoutProfile: ['me', 'payout-profile'] as const,
  socialAccounts: ['me', 'social-accounts'] as const,
  campaigns: ['campaigns'] as const,
  campaignList: (filters: CampaignFilters) => ['campaigns', 'list', filters] as const,
  recommended: (limit: number) => ['campaigns', 'recommended', limit] as const,
  campaign: (slug: string) => ['campaigns', 'detail', slug] as const,
  categories: ['campaign-categories'] as const,
  variants: (campaignId: string) => ['campaigns', 'variants', campaignId] as const,
  trackingLink: (campaignId: string) => ['me', 'tracking-link', campaignId] as const,
  submissions: ['me', 'submissions'] as const,
  submissionList: (params: object) => ['me', 'submissions', 'list', params] as const,
  submission: (id: string) => ['me', 'submissions', 'detail', id] as const,
  earnings: ['me', 'earnings'] as const,
  earningsSummary: ['me', 'earnings', 'summary'] as const,
  earningList: (params: object) => ['me', 'earnings', 'list', params] as const,
  payouts: ['me', 'payouts'] as const,
  payoutList: (page: number) => ['me', 'payouts', 'list', page] as const,
  payout: (id: string) => ['me', 'payouts', 'detail', id] as const,
  referrals: ['me', 'referrals'] as const,
  achievements: ['me', 'achievements'] as const,
  notifications: ['me', 'notifications'] as const,
  notificationList: (params: object) => ['me', 'notifications', 'list', params] as const,
  unreadCount: ['me', 'notifications', 'unread-count'] as const,
  notificationPreferences: ['me', 'notification-preferences'] as const,
  tickets: ['me', 'support'] as const,
  ticketList: (params: object) => ['me', 'support', 'list', params] as const,
  ticket: (id: string) => ['me', 'support', 'detail', id] as const,
  announcements: ['content', 'announcements'] as const,
};

// ---------------------------------------------------------------- home & profile

export const useHome = () =>
  useQuery({ queryKey: qk.home, queryFn: () => api.get<ParticipantHome>('/me/home') });

export const useProfile = () =>
  useQuery({ queryKey: qk.profile, queryFn: () => api.get<Profile>('/me/profile') });

export const usePayoutProfile = () =>
  useQuery({ queryKey: qk.payoutProfile, queryFn: () => api.get<PayoutProfile>('/me/payout-profile') });

export const useAnnouncements = () =>
  useQuery({ queryKey: qk.announcements, queryFn: () => api.get<Announcement[]>('/content/announcements') });

// ---------------------------------------------------------------- social

export const useSocialAccounts = () =>
  useQuery({ queryKey: qk.socialAccounts, queryFn: () => api.get<SocialAccountList>('/me/social-accounts') });

// ---------------------------------------------------------------- campaigns

export function campaignQueryParams(filters: CampaignFilters, deadlineBeforeUtc: string | null) {
  return {
    platform: filters.platform,
    categoryId: filters.categoryId,
    topic: filters.topic,
    minReward: filters.minReward,
    deadlineBefore: deadlineBeforeUtc,
    eligibleOnly: filters.eligibleOnly ? true : undefined,
    search: filters.search,
    sort: filters.sort,
    page: filters.page,
    pageSize: CAMPAIGN_PAGE_SIZE,
  };
}

export const CAMPAIGN_PAGE_SIZE = 12;

export const useCampaigns = (filters: CampaignFilters, deadlineBeforeUtc: string | null) =>
  useQuery({
    queryKey: qk.campaignList(filters),
    queryFn: ({ signal }) =>
      api.get<PagedResult<CampaignCard>>('/campaigns', {
        query: campaignQueryParams(filters, deadlineBeforeUtc),
        signal,
      }),
    placeholderData: keepPreviousData,
  });

export const useRecommended = (limit = 6, enabled = true) =>
  useQuery({
    queryKey: qk.recommended(limit),
    queryFn: () => api.get<RecommendedCampaign[]>('/campaigns/recommended', { query: { limit } }),
    enabled,
  });

export const useCategories = () =>
  useQuery({
    queryKey: qk.categories,
    queryFn: () => api.get<CampaignCategory[]>('/campaign-categories'),
    staleTime: 10 * 60_000,
  });

export const useCampaign = (slug: string) =>
  useQuery({
    queryKey: qk.campaign(slug),
    queryFn: () => api.get<CampaignDetail>(`/campaigns/${encodeURIComponent(slug)}`),
  });

export const useExperimentVariants = (campaignId: string | undefined) =>
  useQuery({
    queryKey: qk.variants(campaignId ?? ''),
    queryFn: () => api.get<ExperimentVariant[]>(`/campaigns/${campaignId}/experiment-variants`),
    enabled: !!campaignId,
    // Assignments are sticky server-side; no need to refetch.
    staleTime: Infinity,
  });

/**
 * The caller's tracking link for a campaign. The API creates it on first request (idempotent) and answers
 * `409 tracking.not_enabled` when the campaign has no tracking destination — that resolves to `null` here so the UI
 * can simply hide the section.
 */
export const useTrackingLink = (campaignId: string | undefined, enabled: boolean) =>
  useQuery({
    queryKey: qk.trackingLink(campaignId ?? ''),
    queryFn: async () => {
      try {
        return await api.post<TrackingLink>(`/me/campaigns/${campaignId}/tracking-link`);
      } catch (error) {
        if (isApiError(error) && error.code === 'tracking.not_enabled') return null;
        throw error;
      }
    },
    enabled: !!campaignId && enabled,
    staleTime: 5 * 60_000,
  });

// ---------------------------------------------------------------- submissions

export interface SubmissionListParams {
  status?: string;
  campaignId?: string;
  page: number;
  pageSize: number;
}

export const useSubmissions = (params: SubmissionListParams) =>
  useQuery({
    queryKey: qk.submissionList(params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<SubmissionListItem>>('/me/submissions', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  });

export const useSubmission = (id: string) =>
  useQuery({
    queryKey: qk.submission(id),
    queryFn: () => api.get<SubmissionDetail>(`/me/submissions/${id}`),
  });

/** Everything that depends on submissions: lists, detail, campaigns (remaining counts), home state, balances. */
export function invalidateAfterSubmission(client: QueryClient) {
  return Promise.all([
    client.invalidateQueries({ queryKey: qk.submissions }),
    client.invalidateQueries({ queryKey: qk.campaigns }),
    client.invalidateQueries({ queryKey: qk.home }),
    client.invalidateQueries({ queryKey: qk.earnings }),
    client.invalidateQueries({ queryKey: qk.achievements }),
  ]);
}

// ---------------------------------------------------------------- earnings & payouts

export const useEarningsSummary = (enabled = true) =>
  useQuery({
    queryKey: qk.earningsSummary,
    queryFn: () => api.get<EarningsSummary>('/me/earnings/summary'),
    enabled,
  });

export interface EarningListParams {
  type?: string;
  status?: string;
  campaignId?: string;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}

export const useEarnings = (params: EarningListParams) =>
  useQuery({
    queryKey: qk.earningList(params),
    queryFn: ({ signal }) => api.get<PagedResult<Earning>>('/me/earnings', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  });

export const usePayouts = (page: number) =>
  useQuery({
    queryKey: qk.payoutList(page),
    queryFn: () => api.get<PagedResult<MyPayout>>('/me/payouts', { query: { page, pageSize: 25 } }),
    placeholderData: keepPreviousData,
  });

export const usePayout = (id: string) =>
  useQuery({ queryKey: qk.payout(id), queryFn: () => api.get<MyPayoutDetail>(`/me/payouts/${id}`) });

// ---------------------------------------------------------------- marketing

export const useReferrals = () =>
  useQuery({ queryKey: qk.referrals, queryFn: () => api.get<Referrals>('/me/referrals') });

export const useAchievements = () =>
  useQuery({ queryKey: qk.achievements, queryFn: () => api.get<Achievement[]>('/me/achievements') });

// ---------------------------------------------------------------- notifications

export const useNotifications = (params: { unreadOnly: boolean; page: number; pageSize: number }) =>
  useQuery({
    queryKey: qk.notificationList(params),
    queryFn: () => api.get<PagedResult<NotificationItem>>('/me/notifications', { query: { ...params } }),
    placeholderData: keepPreviousData,
  });

export const useUnreadCount = () =>
  useQuery({
    queryKey: qk.unreadCount,
    queryFn: () => api.get<{ count: number }>('/me/notifications/unread-count'),
  });

export const useNotificationPreferences = () =>
  useQuery({
    queryKey: qk.notificationPreferences,
    queryFn: () => api.get<NotificationPreferences>('/me/notification-preferences'),
  });

// ---------------------------------------------------------------- support

export const useTickets = (params: { status?: string; page: number }) =>
  useQuery({
    queryKey: qk.ticketList(params),
    queryFn: () =>
      api.get<PagedResult<TicketSummary>>('/me/support/tickets', { query: { ...params, pageSize: 20 } }),
    placeholderData: keepPreviousData,
  });

export const useTicket = (id: string) =>
  useQuery({ queryKey: qk.ticket(id), queryFn: () => api.get<Ticket>(`/me/support/tickets/${id}`) });

export { useQueryClient };
