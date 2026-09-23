import { useQuery } from '@tanstack/react-query';
import { api } from './client';

export type CampaignOptionStatus =
  'Draft' | 'Scheduled' | 'Active' | 'Paused' | 'Ended' | 'Archived' | (string & {});

/** An entry of `GET /campaigns/options` (permission `campaigns.view`): newest first, at most 500. */
export interface CampaignOption {
  id: string;
  title: string;
  status: CampaignOptionStatus;
}

export const campaignOptionKeys = {
  all: ['campaign-options'] as const,
  list: (search: string) => ['campaign-options', search] as const,
};

export const fetchCampaignOptions = (search?: string, signal?: AbortSignal) =>
  api.get<CampaignOption[]>('/campaigns/options', { query: { search: search?.trim() || undefined }, signal });

/**
 * Campaign id/title/status list for staff filters and pickers. `search` narrows by title or slug on the server
 * (useful beyond the 500 newest campaigns).
 */
export function useCampaignOptions(search = '', options: { enabled?: boolean } = {}) {
  const term = search.trim();
  return useQuery({
    queryKey: campaignOptionKeys.list(term),
    queryFn: ({ signal }) => fetchCampaignOptions(term, signal),
    staleTime: 5 * 60_000,
    enabled: options.enabled ?? true,
  });
}
