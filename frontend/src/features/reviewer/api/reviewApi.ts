import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import type {
  AppealDetail,
  AppealListItem,
  AppealResolution,
  AssignResult,
  Claim,
  DecisionRequest,
  DecisionResult,
  LiveCheckItem,
  LiveCheckResultDto,
  ReverseResult,
  ReviewDetail,
  ReviewQueueItem,
  Reviewer,
  ReviewSocialAccount,
  ReviewSocialAccountDetail,
  ReviewStats,
  SocialDecisionRequest,
} from './types';

/** Filters of GET /review/queue, as kept in the queue page URL. */
export interface QueueFilters {
  status?: string;
  campaignId?: string;
  platform?: string;
  minRisk?: string;
  flagged?: string;
  assignedToMe?: boolean;
  claimedByMe?: boolean;
  sort?: 'oldest' | 'risk';
  page?: number;
  pageSize?: number;
}

export interface SocialQueueFilters {
  status?: string;
  platform?: string;
  search?: string;
  oldestFirst?: boolean;
  page?: number;
}

/** React Query keys. Everything under `['review']` is refreshed after a decision. */
export const reviewKeys = {
  all: ['review'] as const,
  stats: () => ['review', 'stats'] as const,
  queue: (filters: QueueFilters) => ['review', 'queue', filters] as const,
  queueRoot: () => ['review', 'queue'] as const,
  detail: (id: string) => ['review', 'submission', id] as const,
  liveChecks: (due: boolean, page: number) => ['review', 'live-checks', { due, page }] as const,
  appeals: (status: string, page: number) => ['review', 'appeals', { status, page }] as const,
  appeal: (id: string) => ['review', 'appeal', id] as const,
  reviewers: () => ['review', 'reviewers'] as const,
  social: (filters: SocialQueueFilters) => ['review', 'social', filters] as const,
  socialRoot: () => ['review', 'social'] as const,
  socialDetail: (id: string) => ['review', 'social-account', id] as const,
};

export const reviewApi = {
  stats: (signal?: AbortSignal) => api.get<ReviewStats>('/review/stats', { signal }),

  queue: (filters: QueueFilters, signal?: AbortSignal) =>
    api.get<PagedResult<ReviewQueueItem>>('/review/queue', {
      signal,
      query: {
        status: filters.status,
        campaignId: filters.campaignId,
        platform: filters.platform,
        minRisk: filters.minRisk,
        flagged: filters.flagged,
        assignedToMe: filters.assignedToMe || undefined,
        claimedByMe: filters.claimedByMe || undefined,
        sort: filters.sort,
        page: filters.page,
        pageSize: filters.pageSize,
      },
    }),

  claim: (id: string) => api.post<Claim>(`/review/submissions/${id}/claim`),
  release: (id: string) => api.post<void>(`/review/submissions/${id}/release`),
  detail: (id: string, signal?: AbortSignal) =>
    api.get<ReviewDetail>(`/review/submissions/${id}`, { signal }),
  decide: (id: string, body: DecisionRequest) =>
    api.post<DecisionResult>(`/review/submissions/${id}/decision`, body),

  liveChecks: (due: boolean, page: number, signal?: AbortSignal) =>
    api.get<PagedResult<LiveCheckItem>>('/review/live-checks', {
      signal,
      query: { due, page, pageSize: 25 },
    }),
  liveCheck: (id: string, body: { result: 'ConfirmedLive' | 'Removed'; note?: string }) =>
    api.post<LiveCheckResultDto>(`/review/submissions/${id}/live-check`, body),

  reverse: (id: string, reason: string) =>
    api.post<ReverseResult>(`/review/submissions/${id}/reverse`, { reason, confirm: true }),

  appeals: (status: string, page: number, signal?: AbortSignal) =>
    api.get<PagedResult<AppealListItem>>('/review/appeals', {
      signal,
      query: { status, page, pageSize: 25 },
    }),
  appeal: (id: string, signal?: AbortSignal) => api.get<AppealDetail>(`/review/appeals/${id}`, { signal }),
  resolveAppeal: (
    id: string,
    body: { outcome: 'Upheld' | 'Overturned'; note: string; concurrencyStamp: string },
  ) => api.post<AppealResolution>(`/review/appeals/${id}/resolve`, body),

  reviewers: (signal?: AbortSignal) => api.get<Reviewer[]>('/review/reviewers', { signal }),
  assign: (submissionIds: string[], reviewerId: string) =>
    api.post<AssignResult>('/review/assign', { submissionIds, reviewerId }),

  socialAccounts: (filters: SocialQueueFilters, signal?: AbortSignal) =>
    api.get<PagedResult<ReviewSocialAccount>>('/review/social-accounts', {
      signal,
      query: {
        status: filters.status,
        platform: filters.platform,
        search: filters.search,
        desc: filters.oldestFirst ? false : true,
        page: filters.page,
        pageSize: 25,
      },
    }),
  socialAccount: (id: string, signal?: AbortSignal) =>
    api.get<ReviewSocialAccountDetail>(`/review/social-accounts/${id}`, { signal }),
  socialDecision: (id: string, body: SocialDecisionRequest) =>
    api.post<ReviewSocialAccountDetail>(`/review/social-accounts/${id}/decision`, body),
};
