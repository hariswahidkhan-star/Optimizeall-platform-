import { useCallback, useSyncExternalStore } from 'react';

/** Breakpoints mirrored from tokens.css. */
export const breakpoints = { sm: 640, md: 768, lg: 1024, xl: 1280 } as const;

function canMatch(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function';
}

export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (onChange: () => void) => {
      if (!canMatch()) return () => undefined;
      const mql = window.matchMedia(query);
      mql.addEventListener('change', onChange);
      return () => mql.removeEventListener('change', onChange);
    },
    [query],
  );
  return useSyncExternalStore(
    subscribe,
    () => (canMatch() ? window.matchMedia(query).matches : false),
    () => false,
  );
}

/** True below the md breakpoint (phones). */
export function useIsMobile(): boolean {
  return useMediaQuery(`(max-width: ${breakpoints.md - 1}px)`);
}
