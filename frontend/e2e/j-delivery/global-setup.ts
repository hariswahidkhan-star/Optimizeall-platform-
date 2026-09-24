import { API_URL, ApiSession } from '../journeys/support/api';
import { accounts, writeState } from './support/delivery';

/**
 * The j-delivery suite runs against the Demo seed with the file-mode dev mailbox (scripts/e2e-journeys.sh with
 * E2E_SUITE=j-delivery). Fails fast, with a clear message, when a demo actor cannot sign in or the mailbox is off; then
 * starts a fresh run state (run id) that the specs fill in as the journey goes.
 */
export default async function globalSetup() {
  const hint = 'is the API running with the Demo seed (E2E_SUITE=j-delivery scripts/e2e-journeys.sh)?';
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
  writeState({ runId: Date.now().toString(36) });
}
