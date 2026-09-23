import { ApiSession } from '../journeys/support/api';
import { accounts, writeState } from './support/agency';

/**
 * The agency suite needs the Demo seed profile (scripts/e2e-journeys.sh seeds Baseline,Demo for E2E_SUITE=agency).
 * Fails fast, with a clear message, when a demo actor cannot sign in; then writes the run id the specs use to name
 * the records they create.
 */
export default async function globalSetup() {
  for (const [key, user] of Object.entries(accounts)) {
    try {
      await ApiSession.login(user.email, user.password);
    } catch (error) {
      throw new Error(
        `Demo account "${key}" (${user.email}) cannot sign in — is the API running with the Demo seed ` +
          `(E2E_SUITE=agency scripts/e2e-journeys.sh)? ${String(error)}`,
      );
    }
  }
  writeState({ runId: Date.now().toString(36) });
}
