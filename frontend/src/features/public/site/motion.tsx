import clsx from 'clsx';
import { useEffect, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent, type ReactNode, type RefObject } from 'react';

/**
 * Motion for the marketing pages, CSS-first and dependency-free:
 * - {@link useReveal}: reveal-on-scroll. Section headings and `[data-reveal]` elements fade and rise in (children of
 *   `[data-reveal='stagger']` one after another) the first time they enter the viewport.
 * - {@link useInViewClass}: toggles `is-inview` so looping CSS animations run only while visible (paused offscreen).
 * - {@link CountUp}: a number that counts up once when it scrolls into view.
 * Nothing is hidden unless the page is mounted, IntersectionObserver exists and the visitor has not asked for reduced
 * motion: the server-rendered HTML, reduced-motion visitors and old browsers always see every word, statically.
 */

export function prefersReducedMotion(): boolean {
  return typeof window === 'undefined' || !window.matchMedia || window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

function motionAllowed(): boolean {
  return typeof IntersectionObserver !== 'undefined' && !prefersReducedMotion();
}

const REVEAL_SELECTOR = '.site-section__head, [data-reveal]';

/**
 * Reveal-on-scroll for everything under `root`. Re-scans after every render (content that arrives from the API later
 * is picked up); each element is observed once and unobserved as soon as it is revealed.
 */
export function useReveal(root: RefObject<HTMLElement>) {
  const observer = useRef<IntersectionObserver | null>(null);
  const seen = useRef(new WeakSet<Element>());

  useEffect(() => {
    const el = root.current;
    if (!el || !motionAllowed()) return;
    el.classList.add('oa-motion');
    observer.current = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) continue;
          entry.target.classList.add('is-revealed');
          observer.current?.unobserve(entry.target);
        }
      },
      { rootMargin: '0px 0px -8% 0px', threshold: 0.08 },
    );
    return () => {
      observer.current?.disconnect();
      observer.current = null;
      // A remount (StrictMode, fast refresh) observes everything again with the new observer.
      seen.current = new WeakSet();
      el.classList.remove('oa-motion');
    };
  }, [root]);

  useEffect(() => {
    const el = root.current;
    const io = observer.current;
    if (!el || !io) return;
    el.querySelectorAll(REVEAL_SELECTOR).forEach((node) => {
      if (seen.current.has(node)) return;
      seen.current.add(node);
      // Stagger children: each gets its index so CSS can delay it.
      if (node.getAttribute('data-reveal') === 'stagger')
        Array.from(node.children).forEach((child, i) => (child as HTMLElement).style.setProperty('--i', String(Math.min(i, 8))));
      io.observe(node);
    });
  });
}

/** Adds `is-inview` to the element while it is (nearly) on screen: looping animations pause offscreen. */
export function useInViewClass<T extends HTMLElement>(): RefObject<T> {
  const ref = useRef<T>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (typeof IntersectionObserver === 'undefined') {
      el.classList.add('is-inview');
      return;
    }
    const io = new IntersectionObserver(([entry]) => el.classList.toggle('is-inview', entry.isIntersecting), { rootMargin: '80px 0px' });
    io.observe(el);
    return () => io.disconnect();
  }, []);
  return ref;
}

const easeOut = (t: number) => 1 - Math.pow(1 - t, 3);

/**
 * A figure that counts up from zero the first time it is visible (1.2s, ease-out). The final value is what assistive
 * technology reads, and the box reserves the final width so nothing shifts while it counts.
 */
export function CountUp({ value, className, suffix = '' }: { value: number; className?: string; suffix?: string }) {
  const ref = useRef<HTMLSpanElement>(null);
  const [shown, setShown] = useState<number>(value);
  const format = (n: number) => n.toLocaleString('en-US') + suffix;

  useEffect(() => {
    const el = ref.current;
    if (!el || !motionAllowed() || value <= 0) {
      setShown(value);
      return;
    }
    let frame = 0;
    let started = false;
    setShown(0);
    const io = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting || started) return;
        started = true;
        io.disconnect();
        const start = performance.now();
        const tick = (now: number) => {
          const t = Math.min(1, (now - start) / 1200);
          setShown(Math.round(easeOut(t) * value));
          if (t < 1) frame = requestAnimationFrame(tick);
        };
        frame = requestAnimationFrame(tick);
      },
      { threshold: 0.4 },
    );
    io.observe(el);
    return () => {
      io.disconnect();
      cancelAnimationFrame(frame);
    };
  }, [value]);

  return (
    <span ref={ref} className={clsx('oa-count', className)} style={{ '--oa-count-ch': format(value).length } as CSSProperties}>
      <span aria-hidden="true">{format(shown)}</span>
      <span className="visually-hidden">{format(value)}</span>
    </span>
  );
}

/**
 * An endless horizontal marquee (decorative duplicate for the loop is aria-hidden). Pauses on hover and focus, while
 * offscreen, and becomes a static wrapped list when reduced motion is requested.
 */
export function Marquee({ items, label, className }: { items: ReactNode[]; label: string; className?: string }) {
  const ref = useInViewClass<HTMLDivElement>();
  if (items.length === 0) return null;
  return (
    <div ref={ref} className={clsx('oa-marquee', className)}>
      <ul className="oa-marquee__track" aria-label={label}>
        {items.map((item, i) => (
          <li key={i}>{item}</li>
        ))}
      </ul>
      <ul className="oa-marquee__track oa-marquee__track--clone" aria-hidden="true">
        {items.map((item, i) => (
          <li key={i}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

/** Pointer-following glow on hover-capable devices: sets --mx/--my (in px) on the card under the pointer. */
export function trackGlow(e: ReactPointerEvent<HTMLElement>) {
  if (e.pointerType !== 'mouse') return;
  const card = (e.target as HTMLElement).closest<HTMLElement>('[data-glow]');
  if (!card) return;
  const r = card.getBoundingClientRect();
  card.style.setProperty('--mx', `${e.clientX - r.left}px`);
  card.style.setProperty('--my', `${e.clientY - r.top}px`);
}
