/**
 * Tells the app's other tabs (same browser profile) that the user signed out, so none of them keeps showing the
 * portal on its in-memory access token. The refresh cookie is shared by every tab, so a sign-out in one tab ends the
 * session for all of them; this makes the other tabs leave at once instead of on their next refresh.
 */
const CHANNEL = 'optimizeall-auth';

type CrossTabMessage = { type: 'signed-out' };

function channel(): BroadcastChannel | null {
  return typeof BroadcastChannel === 'function' ? new BroadcastChannel(CHANNEL) : null;
}

/** Announces a deliberate sign-out to the other tabs. */
export function announceSignOut(): void {
  const ch = channel();
  if (!ch) return;
  ch.postMessage({ type: 'signed-out' } satisfies CrossTabMessage);
  ch.close();
}

/** Calls `listener` when another tab signs out. Returns an unsubscribe function. */
export function onSignOutElsewhere(listener: () => void): () => void {
  const ch = channel();
  if (!ch) return () => undefined;
  ch.onmessage = (event: MessageEvent<CrossTabMessage>) => {
    if (event.data?.type === 'signed-out') listener();
  };
  return () => ch.close();
}
