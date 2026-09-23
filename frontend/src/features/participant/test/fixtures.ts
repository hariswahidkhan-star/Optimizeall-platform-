import { json, session } from '@/test/fetchMock';
import type {
  CampaignCard,
  CampaignDetail,
  EarningsSummary,
  ParticipantHome,
  SocialAccount,
  SubmissionDetail,
} from '../api/types';

export const paged = <T>(items: T[], total = items.length) => ({
  items,
  total,
  page: 1,
  pageSize: 25,
  totalPages: Math.max(1, Math.ceil(total / 25)),
});

export const authRoutes = { 'POST /auth/refresh': () => json(200, session()) };

export function makeHome(overrides: Partial<ParticipantHome> = {}): ParticipantHome {
  return {
    user: {
      id: 'u1',
      displayName: 'Ada Lovelace',
      emailVerified: true,
      tier: 'Standard',
      timeZone: 'Europe/London',
      countryCode: 'GB',
      languageCode: 'en',
    },
    state: 'Ready',
    eligibleFrom: null,
    onboarding: {
      steps: [
        {
          id: 's1',
          key: 'verify-email',
          title: 'Verify your email',
          description: 'Open the link.',
          actionLabel: null,
          actionUrl: null,
          completionRule: 'EmailVerified',
          isManual: false,
          completed: true,
        },
        {
          id: 's2',
          key: 'follow-us',
          title: 'Follow us',
          description: 'Say hello.',
          actionLabel: 'Open profile',
          actionUrl: '/app/profile',
          completionRule: 'Manual',
          isManual: true,
          completed: false,
        },
      ],
      completedCount: 1,
      totalCount: 2,
      progressPercent: 50,
    },
    banners: [],
    announcements: [],
    submissions: {
      total: 0,
      pending: 0,
      underReview: 0,
      approved: 0,
      needsCorrection: 0,
      rejected: 0,
      reversed: 0,
    },
    unreadNotificationCount: 0,
    openSupportTicketCount: 0,
    socialAccounts: { total: 1, eligible: 1, soonestEligibleFrom: null, minAccountAgeDays: 90 },
    ...overrides,
  };
}

export function makeCard(overrides: Partial<CampaignCard> = {}): CampaignCard {
  return {
    id: 'c1',
    slug: 'autumn-launch',
    title: 'Autumn launch',
    summary: 'Share our autumn collection.',
    category: { id: 'cat1', name: 'Fashion', slug: 'fashion' },
    topics: ['fashion'],
    platforms: ['Instagram', 'TikTok'],
    status: 'Active',
    upcoming: false,
    startsAt: '2026-09-01T00:00:00Z',
    endsAt: '2026-12-01T00:00:00Z',
    submissionDeadline: '2026-12-04T00:00:00Z',
    heroImageUrl: null,
    reward: { currency: 'USD', baseAmount: 5, maxAmount: 7, hasBonuses: true },
    eligibility: { isEligible: true, reasons: [] },
    mySubmissionCount: 0,
    remainingSubmissions: 5,
    ...overrides,
  };
}

export function makeCampaign(overrides: Partial<CampaignDetail> = {}): CampaignDetail {
  const card = makeCard();
  return {
    ...card,
    visibility: 'Public',
    isOpenForSubmissions: true,
    trackingEnabled: false,
    timeZone: 'Europe/London',
    landingHeadline: null,
    landingBody: null,
    description: 'About the campaign.',
    postingInstructions: 'Post the hero image.',
    requiredHashtags: '#brand',
    requiredMentions: '@brand',
    assets: [
      {
        id: 'a1',
        type: 'Caption',
        title: 'Main caption',
        url: null,
        fileId: null,
        body: 'Loving it #ad',
        platform: null,
        templateId: null,
        sortOrder: 0,
      },
    ],
    disclosures: [{ platform: 'Instagram', text: 'Paid partnership' }],
    rewardTerms: {
      currency: 'USD',
      baseAmount: 5,
      overrides: [],
      bonuses: [],
      dailyCap: null,
      weeklyCap: null,
      campaignCap: null,
      minPostLiveHours: 48,
      maxSubmissionsPerParticipant: 5,
      requireScreenshot: true,
      ruleSetVersion: 3,
    },
    eligibility: {
      isEligible: true,
      reasons: [],
      accounts: [
        {
          socialAccountId: 'sa1',
          platform: 'Instagram',
          handle: 'ada',
          isEligible: true,
          eligibleFrom: null,
          reasons: [],
        },
      ],
    },
    mySubmissions: [],
    ...overrides,
  };
}

export function makeSubmission(overrides: Partial<SubmissionDetail> = {}): SubmissionDetail {
  return {
    id: 'sub1',
    campaign: { id: 'c1', slug: 'autumn-launch', title: 'Autumn launch' },
    platform: 'Instagram',
    socialAccount: { id: 'sa1', platform: 'Instagram', handle: 'ada' },
    postUrl: 'https://www.instagram.com/p/abc/',
    postedAt: '2026-09-20T10:00:00Z',
    captionText: 'Loving it #ad',
    screenshotUrl: null,
    status: 'Pending',
    submittedAt: '2026-09-20T11:00:00Z',
    decidedAt: null,
    decisionReason: null,
    correctionCount: 0,
    estimatedReward: 5,
    currency: 'USD',
    rewardRuleSetVersion: 3,
    liveCheck: { status: 'NotRequired', dueAt: null, checkedAt: null },
    timeline: [
      {
        action: 'submitted',
        fromStatus: null,
        toStatus: 'Pending',
        reason: null,
        actor: 'You',
        at: '2026-09-20T11:00:00Z',
      },
    ],
    earnings: [],
    appeal: null,
    canEdit: false,
    canAppeal: false,
    appealDeadline: null,
    ...overrides,
  };
}

export function makeSummary(overrides: Partial<EarningsSummary> = {}): EarningsSummary {
  return {
    currency: 'USD',
    pending: 15,
    approved: 12.5,
    onHold: 5,
    scheduled: 3,
    paid: 20,
    reversed: 4,
    availableForNextPayout: 7.5,
    lifetimeEarned: 35.5,
    nextPayout: {
      periodKey: '2026-09-27',
      cutoffAt: '2026-09-27T23:59:59Z',
      paymentDate: '2026-10-02',
      minimumPayoutAmount: 10,
      meetsMinimum: false,
      estimatedAmount: 0,
    },
    activeHold: false,
    holdMessage: null,
    pendingByCurrency: [],
    byCurrency: [
      { currency: 'USD', pendingApproval: 6, approved: 12.5, scheduled: 3, paid: 20, reversed: 4 },
    ],
    ...overrides,
  };
}

export function makeSocial(overrides: Partial<SocialAccount> = {}): SocialAccount {
  return {
    id: 'sa1',
    platform: 'Instagram',
    handle: 'ada',
    profileUrl: 'https://www.instagram.com/ada',
    accountCreatedAt: '2025-01-01T00:00:00Z',
    accountAgeDays: 630,
    followerCount: 2500,
    primaryLanguage: 'en',
    audienceCountryCode: 'GB',
    verificationStatus: 'Verified',
    verificationNote: null,
    isActive: true,
    qualifies: true,
    eligibleFrom: null,
    reasons: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    concurrencyStamp: 'stamp-1',
    ...overrides,
  };
}
