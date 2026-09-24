import { API_URL, ApiSession } from '../journeys/support/api';
import { accounts, writeState } from './support/jadmin';

/**
 * The platform-administration journey suite needs the Demo seed and the non-production test sign-in
 * (DevTools:TestLoginEnabled in a non-Production environment) — scripts/e2e-journeys.sh sets both for
 * E2E_SUITE=j-admin. Fails fast, with a clear message, when either is missing; then writes the run id the specs use to
 * name the records they create.
 */
export default async function globalSetup() {
  const hint = 'is the API running with E2E_SUITE=j-admin scripts/e2e-journeys.sh?';
  for (const [key, user] of Object.entries(accounts)) {
    try {
      await ApiSession.login(user.email, user.password);
    } catch (error) {
      throw new Error(`Demo account "${key}" (${user.email}) cannot sign in — ${hint} ${String(error)}`);
    }
  }
  const testAccounts = await fetch(`${API_URL}/api/v1/dev/test-accounts`);
  if (testAccounts.status !== 200)
    throw new Error(`GET /dev/test-accounts answered ${testAccounts.status}: the test sign-in is off — ${hint}`);
  writeState({ runId: Date.now().toString(36) });
}
