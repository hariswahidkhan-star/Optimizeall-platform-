import {
  api,
  approve,
  expect,
  field,
  modal,
  postReward,
  recall,
  state,
  submitPost,
  test,
  toast,
} from './support/rates';

/**
 * Rates change over time without touching the past: a new card version prices new posts only (the approved earning
 * keeps its amount), and when the personal deal lapses the person falls back to their group rate.
 */
test.describe.serial('rate changes and deal expiry', () => {
  test('a new card version prices new posts; approved earnings keep their price', async ({ as }) => {
    const cardId = recall<string>('cardId');
    const miloSubmission = recall<string>('miloSubmission');
    const [, milo] = state().participants;

    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/rate-cards/${cardId}`);
    await page.getByRole('button', { name: 'New version' }).click();
    const dialog = modal(page, /New version of/);
    // Instagram 12 → 15 (+25%: under the Demo seed's 50% four-eyes threshold, so it applies at once).
    await field(dialog, 'Rate 2 amount').fill('15');
    await field(dialog, 'Reason').fill('Instagram demand is up');
    await dialog.getByRole('button', { name: 'Save version 2' }).click();
    await expect(toast(page, 'Version 2 saved')).toBeVisible();
    await page.getByRole('tab', { name: /^Versions/ }).click();
    await expect(page.getByText('“Instagram demand is up”')).toBeVisible();

    const next = await submitPost(milo!, state().campaign.id);
    expect((await approve(state().reviewer, next)).reward?.total).toBe(15);
    const newLine = await postReward(next);
    expect(newLine.rateCardVersion).toBe(2);
    // The earning approved before the change is untouched.
    const oldLine = await postReward(miloSubmission);
    expect(oldLine.originalAmount).toBe(12);
    expect(oldLine.rateCardVersion).toBe(1);

    // A raise above the four-eyes threshold waits for a second person and prices nothing meanwhile.
    const manager = await api(state().manager);
    await manager.post(`/admin/rate-cards/${cardId}/versions`, {
      currency: 'USD',
      lines: [
        { amount: 10, platform: null, format: null, countryCode: null, label: null },
        { amount: 40, platform: 'Instagram', format: null, countryCode: null, label: 'Creator fee' },
      ],
      reason: 'Aggressive Q4 raise',
      baseVersion: 2,
      confirm: true,
    });
    await page.reload();
    await expect(page.getByText(/raises rates by up to 166\.67% and needs a second approval/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Approve' })).toBeDisabled();
    const pendingPrice = await submitPost(milo!, state().campaign.id);
    expect((await approve(state().reviewer, pendingPrice)).reward?.total).toBe(15);
  });

  test('when the personal deal expires the person falls back to the group rate', async ({ as }) => {
    const [ivy] = state().participants;
    const ivySubmission = recall<string>('ivySubmission');
    // submitPost dates the post 30 s in the past, and a post published while the deal was
    // still valid is (rightly) priced with the deal, so wait until that date is past expiry too.
    const wait = recall<number>('dealExpiresAt') - Date.now() + 45_000;
    if (wait > 0) {
      test.setTimeout(wait + 120_000);
      await new Promise((resolve) => setTimeout(resolve, wait));
    }

    const page = await as(ivy!, /\/app/);
    await page.goto(`/app/campaigns/${state().campaign.slug}`);
    const rate = page.getByRole('region', { name: 'Your rate' });
    await expect(rate.getByText(/A special rate applies to you/)).toBeVisible();
    await expect(rate.getByRole('listitem').filter({ hasText: /^Instagram/ })).toContainText('15.00');
    await expect(page.getByRole('region', { name: 'Your personal rate' })).toHaveCount(0);

    const after = await submitPost(ivy!, state().campaign.id);
    expect((await approve(state().reviewer, after)).reward?.total).toBe(15);
    expect((await postReward(after)).rateSource).toBe('GlobalGroup');
    // The deal-priced earning keeps its 20.
    expect((await postReward(ivySubmission)).originalAmount).toBe(20);

    // The manager sees the lapsed deal as inactive.
    const manager = await as(state().manager, /\/manage$/);
    await manager.goto(`/admin/users/${ivy!.id}#rates`);
    const section = manager.getByRole('region', { name: 'Rates' });
    await expect(
      section
        .getByRole('table', { name: /Effective rates of/ })
        .getByRole('row', { name: /^Instagram · Any format/ }),
    ).toContainText('15.00');
    await expect(
      section.getByRole('table', { name: /Rate assignments of/ }).getByText('Inactive'),
    ).toBeVisible();
  });
});
