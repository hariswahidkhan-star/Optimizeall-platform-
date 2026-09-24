import { approve, expect, postReward, recall, remember, state, submitPost, test } from './support/rates';

/**
 * The rate flows through the money path: the submission is priced (and locked) with the personal deal, the reviewer
 * sees which rate applies, and finance sees the rate source on the ledger line and in the export.
 */
test.describe.serial('submission, approval and ledger', () => {
  test('the estimate uses the personal deal and the reviewer sees the rate source', async ({ as }) => {
    const [ivy, milo] = state().participants;
    const { campaign } = state();
    const ivySubmission = await submitPost(ivy!, campaign.id);
    const miloSubmission = await submitPost(milo!, campaign.id);
    remember('ivySubmission', ivySubmission);
    remember('miloSubmission', miloSubmission);

    const ivyPage = await as(ivy!, /\/app/);
    await ivyPage.goto(`/app/submissions/${ivySubmission}`);
    const reward = ivyPage.getByRole('region', { name: 'Reward' });
    await expect(reward).toContainText('20.00');
    await expect(reward).toContainText('Your personal rate (locked when you submitted)');

    const reviewer = await as(state().reviewer, /\/review/);
    await reviewer.goto(`/review/queue/${ivySubmission}`);
    const source = reviewer.getByLabel('Rate that applies');
    await expect(source).toContainText('Person-level rate');
    await expect(source).toContainText('campaign rate would be');
    await expect(source).toContainText('5.00');
  });

  test('approval writes earnings with the rate source; finance sees it on the ledger', async ({ as }) => {
    const ivySubmission = recall<string>('ivySubmission');
    const miloSubmission = recall<string>('miloSubmission');
    expect((await approve(state().reviewer, ivySubmission)).reward?.total).toBe(20);
    expect((await approve(state().reviewer, miloSubmission)).reward?.total).toBe(12);

    const ivyLine = await postReward(ivySubmission);
    expect(ivyLine.originalAmount).toBe(20);
    expect(ivyLine.rateSource).toBe('GlobalPersonalCustom');
    const miloLine = await postReward(miloSubmission);
    expect(miloLine.originalAmount).toBe(12);
    expect(miloLine.rateSource).toBe('GlobalGroup');
    expect(miloLine.rateCardVersion).toBe(1);
    expect(miloLine.rateSourceLabel).toContain(`Micro influencers ${state().runId}`);

    const finance = await as(state().finance, /\/finance/);
    await finance.goto('/finance/ledger');
    await finance.getByRole('searchbox', { name: /search/i }).fill(miloSubmission);
    const table = finance.getByRole('table', { name: /ledger/i });
    await expect(table.getByText(/Rate: Group 'Micro influencers/)).toBeVisible();
    await table
      .getByRole('row')
      .filter({ hasText: 'Creator fee' })
      .first()
      .getByRole('button', { name: /Actions for/ })
      .click();
    await finance.getByRole('menuitem', { name: 'View details' }).click();
    await expect(finance.getByText(/locked at submission/)).toBeVisible();
  });
});
