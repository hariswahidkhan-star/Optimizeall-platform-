import type { IsoDateTime } from '@/lib/api/types';

export type IntegrationCategory = 'Social' | 'Ads' | 'Messaging' | 'Email' | 'Seo' | 'Payments' | 'Security';
export type IntegrationStatus = 'Unverified' | 'Connected' | 'Error' | 'Disconnected';

export interface ProviderField {
  key: string;
  label: string;
  required: boolean;
  help: string | null;
  pattern: string | null;
  maxLength: number;
  placeholder: string | null;
}

/** `GET /agency/integrations/providers` */
export interface Provider {
  key: string;
  name: string;
  category: IntegrationCategory;
  description: string;
  helpText: string;
  docsUrl: string | null;
  settings: ProviderField[];
  secrets: ProviderField[];
  agencyWide: boolean;
  perClient: boolean;
  supportsVerification: boolean;
  tokensExpire: boolean;
}

/** A secret slot: only whether a value is saved. Secret values are write-only and never returned. */
export interface SecretState {
  key: string;
  label: string;
  required: boolean;
  saved: boolean;
}

export interface Connection {
  id: string;
  provider: string;
  providerName: string;
  clientAccountId: string | null;
  clientName: string | null;
  displayName: string;
  settings: Record<string, string>;
  secrets: SecretState[];
  status: IntegrationStatus;
  statusMessage: string | null;
  lastVerifiedAt: IsoDateTime | null;
  expiresAt: IsoDateTime | null;
  expiringSoon: boolean;
  concurrencyStamp: string;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export const integrationKeys = {
  all: ['agency', 'integrations'] as const,
  providers: ['agency', 'integrations', 'providers'] as const,
  connections: (scope: string) => ['agency', 'integrations', 'connections', scope] as const,
};

export const categoryLabels: Record<IntegrationCategory, string> = {
  Social: 'Social media',
  Ads: 'Advertising',
  Messaging: 'SMS & messaging',
  Email: 'Email delivery',
  Seo: 'SEO data',
  Payments: 'Payments',
  Security: 'Spam protection',
};

export const statusTone: Record<IntegrationStatus, 'success' | 'warning' | 'danger' | 'neutral'> = {
  Connected: 'success',
  Unverified: 'warning',
  Error: 'danger',
  Disconnected: 'neutral',
};
