import { useEffect, useState } from 'react';

/**
 * Tracks whether an element's content overflows it (it scrolls horizontally or vertically). Scroll containers need to
 * be keyboard-focusable when they scroll and hold nothing focusable themselves (WCAG 2.1.1, axe
 * "scrollable-region-focusable"); callers add `tabIndex={0}` (plus a region name) only then, so a table that fits adds
 * no extra tab stop. Returns a callback ref for the container — it works for elements that mount later (after data
 * loads) — and re-measures when the element or any of its children resize, and when children are added or removed.
 */
export function useScrollable<T extends HTMLElement = HTMLElement>(): [(element: T | null) => void, boolean] {
  const [element, setElement] = useState<T | null>(null);
  const [scrollable, setScrollable] = useState(false);

  useEffect(() => {
    if (!element || typeof ResizeObserver === 'undefined') {
      setScrollable(false);
      return;
    }
    const measure = () =>
      setScrollable(
        element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1,
      );
    const resize = new ResizeObserver(measure);
    const observeAll = () => {
      resize.disconnect();
      resize.observe(element);
      for (const child of Array.from(element.children)) resize.observe(child);
      measure();
    };
    observeAll();
    const mutations = typeof MutationObserver === 'undefined' ? null : new MutationObserver(observeAll);
    mutations?.observe(element, { childList: true });
    return () => {
      resize.disconnect();
      mutations?.disconnect();
    };
  }, [element]);

  return [setElement, scrollable];
}
