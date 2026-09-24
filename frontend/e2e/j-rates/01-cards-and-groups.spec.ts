import { api, expect, field, modal, recall, refused, remember, state, test, toast } from './support/rates';

/**
 * Builds the pricing structure in the manager portal: a rate card with a default and an Instagram rate, a rate group,
 * both participants bulk-added to it (multi-select) and the card assigned to the group for every campaign.
 */
test.describe.serial('rate cards and rate groups', () => {
  test('a manager creates a rate card with platform-specific rates', async ({ as }) => {
    const { runId } = state();
    const page = await as(state().manager, /\/manage$/);
    await page.getByRole('link', { name: 'Rate cards' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'Rate cards' })).toBeVisible();
    await page.getByRole('button', { name: 'New rate card' }).first().click();

    const dialog = modal(page, 'New rate card');
    await field(dialog, 'Name').fill(`Micro creators ${runId}`);
    await field(dialog, 'Rate 1 amount').fill('10');
    await dialog.getByRole('button', { name: 'Add rate' }).click();
    await field(dialog, 'Rate 2 platform').selectOption('Instagram');
    await field(dialog, 'Rate 2 amount').fill('12');
    await field(dialog, 'Rate 2 label').fill('Creator fee');
    // A duplicate condition is flagged before the server is asked.
    await dialog.getByRole('button', { name: 'Add rate' }).click();
    await field(dialog, 'Rate 3 platform').selectOption('Instagram');
    await field(dialog, 'Rate 3 amount').fill('99');
    await expect(dialog.getByText(/same platform, format and country/)).toBeVisible();
    await dialog.getByRole('button', { name: 'Remove rate 3' }).click();
    await field(dialog, 'Reason').fill('Micro creator pricing for Q4');
    await dialog.getByRole('button', { name: 'Create rate card' }).click();

    await expect(page.getByRole('heading', { level: 1, name: `Micro creators ${runId}` })).toBeVisible();
    await expect(toast(page, 'Rate card created')).toBeVisible();
    const table = page.getByRole('table', { name: 'Rates of version 1' });
    await expect(table.getByRole('row', { name: /Instagram.*12\.00/ })).toBeVisible();
    await expect(table.getByRole('row', { name: /Any post \(default\).*10\.00/ })).toBeVisible();
    remember('cardId', page.url().split('/').pop());
  });

  test('a group is created, both participants are added at once and the card is assigned to it', async ({
    as,
  }) => {
    const { runId, participants } = state();
    const page = await as(state().manager, /\/manage$/);
    await page.goto('/manage/rate-groups');
    await page.getByRole('button', { name: 'New rate group' }).first().click();
    const create = modal(page, 'New rate group');
    await field(create, 'Name').fill(`Micro influencers ${runId}`);
    await field(create, 'Priority').fill('30');
    await create.getByRole('button', { name: 'Create group' }).click();
    await expect(page.getByRole('heading', { level: 1, name: `Micro influencers ${runId}` })).toBeVisible();
    remember('groupId', page.url().split('/').pop());

    await page.getByRole('button', { name: 'Add people' }).click();
    const add = modal(page, /Add people to/);
    await add.getByLabel('Search participants by name or email').fill(runId);
    for (const p of participants) await add.getByRole('checkbox', { name: p.displayName }).check();
    await add.getByRole('button', { name: 'Add 2 people' }).click();
    await expect(add.getByText('2 added · 0 already members')).toBeVisible();
    await add.getByRole('button', { name: 'Done' }).click();

    const members = page.getByRole('table', { name: 'Group members' });
    for (const p of participants)
      await expect(members.getByRole('link', { name: p.displayName })).toBeVisible();

    await page.getByRole('button', { name: 'Assign rate card' }).click();
    const assign = modal(page, 'Assign a rate card');
    await field(assign, 'Rate card').selectOption({ label: `Micro creators ${runId} (USD, v1)` });
    await field(assign, 'Reason').fill('Q4 micro creator pricing');
    await assign.getByRole('button', { name: 'Assign' }).click();
    await expect(toast(page, 'Rate assigned')).toBeVisible();
    await page.getByRole('tab', { name: /^Rate cards/ }).click();
    await expect(
      page
        .getByRole('table', { name: 'Rate cards assigned to this group' })
        .getByText('Group · all campaigns'),
    ).toBeVisible();

    // The same card can't be assigned to the same group twice for overlapping periods.
    const manager = await api(state().manager);
    const cardId = recall<string>('cardId');
    const groupId = recall<string>('groupId');
    const error = await refused(
      manager.post('/admin/rate-assignments', {
        rateCardId: cardId,
        target: 'Group',
        groupId,
        reason: 'Duplicate on purpose',
      }),
    );
    expect(error.status).toBe(409);
    expect(error.code).toBe('rates.duplicate_assignment');
  });
});
