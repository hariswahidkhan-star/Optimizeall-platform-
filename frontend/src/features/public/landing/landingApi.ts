import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { IsoDateTime } from '@/lib/api/types';

export interface RewardTeaser {
  currency: string;
  baseAmount: number;
}

export interface LandingCategory {
  name: string;
  slug: string;
}

export interface LandingExperiment {
  experimentId: string;
  variantId: string;
  key: string;
}

/** `GET /public/invitations/{code}` (anonymous). 404 when the link is inactive, expired or used up. */
export interface PublicInvitationLanding {
  code: string;
  type: 'campaign' | 'platform';
  headline: string;
  body: string;
  heroImageUrl: string | null;
  campaign: {
    slug: string;
    title: string;
    summary: string;
    platforms: string[];
    reward: RewardTeaser | null;
    startsAt: IsoDateTime;
    endsAt: IsoDateTime;
    category: LandingCategory | null;
  } | null;
  utm: { source: string | null; medium: string | null; campaign: string | null } | null;
  experiment: LandingExperiment | null;
}

/** `GET /public/campaigns/{slug}` (anonymous). 404 unless the campaign is Public and Scheduled/Active. */
export interface PublicCampaignLanding {
  slug: string;
  title: string;
  summary: string;
  headline: string;
  body: string;
  heroImageUrl: string | null;
  platforms: string[];
  reward: RewardTeaser | null;
  startsAt: IsoDateTime;
  endsAt: IsoDateTime;
  submissionDeadline: IsoDateTime;
  category: LandingCategory | null;
  assets: { id: string; title: string; url: string }[];
  disclosure: string;
  experiment: LandingExperiment | null;
}

const VISITOR_KEY = 'oa.visitorId';
let visitorFallback: string | null = null;

function randomId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

/**
 * A random anonymous visitor id (localStorage `oa.visitorId`) sent as `X-Visitor-Id` so landing-page A/B variants
 * stay stable for a browser (the API only stores its hash). Falls back to a per-tab value when storage is blocked.
 */
export function getVisitorId(): string {
  try {
    const existing = window.localStorage.getItem(VISITOR_KEY);
    if (existing) return existing;
    const id = randomId();
    window.localStorage.setItem(VISITOR_KEY, id);
    return id;
  } catch {
    visitorFallback ??= randomId();
    return visitorFallback;
  }
}

export const landingKeys = {
  invitation: (code: string) => ['public', 'invitation', code] as const,
  campaign: (slug: string) => ['public', 'campaign', slug] as const,
};

export function useInvitationLanding(code: string) {
  return useQuery({
    queryKey: landingKeys.invitation(code),
    queryFn: ({ signal }) =>
      api.get<PublicInvitationLanding>(`/public/invitations/${encodeURIComponent(code)}`, {
        headers: { 'X-Visitor-Id': getVisitorId() },
        signal,
      }),
    // Each fetch counts a landing visit: never refetch in the background.
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

export function useCampaignLanding(slug: string) {
  return useQuery({
    queryKey: landingKeys.campaign(slug),
    queryFn: ({ signal }) =>
      api.get<PublicCampaignLanding>(`/public/campaigns/${encodeURIComponent(slug)}`, {
        headers: { 'X-Visitor-Id': getVisitorId() },
        signal,
      }),
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}
