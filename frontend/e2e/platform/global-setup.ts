import { API_URL, ApiSession } from '../journeys/support/api';
import { accounts, writeState } from './support/platform';

/**
 * The platform suite needs the Demo seed, the non-production test sign-in (DevTools:TestLoginEnabled in a non-Production
 * environment) and Google sign-in left unconfigured — scripts/e2e-journeys.sh sets all three for E2E_SUITE=platform.
 * Fails fast, with a clear message, when any of them is missing; then writes the run id the specs use to name the
 * records they create.
 */
export default async function globalSetup() {
  const hint = 'is the API running with E2E_SUITE=platform scripts/e2e-journeys.sh?';
  for (const [key, user] of Object.entries(accounts)) {
    try {
      await ApiSession.login(user.email, user.password);
    } catch (error) {
      throw new Error(`Demo account "${key}" (${user.email}) cannot sign in — ${hint} ${String(error)}`);
    }
  }
  const testAccounts = await fetch(`${API_URL}/api/v1/dev/test-accounts`);
  if (testAccounts.status !== 200)
    throw new Error(
      `GET /dev/test-accounts answered ${testAccounts.status}: the test sign-in is off — ${hint}`,
    );
  const providers = (await (await fetch(`${API_URL}/api/v1/auth/providers`)).json()) as {
    google?: { enabled?: boolean };
  };
  if (providers.google?.enabled)
    throw new Error(`Google sign-in is configured on the API under test; the suite expects it off — ${hint}`);
  writeState({ runId: Date.now().toString(36) });
}
