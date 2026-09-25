import {
  api,
  codes,
  csvFile,
  expect,
  field,
  modal,
  recall,
  refused,
  remember,
  state,
  test,
  toast,
} from './support/codes';

/**
 * A campaign manager creates a brand's discount-code program, imports the codes the brand sent (checked first, with a
 * line-numbered report; duplicates refused), assigns one personal code and one shared code for a rate group.
 */
test.describe.serial('program, codes and assignment', () => {
  test('a manager creates a program for a brand with its payout rules', async ({ as }) => {
    const { runId } = state();
    const page = await as(state().manager, /\/manage$/);
    await page.getByRole('link', { name: 'Discount codes' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'Discount codes' })).toBeVisible();
    await page.getByRole('button', { name: 'New program' }).click();

    const dialog = modal(page, 'New discount-code program');
    await field(dialog, 'Brand / company').fill(`Glow ${runId}`);
    await field(dialog, 'Program name').fill(`Summer ${runId}`);
    await field(dialog, 'Store / landing page').fill('https://glow.example.com/summer');
    await field(dialog, 'What customers get').fill('15% off');
    await field(dialog, 'Percent').fill('10');
    await dialog.getByRole('button', { name: 'Add tier' }).click();
    await field(dialog, 'After (approved sales)').fill('10');
    await field(dialog, 'One-off bonus (USD)').fill('25');
    await dialog.getByRole('button', { name: 'Create program' }).click();

    await expect(page.getByRole('heading', { level: 1, name: `Summer ${runId}` })).toBeVisible();
    await expect(toast(page, 'Program created')).toBeVisible();
    await expect(page.getByText('10% of the order value (net)')).toBeVisible();
    await expect(page.getByText(/bonus 25 USD/)).toBeVisible();
    remember('programId', page.url().split('/').pop());
  });

  test('the brand’s codes are imported from CSV after a check, duplicates refused', async ({ as }) => {
    const c = codes();
    const programId = recall<string>('programId');
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/codes/${programId}`);
    await page.getByRole('tab', { name: /^Codes/ }).click();
    await page.getByRole('button', { name: 'Import CSV' }).click();
    const dialog = modal(page, 'Import codes from the brand');
    const csv = `Coupon Code,Expires,Note\n${c.ivy},,for Ivy\n${c.spare},,\n${c.squad},,shared\n${c.ivy.toLowerCase()},,duplicate\nbad code!,,\n`;
    await dialog.getByLabel('CSV file').setInputFiles(csvFile('glow-codes.csv', csv));
    await dialog.getByRole('button', { name: 'Check file' }).click();
    await expect(dialog.getByText(/5 rows · 3 can be imported/)).toBeVisible();
    await expect(dialog.getByText(/Line 5:/)).toBeVisible(); // the in-file duplicate
    await expect(dialog.getByText(/Line 6:/)).toBeVisible(); // the invalid code
    await dialog.getByRole('button', { name: 'Import 3 codes' }).click();
    await expect(dialog.getByText(/3 imported/)).toBeVisible();
    await dialog.getByRole('button', { name: 'Done' }).click();

    const table = page.getByRole('table', { name: 'Codes' });
    for (const code of [c.ivy, c.spare, c.squad])
      await expect(table.getByRole('button', { name: code, exact: true })).toBeVisible();

    // The same code again is refused.
    const manager = await api(state().manager);
    const error = await refused(
      manager.post(`/admin/code-programs/${programId}/codes`, { code: c.ivy.toLowerCase() }),
    );
    expect(error.status).toBe(409);
    expect(error.code).toBe('code.duplicate');
  });

  test('a personal code goes to Ivy and a shared code to a rate group with Milo in it', async ({ as }) => {
    const c = codes();
    const { runId, ivy, milo } = state();
    const programId = recall<string>('programId');
    const manager = await api(state().manager);
    // The group reuses person-level rate groups (arranged through the API; the rates journey covers their UI).
    const group = await manager.post<{ id: string }>('/admin/rate-groups', {
      name: `Glow squad ${runId}`,
      priority: 10,
      membershipMode: 'Manual',
    });
    await manager.post(`/admin/rate-groups/${group.id}/members`, { userIds: [milo.id], note: 'Glow squad' });
    remember('groupId', group.id);

    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/codes/${programId}`);
    await page.getByRole('tab', { name: /^Codes/ }).click();
    const table = page.getByRole('table', { name: 'Codes' });

    await table.getByRole('button', { name: `Actions for Code ${c.ivy}` }).click();
    await page.getByRole('menuitem', { name: 'Assign…' }).click();
    const assign = modal(page, `Assign ${c.ivy}`);
    await assign.getByLabel('Search participants by name or email').fill(`Ivy Seller ${runId}`);
    await assign.getByRole('checkbox', { name: ivy.displayName }).check();
    await field(assign, 'From').fill(new Date(Date.now() - 2 * 86_400_000).toISOString().slice(0, 10));
    await field(assign, 'Reason').fill('Top seller for the summer launch');
    await assign.getByRole('button', { name: 'Assign' }).click();
    await expect(toast(page, 'Code assigned')).toBeVisible();
    await expect(table.getByRole('row', { name: new RegExp(`${c.ivy}.*${ivy.displayName}`) })).toBeVisible();

    await table.getByRole('button', { name: `Actions for Code ${c.squad}` }).click();
    await page.getByRole('menuitem', { name: 'Assign…' }).click();
    const shared = modal(page, `Assign ${c.squad}`);
    await shared.getByRole('radio', { name: 'A rate group (shared code)' }).check();
    await field(shared, 'Rate group').selectOption({ label: `Glow squad ${runId} (1)` });
    await field(shared, 'From').fill(new Date(Date.now() - 2 * 86_400_000).toISOString().slice(0, 10));
    await field(shared, 'Reason').fill('Shared squad code');
    await shared.getByRole('button', { name: 'Assign' }).click();
    await expect(toast(page, 'Code assigned')).toBeVisible();
    await expect(
      table.getByRole('row', { name: new RegExp(`${c.squad}.*Glow squad ${runId}.*Shared`) }),
    ).toBeVisible();

    // Assigning an already assigned code without "reassign" is refused.
    const detail = await manager.get<{ items: { id: string; code: string }[] }>(
      `/admin/code-programs/${programId}/codes?search=${c.ivy}`,
    );
    const error = await refused(
      manager.post(`/admin/discount-codes/${detail.items[0]!.id}/assign`, {
        target: 'Person',
        userId: milo.id,
        reason: 'Should be refused',
      }),
    );
    expect(error.code).toBe('code.already_assigned');
  });
});
