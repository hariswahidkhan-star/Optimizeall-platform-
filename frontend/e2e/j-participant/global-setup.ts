import { ApiSession, mailLink, publicApi } from '../journeys/support/api';
import type { Credentials, StaffUser } from '../journeys/support/fixtures';
import { makePng } from '../journeys/support/png';
import { type CampaignRef, type Fixtures, type Participant, writeState } from './support/state';

/**
 * Arranges the participant-lifecycle suite through the real API only (fresh database: Baseline seed, bootstrap admin):
 *   - staff: 2 reviewers, 1 campaign manager, 2 finance users (created by the admin, passwords set through the reset
 *     link in the dev mailbox); the admin answers support tickets and runs jobs
 *   - payout schedule: no earning hold, 1 USD minimum (so approved earnings are payable at once)
 *   - two published campaigns by the manager: "main" (Instagram + TikTok, lifestyle, 5 USD base + 1 USD first-post
 *     bonus, up to 5 posts, screenshot required) and "other" (X only, gadgets) for the filters
 *   - a "stranger" participant with a submission and a support ticket, whose ids the journey participant must never
 *     be able to open
 * The journey participants (Pat, Pat's friend and the mobile participant) are only *named* here; they register
 * through the UI (or, for the friend, through Pat's referral link) in the specs.
 */
const PASSWORD = 'Lifecycle-Pass#2026!';
const DAY = 86_400_000;

export default async function globalSetup() {
  const runId = Date.now().toString(36);
  const email = (name: string) => `${name}.${runId}@e2e.optimizeall.test`;
  const admin: Credentials = {
    email: process.env.E2E_ADMIN_EMAIL ?? 'e2e-admin@optimizeall.test',
    password: process.env.E2E_ADMIN_PASSWORD ?? 'E2e-Admin#Journey-2026',
    displayName: 'Platform Admin',
  };
  const adminApi = await ApiSession.login(admin.email, admin.password);

  const createStaff = async (key: string, displayName: string, role: string): Promise<StaffUser> => {
    const address = email(key);
    const created = await adminApi.post<{ profile: { id: string } }>('/admin/users/staff', {
      email: address,
      displayName,
      countryCode: 'GB',
      roles: [role],
    });
    const link = await mailLink(address, '/reset-password');
    await publicApi.post('/auth/reset-password', { token: link.searchParams.get('token'), newPassword: PASSWORD });
    return { id: created.profile.id, email: address, password: PASSWORD, displayName };
  };

  const reviewer1 = await createStaff('lc-reviewer1', 'Remy Reviewer', 'Reviewer');
  const reviewer2 = await createStaff('lc-reviewer2', 'Robin Second', 'Reviewer');
  const manager = await createStaff('lc-manager', 'Max Manager', 'CampaignManager');
  const finance1 = await createStaff('lc-finance1', 'Fay Finance', 'Finance');
  const finance2 = await createStaff('lc-finance2', 'Finn Finance', 'Finance');

  const financeApi = await ApiSession.login(finance1.email, finance1.password);
  const schedule = await financeApi.get<{ current: { anchorCutoffDate: string } }>('/finance/payout-schedule');
  await financeApi.put('/finance/payout-schedule', {
    frequency: 'Weekly',
    anchorCutoffDate: schedule.current.anchorCutoffDate,
    cutoffLocalTime: '23:59:59',
    timeZone: 'UTC',
    paymentDelayDays: 2,
    minimumPayoutAmount: 1,
    settlementCurrency: 'USD',
    earningHoldDays: 0,
    autoPrepareBatches: false,
    effectiveFrom: new Date().toISOString(),
    reason: 'E2E participant lifecycle: pay approved earnings without a hold period',
    confirm: true,
  });

  const managerApi = await ApiSession.login(manager.email, manager.password);
  const now = Date.now();
  const createCampaign = async (
    title: string,
    platforms: string[],
    topics: string[],
    maxSubmissions: number,
  ): Promise<CampaignRef> => {
    const hashtag = `#${title.replace(/[^A-Za-z0-9]/g, '')}`;
    const disclosure = `#ad Paid partnership ${runId}`;
    const created = await managerApi.post<{ id: string; slug: string }>('/admin/campaigns', {
      title,
      summary: `Share ${title} with your followers.`,
      description: 'A full-stack end-to-end test campaign for the participant lifecycle.',
      topics,
      visibility: 'Public',
      startsAt: new Date(now - DAY).toISOString(),
      endsAt: new Date(now + 30 * DAY).toISOString(),
      timeZone: 'UTC',
      postingInstructions: 'Post a photo of the product with the hashtag and the disclosure in the caption.',
      defaultDisclosureText: disclosure,
      requiredHashtags: hashtag,
      budgetAmount: 1000,
      budgetCurrency: 'USD',
      maxSubmissionsPerParticipant: maxSubmissions,
      minPostLiveHours: 0,
      requireScreenshot: true,
      eligibility: { minAccountAgeDays: null, minFollowers: 0, requireVerifiedAccount: false },
      platforms,
      rewardRules: {
        currency: 'USD',
        rules: [
          { type: 'BaseRate', amount: 5 },
          { type: 'FirstPostBonus', amount: 1 },
        ],
      },
    });
    const published = await managerApi.post<{ status: string }>(`/admin/campaigns/${created.id}/publish`);
    if (published.status !== 'Active') throw new Error(`${title} is ${published.status}, expected Active`);
    return { id: created.id, slug: created.slug, title, hashtag, disclosure };
  };
  const main = await createCampaign(`Lifecycle Launch ${runId}`, ['Instagram', 'TikTok'], ['lifestyle'], 5);
  const other = await createCampaign(`Gadget Week ${runId}`, ['X'], ['gadgets'], 2);

  const participant = (key: string, displayName: string): Participant => ({
    email: email(key),
    password: PASSWORD,
    displayName,
    instagram: `${key.replace(/[^a-z0-9]/g, '')}.ig.${runId}`,
    tiktok: `${key.replace(/[^a-z0-9]/g, '')}.tt.${runId}`,
    postPrefix: `Lc${key.replace(/[^a-z0-9]/g, '').slice(0, 4)}${runId}`,
  });

  // The stranger: registered, verified, one qualifying Instagram profile, one submission and one ticket.
  const stranger = participant('stranger', 'Sam Stranger');
  await publicApi.post('/auth/register', {
    email: stranger.email,
    password: stranger.password,
    displayName: stranger.displayName,
    countryCode: 'GB',
    languageCode: 'en',
    timeZone: 'UTC',
    acceptTerms: true,
  });
  const verify = await mailLink(stranger.email, '/verify-email', /verify/i);
  await publicApi.post('/auth/verify-email', { token: verify.searchParams.get('token') });
  const strangerApi = await ApiSession.login(stranger.email, stranger.password);
  const account = await strangerApi.post<{ id: string }>('/me/social-accounts', {
    platform: 'Instagram',
    handle: stranger.instagram,
    profileUrl: `https://www.instagram.com/${stranger.instagram}`,
    accountCreatedAt: new Date(now - 400 * DAY).toISOString(),
    followerCount: 5000,
  });
  const form = new FormData();
  form.append('campaignId', main.id);
  form.append('socialAccountId', account.id);
  form.append('platform', 'Instagram');
  form.append('postUrl', `https://www.instagram.com/p/${stranger.postPrefix}A/`);
  form.append('postedAt', new Date(now - 60_000).toISOString());
  form.append('captionText', `Stranger post ${main.hashtag}`);
  form.append('screenshot', new Blob([makePng(90)], { type: 'image/png' }), 'stranger.png');
  const strangerSubmission = await strangerApi.upload<{ id: string }>('/me/submissions', form);
  const strangerTicket = await strangerApi.post<{ id: string }>('/me/support/tickets', {
    subject: 'Stranger question',
    category: 'General',
    body: 'A question only the stranger may read.',
  });

  const fixtures: Fixtures = {
    runId,
    admin,
    reviewer1,
    reviewer2,
    manager,
    finance1,
    finance2,
    main,
    other,
    pat: participant('pat', 'Pat Lifecycle'),
    friend: participant('friend', 'Frankie Friend'),
    mobile: participant('mobi', 'Mo Mobile'),
    stranger: {
      email: stranger.email,
      password: stranger.password,
      displayName: stranger.displayName,
      submissionId: strangerSubmission.id,
      ticketId: strangerTicket.id,
    },
    memo: {},
  };
  writeState(fixtures);
}
