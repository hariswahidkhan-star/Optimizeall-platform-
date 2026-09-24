import { type Page, expect, test } from '@playwright/test';
import { signIn } from '../journeys/support/ui';
import { daysAgo } from '../journeys/support/ui';
import { state } from './support/state';

/**
 * Participant lifecycle, part 2 — social profiles and campaigns: the 90-day minimum account age at its day boundary
 * (89 days → not qualifying, with the reason and "1 day" left; exactly 90 days → qualifies), a future creation date,
 * a duplicate handle (case-insensitive) and another participant's handle are refused; then the campaign list is
 * searched and filtered (search, topic, platform, eligible only) and the campaign opened (disclosure, terms, which
 * profile is eligible).
 */
test.describe.serial('social profiles and campaigns', () => {
  let page: Page;
  const s = () => state();

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
    await signIn(page, s().pat, /\/app$/);
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  const dialog = () => page.getByRole('dialog', { name: 'Add a social profile' });
  const card = (handle: string) =>
    page.getByRole('article', { name: new RegExp(`@${handle.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`) });

  async function fillProfile(platform: string, handle: string, createdOn: string, followers = '1500') {
    const d = dialog();
    await d.getByLabel('Platform').selectOption(platform);
    await d.getByLabel('Handle').fill(handle);
    await d.getByLabel('Followers').fill(followers);
    const host = platform === 'TikTok' ? 'www.tiktok.com/@' : 'www.instagram.com/';
    await d.getByLabel('Profile link').fill(`https://${host}${handle}`);
    await d.getByLabel('Account created on').fill(createdOn);
  }

  test('a TikTok profile 89 days old does not qualify yet — one day to go', async () => {
    await page.goto('/app/social-accounts');
    await page.getByRole('button', { name: /^Add a (social )?profile$/ }).first().click();
    await expect(dialog()).toBeVisible();
    await expect(dialog()).toContainText('Profiles must be at least 90 days old');
    await fillProfile('TikTok', s().pat.tiktok, daysAgo(89), '320');
    await dialog().getByRole('button', { name: 'Add profile' }).click();
    await expect(dialog()).toBeHidden();

    const tiktok = card(s().pat.tiktok);
    await expect(tiktok.getByText('Doesn’t qualify yet')).toBeVisible();
    await expect(
      tiktok.getByText('Profiles must be at least 90 days old. This one qualifies in 1 day.'),
    ).toBeVisible();
    await expect(tiktok.getByText(/1 day\s*until it qualifies/)).toBeVisible();
    await expect(tiktok).toContainText('(89 days old)');
  });

  test('a creation date in the future is caught before saving', async () => {
    await page.getByRole('button', { name: /^Add a (social )?profile$/ }).first().click();
    const tomorrow = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);
    await fillProfile('Instagram', s().pat.instagram, tomorrow);
    await dialog().getByRole('button', { name: 'Add profile' }).click();
    const created = dialog().getByLabel('Account created on');
    await expect(created).toBeFocused();
    await expect(created).toHaveAccessibleDescription(/can’t be in the future/);
  });

  test('an Instagram profile exactly 90 days old qualifies', async () => {
    await dialog().getByLabel('Account created on').fill(daysAgo(90));
    await dialog().getByRole('button', { name: 'Add profile' }).click();
    await expect(dialog()).toBeHidden();
    const instagram = card(s().pat.instagram);
    await expect(instagram.getByText('Qualifies', { exact: true })).toBeVisible();
    await expect(instagram).toContainText('(90 days old)');
  });

  test('the same handle again (other case) and another participant’s handle are refused on the Handle field', async () => {
    await page.getByRole('button', { name: /^Add a (social )?profile$/ }).first().click();
    await fillProfile('Instagram', s().pat.instagram.toUpperCase(), daysAgo(400));
    await dialog().getByRole('button', { name: 'Add profile' }).click();
    const handle = dialog().getByLabel('Handle');
    await expect(handle).toHaveAttribute('aria-invalid', 'true');
    await expect(handle).toBeFocused();

    await handle.fill(`stranger.ig.${s().runId}`);
    await dialog().getByLabel('Profile link').fill(`https://www.instagram.com/stranger.ig.${s().runId}`);
    await dialog().getByRole('button', { name: 'Add profile' }).click();
    await expect(handle).toHaveAttribute('aria-invalid', 'true');
    await dialog().getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog()).toBeHidden();
    await expect(page.getByRole('article')).toHaveCount(2);
  });

  test('searches and filters the campaign list', async () => {
    const { main, other, runId } = s();
    await page.goto('/app/campaigns');
    await expect(page.getByRole('heading', { level: 1, name: 'Campaigns' })).toBeVisible();
    const mainCard = page.getByRole('link', { name: main.title });
    const otherCard = page.getByRole('link', { name: other.title });

    const search = page.getByRole('searchbox', { name: 'Search' });
    await search.fill(runId);
    await expect(page).toHaveURL(new RegExp(`q=${runId}`));
    await expect(mainCard).toBeVisible();
    await expect(otherCard).toBeVisible();

    await search.fill(`Gadget Week ${runId}`);
    await expect(otherCard).toBeVisible();
    await expect(mainCard).toBeHidden();
    await search.fill(runId);
    await expect(mainCard).toBeVisible();

    const topic = page.getByRole('textbox', { name: 'Topic' });
    await topic.fill('LIFESTYLE');
    await expect(page).toHaveURL(/topic=/);
    await expect(mainCard).toBeVisible();
    await expect(otherCard).toBeHidden();
    await topic.fill('');
    await expect(otherCard).toBeVisible();

    const platform = page.getByRole('combobox', { name: 'Platform' });
    await platform.selectOption('X');
    await expect(page).toHaveURL(/platform=X/);
    await expect(otherCard).toBeVisible();
    await expect(mainCard).toBeHidden();
    await platform.selectOption('TikTok');
    await expect(mainCard).toBeVisible();
    await expect(otherCard).toBeHidden();
    await platform.selectOption('');

    // Pat has no X profile, so only the main campaign is open to Pat.
    // The checkbox mirrors the URL, which updates in a transition: click, then wait for the checked state (check()
    // verifies synchronously and races the URL update).
    const eligibleOnly = page.getByRole('checkbox', { name: /Only campaigns I.m eligible for/ });
    await eligibleOnly.click();
    await expect(eligibleOnly).toBeChecked();
    await expect(page).toHaveURL(/eligible=1/);
    await expect(mainCard).toBeVisible();
    await expect(otherCard).toBeHidden();

    // Filters live in the URL: a reload keeps them.
    await page.reload();
    await expect(page.getByRole('checkbox', { name: /Only campaigns I.m eligible for/ })).toBeChecked();
    await expect(page.getByRole('searchbox', { name: 'Search' })).toHaveValue(runId);
    await expect(mainCard).toBeVisible();
    await expect(otherCard).toBeHidden();
  });

  test('opens the campaign: disclosure, reward terms and which profile is eligible', async () => {
    const { main } = s();
    await page.getByRole('link', { name: main.title }).click();
    await expect(page.getByRole('heading', { level: 1, name: main.title })).toBeVisible();
    await expect(page.getByRole('status', { name: 'Paid-content disclosure required' })).toContainText(
      main.disclosure,
    );
    const terms = page.getByRole('region', { name: 'Reward terms' });
    await expect(terms).toContainText(/Base reward per approved post\s*\$5\.00/);
    await expect(terms).toContainText(/Posts per participant\s*5 posts/);
    await expect(terms).toContainText('+$1.00');

    await page.getByRole('button', { name: 'Submit proof' }).click();
    const proof = page.getByRole('dialog', { name: 'Submit proof of your post' });
    const profile = proof.getByLabel('Profile you posted from');
    await expect(profile.locator('option:checked')).toHaveText(`Instagram · @${s().pat.instagram}`);
    await expect(profile.locator('option', { hasText: s().pat.tiktok })).toHaveText(
      `TikTok · @${s().pat.tiktok} (not eligible)`,
    );
    await expect(profile.locator('option', { hasText: s().pat.tiktok })).toBeDisabled();
    // The participant's own time zone is used for the post time.
    await expect(proof.getByLabel('When it went live')).toHaveAccessibleDescription(/Asia\/Karachi/);
    await proof.getByRole('button', { name: 'Cancel' }).click();
    await expect(proof).toBeHidden();
  });
});
