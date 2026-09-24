import { ApiSession } from '../journeys/support/api';
import { actors } from './support/a11y';

/**
 * The a11y suite audits the Demo seed's pages as its demo accounts (scripts/e2e-journeys.sh with E2E_SUITE=a11y seeds
 * Baseline + Demo). Fails fast, with a clear message, when an account cannot sign in.
 */
export default async function globalSetup() {
  for (const [key, user] of Object.entries(actors)) {
    try {
      await ApiSession.login(user.email, user.password);
    } catch (error) {
      throw new Error(
        `Demo account "${key}" (${user.email}) cannot sign in — is the API running with the Demo seed ` +
          `(E2E_SUITE=a11y E2E_DB_PROVIDER=sqlite scripts/e2e-journeys.sh)? ${String(error)}`,
      );
    }
  }
}
