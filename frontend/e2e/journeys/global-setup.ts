import { ApiSession, mailLink, publicApi } from './support/api';
import { type Credentials, type Fixtures, type StaffUser, writeFixtures } from './support/fixtures';

/**
 * Arranges the journey data through the real API only (the database is fresh: Baseline seed, bootstrap admin):
 *   - staff: 2 reviewers, 1 campaign manager, 2 finance users — created by the admin (POST /admin/users/staff);
 *     each sets its password through the reset link in the dev mailbox
 *   - a referrer participant (registered + verified through the API) whose referral code the journeys use
 *   - payout schedule: earning hold 0 days and a 1 USD minimum, so approved earnings are payable at once
 *     (03-finance moves the cutoff; see there)
 *   - a published campaign created by the campaign manager: Instagram + TikTok, base rate 5 USD, first-post bonus
 *     1 USD, no minimum live time, max 3 submissions, started yesterday
 * Credentials and ids are written to .state/fixtures.json for the specs.
 */
const PASSWORD = 'Journey-Pass#2026!';
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
    await publicApi.post('/auth/reset-password', {
      token: link.searchParams.get('token'),
      newPassword: PASSWORD,
    });
    return { id: created.profile.id, email: address, password: PASSWORD, displayName };
  };

  const reviewer1 = await createStaff('reviewer1', 'Riley Reviewer', 'Reviewer');
  const reviewer2 = await createStaff('reviewer2', 'Sasha Second', 'Reviewer');
  const manager = await createStaff('manager', 'Morgan Manager', 'CampaignManager');
  const finance1 = await createStaff('finance1', 'Frankie Finance', 'Finance');
  const finance2 = await createStaff('finance2', 'Fern Finance', 'Finance');

  // Referrer: an ordinary verified participant.
  const referrerEmail = email('referrer');
  await publicApi.post('/auth/register', {
    email: referrerEmail,
    password: PASSWORD,
    displayName: 'Rae Referrer',
    countryCode: 'GB',
    languageCode: 'en',
    timeZone: 'UTC',
    acceptTerms: true,
  });
  const verify = await mailLink(referrerEmail, '/verify-email');
  await publicApi.post('/auth/verify-email', { token: verify.searchParams.get('token') });
  const referrerApi = await ApiSession.login(referrerEmail, PASSWORD);
  const referrerProfile = await referrerApi.get<{ id: string; referralCode: string }>('/me/profile');

  // Payout schedule: no hold, low minimum (finance user; payouts.settings). Test arrangement, not under test.
  const financeApi = await ApiSession.login(finance1.email, finance1.password);
  const schedule = await financeApi.get<{ current: { anchorCutoffDate: string } }>(
    '/finance/payout-schedule',
  );
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
    reason: 'E2E journeys: pay approved earnings without a hold period',
    confirm: true,
  });

  // Campaign, created and published by the campaign manager.
  const managerApi = await ApiSession.login(manager.email, manager.password);
  const title = `Journey Launch ${runId}`;
  const disclosure = `#ad Paid partnership ${runId}`;
  const hashtag = `#JourneyLaunch${runId}`;
  const now = Date.now();
  const campaign = await managerApi.post<{ id: string; slug: string }>('/admin/campaigns', {
    title,
    summary: 'Share the journey launch with your followers.',
    description: 'A full-stack end-to-end test campaign.',
    topics: ['lifestyle'],
    visibility: 'Public',
    startsAt: new Date(now - DAY).toISOString(),
    endsAt: new Date(now + 30 * DAY).toISOString(),
    timeZone: 'UTC',
    postingInstructions: 'Post a photo of the product with the hashtag and the disclosure in the caption.',
    defaultDisclosureText: disclosure,
    requiredHashtags: hashtag,
    budgetAmount: 1000,
    budgetCurrency: 'USD',
    maxSubmissionsPerParticipant: 3,
    minPostLiveHours: 0,
    requireScreenshot: true,
    eligibility: { minAccountAgeDays: null, minFollowers: 0, requireVerifiedAccount: false },
    platforms: ['Instagram', 'TikTok'],
    rewardRules: {
      currency: 'USD',
      rules: [
        { type: 'BaseRate', amount: 5 },
        { type: 'FirstPostBonus', amount: 1 },
      ],
    },
  });
  const published = await managerApi.post<{ status: string }>(`/admin/campaigns/${campaign.id}/publish`);
  if (published.status !== 'Active') throw new Error(`Campaign is ${published.status}, expected Active`);

  const participant = (key: string, name: string, seed: string) => ({
    email: email(key),
    password: PASSWORD,
    displayName: name,
    instagram: `${key.replace(/[^a-z0-9]/g, '')}.ig.${runId}`,
    tiktok: `${key.replace(/[^a-z0-9]/g, '')}.tt.${runId}`,
    // Instagram shortcode of the journey's first post (case-sensitive, unique per run and project).
    postCode: `Jr${seed}${runId}`,
  });

  const fixtures: Fixtures = {
    runId,
    admin,
    reviewer1,
    reviewer2,
    manager,
    finance1,
    finance2,
    referrer: {
      id: referrerProfile.id,
      email: referrerEmail,
      password: PASSWORD,
      displayName: 'Rae Referrer',
      referralCode: referrerProfile.referralCode,
    },
    campaign: { id: campaign.id, slug: campaign.slug, title, disclosure, hashtag },
    participants: {
      'desktop-chromium': participant('pat-desktop', 'Pat Desktop', 'Dk'),
      'mobile-chromium': participant('pat-mobile', 'Pat Mobile', 'Mb'),
    },
  };
  writeFixtures(fixtures);
}
