import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import type {
  AdminCampaign,
  AdminCampaignListItem,
  AdminCategory,
  PostTemplate,
  RewardRuleSet,
  SettingEntry,
} from './types';

/** Query keys for the manager portal (all under 'manage' so a mutation can invalidate broadly). */
export const qk = {
  all: ['manage'] as const,
  campaigns: (params?: object) => ['manage', 'campaigns', params ?? {}] as const,
  campaignOptions: () => ['manage', 'campaigns', 'options'] as const,
  campaign: (id: string) => ['manage', 'campaign', id] as const,
  rewardRules: (id: string) => ['manage', 'campaign', id, 'reward-rules'] as const,
  categories: () => ['manage', 'categories'] as const,
  templates: (params?: object) => ['manage', 'templates', params ?? {}] as const,
  calendar: (params?: object) => ['manage', 'calendar', params ?? {}] as const,
  invitations: (params?: object) => ['manage', 'invitations', params ?? {}] as const,
  experiments: (params?: object) => ['manage', 'experiments', params ?? {}] as const,
  experiment: (id: string) => ['manage', 'experiment', id] as const,
  experimentResults: (id: string) => ['manage', 'experiment', id, 'results'] as const,
  referrals: (params?: object) => ['manage', 'referrals', params ?? {}] as const,
  achievements: () => ['manage', 'achievements'] as const,
  analytics: (params?: object) => ['manage', 'analytics', params ?? {}] as const,
  campaignAnalytics: (id: string, params?: object) =>
    ['manage', 'analytics', 'campaign', id, params ?? {}] as const,
  retention: (params?: object) => ['manage', 'retention', params ?? {}] as const,
  tracking: (params?: object) => ['manage', 'tracking', params ?? {}] as const,
  settings: () => ['manage', 'settings'] as const,
};

export function fetchCampaign(id: string) {
  return api.get<AdminCampaign>(`/admin/campaigns/${id}`);
}

export function useCampaign(id: string | undefined) {
  return useQuery({
    queryKey: qk.campaign(id ?? 'new'),
    queryFn: () => fetchCampaign(id!),
    enabled: !!id,
  });
}

export function useRewardRuleVersions(id: string | undefined) {
  return useQuery({
    queryKey: qk.rewardRules(id ?? 'new'),
    queryFn: () => api.get<RewardRuleSet[]>(`/admin/campaigns/${id}/reward-rules`),
    enabled: !!id,
  });
}

/** Campaigns for pickers (title lookups, filters). Newest first, at most 200. */
export function useCampaignOptions() {
  return useQuery({
    queryKey: qk.campaignOptions(),
    queryFn: () =>
      api.get<PagedResult<AdminCampaignListItem>>('/admin/campaigns', {
        query: { pageSize: 200, sort: 'newest' },
      }),
    staleTime: 60_000,
  });
}

export function useCategories() {
  return useQuery({
    queryKey: qk.categories(),
    queryFn: () => api.get<AdminCategory[]>('/admin/campaign-categories'),
    staleTime: 5 * 60_000,
  });
}

/** Active templates for pickers (requires marketing.manage; disabled otherwise). */
export function useTemplateOptions(enabled = true) {
  const { hasPermission } = useAuth();
  const allowed = hasPermission(Permissions.MarketingManage);
  return useQuery({
    queryKey: qk.templates({ picker: true }),
    queryFn: () =>
      api.get<PagedResult<PostTemplate>>('/marketing/templates', {
        query: { pageSize: 200, sort: 'name' },
      }),
    enabled: enabled && allowed,
    staleTime: 60_000,
  });
}

const MIN_ACCOUNT_AGE_KEY = 'eligibility.minAccountAgeDays';
/** Backend default for `eligibility.minAccountAgeDays` (SettingKeys doc comment). */
export const DOCUMENTED_MIN_ACCOUNT_AGE_DAYS = 90;

/**
 * The global minimum account age. Only readable through `/admin/settings` (settings.manage); other users see the
 * documented default with a note that an admin may have changed it.
 */
export function useGlobalMinAccountAge(): { days: number; exact: boolean } {
  const { hasPermission } = useAuth();
  const canRead = hasPermission(Permissions.SettingsManage);
  const query = useQuery({
    queryKey: qk.settings(),
    queryFn: () => api.get<SettingEntry[]>('/admin/settings'),
    enabled: canRead,
    staleTime: 5 * 60_000,
  });
  const entry = query.data?.find((s) => s.key === MIN_ACCOUNT_AGE_KEY);
  const value = Number(entry?.value);
  if (entry && Number.isFinite(value)) return { days: value, exact: true };
  return { days: DOCUMENTED_MIN_ACCOUNT_AGE_DAYS, exact: false };
}
