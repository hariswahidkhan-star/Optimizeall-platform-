import { useEffect, useState } from 'react';

/**
 * Scroll affordance for horizontally scrolling strips (tab lists, section navs). Marks the element with
 * `data-more-start` / `data-more-end` while content is hidden past that edge, so CSS can fade the edge and show that
 * the strip scrolls. `activeSelector` names the current item, which is scrolled into view (horizontally only) when the
 * strip mounts or `activeKey` changes — so e.g. a selected "Security" tab is never hidden past the edge.
 * Attributes are written directly (no re-render on scroll).
 */
export function useScrollEdges<T extends HTMLElement = HTMLElement>(
  activeSelector?: string,
  activeKey?: unknown,
): (element: T | null) => void {
  const [element, setElement] = useState<T | null>(null);

  useEffect(() => {
    if (!element) return;
    const update = () => {
      const max = element.scrollWidth - element.clientWidth;
      const left = Math.abs(element.scrollLeft);
      element.toggleAttribute('data-more-start', max > 1 && left > 1);
      element.toggleAttribute('data-more-end', max > 1 && left < max - 1);
    };
    update();
    element.addEventListener('scroll', update, { passive: true });
    const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(update);
    resize?.observe(element);
    return () => {
      element.removeEventListener('scroll', update);
      resize?.disconnect();
    };
  }, [element]);

  useEffect(() => {
    if (!element || !activeSelector || element.scrollWidth <= element.clientWidth) return;
    const active = element.querySelector<HTMLElement>(activeSelector);
    if (!active) return;
    const box = element.getBoundingClientRect();
    const item = active.getBoundingClientRect();
    const pad = 24;
    if (item.left < box.left + pad) element.scrollLeft -= box.left + pad - item.left;
    else if (item.right > box.right - pad) element.scrollLeft += item.right - (box.right - pad);
  }, [element, activeSelector, activeKey]);

  return setElement;
}
