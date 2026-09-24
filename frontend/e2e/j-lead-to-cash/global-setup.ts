import { ApiSession } from '../journeys/support/api';
import { accounts, writeState } from './support/journey';

/**
 * The lead-to-cash journey needs the Demo seed (scripts/e2e-journeys.sh seeds Baseline,Demo for E2E_SUITE=j-lead-to-cash)
 * and the dev mailbox (file-mode email). Fails fast when a demo actor cannot sign in; then writes the run id the specs
 * use to name the records they create.
 */
export default async function globalSetup() {
  for (const key of ['admin', 'am', 'sales', 'finance', 'finance2', 'nimbusOwner'] as const) {
    const user = accounts[key];
    try {
      await ApiSession.login(user.email, user.password);
    } catch (error) {
      throw new Error(
        `Demo account "${key}" (${user.email}) cannot sign in — is the API running with the Demo seed ` +
          `(E2E_SUITE=j-lead-to-cash scripts/e2e-journeys.sh)? ${String(error)}`,
      );
    }
  }
  writeState({ runId: Date.now().toString(36) });
}
