import { type Page, expect, test } from '@playwright/test';
import { mailLink } from '../journeys/support/api';
import { pngFile } from '../journeys/support/png';
import { daysAgo, expectNoHorizontalScroll } from '../journeys/support/ui';
import { state } from './support/state';

/**
 * Participant lifecycle on a phone (mobile project, Pixel 7): a separate participant registers, verifies, signs in,
 * completes the profile, adds a qualifying profile, filters campaigns behind the "Filters" toggle, submits proof and
 * visits every participant page — each one without horizontal scrolling.
 */
test.describe.serial('participant lifecycle on a phone', () => {
  let page: Page;
  const me = () => state().mobile;

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
    expect(page.viewportSize()!.width).toBeLessThan(480);
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  const fits = (where: string) => expectNoHorizontalScroll(page, where);

  test('registers, verifies and signs in', async () => {
    await page.goto('/register');
    await fits('register');
    await page.getByLabel('Email', { exact: true }).fill(me().email);
    await page.getByLabel('Password', { exact: true }).fill(me().password);
    await page.getByLabel('Display name').fill(me().displayName);
    await page.getByLabel('Country').selectOption('AE');
    await page.getByLabel('Time zone').selectOption('Asia/Dubai');
    await page.getByRole('checkbox', { name: /accept the participant rules/i }).check();
    await page.getByRole('button', { name: 'Create account' }).click();
    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await fits('check email');

    const link = await mailLink(me().email, '/verify-email', /verify/i);
    await page.goto(`${link.pathname}${link.search}`);
    await expect(page.getByRole('heading', { name: 'Your email is verified' })).toBeVisible();
    await fits('email verified');

    await page.goto('/login');
    await page.getByLabel('Email', { exact: true }).fill(me().email);
    await page.getByLabel('Password', { exact: true }).fill(me().password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page).toHaveURL(/\/app$/);
    await fits('home (new participant)');
  });

  test('completes the profile and adds a qualifying Instagram profile', async () => {
    await page.goto('/app/profile');
    await page.getByLabel('Language', { exact: true }).selectOption('ar');
    const interest = page.getByLabel('Interests', { exact: true });
    await interest.fill('lifestyle');
    await interest.press('Enter');
    await page.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByText('Profile saved')).toBeVisible();
    await fits('profile');

    await page.goto('/app/social-accounts');
    await page.getByRole('button', { name: /^Add a (social )?profile$/ }).first().click();
    const dialog = page.getByRole('dialog', { name: 'Add a social profile' });
    await dialog.getByLabel('Platform').selectOption('Instagram');
    await dialog.getByLabel('Handle').fill(me().instagram);
    await dialog.getByLabel('Followers').fill('640');
    await dialog.getByLabel('Profile link').fill(`https://www.instagram.com/${me().instagram}`);
    await dialog.getByLabel('Account created on').fill(daysAgo(365));
    await fits('add profile dialog');
    await dialog.getByRole('button', { name: 'Add profile' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByText('Qualifies', { exact: true })).toBeVisible();
    await fits('social accounts');
  });

  test('filters campaigns behind the Filters toggle and submits proof', async () => {
    const { main, other } = state();
    await page.goto('/app/campaigns');
    await fits('campaigns');
    await page.getByRole('button', { name: /^Filters/ }).click();
    // The checkbox mirrors the URL, which updates in a transition: click, then wait for the checked state (check()
    // verifies synchronously and races the URL update).
    const eligibleOnly = page.getByRole('checkbox', { name: /Only campaigns I.m eligible for/ });
    await eligibleOnly.click();
    await expect(eligibleOnly).toBeChecked();
    await expect(page).toHaveURL(/eligible=1/);
    await expect(page.getByRole('link', { name: main.title })).toBeVisible();
    await expect(page.getByRole('link', { name: other.title })).toBeHidden();
    await fits('campaigns (filtered)');

    await page.getByRole('link', { name: main.title }).click();
    await expect(page.getByRole('heading', { level: 1, name: main.title })).toBeVisible();
    await fits('campaign detail');
    await page.getByRole('button', { name: 'Submit proof' }).click();
    const dialog = page.getByRole('dialog', { name: 'Submit proof of your post' });
    await dialog.getByLabel('Link to your post').fill(`https://www.instagram.com/p/${me().postPrefix}A/`);
    await dialog.getByLabel('Screenshot of your post').setInputFiles(pngFile(401));
    await fits('submit proof dialog');
    await dialog.getByRole('button', { name: 'Submit proof' }).click();
    await expect(page).toHaveURL(/\/app\/submissions\/[0-9a-f-]{36}$/);
    await expect(page.getByText('Pending', { exact: true }).first()).toBeVisible();
    await fits('submission detail');
  });

  for (const [path, heading] of [
    ['/app', null],
    ['/app/submissions', 'My submissions'],
    ['/app/earnings', 'Earnings'],
    ['/app/payouts', 'Payouts'],
    ['/app/referrals', 'Referrals'],
    ['/app/notifications', 'Notifications'],
    ['/app/support', 'Support'],
    ['/app/support/new', 'New support ticket'],
    ['/app/profile/payout-details', null],
    ['/app/profile/notification-preferences', null],
    ['/app/profile/security', null],
  ] as const) {
    test(`${path} fits the phone`, async () => {
      await page.goto(path);
      await expect(
        heading ? page.getByRole('heading', { level: 1, name: heading }) : page.getByRole('heading', { level: 1 }),
      ).toBeVisible();
      await fits(path);
    });
  }
});
