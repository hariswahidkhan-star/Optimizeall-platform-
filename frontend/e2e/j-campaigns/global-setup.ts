import { existsSync } from 'node:fs';
import { ApiSession } from '../journeys/support/api';
import { STATE_FILE, addProfiles, createStaff, registerParticipant, writeState } from './support/campaigns';

/**
 * Arranges the campaign-manager + reviewer journey through the real API (fresh database, Baseline + Demo seed,
 * bootstrap admin): two campaign managers, two reviewers, a reviewer who is also a participant, and three verified
 * participants with an Instagram and a TikTok profile declared 400 days old. Everything is named after the run id.
 */
export default async function globalSetup() {
  // Debugging aid: E2E_REUSE_STATE=1 keeps the previous run's users and campaigns so a single spec can be rerun.
  if (process.env.E2E_REUSE_STATE === '1' && existsSync(STATE_FILE)) return;
  const runId = Date.now().toString(36);
  const email = (name: string) => `${name}.${runId}@campaigns.e2e.optimizeall.test`;
  const admin = {
    email: process.env.E2E_ADMIN_EMAIL ?? 'e2e-admin@optimizeall.test',
    password: process.env.E2E_ADMIN_PASSWORD ?? 'E2e-Admin#Journey-2026',
    displayName: 'Platform Admin',
  };
  const adminApi = await ApiSession.login(admin.email, admin.password);

  const manager = await createStaff(adminApi, email('manager'), 'Morgan Manager', ['CampaignManager']);
  const manager2 = await createStaff(adminApi, email('manager2'), 'Max Manager', ['CampaignManager']);
  const reviewer1 = await createStaff(adminApi, email('reviewer1'), 'Riley Reviewer', ['Reviewer']);
  const reviewer2 = await createStaff(adminApi, email('reviewer2'), 'Sasha Second', ['Reviewer']);
  const dualUser = await createStaff(adminApi, email('dual'), 'Dana Dual', ['Reviewer', 'Participant']);
  const dual = await addProfiles(dualUser, `dana${runId}`);

  const participants = [];
  for (const [i, [name, country]] of (
    [
      ['Paula Poster', 'GB'],
      ['Omar Poster', 'PK'],
      ['Tess Poster', 'GB'],
    ] as const
  ).entries()) {
    const user = await registerParticipant(email(`poster${i + 1}`), name, country);
    participants.push(await addProfiles(user, `poster${i + 1}${runId}`));
  }

  writeState({ runId, admin, manager, manager2, reviewer1, reviewer2, dual, participants });
}
