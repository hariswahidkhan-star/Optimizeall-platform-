import { type Page, expect, test } from '@playwright/test';
import { ApiSession, mailLink } from './support/api';
import { fixtures, participantFor } from './support/fixtures';
import { pngFile } from './support/png';
import { daysAgo, expectNoHorizontalScroll } from './support/ui';

/**
 * Participant journey: register with a referral code → verify the email from the mailbox → sign in → add a
 * qualifying and a too-new social profile → browse and filter campaigns → read the campaign terms → submit proof →
 * duplicate post URL rejected → pending earnings. Runs on desktop and on mobile (own participant per project), with
 * horizontal-overflow checks on mobile.
 */
test.describe.serial('participant journey', () => {
  let page: Page;
  let mobile = false;
  let submissionUrl = '';

  test.beforeAll(async ({ browser }, testInfo) => {
    mobile = testInfo.project.name.startsWith('mobile');
    // browser.newContext() applies the project's device settings (Pixel 7 viewport on mobile).
    page = await (await browser.newContext()).newPage();
    if (mobile) expect(page.viewportSize()!.width).toBeLessThan(480);
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  const me = () => participantFor(test.info().project.name);
  const responsive = async (where: string) => {
    if (mobile) await expectNoHorizontalScroll(page, where);
  };

  test('registers through the UI with a referral code', async () => {
    const { referrer } = fixtures();
    await page.goto(`/register?ref=${referrer.referralCode}`);
    await expect(page.getByText('You were invited')).toBeVisible();
    await expect(page.getByText(referrer.referralCode, { exact: true })).toBeVisible();

    await page.getByLabel('Email', { exact: true }).fill(me().email);
    await page.getByLabel('Password', { exact: true }).fill(me().password);
    await page.getByLabel('Display name').fill(me().displayName);
    await page.getByLabel('Country').selectOption('GB');
    await page.getByRole('checkbox', { name: /accept the participant rules/i }).check();
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await expect(page.getByText(me().email)).toBeVisible();
  });

  test('verifies the email with the link from the mailbox', async () => {
    const link = await mailLink(me().email, '/verify-email', /verify/i);
    await page.goto(`${link.pathname}${link.search}`);
    await expect(page.getByRole('heading', { name: 'Your email is verified' })).toBeVisible();

    // The referral was recorded for the referrer (arrangement check through the API).
    const referrer = await ApiSession.login(fixtures().referrer.email, fixtures().referrer.password);
    const referrals = await referrer.get<{ items: { maskedName: string; status: string }[] }>('/me/referrals');
    expect(referrals.items.length).toBeGreaterThan(0);
  });

  test('signs in and home asks for a social profile', async () => {
    await page.getByRole('link', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/login/);
    await page.getByLabel('Email', { exact: true }).fill(me().email);
    await page.getByLabel('Password', { exact: true }).fill(me().password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(/\/app$/);
    await expect(page.getByRole('heading', { name: 'Add the social profile you post from' })).toBeVisible();
    await responsive('home');
  });

  test('adds an Instagram profile declared 400 days old — it qualifies', async () => {
    await page.getByRole('link', { name: 'Add a social profile' }).click();
    const dialog = page.getByRole('dialog', { name: 'Add a social profile' });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel('Platform').selectOption('Instagram');
    await dialog.getByLabel('Handle').fill(me().instagram);
    await dialog.getByLabel('Followers').fill('2500');
    await dialog.getByLabel('Profile link').fill(`https://www.instagram.com/${me().instagram}`);
    await dialog.getByLabel('Account created on').fill(daysAgo(400));
    await dialog.getByRole('button', { name: 'Add profile' }).click();
    await expect(dialog).toBeHidden();

    const card = page.getByRole('article', { name: new RegExp(`@${escape(me().instagram)}`) });
    await expect(card).toBeVisible();
    await expect(card.getByText('Qualifies', { exact: true })).toBeVisible();
  });

  test('adds a TikTok profile declared 5 days old — not qualifying, with days remaining', async () => {
    await page.getByRole('button', { name: 'Add a profile' }).first().click();
    const dialog = page.getByRole('dialog', { name: 'Add a social profile' });
    await dialog.getByLabel('Platform').selectOption('TikTok');
    await dialog.getByLabel('Handle').fill(me().tiktok);
    await dialog.getByLabel('Followers').fill('120');
    await dialog.getByLabel('Profile link').fill(`https://www.tiktok.com/@${me().tiktok}`);
    await dialog.getByLabel('Account created on').fill(daysAgo(5));
    await dialog.getByRole('button', { name: 'Add profile' }).click();
    await expect(dialog).toBeHidden();

    const card = page.getByRole('article', { name: new RegExp(`@${escape(me().tiktok)}`) });
    await expect(card.getByText('Doesn’t qualify yet')).toBeVisible();
    await expect(card.getByText('Profiles must be at least 90 days old. This one qualifies in 85 days.')).toBeVisible();
    await expect(card.getByText(/85 days\s*until it qualifies/)).toBeVisible();
    await responsive('social accounts');
  });

  test('browses campaigns and filters by platform', async () => {
    const { campaign } = fixtures();
    await page.goto('/app/campaigns');
    await expect(page.getByRole('heading', { level: 1, name: 'Campaigns' })).toBeVisible();
    const card = page.getByRole('link', { name: campaign.title });
    await expect(card).toBeVisible();
    await responsive('campaigns');

    if (mobile) await page.getByRole('button', { name: /^Filters/ }).click();
    const platform = page.getByRole('combobox', { name: 'Platform' });
    await platform.selectOption('X');
    await expect(page).toHaveURL(/platform=X/);
    await expect(page.getByRole('heading', { name: 'No campaigns match these filters' })).toBeVisible();
    await expect(card).toBeHidden();

    await platform.selectOption('TikTok');
    await expect(page).toHaveURL(/platform=TikTok/);
    await expect(card).toBeVisible();
    await responsive('campaigns (filtered)');
  });

  test('opens the campaign and sees the disclosure and reward terms', async () => {
    const { campaign } = fixtures();
    await page.getByRole('link', { name: campaign.title }).click();
    await expect(page.getByRole('heading', { level: 1, name: campaign.title })).toBeVisible();

    const disclosure = page.getByText('Paid-content disclosure required').locator('..').locator('..');
    await expect(disclosure).toContainText(campaign.disclosure);

    const terms = page.getByRole('region', { name: 'Reward terms' });
    await expect(terms).toContainText(/Base reward per approved post\s*\$5\.00/);
    await expect(terms).toContainText(/Posts per participant\s*3 posts/);
    await expect(terms).toContainText(/First.post bonus/i);
    await expect(terms).toContainText('+$1.00');
    await responsive('campaign detail');
  });

  test('submits proof with a real PNG screenshot and sees it pending', async () => {
    await page.getByRole('button', { name: 'Submit proof' }).click();
    const dialog = page.getByRole('dialog', { name: 'Submit proof of your post' });
    await expect(dialog).toBeVisible();
    // Only the Instagram profile qualifies, so it is preselected; the TikTok one is listed as not eligible.
    const profile = dialog.getByLabel('Profile you posted from');
    await expect(profile.locator('option:checked')).toHaveText(new RegExp(`Instagram · @${escape(me().instagram)}`));
    await dialog.getByLabel('Link to your post').fill(`https://instagram.com/p/${me().postCode}/`);
    await dialog.getByLabel('Caption you used').fill(`Loving it! ${fixtures().campaign.hashtag}`);
    await dialog.getByLabel('Screenshot of your post').setInputFiles(pngFile(mobile ? 11 : 1));
    await dialog.getByRole('button', { name: 'Submit proof' }).click();

    await expect(page).toHaveURL(/\/app\/submissions\/[0-9a-f-]{36}$/);
    submissionUrl = new URL(page.url()).pathname;
    await expect(page.getByRole('heading', { level: 1, name: fixtures().campaign.title })).toBeVisible();
    await expect(page.getByText('Pending', { exact: true }).first()).toBeVisible();
    await responsive('submission detail');
  });

  test('the same post with www. and ?utm=x is refused as a duplicate on the URL field', async () => {
    await page.getByRole('link', { name: 'View campaign' }).click();
    await page.getByRole('button', { name: 'Submit proof' }).click();
    const dialog = page.getByRole('dialog', { name: 'Submit proof of your post' });
    const url = dialog.getByLabel('Link to your post');
    await url.fill(`https://www.instagram.com/p/${me().postCode}/?utm=x`);
    await dialog.getByLabel('Screenshot of your post').setInputFiles(pngFile(mobile ? 12 : 2));
    await dialog.getByRole('button', { name: 'Submit proof' }).click();

    await expect(url).toHaveAttribute('aria-invalid', 'true');
    await expect(url).toHaveAccessibleDescription(/already been submitted/i);
    await expect(url).toBeFocused();
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toBeHidden();
  });

  test('earnings show the pending amount', async () => {
    await page.goto('/app/earnings');
    await expect(page.getByRole('heading', { level: 1, name: 'Earnings' })).toBeVisible();
    // Stat tiles have no accessible grouping (reported), so the tile is found by its CSS class and label text.
    const pending = page.locator('.ui-stat').filter({ hasText: /^Pending/ });
    await expect(pending).toContainText('$6.00');
    await responsive('earnings');
  });

  test('home and submissions stay within the viewport', async () => {
    await page.goto('/app');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await responsive('home (active)');
    await page.goto(submissionUrl);
    await expect(page.getByRole('heading', { level: 1, name: fixtures().campaign.title })).toBeVisible();
    await responsive('submission detail');
    await page.goto('/app/submissions');
    await expect(page.getByRole('heading', { level: 1, name: 'My submissions' })).toBeVisible();
    await responsive('submissions');
  });
});

function escape(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
