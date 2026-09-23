import { useSyncExternalStore } from 'react';
import { safeStorage } from '@/lib/hooks/storage';

/**
 * Cookie consent. Necessary cookies are always on; analytics (GA4) and marketing (GTM, Meta Pixel) tags load only after
 * the visitor opts in. The choice is stored in localStorage (`oa.consent`) with a version, so changing the categories
 * re-asks everyone.
 */

export const CONSENT_KEY = 'oa.consent';
export const CONSENT_VERSION = 1;

export interface ConsentChoice {
  version: number;
  necessary: true;
  analytics: boolean;
  marketing: boolean;
  decidedAt: string;
}

export interface AnalyticsIds {
  ga4MeasurementId: string | null;
  gtmContainerId: string | null;
  metaPixelId: string | null;
}

const listeners = new Set<() => void>();
let cached: ConsentChoice | null | undefined;

function read(): ConsentChoice | null {
  if (cached !== undefined) return cached;
  try {
    const raw = safeStorage.get(CONSENT_KEY);
    const parsed = raw ? (JSON.parse(raw) as ConsentChoice) : null;
    cached = parsed && parsed.version === CONSENT_VERSION ? parsed : null;
  } catch {
    cached = null;
  }
  return cached;
}

export function getConsent(): ConsentChoice | null {
  return read();
}

export function saveConsent(choice: { analytics: boolean; marketing: boolean }): ConsentChoice {
  const value: ConsentChoice = {
    version: CONSENT_VERSION,
    necessary: true,
    analytics: choice.analytics,
    marketing: choice.marketing,
    decidedAt: new Date().toISOString(),
  };
  safeStorage.set(CONSENT_KEY, JSON.stringify(value));
  cached = value;
  for (const listener of [...listeners]) listener();
  return value;
}

/** Forget the stored choice (tests, or "reset" in the cookie settings). */
export function clearConsent() {
  safeStorage.remove(CONSENT_KEY);
  cached = null;
  for (const listener of [...listeners]) listener();
}

/** Test hook: drop the in-memory cache so the next read hits storage. */
export function resetConsentCache() {
  cached = undefined;
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function useConsent(): ConsentChoice | null {
  return useSyncExternalStore(subscribe, read, () => null);
}

export function hasAnyTag(ids: AnalyticsIds | undefined | null): boolean {
  return !!ids && !!(ids.ga4MeasurementId || ids.gtmContainerId || ids.metaPixelId);
}

// ---------------------------------------------------------------- tag loading (only after consent)

type Gtag = (...args: unknown[]) => void;
interface TagWindow {
  dataLayer?: unknown[];
  gtag?: Gtag;
  fbq?: ((...args: unknown[]) => void) & { queue?: unknown[]; loaded?: boolean; version?: string; callMethod?: unknown };
  _fbq?: unknown;
}

const loaded = new Set<string>();

function addScript(id: string, src: string) {
  if (loaded.has(id) || document.getElementById(id)) return;
  loaded.add(id);
  const script = document.createElement('script');
  script.id = id;
  script.async = true;
  script.src = src;
  document.head.appendChild(script);
}

const SAFE_ID = /^[A-Za-z0-9-]{4,24}$/;

/**
 * Injects the configured tags for the categories the visitor accepted. Script elements load from the vendors' hosts, so
 * the web CSP must allow them (see docs/WEBSITE.md § Analytics and the CSP); ids are validated server-side and here.
 */
export function applyConsent(choice: ConsentChoice | null, ids: AnalyticsIds | null | undefined) {
  if (!choice || !ids) return;
  const w = window as unknown as TagWindow;
  if (choice.analytics && ids.ga4MeasurementId && SAFE_ID.test(ids.ga4MeasurementId)) {
    w.dataLayer = w.dataLayer ?? [];
    w.gtag =
      w.gtag ??
      function gtag(...args: unknown[]) {
        w.dataLayer!.push(args);
      };
    w.gtag('js', new Date());
    w.gtag('config', ids.ga4MeasurementId, { anonymize_ip: true });
    addScript('oa-ga4', `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(ids.ga4MeasurementId)}`);
  }
  if (choice.marketing && ids.gtmContainerId && SAFE_ID.test(ids.gtmContainerId)) {
    w.dataLayer = w.dataLayer ?? [];
    w.dataLayer.push({ 'gtm.start': Date.now(), event: 'gtm.js' });
    addScript('oa-gtm', `https://www.googletagmanager.com/gtm.js?id=${encodeURIComponent(ids.gtmContainerId)}`);
  }
  if (choice.marketing && ids.metaPixelId && /^\d{10,20}$/.test(ids.metaPixelId)) {
    if (!w.fbq) {
      const queue: unknown[] = [];
      const fbq = Object.assign((...args: unknown[]) => void queue.push(args), { queue }) as NonNullable<TagWindow['fbq']>;
      fbq.loaded = true;
      fbq.version = '2.0';
      w.fbq = fbq;
      w._fbq = fbq;
    }
    w.fbq('init', ids.metaPixelId);
    w.fbq('track', 'PageView');
    addScript('oa-meta-pixel', 'https://connect.facebook.net/en_US/fbevents.js');
  }
}
