/**
 * Link-safety rules for every href that comes from the API (banner CTAs, onboarding actions, campaign assets, post and
 * profile URLs, notification links). Defence in depth: the API validates these too.
 *
 * Allowed: absolute `http:`/`https:` URLs (with a host, without credentials) and app-internal paths that start with a
 * single `/`. Rejected: other schemes (`javascript:`, `data:`, `vbscript:`…), protocol-relative `//host`, backslashes
 * (browsers treat `/\host` like `//host`), whitespace/control characters and non-strings.
 */

// eslint-disable-next-line no-control-regex
const SPACE_OR_CONTROL = /[\s\u0000-\u001f\u007f]/;

/** An app-internal path (`/app/earnings`, `/api/v1/files/…`), never another origin. */
export function isInternalHref(href: unknown): href is string {
  return (
    typeof href === 'string' &&
    href.startsWith('/') &&
    !href.startsWith('//') &&
    !href.includes('\\') &&
    !SPACE_OR_CONTROL.test(href)
  );
}

/** An absolute http(s) URL with a host and no embedded credentials. */
export function isExternalHref(href: unknown): href is string {
  if (typeof href !== 'string' || href.includes('\\') || SPACE_OR_CONTROL.test(href)) return false;
  if (!/^https?:\/\/[^/]/i.test(href)) return false;
  try {
    const url = new URL(href);
    return (
      (url.protocol === 'https:' || url.protocol === 'http:') &&
      !!url.hostname &&
      !url.username &&
      !url.password
    );
  } catch {
    return false;
  }
}

/** True for hrefs that may be rendered: internal paths or absolute http(s) URLs. */
export function isSafeHref(href: unknown): href is string {
  return isInternalHref(href) || isExternalHref(href);
}
