import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { ApiSession, accounts } from '../../agency/support/agency';

export { ApiSession, accounts, landing, signIn, watchErrors, axeViolations } from '../../agency/support/agency';

/**
 * Helpers of the learning journey (E2E_SUITE=j-learning): the free academy, a learner's course, knowledge checks, the
 * final exam (fail, retake, pass), the certificate (PDF, verification, LinkedIn) and admin revocation, against the Demo
 * seed. The answer key comes from the course pack file itself (the exam UI never shows answers before submission).
 */
export const COURSE = 'platform-getting-started';
export const COURSE_TITLE = 'Getting started on Optimize All';

interface PackQuestion {
  id: string;
  question: string;
  options: string[];
  correct: number[];
  type: 'single' | 'multiple';
}

interface PackCheck {
  question: string;
  options: string[];
  correct: number[];
}

interface Pack {
  modules: { lessons: { slug: string; title: string; knowledgeCheck: PackCheck[] }[] }[];
  badge: { name: string };
  finalExam: { pool: PackQuestion[] };
}

const here = dirname(fileURLToPath(import.meta.url));
export const pack: Pack = JSON.parse(
  readFileSync(join(here, '..', '..', '..', '..', 'backend', 'src', 'OptimizeAll.Api', 'Modules', 'Learning', 'Catalog', `${COURSE}.json`), 'utf8'),
) as Pack;

export const lessons = pack.modules.flatMap((m) => m.lessons);

export interface CreatedTestUser {
  id: string;
  email: string;
  displayName: string;
  password: string;
}

/** A fresh participant (test account, POST /admin/test-users as the demo admin). */
export async function arrangeLearner(displayName: string): Promise<CreatedTestUser> {
  const admin = await ApiSession.login(accounts.admin.email, accounts.admin.password);
  return admin.post<CreatedTestUser>('/admin/test-users', { roles: ['Participant'], displayName });
}

/** The pool question shown on the exam page (matched by its text). */
export function questionFor(text: string): PackQuestion {
  const q = pack.finalExam.pool.find((p) => text.includes(p.question));
  if (!q) throw new Error(`Unknown exam question: ${text}`);
  return q;
}

/**
 * Answers every question of the attempt on screen (one question per page) correctly or wrongly, then submits and
 * confirms. Returns when the result page shows.
 */
export async function takeExam(page: Page, pass: boolean) {
  const heading = page.locator('#question-heading');
  for (let i = 0; ; i++) {
    await expect(heading).toContainText(`Question ${i + 1} of`);
    const q = questionFor((await heading.textContent()) ?? '');
    const correct = q.correct.map((c) => q.options[c]!);
    const role = q.type === 'multiple' ? 'checkbox' : 'radio';
    const choices = pass ? correct : [q.options.find((o) => !correct.includes(o))!];
    for (const text of choices) await page.getByRole(role, { name: text, exact: true }).check();
    const next = page.getByRole('button', { name: 'Next question' });
    if (await next.isVisible()) await next.click();
    else break;
  }
  await page.getByRole('button', { name: 'Submit answers' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Submit', exact: true }).click();
  await expect(page.getByTestId('exam-outcome')).toBeVisible();
}
