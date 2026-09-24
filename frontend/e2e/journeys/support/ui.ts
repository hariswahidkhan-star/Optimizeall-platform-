import AxeBuilder from '@axe-core/playwright';
import { type Browser, type Page, expect } from '@playwright/test';
import type { Credentials } from './fixtures';

/** A modal by its accessible name: `dialog`, or `alertdialog` for confirmations of sensitive actions. */
export function modal(page: Page, name: string | RegExp) {
  return page.getByRole('dialog', { name }).or(page.getByRole('alertdialog', { name }));
}

/** Signs in through the login form and waits for the portal the user lands in. */
export async function signIn(page: Page, user: Credentials, landing: RegExp) {
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill(user.email);
  await page.getByLabel('Password', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(landing);
}

/** A fresh browser context (so each actor has its own session cookie) signed in as `user`. */
export async function signedInPage(browser: Browser, user: Credentials, landing: RegExp): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, user, landing);
  return page;
}

export async function expectNoHorizontalScroll(page: Page, where: string) {
  const overflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(overflow.scrollWidth, `horizontal scroll on ${where}`).toBeLessThanOrEqual(overflow.innerWidth);
}

/** Runs axe (WCAG 2.1 A/AA) and returns the violations, formatted for an assertion message. */
export async function axeViolations(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  return results.violations.map((v) => ({
    id: v.id,
    impact: v.impact,
    help: v.help,
    targets: v.nodes.slice(0, 5).map((n) => n.target.join(' ')),
  }));
}

/** Today minus `days`, as the yyyy-mm-dd value of a date input (UTC, like the app's social profile dialog). */
export function daysAgo(days: number): string {
  return new Date(Date.now() - days * 86_400_000).toISOString().slice(0, 10);
}
