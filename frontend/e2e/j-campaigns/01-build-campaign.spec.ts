import type { Page } from '@playwright/test';
import {
  DAY,
  api,
  expect,
  field,
  modal,
  pngFile,
  rememberCampaign,
  state,
  test,
  toast,
  utcInput,
} from './support/campaigns';

/**
 * The campaign manager builds a campaign in the editor, section by section: basics with a category and topics, the
 * schedule, targeting (platforms + countries), posting instructions with a per-platform/country disclosure override,
 * a budget and reward rules (base rate, TikTok override, first-post bonus, daily cap), verification limits and the
 * landing content. Then: a reload in the middle of an edit (the saved draft survives, the unsaved edit is gone),
 * uploaded image + caption assets, the payout preview (draft rules, caps) and publishing (Active at once).
 */
test.describe.serial('manager builds a campaign in the editor', () => {
  const title = () => `Aurora Glow ${state().runId}`;
  let campaignId = '';

  const tab = (page: Page, name: string) => page.getByRole('tab', { name: new RegExp(`^${name}`) });
  const ruleCard = (page: Page, type: string) =>
    page
      .getByRole('list', { name: 'Reward rules' })
      .getByRole('listitem')
      .filter({ has: page.getByRole('heading', { level: 3, name: new RegExp(`^${type}`) }) });

  test('creates a draft through every editor section', async ({ as }) => {
    const page = await as(state().manager, /\/manage$/);
    await page.goto('/manage/campaigns/new');
    await expect(page.getByRole('heading', { level: 1, name: 'New campaign' })).toBeVisible();

    // Basics
    await field(page, 'Title').fill(title());
    await field(page, 'Summary').fill('Show your autumn skincare routine with Aurora Glow.');
    await field(page, 'Description').fill('Line one.\nLine two.');
    await field(page, 'Category').selectOption({ label: 'Lifestyle' });
    const topics = field(page, 'Topics');
    await topics.fill('skincare');
    await topics.press('Enter');
    await expect(page.getByRole('list', { name: 'Selected topics' })).toContainText('skincare');

    // Schedule: started an hour ago, UTC.
    await tab(page, 'Schedule').click();
    await field(page, 'Campaign time zone').selectOption('UTC');
    await field(page, 'Starts').fill(utcInput(Date.now() - 3600_000));
    await field(page, 'Ends').fill(utcInput(Date.now() + 30 * DAY));
    await field(page, 'Submission deadline').fill(utcInput(Date.now() + 33 * DAY));

    // Targeting
    await tab(page, 'Targeting').click();
    const platforms = page.getByRole('group', { name: /Platforms/ });
    await platforms.getByRole('checkbox', { name: 'Instagram' }).check();
    await platforms.getByRole('checkbox', { name: 'TikTok' }).check();
    const countries = field(page, 'Countries');
    for (const code of ['gb', 'PK']) {
      await countries.fill(code);
      await countries.press('Enter');
    }
    await expect(page.getByRole('list', { name: 'Selected countries' })).toContainText('GBPK');

    // Content: instructions, hashtags, default disclosure and an Instagram + PK override.
    await tab(page, 'Content').click();
    await field(page, 'Posting instructions').fill(
      'Film your routine and tag us. Keep the post public for a day.',
    );
    await field(page, 'Required hashtags').fill('#AuroraGlow');
    await field(page, 'Default disclosure').fill('#ad');
    const overrides = page.getByRole('region', { name: 'Disclosure overrides' });
    await overrides.getByRole('button', { name: 'Add override' }).click();
    await field(overrides, 'Platform').selectOption('Instagram');
    await field(overrides, 'Country code').fill('pk');
    await field(overrides, 'Disclosure text').fill('#ad #sponsored (PK)');

    // Rewards: budget 50 USD; base 5, TikTok 7, first post +1, daily cap 20.
    await tab(page, 'Rewards').click();
    await page.getByLabel(/^Campaign budget \(USD\)/).fill('50');
    await field(ruleCard(page, 'Base rate'), 'Amount (USD)').fill('5');
    await page.getByRole('button', { name: 'Rate override', exact: true }).click();
    const override = ruleCard(page, 'Rate override');
    await field(override, 'Amount (USD)').fill('7');
    await field(override, 'Platform').selectOption('TikTok');
    await field(override, 'Label').fill('TikTok rate');
    await page.getByRole('button', { name: 'First-post bonus', exact: true }).click();
    await field(ruleCard(page, 'First-post bonus'), 'Amount (USD)').fill('1');
    await field(page, 'Daily cap per participant').fill('20');
    await expect(page.getByText('Check the rules')).toHaveCount(0);

    // Verification + landing
    await tab(page, 'Verification').click();
    await field(page, 'Maximum submissions per participant').fill('5');
    await field(page, 'Minimum hours the post stays live').fill('0');
    await tab(page, 'Landing').click();
    await field(page, 'Headline').fill(`Glow up with Aurora ${state().runId}`);
    await field(page, 'Body').fill('Share your routine and get paid for every approved post.');

    await page.getByRole('button', { name: 'Create draft' }).click();
    await expect(toast(page, 'Draft campaign created')).toBeVisible();
    await expect(page).toHaveURL(/\/manage\/campaigns\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { level: 1, name: title() })).toBeVisible();
    await expect(page.getByText('Draft', { exact: true }).first()).toBeVisible();
    campaignId = page.url().split('/').pop()!;

    // What was saved (API read-back of the draft the UI created).
    const saved = await (
      await api(state().manager)
    ).get<{
      slug: string;
      status: string;
      category: { name: string };
      topics: string[];
      platforms: string[];
      eligibility: { countries: string[] };
      budgetAmount: number;
      budgetCurrency: string;
      maxSubmissionsPerParticipant: number;
      disclosures: { platform: string; countryCode: string; text: string }[];
      currentRuleSet: {
        version: number;
        dailyCapPerParticipant: number;
        rules: { type: string; amount: number; platform: string | null }[];
      };
      landingHeadline: string;
    }>(`/admin/campaigns/${campaignId}`);
    expect(saved).toMatchObject({
      status: 'Draft',
      category: { name: 'Lifestyle' },
      topics: ['skincare'],
      platforms: ['Instagram', 'TikTok'],
      eligibility: { countries: ['GB', 'PK'] },
      budgetAmount: 50,
      budgetCurrency: 'USD',
      maxSubmissionsPerParticipant: 5,
      disclosures: [{ platform: 'Instagram', countryCode: 'PK', text: '#ad #sponsored (PK)' }],
      currentRuleSet: { version: 1, dailyCapPerParticipant: 20 },
    });
    expect(saved.currentRuleSet.rules.map((r) => [r.type, r.amount, r.platform]).sort()).toEqual(
      [
        ['BaseRate', 5, null],
        ['FirstPostBonus', 1, null],
        ['RateOverride', 7, 'TikTok'],
      ].sort(),
    );
    rememberCampaign('glow', { id: campaignId, slug: saved.slug, title: title() });
  });

  test('a reload in the middle of an edit keeps the saved draft and drops the unsaved change', async ({
    as,
  }) => {
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/campaigns/${campaignId}`);
    await expect(field(page, 'Title')).toHaveValue(title());
    await field(page, 'Title').fill(`${title()} — unsaved`);
    await expect(page.getByRole('button', { name: 'Save changes' })).toBeEnabled();
    await page.reload();

    await expect(page.getByRole('heading', { level: 1, name: title() })).toBeVisible();
    await expect(field(page, 'Title')).toHaveValue(title());
    await expect(page.getByRole('button', { name: 'Save changes' })).toBeDisabled();
    await tab(page, 'Content').click();
    await expect(
      page.getByRole('region', { name: 'Disclosure overrides' }).getByLabel('Disclosure text'),
    ).toHaveValue('#ad #sponsored (PK)');
    await tab(page, 'Rewards').click();
    await expect(field(ruleCard(page, 'Rate override'), 'Amount (USD)')).toHaveValue('7');
  });

  test('adds an uploaded image and a caption asset, and reorders them', async ({ as }) => {
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/campaigns/${campaignId}`);
    await tab(page, 'Assets').click();
    await expect(page.getByText('No assets yet')).toBeVisible();

    await page.getByRole('button', { name: 'Add asset' }).click();
    let dialog = modal(page, 'Add asset');
    await field(dialog, 'Type').selectOption('Image');
    await field(dialog, 'Title').fill('Hero bottle shot');
    await dialog.locator('input[type="file"]').setInputFiles(pngFile(11, 'hero.png'));
    await dialog.locator('button', { hasText: 'Upload image' }).click(); // (the file input itself is also named "Upload image")
    await expect(dialog.getByText('Uploaded image attached.')).toBeVisible();
    await dialog.getByRole('button', { name: 'Add asset' }).click();
    await expect(toast(page, 'Asset added')).toBeVisible();

    await page.getByRole('button', { name: 'Add asset' }).click();
    dialog = modal(page, 'Add asset');
    await field(dialog, 'Type').selectOption('Caption');
    await field(dialog, 'Title').fill('Approved caption');
    await field(dialog, 'Caption').fill('My autumn glow routine #AuroraGlow #ad');
    await field(dialog, 'Platform').selectOption('Instagram');
    await dialog.getByRole('button', { name: 'Add asset' }).click();
    await expect(toast(page, 'Asset added')).toBeVisible();

    const assets = page.getByRole('list', { name: 'Campaign assets' }).getByRole('listitem');
    await expect(assets).toHaveCount(2);
    await expect(assets.nth(0)).toContainText('Hero bottle shot');
    await page.getByRole('button', { name: 'Move Approved caption up' }).click();
    await expect(assets.nth(0)).toContainText('Approved caption');

    // The uploaded image is served by the API (public campaign file).
    const img = page.getByRole('list', { name: 'Campaign assets' }).locator('img.mg-asset__thumb'); // decorative (alt="")
    await expect.poll(() => img.evaluate((el: HTMLImageElement) => el.complete && el.naturalWidth)).toBe(400);
  });

  test('previews payouts with the draft rules, including the daily cap', async ({ as }) => {
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/campaigns/${campaignId}`);
    await tab(page, 'Rewards').click();
    // Wait until the tab has settled (the version history loads below the preview and shifts the layout).
    await expect(page.getByRole('list', { name: 'Reward rule versions' })).toContainText('Version 1');
    const preview = page.getByRole('region', { name: 'Preview a payout' });
    await field(preview, 'Platform').selectOption('TikTok');
    await field(preview, 'Country').fill('PK');
    await field(preview, 'First approved post in this campaign').check();
    await expect(field(preview, 'First approved post in this campaign')).toBeChecked();
    await preview.getByRole('button', { name: 'Calculate' }).click();
    const quote = preview.getByTestId('reward-quote');
    await expect(quote).toContainText('TikTok rate');
    await expect(quote.getByText('Total').locator('..')).toContainText('$8.00');
    await expect(quote).toContainText('No caps were applied.');

    await field(preview, 'Earned today').fill('19.5');
    await preview.getByRole('button', { name: 'Calculate' }).click();
    await expect(quote.getByText('Total').locator('..')).toContainText('$0.50');
    await expect(
      quote.getByRole('region', { name: 'Caps applied' }).or(quote.getByText('Caps applied')),
    ).toBeVisible();
    await expect(quote).toContainText('Daily cap per participant');
  });

  test('publishes: the readiness checklist is complete and the campaign goes live at once', async ({
    as,
  }) => {
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/campaigns/${campaignId}`);
    await page.getByRole('button', { name: 'Publish', exact: true }).click();
    const dialog = modal(page, 'Publish campaign');
    const checklist = dialog.getByRole('list', { name: 'Readiness checklist' });
    await expect(checklist.getByRole('listitem')).toHaveCount(7);
    await expect(checklist).not.toContainText('missing');
    await dialog.getByRole('button', { name: 'Publish now' }).click();
    await expect(toast(page, 'Campaign published')).toBeVisible();
    await expect(page.getByText('Active', { exact: true }).first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Publish', exact: true })).toHaveCount(0);
  });
});
