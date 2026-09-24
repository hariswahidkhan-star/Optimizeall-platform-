import { type Page, expect, test } from '@playwright/test';
import { modal, signIn } from '../journeys/support/ui';
import { mailsTo } from './support/mail';
import { as, dispatchNotifications } from './support/staff';
import { recall, state } from './support/state';

/**
 * Participant lifecycle, part 6 — notifications and support: the in-app notification center (the lifecycle so far,
 * mark one read/unread, open one, mark all read), the email copies delivered by the outbox job, preferences (turning
 * off email for support replies, essential kinds locked), and a support ticket (validation → opened from a submission
 * → staff reply → the participant is notified in-app but not by email → closes it → reopens it → replies). Another
 * participant's ticket is not found.
 */
test.describe.serial('notifications and support', () => {
  let page: Page;
  let ticketUrl = '';
  const s = () => state();

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
    await signIn(page, s().pat, /\/app$/);
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  const list = () => page.getByRole('list', { name: 'Notifications' }).getByRole('listitem');

  test('the notification center lists the lifecycle so far', async () => {
    await page.goto('/app/notifications');
    await expect(page.getByRole('heading', { level: 1, name: 'Notifications' })).toBeVisible();
    for (const title of [
      'Submission received',
      'Correction needed',
      'Submission approved',
      'Submission not approved',
      'Appeal reviewed',
      'Your referral qualified',
      'Earning approved',
      'Your payout is scheduled',
      'Your payout was sent',
    ]) {
      await expect(list().filter({ hasText: title }).first(), title).toBeVisible();
    }
  });

  test('marks one read and unread, opens one, then marks all read', async () => {
    const unreadButton = page.getByRole('button', { name: /^Unread/ });
    await expect(unreadButton).toHaveText(/Unread \(\d+\)/);
    const before = Number((await unreadButton.textContent())!.match(/\((\d+)\)/)![1]);

    const appeal = list().filter({ hasText: 'Appeal reviewed' }).first();
    await appeal.getByRole('button', { name: 'Mark “Appeal reviewed” as read' }).click();
    await expect(unreadButton).toHaveText(`Unread (${before - 1})`);
    await appeal.getByRole('button', { name: 'Mark “Appeal reviewed” as unread' }).click();
    await expect(unreadButton).toHaveText(`Unread (${before})`);

    await appeal.getByRole('link', { name: 'Appeal reviewed' }).click();
    await expect(page).toHaveURL(/\/app\/submissions\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('region', { name: 'Your appeal' })).toContainText('Decision upheld');

    await page.goto('/app/notifications?unread=1');
    await expect(unreadButton).toHaveText(`Unread (${before - 1})`);
    await page.getByRole('button', { name: 'Mark all as read' }).click();
    await expect(page.getByText('You’re all caught up')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Mark all as read' })).toBeDisabled();
  });

  test('the outbox job emails the decisions and the payout', async () => {
    await dispatchNotifications();
    const subjects = mailsTo(s().pat.email).map((m) => m.subject);
    expect(subjects.join('\n')).toMatch(/Submission approved/);
    expect(subjects.join('\n')).toMatch(/Correction needed/);
    expect(subjects.join('\n')).toMatch(/Appeal reviewed/);
    expect(subjects.join('\n')).toMatch(/Your payout was sent/);
    // Running the job again sends nothing twice.
    const count = subjects.length;
    await dispatchNotifications();
    expect(mailsTo(s().pat.email)).toHaveLength(count);
  });

  test('turns off email for support replies; essential kinds stay locked on', async () => {
    await page.goto('/app/profile/notification-preferences');
    const email = page.getByRole('checkbox', { name: 'Email for Support replies' });
    await expect(email).toBeChecked();
    await email.uncheck();
    await expect(page.getByRole('status').filter({ hasText: '1 unsaved change' })).toBeVisible();
    await page.getByRole('button', { name: 'Save preferences' }).click();
    await expect(page.getByText('Notification preferences saved')).toBeVisible();
    await page.reload();
    await expect(page.getByRole('checkbox', { name: 'Email for Support replies' })).not.toBeChecked();
    await expect(page.getByText(/Email for Payout paid is essential and can’t be changed/)).toBeAttached();
  });

  test('a new ticket is validated, then opened from the submission page', async () => {
    const { stranger } = s();
    await page.goto(`/app/support/${stranger.ticketId}`);
    await expect(page.getByText('This ticket isn’t available')).toBeVisible();
    await expect(page.getByText('Stranger question')).toHaveCount(0);

    const submissionA = recall('submissionA');
    await page.goto(`/app/submissions/${submissionA}`);
    await page.getByRole('link', { name: 'Contact support' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'New support ticket' })).toBeVisible();
    await expect(page.getByLabel('Category')).toHaveValue('Submission');
    await expect(page.getByLabel('Related submission')).toHaveValue(submissionA);

    await page.getByLabel('Subject').fill('Hi');
    await page.getByLabel('How can we help?').fill('Too short');
    await page.getByRole('button', { name: 'Send ticket' }).click();
    await expect(page.getByLabel('Subject')).toBeFocused();
    await expect(page.getByLabel('Subject')).toHaveAccessibleDescription(/at least 3 characters/);
    await expect(page.getByLabel('How can we help?')).toHaveAccessibleDescription(/at least 10 characters/);

    // Subject at its 200-character limit.
    const subject = `Bonus question ${'q'.repeat(185)}`;
    expect(subject).toHaveLength(200);
    await page.getByLabel('Subject').fill(`${subject}overflow`);
    await expect(page.getByLabel('Subject')).toHaveValue(subject);
    await page.getByLabel('How can we help?').fill('Why was the first-post bonus 1 USD and not more?');
    await page.getByRole('button', { name: 'Send ticket' }).click();
    await expect(page).toHaveURL(/\/app\/support\/[0-9a-f-]{36}$/);
    ticketUrl = new URL(page.url()).pathname;
    await expect(page.getByRole('heading', { level: 1, name: subject })).toBeVisible();
    await expect(page.getByRole('list', { name: 'Messages' })).toContainText('Why was the first-post bonus');
  });

  test('staff reply: in-app notification, no email (muted), shown in the ticket', async () => {
    const admin = await as(s().admin);
    const ticketId = ticketUrl.split('/').pop()!;
    const mailsBefore = mailsTo(s().pat.email).length;
    await admin.post(`/admin/support/tickets/${ticketId}/messages`, {
      body: 'The first-post bonus of this campaign is 1 USD.',
      isInternalNote: false,
    });
    await admin.post(`/admin/support/tickets/${ticketId}/messages`, {
      body: 'INTERNAL: checked the reward rules.',
      isInternalNote: true,
    });
    await dispatchNotifications();
    expect(mailsTo(s().pat.email)).toHaveLength(mailsBefore);

    await page.goto('/app/notifications');
    await expect(list().filter({ hasText: 'New reply on your support ticket' }).first()).toBeVisible();

    await page.goto(ticketUrl);
    const messages = page.getByRole('list', { name: 'Messages' });
    await expect(messages).toContainText('The first-post bonus of this campaign is 1 USD.');
    await expect(messages).not.toContainText('INTERNAL');
    await expect(page.getByText('Awaiting your reply').first()).toBeVisible();
  });

  test('closes the ticket, reopens it and replies', async () => {
    await page.getByRole('button', { name: 'Close ticket' }).click();
    const confirm = modal(page, 'Close this ticket?');
    await confirm.getByRole('button', { name: 'Close ticket' }).click();
    await expect(page.getByText('This ticket is closed.')).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Your reply' })).toHaveCount(0);

    await page.getByRole('button', { name: 'Reopen ticket' }).click();
    const reply = page.getByRole('textbox', { name: 'Your reply' });
    await expect(reply).toBeVisible();
    await page.getByRole('button', { name: 'Send reply' }).click();
    await expect(reply).toHaveAccessibleDescription(/Write a message first/);
    await reply.fill('Thanks — one more question about the next campaign.');
    await page.getByRole('button', { name: 'Send reply' }).click();
    await expect(page.getByRole('list', { name: 'Messages' })).toContainText('one more question');
    await expect(reply).toHaveValue('');

    const admin = await as(s().admin);
    const ticket = await admin.get<{ status: string }>(
      `/admin/support/tickets/${ticketUrl.split('/').pop()}`,
    );
    expect(ticket.status).toBe('AwaitingStaff');
  });
});
