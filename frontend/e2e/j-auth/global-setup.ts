import { API_URL, ApiSession } from '../journeys/support/api';
import { accounts, writeState } from './support/auth';

/**
 * The j-auth suite needs the Demo seed, the dev mailbox, the non-production test sign-in and Google sign-in left
 * unconfigured — scripts/e2e-journeys.sh sets all of them for E2E_SUITE=j-auth. Fails fast when any is missing, then
 * writes the run id the specs use to name the accounts they register.
 */
export default async function globalSetup() {
  const hint = 'is the API running with E2E_SUITE=j-auth scripts/e2e-journeys.sh?';
  for (const [key, user] of Object.entries(accounts)) {
    if (key === 'suspended') continue; // cannot sign in by design
    try {
      await ApiSession.login(user.email, user.password);
    } catch (error) {
      throw new Error(`Demo account "${key}" (${user.email}) cannot sign in — ${hint} ${String(error)}`);
    }
  }
  if (!process.env.E2E_MAIL_DIR) throw new Error(`E2E_MAIL_DIR is not set — ${hint}`);
  const testAccounts = await fetch(`${API_URL}/api/v1/dev/test-accounts`);
  if (testAccounts.status !== 200) throw new Error(`The test sign-in is off — ${hint}`);
  const providers = (await (await fetch(`${API_URL}/api/v1/auth/providers`)).json()) as {
    google?: { enabled?: boolean };
  };
  if (providers.google?.enabled)
    throw new Error(`Google sign-in is configured; the suite expects it off — ${hint}`);
  writeState({ runId: Date.now().toString(36), memo: {} });
}
