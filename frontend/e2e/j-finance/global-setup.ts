import { ApiSession, mailLink, publicApi } from '../journeys/support/api';
import { type FinanceState, type Participant, accounts, writeState } from './support/finance';

/**
 * Arranges the finance journey through the real API (no database edits). Needs the Demo seed and the dev mailbox —
 * scripts/e2e-journeys.sh sets both for E2E_SUITE=j-finance:
 *   - checks every Demo actor of the journey can sign in;
 *   - registers five participants (verified through the mailbox link) and saves payout details for four of them — Eve
 *     has none, so her item is held for "No payout details on file";
 *   - creates a test account (Participant) through Admin → test users: it is never paid.
 * Earnings are NOT arranged here: the journey creates them through the ledger UI (01-ledger.spec.ts).
 */
const PASSWORD = 'Finance-Journey#2026!';

export default async function globalSetup() {
  const hint =
    'is the API running with E2E_SUITE=j-finance scripts/e2e-journeys.sh (Demo seed, dev mailbox)?';
  const sessions: Record<string, ApiSession> = {};
  for (const [key, user] of Object.entries(accounts)) {
    try {
      sessions[key] = await ApiSession.login(user.email, user.password);
    } catch (error) {
      throw new Error(`Demo account "${key}" (${user.email}) cannot sign in — ${hint} ${String(error)}`);
    }
  }
  const runId = Date.now().toString(36);
  const email = (name: string) => `${name}.${runId}@finance-journey.test`;

  const register = async (
    key: string,
    displayName: string,
    payout?: { method: string; destination: string },
  ) => {
    const address = email(key);
    await publicApi.post('/auth/register', {
      email: address,
      password: PASSWORD,
      displayName,
      countryCode: 'GB',
      languageCode: 'en',
      timeZone: 'UTC',
      acceptTerms: true,
    });
    const verify = await mailLink(address, '/verify-email');
    await publicApi.post('/auth/verify-email', { token: verify.searchParams.get('token') });
    const session = await ApiSession.login(address, PASSWORD);
    const profile = await session.get<{ id: string }>('/me/profile');
    if (payout)
      await session.put('/me/payout-profile', {
        method: payout.method,
        accountHolderName: displayName,
        destination: payout.destination,
        preferredCurrency: 'USD',
        countryCode: 'GB',
      });
    return { id: profile.id, email: address, password: PASSWORD, displayName } satisfies Participant;
  };

  const participants = {
    ana: await register('ana', `Ana Finance ${runId}`, {
      method: 'PayPal',
      destination: `ana.${runId}@example.com`,
    }),
    ben: await register('ben', `Ben Finance ${runId}`, {
      method: 'BankTransfer',
      destination: 'GB82WEST12345698765432',
    }),
    cat: await register('cat', `Cat Finance ${runId}`, {
      method: 'PayPal',
      destination: `cat.${runId}@example.com`,
    }),
    dan: await register('dan', `Dan Finance ${runId}`, {
      method: 'PayPal',
      destination: `dan.${runId}@example.com`,
    }),
    eve: await register('eve', `Eve Finance ${runId}`),
  };

  const tessUser = await sessions.admin!.post<{
    id: string;
    email: string;
    password: string;
    displayName: string;
  }>('/admin/test-users', { roles: ['Participant'], displayName: `Tess Test ${runId}`, countryCode: 'GB' });
  const tessSession = await ApiSession.login(tessUser.email, tessUser.password);
  await tessSession.put('/me/payout-profile', {
    method: 'PayPal',
    accountHolderName: tessUser.displayName,
    destination: `tess.${runId}@example.com`,
    preferredCurrency: 'USD',
    countryCode: 'GB',
  });

  const clients = await sessions.finance1!.get<{ id: string; slug: string }[]>('/agency/billing/clients');
  const clientId = (slug: string) => {
    const found = clients.find((c) => c.slug === slug);
    if (!found) throw new Error(`Demo client ${slug} not found — ${hint}`);
    return found.id;
  };

  const state: FinanceState = {
    runId,
    participants,
    tess: {
      id: tessUser.id,
      email: tessUser.email,
      password: tessUser.password,
      displayName: tessUser.displayName,
    },
    clientIds: {
      nimbus: clientId('nimbus-fitness'),
      karachi: clientId('karachi-eats'),
      aurora: clientId('aurora-skincare'),
      wanderly: clientId('wanderly-travel'),
    },
  };
  writeState(state);
}
