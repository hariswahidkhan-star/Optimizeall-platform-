import { API_URL, ApiSession, mailLink, publicApi } from '../journeys/support/api';
import { CLIENT_PASSWORD, accounts, writeState } from './support/email';

/**
 * The j-email suite runs against the Demo seed with the file-mode dev mailbox and background jobs off
 * (E2E_SUITE=j-email scripts/e2e-journeys.sh). Fails fast when a demo actor cannot sign in or the mailbox is off, then
 * onboards the journey's own client through the API — so its email workspace starts empty (no settings, senders, lists
 * or suppressions) — and invites the client's owner, who chooses a password through the emailed link.
 */
export default async function globalSetup() {
  const hint = 'is the API running with the Demo seed (E2E_SUITE=j-email scripts/e2e-journeys.sh)?';
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
  if (!process.env.E2E_MAIL_DIR) throw new Error(`E2E_MAIL_DIR is not set — ${hint}`);

  const runId = Date.now().toString(36);
  const am = await ApiSession.login(accounts.am.email, accounts.am.password);
  const name = `Lumen Labs ${runId}`;
  const client = await am.post<{ id: string; name: string }>('/agency/clients', {
    name,
    summary: 'Email-marketing journey client (E2E).',
    industry: 'Software',
    countryCode: 'GB',
    timeZone: 'Europe/London',
    currency: 'GBP',
    status: 'Active',
  });

  const ownerEmail = `owner.${runId}@e2e-mail.optimizeall.test`;
  const invited = await am.post<{ member: { userId: string } }>(`/agency/clients/${client.id}/members`, {
    email: ownerEmail,
    displayName: 'Lena Lumen',
    role: 'Owner',
  });
  const link = await mailLink(ownerEmail, '/reset-password');
  await publicApi.post('/auth/reset-password', {
    token: link.searchParams.get('token'),
    newPassword: CLIENT_PASSWORD,
  });

  writeState({
    runId,
    client: { id: client.id, name },
    owner: {
      id: invited.member.userId,
      email: ownerEmail,
      password: CLIENT_PASSWORD,
      displayName: 'Lena Lumen',
    },
    memo: {},
  });
}
