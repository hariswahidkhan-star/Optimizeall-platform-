import type { CampaignFilters, CampaignSort } from '../api/types';

const SORTS: CampaignSort[] = ['deadline', 'reward', 'newest'];

/** URL ⇄ filter state. Short, readable keys so a filtered list can be bookmarked and shared. */
export function filtersFromSearch(params: URLSearchParams): CampaignFilters {
  const page = Number(params.get('page') ?? '1');
  const sort = params.get('sort') as CampaignSort | null;
  const clean = (key: string) => {
    const value = params.get(key)?.trim();
    return value ? value : undefined;
  };
  return {
    platform: clean('platform'),
    categoryId: clean('category'),
    topic: clean('topic'),
    minReward: clean('minReward'),
    deadlineBefore: clean('deadlineBefore'),
    eligibleOnly: params.get('eligible') === '1',
    search: clean('q'),
    sort: sort && SORTS.includes(sort) ? sort : undefined,
    page: Number.isFinite(page) && page >= 1 ? Math.floor(page) : 1,
  };
}

export function searchFromFilters(filters: CampaignFilters): URLSearchParams {
  const params = new URLSearchParams();
  const set = (key: string, value: string | undefined) => {
    if (value) params.set(key, value);
  };
  set('q', filters.search);
  set('platform', filters.platform);
  set('category', filters.categoryId);
  set('topic', filters.topic);
  set('minReward', filters.minReward);
  set('deadlineBefore', filters.deadlineBefore);
  if (filters.eligibleOnly) params.set('eligible', '1');
  set('sort', filters.sort);
  if (filters.page > 1) params.set('page', String(filters.page));
  return params;
}

export function activeFilterCount(filters: CampaignFilters): number {
  return [
    filters.search,
    filters.platform,
    filters.categoryId,
    filters.topic,
    filters.minReward,
    filters.deadlineBefore,
    filters.eligibleOnly ? '1' : undefined,
  ].filter(Boolean).length;
}
