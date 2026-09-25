import { expect, test, type Page } from '@playwright/test';
import { mailLink } from '../journeys/support/api';
import { COURSE, COURSE_TITLE, axeViolations, lessons, watchErrors } from './support/jlearning';

/**
 * The direct enrol flow from the public academy (docs/LEARNING.md § "Direct enrol"): an anonymous visitor opens a course
 * page → "Enrol for free — start learning" → registers (the return path to the course is carried, validated) → verifies
 * the email from the mailbox → signs in → lands back on the course, the enrolment completes automatically and lesson 1
 * opens in My learning → marks it complete → the progress is saved (My learning resumes at lesson 2). Also: lesson
 * preview without an account, the phone sticky enrol bar, and that a hostile return path is ignored.
 */
test.describe.serial('direct enrol from the public course page', () => {
  const stamp = Date.now().toString(36);
  const visitor = {
    email: `enrol-${stamp}@example.test`,
    password: `Enrol#Journey-${stamp}-2026`,
    displayName: `Enrol ${stamp}`,
  };
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  test('an anonymous visitor previews lesson 1 without an account', async () => {
    await page.goto(`/learn/${COURSE}`);
    await expect(page.getByRole('heading', { level: 1, name: COURSE_TITLE })).toBeVisible();
    await page.getByRole('link', { name: /Preview lesson 1/ }).click();
    await expect(page.getByRole('heading', { level: 1, name: lessons[0]!.title })).toBeVisible();
    await page.goBack();
    await expect(page.getByRole('heading', { level: 1, name: COURSE_TITLE })).toBeVisible();
  });

  test('enrol → register with a return path to the course', async () => {
    const errors = watchErrors(page);
    await page.getByRole('button', { name: 'Enrol for free — start learning' }).first().click();
    await expect(page).toHaveURL(/\/register\?next=/);
    expect(new URL(page.url()).searchParams.get('next')).toBe(`/learn/${COURSE}?enrol=1`);

    await page.getByLabel('Email', { exact: true }).fill(visitor.email);
    await page.getByLabel('Password', { exact: true }).fill(visitor.password);
    await page.getByLabel('Display name').fill(visitor.displayName);
    await page.getByLabel('Country').selectOption('GB');
    await page.getByRole('checkbox', { name: /accept the participant rules/i }).check();
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await expect(page.getByText(/take you straight back to your course/)).toBeVisible();
    errors.expectClean('registration');
  });

  test('verify the email, sign in and land back on the course: enrolled, lesson 1 open', async () => {
    // The verification link opens in the same browser (a new tab loses ?next=, the remembered intent carries it).
    const link = await mailLink(visitor.email, '/verify-email', /verify/i);
    await page.goto(`${link.pathname}${link.search}`);
    await expect(page.getByRole('heading', { name: 'Your email is verified' })).toBeVisible();
    await page.getByRole('link', { name: 'Sign in and start learning' }).click();
    await expect(page).toHaveURL(/\/login\?/);

    await page.getByLabel('Email', { exact: true }).fill(visitor.email);
    await page.getByLabel('Password', { exact: true }).fill(visitor.password);
    await page.getByRole('form', { name: 'Sign in' }).getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(new RegExp(`/app/learning/courses/${COURSE}/lessons/${lessons[0]!.slug}$`), { timeout: 20_000 });
    await expect(page.getByRole('heading', { level: 1, name: lessons[0]!.title })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

  test('mark lesson 1 complete: the progress is saved and My learning resumes at lesson 2', async () => {
    await page.getByRole('button', { name: 'Mark lesson complete' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Lesson completed' })).toBeVisible();
    await page.goto('/app/learning');
    await expect(page.getByText(`Next: ${lessons[1]!.title}`)).toBeVisible();
    // Going back to the public course page with ?enrol=1 again is idempotent: it resumes, it does not re-enrol.
    await page.goto(`/learn/${COURSE}?enrol=1`);
    await expect(page).toHaveURL(new RegExp(`/app/learning/courses/${COURSE}/lessons/${lessons[1]!.slug}$`), { timeout: 20_000 });
  });

  test('a hostile return path is ignored after sign-in', async ({ browser }) => {
    const other = await (await browser.newContext()).newPage();
    await other.goto(`/login?next=${encodeURIComponent('//evil.example.com/steal')}`);
    await other.getByLabel('Email', { exact: true }).fill(visitor.email);
    await other.getByLabel('Password', { exact: true }).fill(visitor.password);
    await other.getByRole('form', { name: 'Sign in' }).getByRole('button', { name: 'Sign in' }).click();
    await expect(other).toHaveURL(/\/app(\/|$)/, { timeout: 20_000 });
    expect(new URL(other.url()).host).toBe(new URL(page.url()).host);
    await other.context().close();
  });

  test('on a phone the enrol bar sticks to the bottom once the hero scrolls away', async ({ browser }) => {
    const phone = await (await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true })).newPage();
    await phone.goto(`/learn/${COURSE}`);
    await expect(phone.getByRole('heading', { level: 1, name: COURSE_TITLE })).toBeVisible();
    await phone.mouse.wheel(0, 1400);
    const bar = phone.getByRole('region', { name: 'Course actions' });
    await expect(bar).toBeVisible();
    await expect(bar.getByRole('button', { name: 'Enrol for free — start learning' })).toBeVisible();
    const width = await phone.evaluate(() => document.documentElement.scrollWidth);
    expect(width).toBeLessThanOrEqual(390);
    await phone.context().close();
  });
});
