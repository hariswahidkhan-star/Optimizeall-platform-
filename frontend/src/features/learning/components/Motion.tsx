import { useEffect, useRef, useState, type CSSProperties, type RefObject } from 'react';

/**
 * Academy motion (docs/LEARNING.md § "Motion"): CSS/SVG + IntersectionObserver only, no animation library. Everything
 * is progressive: content is fully visible without JavaScript, without IntersectionObserver and with
 * `prefers-reduced-motion: reduce` (the hidden "pending" state only exists while an observer is waiting to reveal it,
 * and the CSS applies it only under `prefers-reduced-motion: no-preference`). Animations use transform/opacity only, so
 * they never shift layout.
 */

export function prefersReducedMotion(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/**
 * Reveal-on-scroll for a container: sets `data-reveal="pending"` until it scrolls into view, then `"in"`. Children
 * stagger with `style={{ '--lx-i': index }}` (see `.lx-reveal` in academy.css). Re-arms when `key` changes (new data).
 */
export function useReveal<T extends HTMLElement>(key?: unknown): RefObject<T> {
  const ref = useRef<T>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || typeof IntersectionObserver === 'undefined' || prefersReducedMotion()) {
      el?.setAttribute('data-reveal', 'in');
      return;
    }
    // Already on screen at mount (above the fold): animate in right away, next frame.
    el.setAttribute('data-reveal', 'pending');
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          el.setAttribute('data-reveal', 'in');
          observer.disconnect();
        }
      },
      { rootMargin: '0px 0px -8% 0px', threshold: 0.05 },
    );
    observer.observe(el);
    // Safety net: never leave content hidden (e.g. a print or an observer that never fires).
    const timer = window.setTimeout(() => el.setAttribute('data-reveal', 'in'), 2500);
    return () => {
      observer.disconnect();
      window.clearTimeout(timer);
    };
  }, [key]);
  return ref;
}

/** Stagger index for a revealed child (capped so long grids don't wait). */
export function stagger(index: number): CSSProperties {
  return { ['--lx-i' as string]: Math.min(index, 11) } as CSSProperties;
}

/** Counts up to `value` once (for the hub's live numbers); immediate with reduced motion or in tests. */
export function useCountUp(value: number, durationMs = 900): number {
  const [shown, setShown] = useState(value);
  const from = useRef(0);
  useEffect(() => {
    if (prefersReducedMotion() || typeof requestAnimationFrame === 'undefined' || value <= 0) {
      setShown(value);
      return;
    }
    const start = performance.now();
    const origin = from.current;
    let frame = 0;
    const tick = (now: number) => {
      const t = Math.min(1, (now - start) / durationMs);
      const eased = 1 - Math.pow(1 - t, 3);
      setShown(Math.round(origin + (value - origin) * eased));
      if (t < 1) frame = requestAnimationFrame(tick);
      else from.current = value;
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [value, durationMs]);
  return shown;
}

const CONFETTI_COLORS = ['var(--color-primary)', 'var(--color-accent)', '#10b981', '#f59e0b', '#ec4899', '#3b82f6'];

/**
 * A short celebratory burst (lesson done, course done, exam passed). Decorative (aria-hidden) — the accessible
 * confirmation is always the text next to it. Renders nothing with reduced motion. `size="lg"` for course/exam success.
 */
export function Celebrate({ trigger, size = 'sm' }: { trigger: number; size?: 'sm' | 'lg' }) {
  const [active, setActive] = useState(0);
  useEffect(() => {
    if (!trigger || prefersReducedMotion()) return;
    setActive(trigger);
    const t = window.setTimeout(() => setActive(0), size === 'lg' ? 2200 : 1400);
    return () => window.clearTimeout(t);
  }, [trigger, size]);
  if (!active) return null;
  const count = size === 'lg' ? 28 : 14;
  return (
    <span className={`lx-confetti lx-confetti--${size}`} aria-hidden="true" key={active}>
      {Array.from({ length: count }, (_, i) => {
        const angle = (i / count) * Math.PI * 2 + (i % 3) * 0.2;
        const distance = (size === 'lg' ? 150 : 90) * (0.6 + ((i * 37) % 40) / 100);
        return (
          <span
            key={i}
            className="lx-confetti__piece"
            style={
              {
                '--dx': `${Math.cos(angle) * distance}px`,
                '--dy': `${Math.sin(angle) * distance - 40}px`,
                '--rot': `${(i * 67) % 360}deg`,
                '--delay': `${(i % 5) * 30}ms`,
                background: CONFETTI_COLORS[i % CONFETTI_COLORS.length],
              } as CSSProperties
            }
          />
        );
      })}
    </span>
  );
}
