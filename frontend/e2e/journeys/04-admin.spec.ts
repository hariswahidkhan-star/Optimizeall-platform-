import { type Page, expect, test } from '@playwright/test';
import { ApiSession } from './support/api';
import { fixtures, participantFor } from './support/fixtures';
import { modal, signedInPage } from './support/ui';

/**
 * Admin journey: change the minimum account age (reason + confirm) → the participant's TikTok qualification text
 * follows → suspend the participant (their next navigation signs them out) → reactivate → the audit log shows the
 * setting change and the suspension. The setting is restored afterwards (API) for the journeys that follow.
 */
const NEW_MIN_AGE = 30;
const DEFAULT_MIN_AGE = 90;

test.describe.serial('admin journey', () => {
  const participant = participantFor('desktop-chromium');
  const settingReason = `E2E ${Date.now().toString(36)}: newer creator accounts may join`;
  const suspendReason = `E2E ${Date.now().toString(36)}: suspicious submission pattern under investigation`;
  let admin: Page;
  let pat: Page;

  test.beforeAll(async ({ browser }) => {
    admin = await signedInPage(browser, fixtures().admin, /\/admin$/);
    pat = await signedInPage(browser, participant, /\/app$/);
  });
  test.afterAll(async () => {
    const api = await ApiSession.login(fixtures().admin.email, fixtures().admin.password);
    await api.put('/admin/settings/eligibility.minAccountAgeDays', {
      value: DEFAULT_MIN_AGE,
      reason: 'E2E journeys: restore the default minimum account age',
      confirm: true,
    });
    for (const page of [admin, pat]) await page?.context().close();
  });

  test('the admin changes the minimum account age with a reason and confirmation', async () => {
    await admin.goto('/admin/settings');
    const setting = admin.getByRole('region', { name: 'Minimum social account age' });
    await expect(setting).toContainText(`Current${DEFAULT_MIN_AGE} days`);
    await setting.getByLabel('New value').fill(String(NEW_MIN_AGE));
    await setting.getByRole('button', { name: 'Save…' }).click();

    const dialog = modal(admin, 'Change Minimum social account age?');
    const confirm = dialog.getByRole('button', { name: 'Save setting' });
    await dialog.getByLabel('Reason').fill(settingReason);
    await confirm.click();
    await expect(dialog).toBeHidden();
    await expect(setting).toContainText(`Current${NEW_MIN_AGE} days`);
    await expect(setting.getByText('Customised')).toBeVisible();
  });

  test('the participant’s TikTok qualification text follows the new minimum', async () => {
    const api = await ApiSession.login(participant.email, participant.password);
    const accounts = await api.get<{ items: { platform: string; accountAgeDays: number }[] }>(
      '/me/social-accounts',
    );
    const age = accounts.items.find((a) => a.platform === 'TikTok')!.accountAgeDays;
    const remaining = NEW_MIN_AGE - age;

    await pat.goto('/app/social-accounts');
    const card = pat.getByRole('article', {
      name: new RegExp(`@${participant.tiktok.replace(/\./g, '\\.')}`),
    });
    await expect(
      card.getByText(
        `Profiles must be at least ${NEW_MIN_AGE} days old. This one qualifies in ${remaining} days.`,
      ),
    ).toBeVisible();
    await expect(card.getByText(new RegExp(`${remaining} days\\s*until it qualifies`))).toBeVisible();
  });

  test('the admin suspends the participant; their next navigation signs them out', async () => {
    await admin.goto('/admin/users');
    await admin.getByRole('searchbox', { name: 'Search' }).fill(participant.email);
    await admin.getByRole('link', { name: participant.displayName }).click();
    await expect(admin.getByRole('heading', { level: 1, name: participant.displayName })).toBeVisible();

    await admin.getByRole('button', { name: 'Suspend', exact: true }).click();
    const dialog = modal(admin, `Suspend ${participant.displayName}?`);
    const confirm = dialog.getByRole('button', { name: 'Suspend account' });
    await dialog.getByLabel('Reason').fill(suspendReason);
    await dialog.getByLabel(`Type ${participant.email} to confirm`).fill(participant.email);
    await confirm.click();
    await expect(dialog).toBeHidden();
    await expect(admin.getByRole('button', { name: 'Reactivate', exact: true })).toBeVisible();
    await expect(admin.getByText('Suspended', { exact: true }).first()).toBeVisible();

    // The participant's page is still open; the next in-app navigation fetches data, is refused, and the app
    // signs them out to the login page (keeping where they were going) and tells them their session ended.
    await pat.getByRole('link', { name: 'Referrals' }).first().click();
    await expect(pat).toHaveURL(/\/login\?expired=1&next=%2Fapp%2Freferrals$/);
    await expect(pat.getByRole('heading', { level: 1, name: 'Welcome back' })).toBeVisible();
    await expect(pat.getByText('Your session has expired')).toBeVisible();
    // Signed out for real: the portal is no longer reachable without signing in (an ordinary redirect now).
    await pat.goto('/app');
    await expect(pat).toHaveURL(/\/login\?next=%2Fapp$/);
  });

  test('the admin reactivates the participant, who can sign in again', async () => {
    await admin.getByRole('button', { name: 'Reactivate', exact: true }).click();
    const dialog = modal(admin, `Reactivate ${participant.displayName}?`);
    await dialog.getByLabel('Reason').fill('Investigation closed, nothing found.');
    await dialog.getByRole('button', { name: 'Reactivate account' }).click();
    await expect(dialog).toBeHidden();
    await expect(admin.getByRole('button', { name: 'Suspend', exact: true })).toBeVisible();

    await pat.getByLabel('Email', { exact: true }).fill(participant.email);
    await pat.getByLabel('Password', { exact: true }).fill(participant.password);
    await pat.getByRole('button', { name: 'Sign in' }).click();
    await expect(pat).toHaveURL(/\/app$/);
    await expect(pat.getByRole('heading', { level: 1, name: /Pat/ })).toBeVisible();
  });

  test('the audit log shows the setting change and the suspension', async () => {
    const search = async (action: string) => {
      await admin.goto('/admin/audit');
      await admin.getByLabel('Action').fill(action);
      await admin.getByRole('button', { name: 'Apply filters' }).click();
      await expect(admin).toHaveURL(new RegExp(`action=${action.replace('.', '\\.')}`));
      return admin.getByRole('region', { name: 'Audit entries' }).getByRole('listitem');
    };

    const settingEntry = (await search('admin.setting_changed')).filter({ hasText: settingReason });
    await expect(settingEntry).toHaveCount(1);
    await expect(settingEntry.getByRole('heading')).toContainText('admin.setting_changed');
    await settingEntry.getByRole('button', { name: /admin\.setting_changed/ }).click();
    await expect(settingEntry).toContainText(String(NEW_MIN_AGE));

    const suspendEntry = (await search('admin.user_suspended')).filter({ hasText: suspendReason });
    await expect(suspendEntry).toHaveCount(1);
    await expect(suspendEntry).toContainText(fixtures().admin.displayName);
  });
});
