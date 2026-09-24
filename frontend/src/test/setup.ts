import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterEach, beforeEach, vi } from 'vitest';
import { tokenStore } from '@/lib/api/client';
import { setViewportWidth, viewportWidth } from './viewport';

// findBy*/waitFor give up after 1 s by default. On a busy CI runner or a loaded dev machine a lazy page's first render
// can take longer than that, which fails a correct test; allow 5 s. Assertions are unchanged — only the patience.
configure({ asyncUtilTimeout: 5_000 });

/** jsdom lacks matchMedia: evaluate min/max-width queries against a configurable width. */
function evaluate(query: string): boolean {
  const width = viewportWidth();
  const max = /max-width:\s*(\d+)px/.exec(query);
  const min = /min-width:\s*(\d+)px/.exec(query);
  if (max && width > Number(max[1])) return false;
  if (min && width < Number(min[1])) return false;
  return Boolean(max || min);
}

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: evaluate(query),
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }),
});

if (!URL.createObjectURL) {
  Object.defineProperty(URL, 'createObjectURL', { writable: true, value: () => 'blob:mock' });
  Object.defineProperty(URL, 'revokeObjectURL', { writable: true, value: () => undefined });
}

window.scrollTo = vi.fn() as unknown as typeof window.scrollTo;
Element.prototype.scrollIntoView = vi.fn();

beforeEach(() => {
  setViewportWidth(1280);
});

afterEach(() => {
  cleanup();
  tokenStore.clear();
  document.body.style.overflow = '';
});
