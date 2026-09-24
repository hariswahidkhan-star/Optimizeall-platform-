import { API_URL, ApiSession } from '../journeys/support/api';
import { accounts, inviteClientUser, writeState } from './support/social';
import { STUB_PORT, startStub } from './support/stub';

/**
 * j-social runs against Baseline + Demo with the file-mode dev mailbox (E2E_SUITE=j-social scripts/e2e-journeys.sh).
 * Fails fast when a demo actor cannot sign in or the mailbox is off, starts the local Graph API stub the API publishes
 * to, and creates the journey's own client — client approval required, an Approver and a Viewer client user.
 */
export default async function globalSetup() {
  const hint = 'is the API running with the Demo seed (E2E_SUITE=j-social scripts/e2e-journeys.sh)?';
  for (const [key, user] of Object.entries(accounts)) {
    try {
      await ApiSession.login(user.email, user.password);
    } catch (error) {
      throw new Error(`Demo account "${key}" (${user.email}) cannot sign in — ${hint} ${String(error)}`);
    }
  }
  const mailbox = await fetch(`${API_URL}/api/v1/dev/mailbox?to=nobody@e2e.optimizeall.test`);
  if (mailbox.status !== 404 && mailbox.status !== 200)
    throw new Error(`GET /dev/mailbox answered ${mailbox.status}: the dev mailbox is off — ${hint}`);

  let stub;
  try {
    stub = await startStub();
  } catch (error) {
    throw new Error(`Cannot serve the Graph API stub on :${STUB_PORT} (E2E_STUB_PORT): ${String(error)}`);
  }

  const runId = Date.now().toString(36);
  const am = await ApiSession.login(accounts.am.email, accounts.am.password);
  const name = `Helio Coffee ${runId}`;
  const client = await am.post<{ id: string; name: string; currency: string }>('/agency/clients', {
    name,
    industry: 'Coffee roasting',
    website: 'https://helio-coffee.example.com',
    countryCode: 'DE',
    timeZone: 'Europe/Berlin',
    currency: 'EUR',
    status: 'Active',
  });
  const social = await ApiSession.login(accounts.socialManager.email, accounts.socialManager.password);
  const settings = await social.get<{ concurrencyStamp: string }>(
    `/agency/social/clients/${client.id}/settings`,
  );
  await social.put(`/agency/social/clients/${client.id}/settings`, {
    requireClientApproval: true,
    defaultUtmMedium: 'social',
    concurrencyStamp: settings.concurrencyStamp,
  });
  const approver = await inviteClientUser(
    client.id,
    `approver.${runId}@helio.e2e.optimizeall.test`,
    `Helio Approver ${runId}`,
    'Approver',
  );
  const viewer = await inviteClientUser(
    client.id,
    `viewer.${runId}@helio.e2e.optimizeall.test`,
    `Helio Viewer ${runId}`,
    'Viewer',
  );
  writeState({ runId, client: { id: client.id, name, currency: 'EUR' }, approver, viewer });

  return async () => {
    await new Promise<void>((resolve) => stub.close(() => resolve()));
  };
}
