/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// Read as text (vitest runs from the frontend root): the test config turns CSS imports off, so `?raw` would be empty.
const css = readFileSync(resolve(process.cwd(), 'src/components/ui/overlay.css'), 'utf8');

/** z-index of the rule for `selector` in overlay.css, relative to the modal layer (var(--z-modal) = 0). */
function layer(selector: string): number {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const rule = new RegExp(`(^|\\n)${escaped}\\s*\\{([^}]*)\\}`).exec(css);
  if (!rule) throw new Error(`No rule for ${selector}`);
  const value = /z-index:\s*([^;]+);/.exec(rule[2] ?? '')?.[1]?.trim();
  if (!value) throw new Error(`No z-index for ${selector}`);
  if (value === 'var(--z-modal)') return 0;
  const offset = /^calc\(var\(--z-modal\)\s*\+\s*(\d+)\)$/.exec(value);
  if (offset) return Number(offset[1]);
  throw new Error(`Unexpected z-index for ${selector}: ${value}`);
}

describe('overlay stacking', () => {
  it('never puts a drawer above a dialog opened from it (modals stack in the order they open)', () => {
    // A dialog's backdrop and panel sit on the modal layer. A drawer on a higher layer covered the part of a dialog
    // opened from inside it (e.g. Services → Edit service → Add package): clicks hit the drawer, so the package's
    // switches could not be used.
    expect(layer('.ui-drawer')).toBeLessThanOrEqual(layer('.ui-backdrop'));
    expect(layer('.ui-drawer-backdrop')).toBeLessThanOrEqual(layer('.ui-backdrop'));
  });
});
