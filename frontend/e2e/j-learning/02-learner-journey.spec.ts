import { expect, test } from '@playwright/test';
import {
  COURSE,
  COURSE_TITLE,
  accounts,
  arrangeLearner,
  axeViolations,
  landing,
  lessons,
  pack,
  signIn,
  takeExam,
  watchErrors,
  type CreatedTestUser,
} from './support/jlearning';

/**
 * One learner end to end: enrol → every lesson (knowledge check, mark complete, resume) → the final exam fails → retake
 * → pass → certificate (PDF download, LinkedIn "Add to profile" and "Share" links) → public verification → an admin
 * revokes it → the verification page shows it revoked.
 */
test.describe.serial('learner journey', () => {
  let learner: CreatedTestUser;
  let certificatePath = '';
  let verificationCode = '';

  test.beforeAll(async () => {
    learner = await arrangeLearner(`Learner ${Date.now().toString(36)}`);
  });

  test('enrol, work through every lesson and unlock the assessment', async ({ page }) => {
    const errors = watchErrors(page);
    await signIn(page, { email: learner.email, password: learner.password, displayName: learner.displayName }, landing.participant);
    await page.getByRole('link', { name: 'Learning', exact: true }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'My learning' })).toBeVisible();
    await page.getByRole('link', { name: 'Browse all courses' }).click();
    await page.getByRole('link', { name: COURSE_TITLE }).click();
    await expect(page.getByRole('heading', { level: 1, name: COURSE_TITLE })).toBeVisible();
    await page.getByRole('button', { name: 'Enrol for free' }).click();
    await expect(page.getByRole('progressbar', { name: 'Course progress' })).toBeVisible();
    await page.getByRole('link', { name: 'Start the first lesson' }).click();

    for (const [i, lesson] of lessons.entries()) {
      await expect(page.getByRole('heading', { level: 1, name: lesson.title })).toBeVisible();
      // Answer the first knowledge check (graded on the server, not for the certificate).
      const check = lesson.knowledgeCheck[0]!;
      const group = page.getByRole('group', { name: check.question });
      for (const c of check.correct) await group.getByRole(check.correct.length > 1 ? 'checkbox' : 'radio', { name: check.options[c]!, exact: true }).check();
      await group.getByRole('button', { name: 'Check answer' }).click();
      await expect(group.getByText('Correct', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Mark lesson complete' }).click();
      await expect(page.getByRole('status').filter({ hasText: 'Lesson completed' })).toBeVisible();
      if (i === 1) {
        // Resume where you left off: My learning points at the next lesson.
        await page.goto('/app/learning');
        await expect(page.getByText(`Next: ${lessons[2]!.title}`)).toBeVisible();
        await page.getByRole('link', { name: 'Resume' }).click();
        continue;
      }
      if (i < lessons.length - 1) await page.getByRole('link', { name: new RegExp(`^Next\\s*${lessons[i + 1]!.title}`) }).click();
    }
    await page.getByRole('link', { name: 'Take the final assessment' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: COURSE_TITLE })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
    errors.expectClean('lessons');
  });

  test('fail the exam, see the review with explanations, retake and pass', async ({ page }) => {
    await signIn(page, { email: learner.email, password: learner.password, displayName: learner.displayName }, landing.participant);
    await page.goto(`/app/learning/courses/${COURSE}/exam`);
    await page.getByRole('button', { name: 'Start the assessment' }).click();
    await expect(page.getByRole('timer', { name: 'Time left' })).toBeVisible();
    await takeExam(page, false);
    await expect(page.getByTestId('exam-outcome')).toHaveText('Not passed this time');
    // Per-question review with the correct answer and the explanation.
    await expect(page.getByText('Correct answer').first()).toBeVisible();
    await expect(page.getByText('Why:').first()).toBeVisible();
    const explanations = await page.locator('.lx-explanation').allTextContents();
    expect(explanations.every((text) => pack.finalExam.pool.some((q) => text.includes(q.explanation)))).toBe(true);
    expect(await axeViolations(page)).toEqual([]);

    await page.getByRole('link', { name: /Retake/ }).click();
    await page.getByRole('button', { name: 'Start the assessment' }).click();
    await takeExam(page, true);
    await expect(page.getByTestId('exam-outcome')).toHaveText('You passed!');
    await page.getByRole('link', { name: 'Get your certificate' }).click();
    await expect(page).toHaveURL(/\/app\/learning\/certificates\//);
    certificatePath = new URL(page.url()).pathname;
  });

  test('download the certificate PDF and check the LinkedIn links and the verification page', async ({ page }) => {
    await signIn(page, { email: learner.email, password: learner.password, displayName: learner.displayName }, landing.participant);
    await page.goto(certificatePath);
    await expect(page.getByRole('heading', { level: 1, name: pack.badge.name })).toBeVisible();
    verificationCode = (await page.getByLabel('Credential ID').inputValue()).trim();
    expect(verificationCode).toMatch(/^OA-[A-Z2-9]{4}-[A-Z2-9]{4}$/);
    const certificateId = certificatePath.split('/').pop()!;

    const pdfHref = await page.getByRole('link', { name: /Download PDF/ }).getAttribute('href');
    const pdf = await page.request.get(pdfHref!);
    expect(pdf.headers()['content-type']).toContain('application/pdf');
    const body = await pdf.body();
    expect(body.length).toBeGreaterThan(10_000);
    expect(body.subarray(0, 5).toString('latin1')).toBe('%PDF-');

    const add = new URL((await page.getByTestId('linkedin-add').getAttribute('href'))!);
    expect(`${add.origin}${add.pathname}`).toBe('https://www.linkedin.com/profile/add');
    expect(add.searchParams.get('startTask')).toBe('CERTIFICATION_NAME');
    expect(add.searchParams.get('name')).toBe(pack.badge.name);
    expect(add.searchParams.get('organizationName') ?? add.searchParams.get('organizationId')).toBeTruthy();
    expect(add.searchParams.get('certId')).toBe(verificationCode);
    expect(add.searchParams.get('certUrl')).toMatch(new RegExp(`/verify/certificates/${certificateId}$`));
    const now = new Date();
    expect(add.searchParams.get('issueYear')).toBe(String(now.getUTCFullYear()));
    expect(add.searchParams.get('issueMonth')).toBe(String(now.getUTCMonth() + 1));
    const share = new URL((await page.getByTestId('linkedin-share').getAttribute('href'))!);
    expect(`${share.origin}${share.pathname}`).toBe('https://www.linkedin.com/sharing/share-offsite/');
    expect(share.searchParams.get('url')).toMatch(new RegExp(`/verify/certificates/${certificateId}$`));
    expect(await axeViolations(page)).toEqual([]);

    // The public verification page (anonymous).
    const anon = await page.context().browser()!.newPage();
    await anon.goto(`/verify/certificates/${certificateId}`);
    await expect(anon.getByTestId('verify-status')).toHaveText(/Valid certificate/);
    await expect(anon.getByRole('heading', { level: 1, name: learner.displayName })).toBeVisible();
    await expect(anon.getByText(verificationCode)).toBeVisible();
    expect(await axeViolations(anon)).toEqual([]);
    await anon.close();
  });

  test('an admin revokes the certificate and the verification page shows it revoked', async ({ page, browser }) => {
    await signIn(page, accounts.admin, landing.admin);
    await page.goto('/admin/learning/certificates');
    await page.getByLabel('Search certificates').fill(verificationCode);
    await expect(page.getByText(verificationCode)).toBeVisible();
    await page.getByRole('button', { name: new RegExp(`Actions for ${verificationCode}`) }).click();
    await page.getByRole('menuitem', { name: 'Revoke certificate' }).click();
    const dialog = page.getByRole('alertdialog');
    await dialog.getByLabel(/Reason/).fill('Exam taken by someone else (support ticket SUP-E2E)');
    await dialog.getByRole('button', { name: 'Revoke certificate' }).click();
    await expect(page.getByText('Certificate revoked')).toBeVisible();

    const anon = await browser.newPage();
    const certificateId = certificatePath.split('/').pop()!;
    await anon.goto(`/verify/certificates/${certificateId}`);
    await expect(anon.getByTestId('verify-status')).toHaveText(/Revoked/);
    await expect(anon.getByRole('link', { name: /Download certificate/ })).toHaveCount(0);
    const assertion = await anon.request.get(`/api/v1/public/learning/openbadges/assertions/${certificateId}`);
    expect(assertion.status()).toBe(410);
    await anon.close();
  });
});
