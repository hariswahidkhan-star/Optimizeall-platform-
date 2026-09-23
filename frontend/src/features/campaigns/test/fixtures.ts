import { json, makeUser, session } from '@/test/fetchMock';
import type { AdminCampaign, RewardRuleSet } from '../api/types';

export const MANAGER_PERMISSIONS = [
  'campaigns.view',
  'campaigns.manage',
  'campaigns.publish',
  'rewards.edit',
  'rewards.approve_bonus',
  'marketing.manage',
  'analytics.view',
  'review.assign',
  'users.view',
];

export function managerSession(permissions: string[] = MANAGER_PERMISSIONS) {
  return () =>
    json(
      200,
      session(
        makeUser({ roles: ['CampaignManager'], permissions, timeZone: 'UTC', displayName: 'Maya Manager' }),
      ),
    );
}

export function makeRuleSet(overrides: Partial<RewardRuleSet> = {}): RewardRuleSet {
  return {
    id: 'rs1',
    version: 1,
    currency: 'USD',
    dailyCapPerParticipant: null,
    weeklyCapPerParticipant: null,
    campaignCapPerParticipant: null,
    effectiveFrom: '2026-09-01T00:00:00Z',
    createdAt: '2026-09-01T00:00:00Z',
    createdBy: { id: 'u1', displayName: 'Maya Manager' },
    reason: 'Initial reward rules',
    summary: 'v1 USD: base 5.00',
    isCurrent: true,
    inUseBySubmissions: 3,
    rules: [
      {
        id: 'r1',
        type: 'BaseRate',
        amount: 5,
        platform: null,
        countryCode: null,
        tier: null,
        validFrom: null,
        validTo: null,
        approvalMode: 'Automatic',
        priority: 0,
        label: null,
      },
    ],
    ...overrides,
  };
}

export function makeCampaign(overrides: Partial<AdminCampaign> = {}): AdminCampaign {
  return {
    id: 'c1',
    slug: 'spring-drop',
    title: 'Spring drop',
    summary: 'Share our spring collection',
    description: '',
    category: null,
    topics: [],
    status: 'Draft',
    visibility: 'Public',
    startsAt: '2030-01-01T10:00:00Z',
    endsAt: '2030-02-01T10:00:00Z',
    submissionDeadline: '2030-02-04T10:00:00Z',
    timeZone: 'UTC',
    postingInstructions: 'Post the hero image.',
    defaultDisclosureText: '#ad',
    requiredHashtags: null,
    requiredMentions: null,
    budgetAmount: 1000,
    budgetCurrency: 'USD',
    spent: 45,
    budgetRemaining: 955,
    maxSubmissionsPerParticipant: 1,
    minPostLiveHours: 0,
    requireScreenshot: true,
    eligibility: {
      minAccountAgeDays: null,
      minFollowers: 0,
      requireVerifiedAccount: false,
      countries: [],
      languages: [],
      interests: [],
      tiers: [],
    },
    platforms: ['Instagram'],
    landingHeadline: null,
    landingBody: null,
    heroImageUrl: null,
    trackingDestinationUrl: null,
    utmCampaign: null,
    assets: [],
    disclosures: [],
    currentRuleSet: makeRuleSet(),
    submissions: { total: 0, pending: 0, approved: 0, rejected: 0 },
    createdByUserId: 'u1',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    publishedAt: null,
    concurrencyStamp: 'stamp-1',
    ...overrides,
  };
}
