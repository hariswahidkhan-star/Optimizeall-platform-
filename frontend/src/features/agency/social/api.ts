import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { IsoDateTime, PagedResult } from '@/lib/api/types';

/** Mirrors backend Modules/SocialMedia DTOs (camelCase JSON, string enums, UTC timestamps). */

export const NETWORKS = [
  'Facebook',
  'Instagram',
  'X',
  'LinkedIn',
  'TikTok',
  'YouTube',
  'Pinterest',
  'GoogleBusiness',
] as const;
export type SocialNetwork = (typeof NETWORKS)[number];

export const POST_STATUSES = [
  'Draft',
  'InternalReview',
  'ClientApproval',
  'Approved',
  'Scheduled',
  'Publishing',
  'Published',
  'Failed',
] as const;
export type PostStatus = (typeof POST_STATUSES)[number];

export type VariantPublishStatus = 'Pending' | 'Publishing' | 'Published' | 'Failed';
export type PublishFailureKind =
  | 'None'
  | 'NotConfigured'
  | 'NotSupported'
  | 'Authorization'
  | 'Rejected'
  | 'Transient'
  | 'Unknown';
export type LinkHandling = 'Attachment' | 'InText' | 'NotClickable' | 'Destination';
export type MediaKind = 'Image' | 'Video';
export type Sentiment = 'Positive' | 'Neutral' | 'Negative';

export interface ClientOption {
  id: string;
  name: string;
  slug: string;
  countryCode: string;
  currency: string;
  timeZone: string;
}

export interface Preset {
  network: SocialNetwork;
  label: string;
  maxTextLength: number;
  maxTitleLength: number | null;
  requiresTitle: boolean;
  maxHashtags: number;
  recommendedHashtags: number | null;
  maxMentions: number;
  maxMedia: number;
  maxVideos: number;
  requiresMedia: boolean;
  requiresVideo: boolean;
  allowsMixedMedia: boolean;
  minAspectRatio: number | null;
  maxAspectRatio: number | null;
  minVideoSeconds?: number | null;
  maxVideoSeconds?: number | null;
  maxImageBytes?: number | null;
  maxAltTextLength: number;
  supportsFirstComment: boolean;
  linkHandling: LinkHandling;
  urlWeight: number | null;
  recommendedTimes: string[];
  source: string;
}

export interface ValidationIssue {
  network: SocialNetwork;
  field: string;
  severity: 'Error' | 'Warning';
  code: string;
  message: string;
}

export interface VariantValidation {
  network: SocialNetwork;
  finalText: string;
  textLength: number;
  maxTextLength: number;
  titleLength: number | null;
  maxTitleLength: number | null;
  hashtagCount: number;
  mentionCount: number;
  mediaCount: number;
  issues: ValidationIssue[];
  isValid: boolean;
}

export interface Variant {
  id: string;
  profileId: string;
  profileHandle: string;
  profileName: string;
  network: SocialNetwork;
  text: string;
  title: string | null;
  mediaIds: string[];
  altTexts: string[];
  link: string | null;
  effectiveLink: string | null;
  firstComment: string | null;
  hashtags: string[];
  mentions: string[];
  publishStatus: VariantPublishStatus;
  attempts: number;
  nextAttemptAt: IsoDateTime | null;
  failureKind: PublishFailureKind;
  failureReason: string | null;
  externalPostId: string | null;
  publishedUrl: string | null;
  publishedAt: IsoDateTime | null;
  publishedManually: boolean;
  validation: VariantValidation;
}

export interface PostComment {
  id: string;
  authorName: string;
  isClient: boolean;
  isInternal: boolean;
  kind: string;
  body: string;
  createdAt: IsoDateTime;
  authorUserId?: string | null;
  /** Feedback marked as addressed ("done"). */
  isResolved?: boolean;
  resolvedAt?: IsoDateTime | null;
}

export interface Post {
  id: string;
  clientAccountId: string;
  clientName: string;
  title: string;
  status: PostStatus;
  scheduledAt: IsoDateTime | null;
  campaignId: string | null;
  autoAppendUtm: boolean;
  isEvergreen: boolean;
  evergreenIntervalDays: number;
  evergreenMaxRepeats: number;
  evergreenRepeatCount: number;
  recycledFromPostId: string | null;
  recycleNumber: number | null;
  publishedAt: IsoDateTime | null;
  failureReason: string | null;
  requiresClientApproval: boolean;
  isValid: boolean;
  allowedActions: string[];
  variants: Variant[];
  comments: PostComment[];
  createdByName: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface PostSummary {
  id: string;
  clientAccountId: string;
  clientName: string;
  title: string;
  status: PostStatus;
  scheduledAt: IsoDateTime | null;
  networks: SocialNetwork[];
  preview: string;
  isEvergreen: boolean;
  publishedAt: IsoDateTime | null;
  failureReason: string | null;
  concurrencyStamp: string;
}

export interface VariantInput {
  profileId: string;
  text: string;
  title?: string | null;
  mediaIds: string[];
  altTexts: string[];
  link?: string | null;
  firstComment?: string | null;
  hashtags: string[];
  mentions: string[];
}

export interface PostInput {
  clientAccountId: string;
  title: string;
  scheduledAt?: IsoDateTime | null;
  campaignId?: string | null;
  autoAppendUtm: boolean;
  isEvergreen: boolean;
  evergreenIntervalDays: number;
  evergreenMaxRepeats: number;
  variants: VariantInput[];
  concurrencyStamp?: string;
}

export interface AwarenessDay {
  date: string;
  name: string;
  countries: string[];
  sourceUrl: string;
}

export interface BestTime {
  network: SocialNetwork;
  times: string[];
  source: string;
}

export interface CalendarResponse {
  posts: PostSummary[];
  awarenessDays: AwarenessDay[];
  bestTimes: BestTime[];
}

export interface QueueSlot {
  id: string;
  day: string;
  time: string;
}

export interface Profile {
  id: string;
  clientAccountId: string;
  network: SocialNetwork;
  networkLabel: string;
  handle: string;
  displayName: string;
  profileUrl: string | null;
  avatarUrl: string | null;
  externalId: string | null;
  connectionState: 'Connected' | 'Error' | 'AppCredentialsRequired' | 'Disconnected' | 'NotConnected';
  connectionStatus: string;
  statusMessage: string | null;
  appCredentialsConfigured: boolean;
  publishingSupported: boolean;
  tokenExpiresAt: IsoDateTime | null;
  connectedAt: IsoDateTime | null;
  isActive: boolean;
  queueSlots: QueueSlot[];
  concurrencyStamp: string;
}

export interface Media {
  id: string;
  clientAccountId: string;
  kind: MediaKind;
  title: string;
  previewUrl: string;
  publicUrl: string | null;
  isUpload: boolean;
  width: number | null;
  height: number | null;
  durationSeconds: number | null;
  sizeBytes: number | null;
  altText: string | null;
  tags: string[];
  isPublic: boolean;
  createdAt: IsoDateTime;
}

export interface HashtagSet {
  id: string;
  name: string;
  hashtags: string[];
  concurrencyStamp: string;
}

export interface Snippet {
  id: string;
  name: string;
  body: string;
  concurrencyStamp: string;
}

export interface SocialCampaign {
  id: string;
  clientAccountId?: string;
  name: string;
  utmCampaign: string;
  utmSource: string | null;
  utmMedium: string | null;
  utmContent?: string | null;
  utmTerm?: string | null;
  concurrencyStamp?: string;
  /** Archived campaigns keep their posts but cannot be picked for new ones. */
  isArchived?: boolean;
}

/** Agency-wide network preset with its edit state (`GET /agency/social/admin/presets`). */
export interface AdminPreset {
  preset: Preset;
  isCustomized: boolean;
  updatedAt: IsoDateTime | null;
  concurrencyStamp: string;
}

/** Awareness day as managed by admins (`GET /agency/social/admin/awareness-days`). */
export interface AwarenessDayAdmin {
  id: string;
  month: number;
  day: number;
  year: number | null;
  name: string;
  countries: string[];
  sourceUrl: string;
  isActive: boolean;
  isBuiltIn: boolean;
  concurrencyStamp: string;
}

export interface SocialSettings {
  clientAccountId: string;
  requireClientApproval: boolean;
  defaultUtmMedium: string;
  concurrencyStamp: string;
}

export interface VariantState {
  id: string;
  network: SocialNetwork;
  profileHandle: string;
  publishStatus: VariantPublishStatus;
  attempts: number;
  nextAttemptAt: IsoDateTime | null;
  failureKind: PublishFailureKind;
  failureReason: string | null;
  publishedUrl: string | null;
  publishedAt: IsoDateTime | null;
  publishedManually: boolean;
}

export interface PublishingRow {
  post: PostSummary;
  variants: VariantState[];
}

export interface Mention {
  id: string;
  network: SocialNetwork;
  authorHandle: string;
  text: string;
  url: string | null;
  postedAt: IsoDateTime;
  sentiment: Sentiment | null;
  sentimentSource: 'Manual' | 'Automatic' | null;
  sentimentScore: number | null;
  sentimentLabel: string | null;
  source: 'Manual' | 'Api';
}

export interface ListeningQuery {
  id: string;
  kind: 'Keyword' | 'Hashtag' | 'CompetitorHandle';
  term: string;
  networks: SocialNetwork[];
  isActive: boolean;
  concurrencyStamp?: string;
}

export interface InboxItem {
  id: string;
  profileId: string | null;
  network: SocialNetwork;
  kind: 'Comment' | 'DirectMessage' | 'Mention';
  authorHandle: string;
  text: string;
  url: string | null;
  receivedAt: IsoDateTime;
  status: 'Open' | 'Assigned' | 'Replied' | 'Closed';
  assignedToUserId: string | null;
  assignedToName: string | null;
  sentiment: Sentiment | null;
  source: 'Manual' | 'Api';
  replies: { id: string; body: string; sentViaApi: boolean; byName: string; createdAt: IsoDateTime }[];
  concurrencyStamp: string;
}

export interface Totals {
  impressions: number;
  reach: number;
  engagements: number;
  clicks: number;
  videoViews: number;
  engagementRate: number | null;
  followersStart: number | null;
  followersEnd: number | null;
  followersGrowth: number | null;
}

export interface SocialKpis {
  clientAccountId: string;
  from: string;
  to: string;
  totals: Totals;
  postsPublished: number;
  profiles: { profileId: string; network: SocialNetwork; handle: string; totals: Totals; sourceLabel: string }[];
  sourceLabel: string;
  sources: string[];
  definitions: string;
}

export interface SeriesPoint {
  date: string;
  impressions: number;
  engagements: number;
  followers: number;
  reach: number;
}

export interface TopPost {
  postKey: string;
  postId: string | null;
  title: string | null;
  network: SocialNetwork;
  profileHandle: string;
  publishedAt: IsoDateTime | null;
  impressions: number;
  reach: number;
  engagements: number;
  clicks: number;
  videoViews: number;
  engagementRate: number | null;
  sourceLabel: string;
  url: string | null;
}

export interface AnalyticsResponse {
  kpis: SocialKpis;
  series: SeriesPoint[];
  topPosts: TopPost[];
}

export interface BestTimes {
  enoughData: boolean;
  postsAnalysed: number;
  slots: { day: string; hour: number; posts: number; avgEngagementRate: number | null }[];
  sourceLabel: string;
  note: string;
  presetGuidance: BestTime[];
}

export interface Competitor {
  id: string;
  name: string;
  network: SocialNetwork;
  handle: string;
  profileUrl: string | null;
  snapshots: {
    id: string;
    date: string;
    followers: number;
    engagementRate: number | null;
    postsLast30Days: number | null;
    source: string;
    sourceLabel: string;
  }[];
  concurrencyStamp?: string;
}

export interface ImportPreview {
  headers: string[];
  suggestedMapping: Record<string, string>;
  targetFields: string[];
  requiredFields: string[];
  sampleRows: string[][];
  rowCount: number;
  errors: string[];
}

export interface ImportResult {
  importId: string;
  rowsTotal: number;
  rowsImported: number;
  rowsUpdated: number;
  rowsSkipped: number;
  errors: string[];
  sourceLabel: string;
}

export const socialKeys = {
  all: ['social'] as const,
  clients: () => ['social', 'clients'] as const,
  presets: () => ['social', 'presets'] as const,
  calendar: (params: Record<string, unknown>) => ['social', 'calendar', params] as const,
  posts: (params: Record<string, unknown>) => ['social', 'posts', params] as const,
  post: (id: string) => ['social', 'post', id] as const,
  approvals: (clientId?: string) => ['social', 'approvals', clientId ?? 'all'] as const,
  publishing: (params: Record<string, unknown>) => ['social', 'publishing', params] as const,
  profiles: (clientId: string) => ['social', 'profiles', clientId] as const,
  media: (clientId: string, search?: string) => ['social', 'media', clientId, search ?? ''] as const,
  hashtags: (clientId: string) => ['social', 'hashtags', clientId] as const,
  snippets: (clientId: string) => ['social', 'snippets', clientId] as const,
  campaigns: (clientId: string) => ['social', 'campaigns', clientId] as const,
  settings: (clientId: string) => ['social', 'settings', clientId] as const,
  analytics: (clientId: string, params: Record<string, unknown>) => ['social', 'analytics', clientId, params] as const,
  bestTimes: (clientId: string) => ['social', 'best-times', clientId] as const,
  mentions: (clientId: string, params: Record<string, unknown>) => ['social', 'mentions', clientId, params] as const,
  queries: (clientId: string) => ['social', 'queries', clientId] as const,
  inbox: (clientId: string, params: Record<string, unknown>) => ['social', 'inbox', clientId, params] as const,
  competitors: (clientId: string) => ['social', 'competitors', clientId] as const,
  adminPresets: () => ['social', 'admin', 'presets'] as const,
  awarenessDays: () => ['social', 'admin', 'awareness-days'] as const,
};

export const useSocialClients = () =>
  useQuery({ queryKey: socialKeys.clients(), queryFn: () => api.get<ClientOption[]>('/agency/social/clients') });

export const usePresets = () =>
  useQuery({
    queryKey: socialKeys.presets(),
    queryFn: () => api.get<Preset[]>('/agency/social/presets'),
    staleTime: 10 * 60_000,
  });

export const useProfiles = (clientId: string | undefined) =>
  useQuery({
    queryKey: socialKeys.profiles(clientId ?? ''),
    queryFn: () => api.get<Profile[]>(`/agency/social/clients/${clientId}/profiles`),
    enabled: !!clientId,
  });

export const useMedia = (clientId: string | undefined, search?: string) =>
  useQuery({
    queryKey: socialKeys.media(clientId ?? '', search),
    queryFn: () =>
      api.get<PagedResult<Media>>(`/agency/social/clients/${clientId}/media`, {
        query: { search, pageSize: 100 },
      }),
    enabled: !!clientId,
  });

export const useHashtagSets = (clientId: string | undefined) =>
  useQuery({
    queryKey: socialKeys.hashtags(clientId ?? ''),
    queryFn: () => api.get<HashtagSet[]>(`/agency/social/clients/${clientId}/hashtag-sets`),
    enabled: !!clientId,
  });

export const useSnippets = (clientId: string | undefined) =>
  useQuery({
    queryKey: socialKeys.snippets(clientId ?? ''),
    queryFn: () => api.get<Snippet[]>(`/agency/social/clients/${clientId}/snippets`),
    enabled: !!clientId,
  });

export const useSocialCampaigns = (clientId: string | undefined, includeArchived = false) =>
  useQuery({
    queryKey: [...socialKeys.campaigns(clientId ?? ''), includeArchived ? 'all' : 'active'],
    queryFn: () => api.get<SocialCampaign[]>(`/agency/social/clients/${clientId}/campaigns`, { query: { includeArchived: includeArchived || undefined } }),
    enabled: !!clientId,
  });

export const NETWORK_LABELS: Record<SocialNetwork, string> = {
  Facebook: 'Facebook',
  Instagram: 'Instagram',
  X: 'X',
  LinkedIn: 'LinkedIn',
  TikTok: 'TikTok',
  YouTube: 'YouTube',
  Pinterest: 'Pinterest',
  GoogleBusiness: 'Google Business Profile',
};

export const STATUS_LABELS: Record<PostStatus, string> = {
  Draft: 'Draft',
  InternalReview: 'Internal review',
  ClientApproval: 'Client approval',
  Approved: 'Approved',
  Scheduled: 'Scheduled',
  Publishing: 'Publishing',
  Published: 'Published',
  Failed: 'Failed',
};
