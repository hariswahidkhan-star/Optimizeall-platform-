import type { HTMLAttributes } from 'react';
import { useScrollable } from '@/lib/hooks/useScrollable';

export interface ScrollAreaProps extends HTMLAttributes<HTMLDivElement> {
  /** Accessible name of the scroll area while it scrolls. */
  label?: string;
  /** Id of the element naming the scroll area while it scrolls (e.g. a table caption). */
  labelledBy?: string;
}

/**
 * A scroll container (wide tables, previews, carousels) that keyboard users can scroll: while its content overflows it
 * becomes a focusable, named group (`tabIndex=0`, `role="group"`), so arrow keys scroll it (WCAG 2.1.1) and screen
 * readers announce what it holds. A group rather than a region: it usually sits in a section that already carries the
 * same name, and a second landmark with that name would only add noise (axe "landmark-unique"). When the content fits
 * it is a plain div and adds no tab stop (and carries no name, which a generic div may not have).
 */
export function ScrollArea({ label, labelledBy, children, ...rest }: ScrollAreaProps) {
  const [ref, scrollable] = useScrollable<HTMLDivElement>();
  const named = Boolean(label || labelledBy);
  return (
    <div
      ref={ref}
      {...rest}
      {...(scrollable
        ? {
            tabIndex: 0,
            role: named ? 'group' : undefined,
            'aria-label': label,
            'aria-labelledby': labelledBy,
          }
        : {})}
      data-scrollable={scrollable || undefined}
    >
      {children}
    </div>
  );
}
