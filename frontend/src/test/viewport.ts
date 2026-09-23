const KEY = '__testViewportWidth';

type WithViewport = typeof globalThis & { [KEY]?: number };

/** Sets the width the matchMedia mock (see setup.ts) evaluates min/max-width queries against. */
export function setViewportWidth(width: number): void {
  (globalThis as WithViewport)[KEY] = width;
}

export function viewportWidth(): number {
  return (globalThis as WithViewport)[KEY] ?? 1280;
}
