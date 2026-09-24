import { expect, field, modal, remember, state, test, toast, utcInput } from './support/rates';

/**
 * A negotiated personal deal on top of the group rate: created from the person's Rates section with an expiry,
 * "explain this rate" shows why it wins, and each participant sees only their own rate on the campaign page.
 */
test.describe.serial('personal deal, explanation and the participant view', () => {
  test('a manager negotiates an expiring personal rate and the explanation shows why it wins', async ({
    as,
  }) => {
    const [ivy] = state().participants;
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/admin/users/${ivy!.id}#rates`);
    const section = page.getByRole('region', { name: 'Rates' });
    await expect(
      section
        .getByRole('list', { name: 'Rate groups' })
        .getByRole('link', { name: new RegExp(`Micro influencers ${state().runId}`) }),
    ).toBeVisible();
    await section.getByRole('button', { name: 'Custom rate' }).click();

    const dialog = modal(page, /Custom rate for/);
    await field(dialog, 'Rate 1 platform').selectOption('Instagram');
    await field(dialog, 'Rate 1 amount').fill('20');
    await field(dialog, 'Rate 1 label').fill('Ambassador fee');
    // Expires a few minutes from now (spec 04 waits for it): at least 3 minutes so the form never sends a past time.
    const expires = Math.max(state().dealExpiresAt, Date.now() + 3 * 60_000);
    remember('dealExpiresAt', expires);
    await field(dialog, 'Valid until (UTC)').fill(utcInput(expires));
    await field(dialog, 'Reason / deal reference').fill('Ambassador deal DL-7');
    await dialog.getByRole('button', { name: 'Save custom rate' }).click();
    await expect(toast(page, 'Custom rate saved')).toBeVisible();

    const effective = section.getByRole('table', { name: /Effective rates of/ });
    const instagram = effective.getByRole('row', { name: /^Instagram · Any format/ });
    await expect(instagram).toContainText('20.00');
    await expect(instagram).toContainText('Custom rate · all campaigns');
    await expect(effective.getByRole('row', { name: /^TikTok · Any format/ })).toContainText('10.00');

    await instagram.getByRole('button', { name: 'Explain' }).click();
    const explain = modal(page, 'Explain this rate');
    await expect(explain.getByText(/wins: 20/)).toBeVisible();
    const candidates = explain.getByRole('table', { name: 'Candidate rates' });
    await expect(candidates.getByRole('row', { name: /Applies.*Custom rate/ })).toBeVisible();
    await expect(candidates.getByRole('row', { name: /Outranked.*Micro creators/ })).toBeVisible();
    await explain.getByRole('button', { name: 'Close', exact: true }).last().click();
  });

  test('each participant sees only their own rate on the campaign', async ({ as }) => {
    const [ivy, milo] = state().participants;
    const { campaign, runId } = state();

    const ivyPage = await as(ivy!, /\/app/);
    await ivyPage.goto(`/app/campaigns/${campaign.slug}`);
    const personal = ivyPage.getByRole('region', { name: 'Your personal rate' });
    await expect(personal).toBeVisible();
    await expect(personal.getByRole('listitem').filter({ hasText: /^Instagram/ })).toContainText('20.00');
    await expect(personal.getByRole('listitem').filter({ hasText: /^TikTok/ })).toContainText('10.00');
    await expect(personal.getByText(/posts made until/)).toBeVisible();
    // Commercial terms of groups and cards are never shown to participants.
    await expect(ivyPage.getByText(`Micro influencers ${runId}`)).toHaveCount(0);
    await expect(ivyPage.getByText(`Micro creators ${runId}`)).toHaveCount(0);

    const miloPage = await as(milo!, /\/app/);
    await miloPage.goto(`/app/campaigns/${campaign.slug}`);
    const special = miloPage.getByRole('region', { name: 'Your rate' });
    await expect(special.getByText(/A special rate applies to you/)).toBeVisible();
    await expect(special.getByRole('listitem').filter({ hasText: /^Instagram/ })).toContainText('12.00');
    await miloPage.goto('/app/campaigns');
    await expect(
      miloPage
        .getByRole('article')
        .filter({ hasText: campaign.title })
        .getByText(/Your rate:/),
    ).toBeVisible();
  });
});
