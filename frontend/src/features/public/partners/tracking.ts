import { useEffect, useRef, type RefObject } from 'react';

/**
 * Lightweight partner impressions: a placement counts once per partner, slot and page per visit (browser tab) when at
 * least half of it has been on screen.
 * Impressions are queued and sent in batches (≤ 20) to `POST /api/v1/public/partners/impressions` a couple of seconds
 * after the last one, or when the page is hidden (`fetch` with `keepalive`, so it survives navigation). No cookies or
 * visitor ids are sent; the API ignores bots. Clicks are counted by the redirect behind each partner link.
 */
export interface Impression {
  partner: string;
  slot: string;
  path: string;
}

const ENDPOINT = '/api/v1/public/partners/impressions';
const MAX_BATCH = 20;
const FLUSH_DELAY_MS = 2000;

let queue: Impression[] = [];
const seen = new Set<string>();
let timer: ReturnType<typeof setTimeout> | null = null;
let listening = false;

const keyOf = (i: Impression) => `${i.partner}|${i.slot}|${i.path}`;

/** Sends what is queued (in batches of 20). */
export function flushImpressions(): void {
  if (timer) {
    clearTimeout(timer);
    timer = null;
  }
  while (queue.length > 0) {
    const items = queue.splice(0, MAX_BATCH);
    try {
      void fetch(ENDPOINT, {
        method: 'POST',
        keepalive: true,
        credentials: 'omit',
        headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
        body: JSON.stringify({ items }),
      }).catch(() => undefined);
    } catch {
      // Counting must never break the page.
    }
  }
}

function listen() {
  if (listening || typeof document === 'undefined') return;
  listening = true;
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') flushImpressions();
  });
  window.addEventListener('pagehide', flushImpressions);
}

/** Queues one impression (once per partner, slot and page per visit). */
export function recordImpression(impression: Impression): void {
  const key = keyOf(impression);
  if (seen.has(key)) return;
  seen.add(key);
  queue.push(impression);
  listen();
  if (queue.length >= MAX_BATCH) flushImpressions();
  else {
    if (timer) clearTimeout(timer);
    timer = setTimeout(flushImpressions, FLUSH_DELAY_MS);
  }
}

/** Test helper. */
export function pendingImpressions(): readonly Impression[] {
  return queue;
}

export function resetImpressionTracking(): void {
  queue = [];
  seen.clear();
  if (timer) clearTimeout(timer);
  timer = null;
}

/**
 * Counts an impression of `impression` when the element is at least half visible (immediately where
 * IntersectionObserver is unavailable). Pass null to count nothing.
 */
export function useImpression(ref: RefObject<Element>, impression: Impression | null): void {
  const key = impression ? keyOf(impression) : null;
  const latest = useRef(impression);
  latest.current = impression;

  useEffect(() => {
    const el = ref.current;
    if (!key || !el) return;
    if (typeof IntersectionObserver === 'undefined') {
      recordImpression(latest.current!);
      return;
    }
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting && e.intersectionRatio >= 0.5)) {
          recordImpression(latest.current!);
          observer.disconnect();
        }
      },
      { threshold: [0.5] },
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, [key, ref]);
}
