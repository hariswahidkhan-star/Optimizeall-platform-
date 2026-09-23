const STORAGE_KEY = 'oa.deviceId';

let memoryFallback: string | null = null;

function randomId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

/**
 * A random per-browser identifier sent on registration as a referral-fraud signal (the API stores only its hash).
 * Persisted in localStorage when available; falls back to a per-tab value in private mode.
 */
export function getDeviceId(): string {
  try {
    const existing = window.localStorage.getItem(STORAGE_KEY);
    if (existing) return existing;
    const id = randomId();
    window.localStorage.setItem(STORAGE_KEY, id);
    return id;
  } catch {
    memoryFallback ??= randomId();
    return memoryFallback;
  }
}
