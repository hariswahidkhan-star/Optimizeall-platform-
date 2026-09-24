import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { IsoDateTime, PagedResult } from '@/lib/api/types';

/** Mirrors `Modules/Seo` DTOs (camelCase JSON, string enums). */

export type SeoSeverity = 'Error' | 'Warning' | 'Notice';
export type AuditStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
export type KeywordIntent = 'Unknown' | 'Informational' | 'Navigational' | 'Commercial' | 'Transactional';
export type BacklinkStatus = 'Unchecked' | 'Live' | 'Nofollow' | 'Lost' | 'Error';
export type OutreachStatus = 'Identified' | 'Contacted' | 'FollowedUp' | 'Replied' | 'Won' | 'Lost';
export type CitationStatus = 'NotStarted' | 'Submitted' | 'Live' | 'NeedsUpdate' | 'Rejected';
export type BriefStatus = 'Draft' | 'Ready' | 'HandedOff' | 'Published';
export type IssueStatus = 'Open' | 'Fixed' | 'Ignored';

export interface Site {
  id: string;
  clientAccountId: string;
  clientName: string;
  name: string;
  domain: string;
  protocol: 'http' | 'https';
  baseUrl: string;
  sitemapUrl: string | null;
  targetCountry: string;
  targetLanguage: string;
  competitors: string[];
  maxPages: number;
  maxDepth: number;
  isArchived: boolean;
  healthScore: number | null;
  lastAuditAt: IsoDateTime | null;
  lastAuditStatus: AuditStatus | null;
  keywordCount: number;
  concurrencyStamp: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface SiteInput {
  clientAccountId: string;
  name: string;
  domain: string;
  protocol?: string | null;
  sitemapUrl?: string | null;
  targetCountry: string;
  targetLanguage: string;
  competitors: string[];
  maxPages?: number | null;
  maxDepth?: number | null;
  concurrencyStamp?: string;
}

export interface AuditSummary {
  id: string;
  siteId: string;
  status: AuditStatus;
  queuedAt: IsoDateTime;
  startedAt: IsoDateTime | null;
  finishedAt: IsoDateTime | null;
  pagesCrawled: number;
  healthScore: number | null;
  errorCount: number;
  warningCount: number;
  noticeCount: number;
  robotsTxtFound: boolean;
  sitemapFound: boolean;
  failureMessage: string | null;
  maxPages: number;
  maxDepth: number;
}

export interface IssueHit {
  url: string;
  detail: string | null;
}

export interface AuditIssue {
  ruleKey: string;
  title: string;
  category: string;
  severity: SeoSeverity;
  whyItMatters: string;
  howToFix: string;
  affectedCount: number;
  hits: IssueHit[];
  /** Triage state (absent on older payloads = Open). */
  status?: IssueStatus;
  statusNote?: string | null;
  statusChangedAt?: IsoDateTime | null;
}

/** Agency-wide audit rule settings (`GET /agency/seo/rules`). */
export interface AuditRule {
  key: string;
  title: string;
  category: string;
  severity: SeoSeverity;
  defaultSeverity: SeoSeverity;
  whyItMatters: string;
  howToFix: string;
  isEnabled: boolean;
  isCustomized: boolean;
  concurrencyStamp: string;
}

/** Local-SEO directory (`GET /agency/seo/citation-sources`). */
export interface CitationSource {
  id: string;
  key: string;
  name: string;
  url: string;
  category: string;
  countries: string[];
  sortOrder: number;
  isActive: boolean;
  isCustom: boolean;
  citationCount: number;
  concurrencyStamp: string;
}

export interface AuditDetail {
  audit: AuditSummary;
  siteName: string;
  siteBaseUrl: string;
  previousAuditId: string | null;
  issues: AuditIssue[];
}

export interface DiffIssue {
  ruleKey: string;
  title: string;
  severity: SeoSeverity;
  urls: string[];
}

export interface AuditDiff {
  auditId: string;
  againstAuditId: string | null;
  healthScoreChange: number | null;
  newCount: number;
  fixedCount: number;
  newIssues: DiffIssue[];
  fixedIssues: DiffIssue[];
}

export interface AuditPage {
  id: string;
  url: string;
  statusCode: number | null;
  depth: number;
  responseTimeMs: number;
  contentLength: number;
  title: string | null;
  metaDescription: string | null;
  h1Count: number;
  wordCount: number;
  canonical: string | null;
  isNoindex: boolean;
  inSitemap: boolean;
  inboundLinks: number;
  redirectChain: string | null;
  fetchError: string | null;
}

export interface Keyword {
  id: string;
  siteId: string;
  keyword: string;
  intent: KeywordIntent;
  searchVolume: number | null;
  difficulty: number | null;
  targetUrl: string | null;
  tags: string[];
  isTracked: boolean;
  position: number | null;
  previousPosition: number | null;
  change: number | null;
  bestPosition: number | null;
  rankingUrl: string | null;
  lastCheckedOn: string | null;
  serpFeatures: string[];
  concurrencyStamp: string;
}

export interface RankPoint {
  date: string;
  domain: string;
  position: number | null;
  url: string | null;
  serpFeatures: string[];
  source: string;
}

export interface RankingsOverview {
  from: string;
  to: string;
  trackedKeywords: number;
  distribution: { top3: number; top10: number; top20: number; top100: number; notRanking: number };
  averagePosition: number | null;
  averagePositionChange: number | null;
  trend: { date: string; averagePosition: number | null; top10: number; ranked: number }[];
  winners: Mover[];
  losers: Mover[];
  shareOfVoice: { domain: string; visibility: number; share: number }[];
  serpFeatures: Record<string, number>;
  providerStatus: string;
}

export interface Mover {
  keywordId: string;
  keyword: string;
  previous: number | null;
  current: number | null;
  change: number;
}

export interface SearchPerformance {
  from: string;
  to: string;
  clicks: number;
  impressions: number;
  ctr: number | null;
  averagePosition: number | null;
  daily: { date: string; clicks: number; impressions: number }[];
  topQueries: { query: string; clicks: number; impressions: number; ctr: number; position: number }[];
}

export interface ImportResult {
  rowsRead: number;
  created: number;
  updated: number;
  unchanged: number;
  keywordsCreated: number;
  errors: string[];
}

export interface ProviderRun {
  outcome: 'Ok' | 'NotConfigured' | 'Error';
  message: string | null;
  checked: number;
}

export interface Backlink {
  id: string;
  siteId: string;
  sourceUrl: string;
  sourceDomain: string;
  targetUrl: string;
  anchorText: string | null;
  rel: string | null;
  firstSeenAt: IsoDateTime;
  lastCheckedAt: IsoDateTime | null;
  status: BacklinkStatus;
  lastStatusCode: number | null;
  checkMessage: string | null;
}

export interface BacklinkList {
  page: PagedResult<Backlink>;
  summary: { total: number; live: number; nofollow: number; lost: number; error: number; unchecked: number; referringDomains: number };
}

export interface Outreach {
  id: string;
  siteId: string;
  prospectUrl: string;
  contactName: string | null;
  contactEmail: string | null;
  status: OutreachStatus;
  notes: string | null;
  lastContactedAt: IsoDateTime | null;
  concurrencyStamp: string;
  updatedAt: IsoDateTime;
}

export interface Citation {
  sourceId: string;
  sourceKey: string;
  name: string;
  url: string;
  category: string;
  countries: string[];
  status: CitationStatus;
  listingUrl: string | null;
  listedName: string | null;
  listedAddress: string | null;
  listedPhone: string | null;
  notes: string | null;
  nameMatches: boolean | null;
  addressMatches: boolean | null;
  phoneMatches: boolean | null;
  consistent: boolean;
  updatedAt: IsoDateTime | null;
}

export interface Review {
  id: string;
  platform: string;
  rating: number;
  authorName: string | null;
  text: string | null;
  reviewedAt: IsoDateTime;
  responded: boolean;
  responseText: string | null;
}

export interface LocalSeo {
  siteId: string;
  profile: { businessName: string; address: string | null; phone: string | null; website: string | null; concurrencyStamp: string } | null;
  checklist: { key: string; title: string; guidance: string; done: boolean }[];
  checklistDone: number;
  citations: Citation[];
  citationsLive: number;
  citationsInconsistent: number;
  reviews: { count: number; averageRating: number | null; responseRate: number | null; byRating: Record<string, number> };
}

export interface Brief {
  id: string;
  siteId: string;
  title: string;
  targetKeyword: string;
  relatedKeywords: string[];
  questions: string[];
  outline: string[];
  wordCountTarget: number;
  competitorUrls: string[];
  notes: string | null;
  status: BriefStatus;
  concurrencyStamp: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface OnPageResult {
  keyword: string;
  url: string | null;
  score: number;
  wordCount: number;
  keywordOccurrences: number;
  keywordDensity: number;
  fleschReadingEase: number | null;
  readabilityLabel: string | null;
  readabilityNote: string;
  title: string | null;
  metaDescription: string | null;
  h1: string[];
  headings: { level: number; text: string }[];
  internalLinks: number;
  externalLinks: number;
  images: number;
  imagesMissingAlt: number;
  schemaTypesFound: string[];
  schemaSuggestions: string[];
  checklist: { key: string; label: string; passed: boolean; weight: number; detail: string }[];
}

export interface ClientSeoKpis {
  clientAccountId: string;
  clientName: string;
  from: string;
  to: string;
  sites: SiteKpi[];
  leads: LeadStats;
}

export interface SiteKpi {
  siteId: string;
  name: string;
  domain: string;
  healthScore: number | null;
  healthScoreChange: number | null;
  lastAuditAt: IsoDateTime | null;
  trackedKeywords: number;
  top3: number;
  top10: number;
  averagePosition: number | null;
  averagePositionChange: number | null;
  clicks: number;
  impressions: number;
  liveBacklinks: number;
  lostBacklinks: number;
  shareOfVoice: number | null;
}

export interface LeadStats {
  views: number;
  submissions: number;
  conversionRate: number | null;
  pages: { pageId: string; name: string; slug: string; views: number; submissions: number; conversionRate: number | null }[];
  daily: { date: string; submissions: number }[];
}

export const seoKeys = {
  all: ['agency', 'seo'] as const,
  sites: (params: object) => ['agency', 'seo', 'sites', params] as const,
  site: (id: string) => ['agency', 'seo', 'site', id] as const,
  audits: (siteId: string) => ['agency', 'seo', 'site', siteId, 'audits'] as const,
  audit: (id: string) => ['agency', 'seo', 'audit', id] as const,
  keywords: (siteId: string) => ['agency', 'seo', 'site', siteId, 'keywords'] as const,
  rankings: (siteId: string) => ['agency', 'seo', 'site', siteId, 'rankings'] as const,
  search: (siteId: string) => ['agency', 'seo', 'site', siteId, 'search-console'] as const,
  backlinks: (siteId: string, params: object) => ['agency', 'seo', 'site', siteId, 'backlinks', params] as const,
  outreach: (siteId: string) => ['agency', 'seo', 'site', siteId, 'outreach'] as const,
  local: (siteId: string) => ['agency', 'seo', 'site', siteId, 'local'] as const,
  briefs: (siteId: string) => ['agency', 'seo', 'site', siteId, 'briefs'] as const,
  rules: ['agency', 'seo', 'rules'] as const,
  sources: ['agency', 'seo', 'citation-sources'] as const,
};

export function useSite(id: string) {
  return useQuery({ queryKey: seoKeys.site(id), queryFn: () => api.get<Site>(`/agency/seo/sites/${id}`) });
}

export function useAuditHistory(siteId: string) {
  return useQuery({
    queryKey: seoKeys.audits(siteId),
    queryFn: () => api.get<PagedResult<AuditSummary>>(`/agency/seo/sites/${siteId}/audits`, { query: { pageSize: 20 } }),
    // Keep polling while an audit is queued or running.
    refetchInterval: (query) =>
      query.state.data?.items.some((a) => a.status === 'Queued' || a.status === 'Running') ? 5000 : false,
  });
}

export const severityOrder: SeoSeverity[] = ['Error', 'Warning', 'Notice'];

export const severityTone: Record<SeoSeverity, 'danger' | 'warning' | 'info'> = {
  Error: 'danger',
  Warning: 'warning',
  Notice: 'info',
};
