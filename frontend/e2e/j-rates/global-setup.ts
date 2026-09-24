import { existsSync } from 'node:fs';
import { ApiSession } from '../journeys/support/api';
import { STATE_FILE, addProfiles, createStaff, registerParticipant, writeState } from './support/rates';

const DAY = 86_400_000;

/**
 * Arranges the person-level pricing journey through the real API (fresh database, Baseline + Demo seed, bootstrap
 * admin): a campaign manager, a reviewer, a finance user, two verified participants (Instagram + TikTok, 5,000
 * followers) and a published USD campaign whose own base rate is 5 USD. Everything is named after the run id.
 */
export default async function globalSetup() {
  if (process.env.E2E_REUSE_STATE === '1' && existsSync(STATE_FILE)) return;
  const runId = Date.now().toString(36);
  const email = (name: string) => `${name}.${runId}@rates.e2e.optimizeall.test`;
  const admin = {
    email: process.env.E2E_ADMIN_EMAIL ?? 'e2e-admin@optimizeall.test',
    password: process.env.E2E_ADMIN_PASSWORD ?? 'E2e-Admin#Journey-2026',
    displayName: 'Platform Admin',
  };
  const adminApi = await ApiSession.login(admin.email, admin.password);
  const manager = await createStaff(adminApi, email('manager'), 'Rhea Rates', ['CampaignManager']);
  const reviewer = await createStaff(adminApi, email('reviewer'), 'Ravi Reviewer', ['Reviewer']);
  const finance = await createStaff(adminApi, email('finance'), 'Fiona Finance', ['Finance']);

  const participants = [];
  for (const [i, [name, country]] of (
    [
      ['Ivy Influencer', 'GB'],
      ['Milo Micro', 'PK'],
    ] as const
  ).entries()) {
    const user = await registerParticipant(email(`creator${i + 1}`), `${name} ${runId}`, country);
    participants.push(await addProfiles(user, `creator${i + 1}${runId}`));
  }

  const managerApi = await ApiSession.login(manager.email, manager.password);
  const now = Date.now();
  const created = await managerApi.post<{ id: string; slug: string; title: string }>('/admin/campaigns', {
    title: `Rates Launch ${runId}`,
    summary: 'Share the launch.',
    description: '',
    topics: [],
    visibility: 'Public',
    startsAt: new Date(now - 3600_000).toISOString(),
    endsAt: new Date(now + 20 * DAY).toISOString(),
    timeZone: 'UTC',
    postingInstructions: 'Post about the launch.',
    defaultDisclosureText: '#ad',
    maxSubmissionsPerParticipant: 10,
    minPostLiveHours: 0,
    requireScreenshot: true,
    eligibility: { minAccountAgeDays: null, minFollowers: 0, requireVerifiedAccount: false },
    platforms: ['Instagram', 'TikTok'],
    rewardRules: { currency: 'USD', rules: [{ type: 'BaseRate', amount: 5 }] },
  });
  await managerApi.post(`/admin/campaigns/${created.id}/publish`);

  writeState({
    runId,
    admin,
    manager,
    reviewer,
    finance,
    participants,
    campaign: created,
    // The personal deal (spec 02) runs until 5 minutes after setup; spec 04 waits for it to lapse.
    dealExpiresAt: now + 5 * 60_000,
  });
}
