import { safeNextPath } from '@/app/redirects';

/**
 * The direct "Enrol for free — start learning" flow (docs/LEARNING.md § "Direct enrol"):
 *
 * - signed in → enrol (idempotent) and open the first unfinished lesson in the portal;
 * - signed out → sign in / register with `next=/learn/<slug>?enrol=1` (a same-origin path, validated by
 *   {@link safeNextPath} everywhere it is read). Back on the course page with `?enrol=1` and a session, the enrolment
 *   completes automatically and lesson 1 opens.
 *
 * Registration goes through email verification (possibly in another tab, where the `next` parameter is gone), so the
 * intent is also remembered in this browser for a week: the sign-in page falls back to it when it has no `next`.
 */
const KEY = 'oa.learn.enrolIntent';
const TTL_MS = 7 * 24 * 60 * 60 * 1000;
const SLUG_RE = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

export const ENROL_PARAM = 'enrol';

/** The course page that finishes the enrolment after sign-in: /learn/<slug>?enrol=1. */
export function enrolReturnPath(slug: string): string {
  return `/learn/${encodeURIComponent(slug)}?${ENROL_PARAM}=1`;
}

/** Sign-in or registration URL that comes back to the course and enrols. */
export function authPathForEnrol(slug: string, mode: 'login' | 'register'): string {
  return `/${mode}?next=${encodeURIComponent(enrolReturnPath(slug))}`;
}

interface Stored {
  slug: string;
  at: number;
}

function storage(): Storage | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage;
  } catch {
    return null;
  }
}

export function rememberEnrolIntent(slug: string, now = Date.now()): void {
  if (!SLUG_RE.test(slug)) return;
  try {
    storage()?.setItem(KEY, JSON.stringify({ slug, at: now } satisfies Stored));
  } catch {
    /* private mode or blocked storage: the ?next= parameter still carries the intent */
  }
}

/** The remembered course slug (within a week), or null. */
export function pendingEnrolSlug(now = Date.now()): string | null {
  try {
    const raw = storage()?.getItem(KEY);
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<Stored>;
    if (typeof value.slug !== 'string' || !SLUG_RE.test(value.slug) || typeof value.at !== 'number') return null;
    if (now - value.at > TTL_MS || value.at > now + 60_000) return null;
    return value.slug;
  } catch {
    return null;
  }
}

/** Where to go after sign-in when the page has no `next`: the remembered course's enrol path, or null. */
export function pendingEnrolPath(now = Date.now()): string | null {
  const slug = pendingEnrolSlug(now);
  return slug ? safeNextPath(enrolReturnPath(slug)) : null;
}

export function clearEnrolIntent(): void {
  try {
    storage()?.removeItem(KEY);
  } catch {
    /* ignore */
  }
}
