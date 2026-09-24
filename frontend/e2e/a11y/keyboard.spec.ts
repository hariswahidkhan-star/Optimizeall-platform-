import { type Locator, type Page, expect, test } from '@playwright/test';
import { actors, axeLight, settle, signIn } from './support/a11y';

/**
 * Keyboard operability and motion across the shared shells: skip links, the phone navigation drawer, dialogs and menus
 * (focus moves in, is trapped, Escape closes, focus returns), a visible focus indicator on every stop of the portal
 * chrome, the toast live region and reduced motion. Read-only, like the rest of the suite.
 */

/** True when the focused element draws a focus indicator (outline or ring). */
async function focusIndicator(page: Page) {
  return page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null;
    if (!el || el === document.body) return { name: 'body', visible: false };
    const cs = getComputedStyle(el);
    const ring = (cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) > 0) || cs.boxShadow !== 'none';
    // A few components draw the ring on a child (switch track) or an ancestor (whole-card links).
    const child = Array.from(el.querySelectorAll('*')).some(
      (c) => getComputedStyle(c).outlineStyle !== 'none',
    );
    const card = el.closest('.ui-card--interactive, .site-card');
    const ancestor = !!card && getComputedStyle(card).outlineStyle !== 'none';
    const name = `${el.tagName.toLowerCase()} "${(el.getAttribute('aria-label') ?? el.textContent ?? '').trim().slice(0, 40)}"`;
    return { name, visible: ring || child || ancestor };
  });
}

async function expectFocusInside(container: Locator, what: string) {
  expect(await container.evaluate((el) => el.contains(document.activeElement)), `focus inside ${what}`).toBe(
    true,
  );
}

test.describe('skip links', () => {
  test('the public site and a portal start with a working "Skip to content" link', async ({ page }) => {
    for (const path of ['/', '/contact']) {
      await page.goto(path);
      await settle(page);
      await page.keyboard.press('Tab');
      const skip = page.getByRole('link', { name: /skip to (main )?content/i });
      await expect(skip).toBeFocused();
      await expect(skip).toBeInViewport();
      await page.keyboard.press('Enter');
      expect(
        await page.evaluate(() => !!document.activeElement?.closest('main')),
        `focus in <main> on ${path}`,
      ).toBe(true);
    }

    // A fresh page load (after a client-side navigation the portal moves focus to <main> on purpose).
    await signIn(page, actors.participant, /\/app(\/|$)/);
    await page.goto('/app/earnings');
    await settle(page);
    await page.keyboard.press('Tab');
    const skip = page.getByRole('link', { name: 'Skip to content' });
    await expect(skip).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.locator('main#main')).toBeFocused();
  });
});

test.describe('portal chrome', () => {
  test('every stop in the top bar and sidebar shows a visible focus indicator', async ({ page }) => {
    await signIn(page, actors.admin, /\/admin(\/|$)/);
    await page.goto('/agency/clients');
    await settle(page);
    const seen = new Set<string>();
    for (let i = 0; i < 40; i++) {
      await page.keyboard.press('Tab');
      const { name, visible } = await focusIndicator(page);
      if (seen.has(name)) break;
      seen.add(name);
      expect.soft(visible, `visible focus indicator on ${name}`).toBe(true);
      if (await page.evaluate(() => !!document.activeElement?.closest('main'))) break;
    }
    expect(seen.size, 'tab stops before the content').toBeGreaterThan(5);
  });

  test('menus: Escape closes and focus returns to the trigger', async ({ page }) => {
    await signIn(page, actors.admin, /\/admin(\/|$)/);
    await settle(page);
    const trigger = page.getByRole('button', { name: /^Account menu for / });
    await trigger.focus();
    await page.keyboard.press('Enter');
    const menu = page.getByRole('menu');
    await expect(menu).toBeVisible();
    await expectFocusInside(menu, 'the account menu');
    await page.keyboard.press('ArrowDown');
    await expectFocusInside(menu, 'the account menu after ArrowDown');
    await page.keyboard.press('Escape');
    await expect(menu).toBeHidden();
    await expect(trigger).toBeFocused();
  });
});

test.describe('phone navigation drawer', () => {
  test.use({ viewport: { width: 360, height: 780 }, isMobile: true, hasTouch: true });

  for (const [who, landing, path] of [
    ['admin', /\/admin(\/|$)/, '/agency/clients'],
    ['client', /\/client(\/|$)/, '/client'],
  ] as const) {
    test(`${who}: opens from the menu button, traps focus, closes with Escape and restores focus`, async ({
      page,
    }) => {
      await signIn(page, actors[who], landing);
      await page.goto(path);
      await settle(page);
      const open = page.getByRole('button', { name: 'Open navigation' });
      await expect(open).toBeVisible();
      const box = (await open.boundingBox())!;
      expect(Math.min(box.width, box.height), 'menu button touch target').toBeGreaterThanOrEqual(24);
      await open.focus();
      await page.keyboard.press('Enter');
      const drawer = page.getByRole('dialog', { name: /menu$/ });
      await expect(drawer).toBeVisible();
      await expectFocusInside(drawer, 'the drawer');
      for (let i = 0; i < 60; i++) await page.keyboard.press('Tab');
      await expectFocusInside(drawer, 'the drawer after 60 tabs');
      expect(await axeLight(page), 'axe violations with the drawer open').toEqual([]);
      await page.keyboard.press('Escape');
      await expect(drawer).toBeHidden();
      await expect(open).toBeFocused();
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth),
      ).toBeLessThanOrEqual(0);
    });
  }
});

test.describe('dialogs', () => {
  for (const [path, button] of [
    ['/agency/clients', 'New client'],
    ['/agency/crm/deals', 'New deal'],
    ['/manage/templates', 'New template'],
  ] as const) {
    test(`"${button}" on ${path}: focus in, trapped, Escape closes, focus restored, no axe violations`, async ({
      page,
    }) => {
      await signIn(page, actors.admin, /\/admin(\/|$)/);
      await page.goto(path);
      await settle(page);
      const trigger = page.getByRole('button', { name: button, exact: true });
      await trigger.focus();
      await page.keyboard.press('Enter');
      const dialog = page.getByRole('dialog');
      await expect(dialog).toBeVisible();
      await expectFocusInside(dialog, 'the dialog');
      expect(await axeLight(page), `axe violations in the "${button}" dialog`).toEqual([]);
      for (let i = 0; i < 40; i++) await page.keyboard.press('Shift+Tab');
      await expectFocusInside(dialog, 'the dialog after 40 Shift+Tabs');
      await page.keyboard.press('Escape');
      await expect(dialog).toBeHidden();
      await expect(trigger).toBeFocused();
    });
  }
});

test.describe('announcements and motion', () => {
  test('toasts go to a polite live region that exists before the first toast', async ({ page }) => {
    await signIn(page, actors.participant, /\/app(\/|$)/);
    await settle(page);
    const region = page.getByRole('region', { name: 'Notifications' });
    await expect(region).toHaveCount(1);
    await expect(region.locator('[aria-live="polite"]')).toHaveCount(1);
  });

  test('prefers-reduced-motion: motion tokens are zero and opening a drawer runs no animation', async ({
    page,
  }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.setViewportSize({ width: 360, height: 780 });
    await signIn(page, actors.admin, /\/admin(\/|$)/);
    await settle(page);
    const durations = await page.evaluate(() =>
      ['--duration-fast', '--duration-base', '--duration-slow'].map((t) =>
        getComputedStyle(document.documentElement).getPropertyValue(t).trim(),
      ),
    );
    expect(durations).toEqual(['0ms', '0ms', '0ms']);
    await page.getByRole('button', { name: 'Open navigation' }).click();
    await expect(page.getByRole('dialog', { name: /menu$/ })).toBeVisible();
    const running = await page.evaluate(() =>
      document
        .getAnimations()
        .map((a) => Number(a.effect?.getComputedTiming().duration ?? 0))
        .filter((d) => d > 1),
    );
    expect(running, 'animations longer than 1ms under reduced motion').toEqual([]);
    expect(await page.evaluate(() => getComputedStyle(document.documentElement).scrollBehavior)).toBe('auto');
  });
});
