import { existsSync } from 'node:fs';
import { ApiSession } from '../journeys/support/api';
import { STATE_FILE, accounts, writeState } from './support/content';

/**
 * Arranges the content journey through the real API (fresh database, Baseline + Demo seed): a custom role
 * "Website editor <run>" with only site.manage + blog.publish, and a test account (ContentCreator) that holds it, so the
 * suite proves that content permissions granted by a custom role work end to end — and that the account can be
 * impersonated (it is not an admin).
 */
export default async function globalSetup() {
  // Debugging aid: E2E_REUSE_STATE=1 keeps the previous run's editor so a single spec can be rerun.
  if (process.env.E2E_REUSE_STATE === '1' && existsSync(STATE_FILE)) return;
  const runId = Date.now().toString(36);
  const admin = await ApiSession.login(accounts.admin.email, accounts.admin.password);

  const role = await admin.post<{ role: { id: string } }>('/admin/roles', {
    name: `Website editor ${runId}`,
    description: 'Public website content and blog publishing (e2e j-content).',
    permissions: ['site.manage', 'blog.publish'],
  });
  const user = await admin.post<{ id: string; email: string; displayName: string; password: string }>(
    '/admin/test-users',
    { roles: ['ContentCreator'], displayName: `Wendy Web ${runId}` },
  );
  await admin.put(`/admin/roles/${role.role.id}/users/${user.id}`);

  writeState({
    runId,
    roleId: role.role.id,
    editor: { id: user.id, email: user.email, password: user.password, displayName: user.displayName },
  });
}
