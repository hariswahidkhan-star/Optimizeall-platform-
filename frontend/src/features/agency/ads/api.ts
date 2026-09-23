import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { IsoDateTime } from '@/lib/api/types';

/** Mirrors backend Modules/Ads DTOs. Money values are always paired with their currency. */

export const AD_PLATFORMS = ['GoogleAds', 'MetaAds', 'TikTokAds', 'LinkedInAds', 'MicrosoftAds', 'SnapchatAds'] as const;
export type AdPlatform = (typeof AD_PLATFORMS)[number];

export const PLATFORM_LABELS: Record<AdPlatform, string> = {
  GoogleAds: 'Google Ads',
  MetaAds: 'Meta Ads',
  TikTokAds: 'TikTok Ads',
  LinkedInAds: 'LinkedIn Ads',
  MicrosoftAds: 'Microsoft Ads',
  SnapchatAds: 'Snapchat Ads',
};

export interface Totals {
  spend: number;
  impressions: number;
  clicks: number;
  conversions: number;
  conversionValue: number;
  reach: number;
  videoViews: number;
}

export interface Kpis {
  ctr: number | null;
  cpc: number | null;
  cpm: number | null;
  cpa: number | null;
  roas: number | null;
  conversionRate: number | null;
  frequency: number | null;
}

export interface ClientOption {
  id: string;
  name: string;
  slug: string;
  countryCode: string;
  currency: string;
  timeZone: string;
}

export interface AdAccount {
  id: string;
  clientAccountId: string;
  clientName: string;
  platform: AdPlatform;
  externalAccountId: string;
  name: string;
  currency: string;
  timeZone: string;
  status: 'NotConnected' | 'Connected' | 'Error' | 'Disconnected';
  statusMessage: string | null;
  syncSupported: boolean;
  lastSyncedAt: IsoDateTime | null;
  lastSyncMessage: string | null;
  managerUserId: string | null;
  managerName: string | null;
  isActive: boolean;
  last30Days: Totals;
  last30DaysKpis: Kpis;
  concurrencyStamp: string;
}

export interface CampaignRow {
  id: string;
  adAccountId: string;
  externalId: string | null;
  name: string;
  objective: string | null;
  status: string;
  budgetType: 'Daily' | 'Lifetime';
  budgetAmount: number | null;
  currency: string;
  bidStrategy: string | null;
  startDate: string | null;
  endDate: string | null;
  targetingSummary: string | null;
  targetCpa: number | null;
  targetRoas: number | null;
  source: 'Plan' | 'Synced' | 'Imported';
  namingCompliant: boolean;
  totals: Totals;
  kpis: Kpis;
  spendSparkline: number[];
  sourceLabel: string;
  concurrencyStamp: string;
}

export interface DailyPoint {
  date: string;
  spend: number;
  conversionValue: number;
  conversions: number;
  clicks: number;
  impressions: number;
}

export interface AccountDetail {
  account: AdAccount;
  totals: Totals;
  kpis: Kpis;
  daily: DailyPoint[];
  campaigns: CampaignRow[];
  sourceLabel: string;
}

export interface OverviewRow {
  clientAccountId: string;
  clientName: string;
  currency: string;
  totals: Totals;
  kpis: Kpis;
  fxMissing: string[];
  accounts: number;
  openAlerts: number;
}

export interface Overview {
  from: string;
  to: string;
  reportingCurrency: string;
  totals: Totals;
  kpis: Kpis;
  clients: OverviewRow[];
  daily: DailyPoint[];
  fxMissing: string[];
  definitions: string;
}

export interface PacingResult {
  budget: number;
  actualToDate: number;
  expectedToDate: number;
  pacingRatio: number | null;
  projectedMonthEnd: number;
  projectedVsBudget: number | null;
  daysElapsed: number;
  daysInMonth: number;
  dailyRunRate: number;
  state: 'NoBudget' | 'NotStarted' | 'OnTrack' | 'Over' | 'Under';
}

export interface Budget {
  id: string;
  clientAccountId: string;
  clientName: string;
  month: string;
  platform: AdPlatform | null;
  campaignId: string | null;
  campaignName: string | null;
  scopeLabel: string;
  amount: number;
  currency: string;
  overPacingThreshold: number;
  underPacingThreshold: number;
  targetCpa: number | null;
  targetRoas: number | null;
  notes: string | null;
  pacing: PacingResult;
  actualCpa: number | null;
  actualRoas: number | null;
  conversions: number;
  fxMissing: string[];
  concurrencyStamp: string;
}

export interface AdAlert {
  id: string;
  clientAccountId: string;
  clientName: string;
  kind: 'OverPacing' | 'UnderPacing' | 'CpaAboveTarget' | 'RoasBelowTarget' | 'SpendWithoutConversions';
  severity: 'Info' | 'Warning' | 'Critical';
  title: string;
  message: string;
  budgetId: string | null;
  campaignId: string | null;
  evaluatedFor: string;
  status: 'Open' | 'Acknowledged' | 'Resolved';
  createdAt: IsoDateTime;
  acknowledgedAt: IsoDateTime | null;
}

export interface MediaPlanLine {
  id?: string;
  platform: AdPlatform;
  channel: string;
  objective: string | null;
  plannedBudget: number;
  flightStart: string;
  flightEnd: string;
  kpiName: string;
  kpiTarget: number | null;
}

export interface MediaPlan {
  id: string;
  clientAccountId: string;
  clientName: string;
  name: string;
  month: string;
  currency: string;
  status: 'Draft' | 'Approved' | 'Archived';
  notes: string | null;
  plannedTotal: number;
  lines: MediaPlanLine[];
  concurrencyStamp: string;
}

export interface PlanActuals {
  plan: MediaPlan;
  lines: {
    line: MediaPlanLine;
    actualSpend: number;
    spendVsPlan: number | null;
    impressions: number;
    clicks: number;
    conversions: number;
    conversionValue: number;
    kpiActual: number | null;
    kpiMet: boolean | null;
    kpiDirection: string;
  }[];
  plannedTotal: number;
  actualTotal: number;
  fxMissing: string[];
  sourceLabel: string;
  note: string;
}

export interface CopyIssue {
  field: string;
  index: number;
  severity: 'Error' | 'Warning';
  message: string;
}

export interface CopyLimit {
  field: string;
  max: number;
  hard: boolean;
  minCount: number;
  maxCount: number;
}

export interface Creative {
  id: string;
  clientAccountId: string;
  name: string;
  platform: AdPlatform;
  format: string;
  headlines: string[];
  descriptions: string[];
  primaryText: string | null;
  callToAction: string | null;
  finalUrl: string | null;
  mediaAssetIds: string[];
  campaignId: string | null;
  status: 'Draft' | 'InternalReview' | 'ClientApproval' | 'Approved';
  reviewNote: string | null;
  issues: CopyIssue[];
  limits: CopyLimit[];
  allowedActions: string[];
  updatedAt: IsoDateTime;
  concurrencyStamp: string;
}

export interface Experiment {
  id: string;
  clientAccountId: string;
  platform: AdPlatform;
  name: string;
  hypothesis: string;
  metric: 'ConversionRate' | 'Ctr';
  startDate: string | null;
  endDate: string | null;
  status: 'Planned' | 'Running' | 'Concluded';
  result: string | null;
  winnerVariant: string | null;
  enteredPValue: number | null;
  variants: {
    id: string;
    name: string;
    isControl: boolean;
    impressions: number;
    clicks: number;
    conversions: number;
    spend: number;
    rate: number | null;
    zScore: number | null;
    pValue: number | null;
    significant: boolean | null;
  }[];
  significanceSource: string;
  method: string;
}

export interface ImportTemplate {
  id: string;
  name: string;
  platform: AdPlatform | null;
  headerAliases: Record<string, string[]>;
  sampleUrl: string;
}

export interface ImportPreview {
  headers: string[];
  headerRow: number;
  mapping: Record<string, string>;
  targetFields: string[];
  requiredFields: string[];
  sample: {
    rowNumber: number;
    date: string;
    level: string;
    entity: string;
    currency: string;
    spend: number;
    impressions: number;
    clicks: number;
    conversions: number;
    conversionValue: number;
  }[];
  rowsTotal: number;
  validRows: number;
  existingRows: number;
  fromDate: string | null;
  toDate: string | null;
  errors: string[];
  warnings: string[];
}

export interface ImportResult {
  batchId: string;
  rowsTotal: number;
  rowsImported: number;
  rowsUpdated: number;
  rowsSkipped: number;
  errors: string[];
  fromDate: string | null;
  toDate: string | null;
  sourceLabel: string;
}

export interface AdsSettings {
  clientAccountId: string;
  campaignNamingTemplate: string | null;
  defaultUtmSource: string;
  defaultUtmMedium: string;
  lowercaseUtm: boolean;
  tokens: string[];
  concurrencyStamp: string;
}

export interface UtmLink {
  id: string | null;
  baseUrl: string;
  source: string;
  medium: string;
  campaign: string;
  term: string | null;
  content: string | null;
  taggedUrl: string;
  createdAt: IsoDateTime | null;
}

export const adsKeys = {
  all: ['ads'] as const,
  clients: () => ['ads', 'clients'] as const,
  overview: (params: Record<string, unknown>) => ['ads', 'overview', params] as const,
  accounts: (clientId?: string) => ['ads', 'accounts', clientId ?? 'all'] as const,
  account: (id: string, params: Record<string, unknown>) => ['ads', 'account', id, params] as const,
  pacing: (params: Record<string, unknown>) => ['ads', 'pacing', params] as const,
  alerts: (params: Record<string, unknown>) => ['ads', 'alerts', params] as const,
  plans: (clientId?: string) => ['ads', 'plans', clientId ?? 'all'] as const,
  planActuals: (id: string) => ['ads', 'plan-actuals', id] as const,
  creatives: (clientId?: string) => ['ads', 'creatives', clientId ?? 'all'] as const,
  experiments: (clientId?: string) => ['ads', 'experiments', clientId ?? 'all'] as const,
  settings: (clientId: string) => ['ads', 'settings', clientId] as const,
  utm: (clientId: string) => ['ads', 'utm', clientId] as const,
  templates: () => ['ads', 'templates'] as const,
};

export const useAdsClients = () => useQuery({ queryKey: adsKeys.clients(), queryFn: () => api.get<ClientOption[]>('/agency/ads/clients') });

export const useAdAccounts = (clientId?: string) =>
  useQuery({ queryKey: adsKeys.accounts(clientId), queryFn: () => api.get<AdAccount[]>('/agency/ads/accounts', { query: { clientId } }) });
