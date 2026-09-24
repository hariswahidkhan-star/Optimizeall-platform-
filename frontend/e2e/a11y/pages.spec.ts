import { expect, test } from '@playwright/test';
import {
  actors,
  axeDarkContrast,
  axeLight,
  horizontalOverflow,
  portalSets,
  resolveTarget,
  settle,
  signIn,
  visit,
  widths,
} from './support/a11y';

/**
 * Every representative page of every portal, at 360, 768 and 1280 px: no axe violations (WCAG 2.2 A/AA; contrast in
 * the light and the dark theme) and no horizontal page scroll. One test per portal and width signs in once and walks
 * the portal's pages; failures are collected per page (soft assertions) so one run reports every problem.
 */
for (const set of portalSets) {
  for (const size of widths) {
    test.describe(`${set.id} at ${size.width}px`, () => {
      test.use({
        viewport: { width: size.width, height: size.height },
        isMobile: size.isMobile,
        hasTouch: size.isMobile,
      });

      test(`${set.id} pages are accessible and fit ${size.width}px`, async ({ page }) => {
        test.setTimeout(60_000 + set.pages.length * 30_000);
        if (set.actor) await signIn(page, actors[set.actor], set.landing!);

        for (const target of set.pages) {
          const { path, name } = await resolveTarget(page, target);
          await test.step(name, async () => {
            await visit(page, path);
            await settle(page);
            expect.soft(await horizontalOverflow(page), `horizontal page scroll on ${name}`).toBe(0);
            // The full audit runs at every width (layouts differ: stacked tables, drawers, bottom bars); the dark-theme
            // contrast pass runs on the phone and the desktop layout.
            expect.soft(await axeLight(page), `axe violations on ${name} (light theme)`).toEqual([]);
            if (size.name !== 'tablet')
              expect
                .soft(await axeDarkContrast(page), `contrast violations on ${name} (dark theme)`)
                .toEqual([]);
          });
        }
      });
    });
  }
}
