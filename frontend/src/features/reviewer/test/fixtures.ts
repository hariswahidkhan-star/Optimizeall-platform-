import type { PagedResult } from '@/lib/api/types';
import { json, makeUser, session } from '@/test/fetchMock';
import type { AppealDetail, ReviewDetail, ReviewQueueItem } from '../api/types';

export const REVIEWER_ID = 'rev-1';

export function reviewerSession(permissions: string[] = []) {
  return () =>
    json(
      200,
      session(
        makeUser({
          id: REVIEWER_ID,
          displayName: 'Rita Reviewer',
          roles: ['Reviewer'],
          permissions: ['submissions.review', 'appeals.resolve', 'social.verify', ...permissions],
        }),
      ),
    );
}

export function queueItem(overrides: Partial<ReviewQueueItem> = {}): ReviewQueueItem {
  return {
    id: 's1',
    campaign: { id: 'c1', title: 'Autumn Launch' },
    participant: { id: 'p1', displayName: 'Sara Khan', countryCode: 'PK' },
    platform: 'Instagram',
    handle: 'sara',
    status: 'Pending',
    submittedAt: '2026-09-23T10:00:00Z',
    riskScore: 45,
    flagTypes: ['DuplicateScreenshot', 'NewParticipant'],
    claimedBy: null,
    claimExpiresAt: null,
    assignedReviewer: null,
    correctionCount: 0,
    ...overrides,
  };
}

export function paged<T>(items: T[]): PagedResult<T> {
  return { items, total: items.length, page: 1, pageSize: 25, totalPages: 1 };
}

export function reviewDetail(
  overrides: {
    claimMine?: boolean;
    status?: ReviewDetail['submission']['status'];
    participantStatus?: ReviewDetail['participant']['status'];
    qualityBonusMax?: number | null;
  } = {},
): ReviewDetail {
  const mine = overrides.claimMine ?? true;
  return {
    requirements: {
      campaignId: 'c1',
      campaignTitle: 'Autumn Launch',
      postingInstructions: 'Post the launch image.',
      requiredHashtags: '#brand',
      requiredMentions: '@brand',
      disclosureText: '#ad',
      allowedPlatforms: ['Instagram'],
      startsAt: '2026-09-01T00:00:00Z',
      endsAt: '2026-10-30T00:00:00Z',
      submissionDeadline: '2026-11-02T00:00:00Z',
      minPostLiveHours: 48,
      requireScreenshot: true,
    },
    submission: {
      id: 's1',
      postUrl: 'https://www.instagram.com/p/abc/?utm_source=x',
      normalizedPostUrl: 'https://instagram.com/p/abc',
      platform: 'Instagram',
      postedAt: '2026-09-22T10:00:00Z',
      captionText: 'Loving it #brand @brand #ad',
      screenshotUrl: '/api/v1/files/f1',
      screenshotSha256: 'abc',
      submittedAt: '2026-09-22T12:00:00Z',
      status: overrides.status ?? 'UnderReview',
      correctionCount: 0,
      riskScore: 45,
      rewardRuleSetVersion: 1,
      estimatedReward: 5,
      currency: 'USD',
      decidedAt: null,
      decidedBy: null,
      decisionReason: null,
      liveCheckStatus: 'NotRequired',
      liveCheckDueAt: null,
      assignedReviewer: null,
      concurrencyStamp: 'stamp-1',
      claim: {
        claimedBy: {
          id: mine ? REVIEWER_ID : 'rev-2',
          displayName: mine ? 'Rita Reviewer' : 'Omar Reviewer',
        },
        claimExpiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
        isMine: mine,
        isActive: true,
      },
    },
    account: {
      id: 'a1',
      handle: 'sara',
      profileUrl: 'https://www.instagram.com/sara',
      platform: 'Instagram',
      accountCreatedAt: '2021-01-01T00:00:00Z',
      accountAgeDays: 1000,
      followerCount: 5000,
      verificationStatus: 'Verified',
      isActive: true,
    },
    participant: {
      id: 'p1',
      displayName: 'Sara Khan',
      email: 'sara@example.com',
      countryCode: 'PK',
      tier: 'Standard',
      joinedAt: '2026-01-01T00:00:00Z',
      approvedCount: 3,
      rejectedCount: 0,
      reversedCount: 0,
      status: overrides.participantStatus ?? 'Active',
    },
    history: [],
    flags: [
      {
        id: 'f1',
        type: 'DuplicateScreenshot',
        detail: 'Same screenshot as another submission.',
        weight: 40,
        createdAt: '2026-09-22T12:00:00Z',
        resolved: false,
        resolvedAt: null,
        resolutionNote: null,
      },
    ],
    relatedSubmissions: [],
    events: [
      {
        action: 'submitted',
        fromStatus: null,
        toStatus: 'Pending',
        reason: null,
        actor: null,
        at: '2026-09-22T12:00:00Z',
      },
    ],
    rewardQuote: {
      ruleSetId: 'r1',
      ruleSetVersion: 1,
      currency: 'USD',
      lines: [
        {
          type: 'PostReward',
          ruleId: 'x',
          amount: 5,
          uncappedAmount: 5,
          requiresApproval: false,
          label: 'Post reward',
        },
      ],
      total: 5,
      appliedCaps: [],
      ruleSetSummary: 'v1 USD: base 5.00',
    },
    earnings: [],
    appeals: [],
    qualityBonusMax: overrides.qualityBonusMax === undefined ? 10 : overrides.qualityBonusMax,
  };
}

export function appealDetail(decidedById: string): AppealDetail {
  const review = reviewDetail({ status: 'Rejected' });
  review.submission.claim = { claimedBy: null, claimExpiresAt: null, isMine: false, isActive: false };
  review.submission.decidedBy = { id: decidedById, displayName: 'Someone' };
  review.submission.decisionReason = 'Disclosure missing';
  return {
    appeal: {
      id: 'ap1',
      status: 'Open',
      decisionAppealed: 'Rejected',
      reason: 'The disclosure is in the first comment.',
      createdAt: '2026-09-23T08:00:00Z',
      resolvedBy: null,
      resolutionNote: null,
      resolvedAt: null,
      concurrencyStamp: 'ap-stamp',
    },
    originalDecidedBy: { id: decidedById, displayName: 'Someone' },
    review,
  };
}
