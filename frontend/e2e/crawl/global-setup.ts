import { ApiSession, roles } from './support/crawl';

/**
 * The crawl needs the Demo seed (every role's account) — scripts/e2e-journeys.sh seeds it for E2E_SUITE=crawl. Fails
 * fast, with a clear message, when an account cannot sign in instead of letting every role's crawl time out.
 */
export default async function globalSetup() {
  for (const role of roles) {
    try {
      await ApiSession.login(role.user.email, role.user.password);
    } catch (error) {
      throw new Error(
        `Demo account "${role.key}" (${role.user.email}) cannot sign in — is the API running with E2E_SUITE=crawl ` +
          `scripts/e2e-journeys.sh (Demo seed)? ${String(error)}`,
      );
    }
  }
}
