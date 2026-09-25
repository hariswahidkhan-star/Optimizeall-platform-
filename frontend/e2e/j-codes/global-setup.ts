import { existsSync } from 'node:fs';
import { ApiSession } from '../journeys/support/api';
import { STATE_FILE, createStaff, registerParticipant, writeState } from './support/codes';

/**
 * Arranges the discount-code journey through the real API (fresh database, Baseline + Demo seed, bootstrap admin): a
 * campaign manager, a reviewer, a finance user and two verified participants. Everything else (the program, codes,
 * group, sales) is built by the specs.
 */
export default async function globalSetup() {
  if (process.env.E2E_REUSE_STATE === '1' && existsSync(STATE_FILE)) return;
  const runId = Date.now().toString(36);
  const email = (name: string) => `${name}.${runId}@codes.e2e.optimizeall.test`;
  const admin = {
    email: process.env.E2E_ADMIN_EMAIL ?? 'e2e-admin@optimizeall.test',
    password: process.env.E2E_ADMIN_PASSWORD ?? 'E2e-Admin#Journey-2026',
    displayName: 'Platform Admin',
  };
  const adminApi = await ApiSession.login(admin.email, admin.password);
  const manager = await createStaff(adminApi, email('manager'), 'Cora Codes', ['CampaignManager']);
  const reviewer = await createStaff(adminApi, email('reviewer'), 'Remy Reviewer', ['Reviewer']);
  const finance = await createStaff(adminApi, email('finance'), 'Fern Finance', ['Finance']);
  const ivy = await registerParticipant(email('ivy'), `Ivy Seller ${runId}`, 'GB');
  const milo = await registerParticipant(email('milo'), `Milo Squad ${runId}`, 'PK');
  writeState({ runId, admin, manager, reviewer, finance, ivy, milo });
}
