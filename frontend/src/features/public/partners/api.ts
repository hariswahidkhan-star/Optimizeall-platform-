import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { JsonLd, PublicSeo } from '../site/api';
import type { PartnerLinkRule } from './partnerLinks';
import type { PartnerUnitSlot } from './slots';

/** Types and hooks of the public partner API (`/api/v1/public/partners…`). See docs/api/website.md. */

export interface PartnerOffer {
  text: string;
  code: string | null;
  expiresAt: string | null;
}

export interface PartnerCard {
  slug: string;
  name: string;
  logoUrl: string;
  tagline: string;
  relationshipLabel: string;
  brandColor: string | null;
  /** Null while the partner has no website: every outbound link is then hidden. */
  websiteHost: string | null;
  /** The click counter (`/api/v1/public/partners/{slug}/visit`), which redirects to the partner with UTM tags. */
  visitUrl: string | null;
  profilePath: string;
  slots: string[];
  offer: PartnerOffer | null;
}

export interface PartnerOffering {
  title: string;
  summary: string | null;
  facts: string[];
  anchor: string | null;
  link: string | null;
}

export interface PartnerDirectory {
  partners: PartnerCard[];
  linkRules: PartnerLinkRule[];
  seo: PublicSeo;
  jsonLd: JsonLd[];
}

export interface PartnerProfile extends Omit<PartnerCard, 'profilePath' | 'slots'> {
  descriptionMarkdown: string | null;
  highlights: string[];
  offerings: PartnerOffering[];
  keywords: string[];
  related: PartnerCard[];
  updatedAt: string;
  seo: PublicSeo;
  jsonLd: JsonLd[];
}

export interface PartnerPlacement {
  slot: string;
  partner: PartnerCard | null;
}

const fiveMinutes = 5 * 60_000;

export const partnerKeys = {
  directory: ['public', 'partners'] as const,
  profile: (slug: string) => ['public', 'partners', 'profile', slug] as const,
  placement: (slot: string, keywords: string[], categories: string[], path: string) =>
    ['public', 'partners', 'placement', slot, keywords, categories, path] as const,
};

/** Active partners (partners page, home strip, footer line) and the partner-link rules for editorial content. */
export const usePartners = () =>
  useQuery({
    queryKey: partnerKeys.directory,
    queryFn: () => api.get<PartnerDirectory>('/public/partners'),
    staleTime: fiveMinutes,
    retry: false,
  });

export const usePartner = (slug: string) =>
  useQuery({
    queryKey: partnerKeys.profile(slug),
    queryFn: () => api.get<PartnerProfile>(`/public/partners/${encodeURIComponent(slug)}`),
    enabled: !!slug,
  });

/** The ad unit for a slot on this page (at most one partner). */
export const usePartnerPlacement = (slot: PartnerUnitSlot, keywords: string[], categories: string[], path: string) =>
  useQuery({
    queryKey: partnerKeys.placement(slot, keywords, categories, path),
    queryFn: () =>
      api.get<PartnerPlacement>('/public/partners/placement', {
        query: { slot, keywords: keywords.slice(0, 30), categories: categories.slice(0, 30), path },
      }),
    staleTime: fiveMinutes,
    retry: false,
  });
