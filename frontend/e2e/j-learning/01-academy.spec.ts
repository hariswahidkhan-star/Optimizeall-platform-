import { expect, test } from '@playwright/test';
import { COURSE, COURSE_TITLE, axeViolations, lessons, pack, watchErrors } from './support/jlearning';

/**
 * The free public academy: anyone can browse the catalog, read a course page (with schema.org Course JSON-LD) and a
 * lesson, and try its knowledge check in the browser; progress, the exam and the certificate need a free account.
 */
test('anonymous visitors browse the academy, read a lesson and try its knowledge check', async ({ page }) => {
  const errors = watchErrors(page);
  await page.goto('/learn');
  await expect(page.getByRole('heading', { level: 1, name: /Free courses/ })).toBeVisible();
  await page.getByRole('button', { name: /Optimize All platform/ }).click();
  await expect(page).toHaveURL(/category=Platform/);
  await page.getByRole('link', { name: COURSE_TITLE }).click();

  await expect(page).toHaveURL(new RegExp(`/learn/${COURSE}$`));
  await expect(page.getByRole('heading', { level: 1, name: COURSE_TITLE })).toBeVisible();
  const jsonLd = await page.locator('script[type="application/ld+json"]').allTextContents();
  const course = jsonLd.map((t) => JSON.parse(t) as Record<string, unknown>).find((o) => o['@type'] === 'Course');
  expect(course?.isAccessibleForFree).toBe(true);
  expect((course?.offers as { price: number }).price).toBe(0);
  expect(await page.title()).toContain(COURSE_TITLE);
  expect(await axeViolations(page)).toEqual([]);

  // The syllabus lists every lesson; open the first one.
  await page.getByRole('link', { name: lessons[0]!.title }).click();
  await expect(page.getByRole('heading', { level: 1, name: lessons[0]!.title })).toBeVisible();
  await expect(page.getByText('Create a free account to track progress, take the exam and earn your certificate')).toBeVisible();

  // Knowledge check: feedback in the browser, no account needed.
  const check = lessons[0]!.knowledgeCheck[0]!;
  const group = page.getByRole('group', { name: check.question });
  const wrong = check.options.find((_, i) => !check.correct.includes(i))!;
  await group.getByRole('radio', { name: wrong, exact: true }).check();
  await group.getByRole('button', { name: 'Check answer' }).click();
  await expect(group.getByText('Not quite')).toBeVisible();
  // After a wrong answer the correct option is revealed (its accessible name gains "(correct answer)").
  const right = check.options[check.correct[0]!]!.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  await group.getByRole('radio', { name: new RegExp(`^${right}( \\(correct answer\\))?$`) }).check();
  await group.getByRole('button', { name: 'Check answer' }).click();
  await expect(group.getByText('Correct', { exact: true })).toBeVisible();

  // The video lesson shows the "video coming soon" note until the video is produced.
  await page.getByRole('link', { name: new RegExp(lessons[1]!.title) }).first().click();
  await expect(page.getByText('Video coming soon')).toBeVisible();
  expect(await axeViolations(page)).toEqual([]);

  // Exams need an account.
  await page.getByRole('link', { name: 'Create a free account' }).first().click();
  await expect(page).toHaveURL(/\/register/);
  expect(pack.badge.name).toBeTruthy();
  errors.expectClean('academy');
});
