import { expectNoHorizontalScroll } from '../journeys/support/ui';
import { accounts, expect, landing, test, watchErrors } from './support/delivery';

/**
 * Phone layout (390×844, touch) of the delivery journey's busiest screens: the client portal (home, approvals, messages)
 * and the kanban board's one-column view with its "Move to" menu. Runs in the mobile-chromium project only and uses the
 * Demo seed's accounts, so it does not depend on the desktop journey.
 */
test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

test('client portal on a phone: home, approvals and messages fit the screen', async ({ as }) => {
  const approver = await as(accounts.nimbusApprover, landing.client);
  const errors = watchErrors(approver);
  await expect(approver.getByRole('heading', { level: 1, name: 'Welcome back' })).toBeVisible();
  await expectNoHorizontalScroll(approver, 'the client home');
  for (const [path, heading] of [
    ['/client/approvals', 'Approvals'],
    ['/client/messages', 'Messages'],
    ['/client/projects', 'Projects'],
  ] as const) {
    await approver.goto(path);
    await expect(approver.getByRole('heading', { level: 1, name: heading })).toBeVisible();
    await expectNoHorizontalScroll(approver, path);
  }
  errors.expectClean('the client portal on a phone');
});

test('kanban on a phone: one column at a time and a "Move to" menu per card', async ({ as }) => {
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto('/agency/projects');
  await am.getByRole('main').getByRole('link', { name: 'SEO retainer' }).first().click();
  await expect(am.getByRole('heading', { level: 1, name: 'SEO retainer' })).toBeVisible();
  const columnPicker = am.getByLabel('Column');
  await expect(columnPicker).toBeVisible();
  await expect(am.locator('.dl-kanban__column')).toHaveCount(1);
  await columnPicker.selectOption('Todo');
  const card = am.getByRole('list', { name: 'To do tasks' }).getByRole('listitem').first();
  const title = (await card.getByRole('button').first().innerText()).trim();
  await card.getByLabel(`Move ${title} to`).selectOption('InProgress');
  await columnPicker.selectOption('InProgress');
  await expect(am.getByRole('list', { name: 'In progress tasks' }).getByRole('button', { name: title })).toBeVisible();
  await expectNoHorizontalScroll(am, 'the kanban board');
  errors.expectClean('the kanban board on a phone');
});
