import { json, makeUser, session } from '@/test/fetchMock';
import type { Preset, Profile, PostSummary } from '../api';

export const SOCIAL_PERMISSIONS = ['clients.view', 'projects.view', 'social.manage', 'social.publish', 'reports.manage'];
export const ADS_PERMISSIONS = ['clients.view', 'projects.view', 'ads.manage', 'reports.manage', 'analytics.view'];

export function staffSession(permissions: string[] = SOCIAL_PERMISSIONS, role = 'SocialMediaManager') {
  return () => json(200, session(makeUser({ roles: [role], permissions, timeZone: 'UTC', displayName: 'Sofia Social' })));
}

const base: Omit<Preset, 'network' | 'label' | 'maxTextLength' | 'linkHandling' | 'urlWeight'> = {
  maxTitleLength: null,
  requiresTitle: false,
  maxHashtags: 30,
  recommendedHashtags: 3,
  maxMentions: 50,
  maxMedia: 4,
  maxVideos: 1,
  requiresMedia: false,
  requiresVideo: false,
  allowsMixedMedia: false,
  minAspectRatio: null,
  maxAspectRatio: null,
  maxAltTextLength: 1000,
  supportsFirstComment: true,
  recommendedTimes: ['Tue 09:00'],
  source: 'test',
};

export const presets: Preset[] = [
  { ...base, network: 'X', label: 'X', maxTextLength: 280, linkHandling: 'InText', urlWeight: 23 },
  { ...base, network: 'Instagram', label: 'Instagram', maxTextLength: 2200, linkHandling: 'NotClickable', urlWeight: null, requiresMedia: true },
  { ...base, network: 'Facebook', label: 'Facebook', maxTextLength: 63206, linkHandling: 'Attachment', urlWeight: null },
];

export function profile(overrides: Partial<Profile> = {}): Profile {
  return {
    id: 'p-x',
    clientAccountId: 'c1',
    network: 'X',
    networkLabel: 'X',
    handle: 'nimbusfit',
    displayName: 'Nimbus Fitness',
    profileUrl: null,
    avatarUrl: null,
    externalId: null,
    connectionState: 'AppCredentialsRequired',
    connectionStatus: 'NotConnected',
    statusMessage: null,
    appCredentialsConfigured: false,
    publishingSupported: true,
    tokenExpiresAt: null,
    connectedAt: null,
    isActive: true,
    queueSlots: [],
    concurrencyStamp: 's1',
    ...overrides,
  };
}

export function summary(overrides: Partial<PostSummary> = {}): PostSummary {
  return {
    id: 'post1',
    clientAccountId: 'c1',
    clientName: 'Nimbus Fitness',
    title: 'Launch day',
    status: 'Scheduled',
    scheduledAt: '2026-09-10T14:00:00.000Z',
    networks: ['X'],
    preview: 'Launch day!',
    isEvergreen: false,
    publishedAt: null,
    failureReason: null,
    concurrencyStamp: 'stamp-1',
    ...overrides,
  };
}

export const clients = [
  { id: 'c1', name: 'Nimbus Fitness', slug: 'nimbus-fitness', countryCode: 'US', currency: 'USD', timeZone: 'America/New_York' },
];
