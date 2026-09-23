/**
 * Campaign attribution for lead forms: UTM parameters, the external referrer and the landing path of the visitor's first
 * page view in this session (sessionStorage `oa.attribution`, try/catch with an in-memory fallback).
 */

export interface Attribution {
  utm: { source?: string; medium?: string; campaign?: string; term?: string; content?: string };
  referrer?: string;
  landingPath?: string;
}

const KEY = 'oa.attribution';
let memory: Attribution | null = null;

function clip(value: string | null | undefined, max = 150): string | undefined {
  if (!value) return undefined;
  const v = value.trim();
  return v ? v.slice(0, max) : undefined;
}

/** Records attribution on the first page view of the session (later navigations keep the original values). */
export function captureAttribution(location: { pathname: string; search: string } = window.location) {
  try {
    if (memory || sessionStorage.getItem(KEY)) return;
  } catch {
    if (memory) return;
  }
  const params = new URLSearchParams(location.search);
  let referrer: string | undefined;
  try {
    const ref = document.referrer ? new URL(document.referrer) : null;
    if (ref && ref.origin !== window.location.origin && /^https?:$/.test(ref.protocol)) referrer = clip(ref.href, 500);
  } catch {
    referrer = undefined;
  }
  const value: Attribution = {
    utm: {
      source: clip(params.get('utm_source')),
      medium: clip(params.get('utm_medium')),
      campaign: clip(params.get('utm_campaign')),
      term: clip(params.get('utm_term')),
      content: clip(params.get('utm_content')),
    },
    referrer,
    landingPath: clip(location.pathname, 500),
  };
  memory = value;
  try {
    sessionStorage.setItem(KEY, JSON.stringify(value));
  } catch {
    // storage unavailable: keep the in-memory copy
  }
}

export function getAttribution(): Attribution {
  try {
    const raw = sessionStorage.getItem(KEY);
    if (raw) return JSON.parse(raw) as Attribution;
  } catch {
    // fall through
  }
  return memory ?? { utm: {} };
}
