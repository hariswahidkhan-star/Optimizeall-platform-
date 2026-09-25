import {
  api,
  codes,
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
 * Participants see their own codes (never someone else's) with the share link, terms and their rate, and report a sale
 * with an order number, date and value. The same order from another group member is refused (the first report counts).
 */
test.describe.serial('participant codes and sales', () => {
  test('Ivy sees her code, copies the share link and reports a sale', async ({ as }) => {
    const c = codes();
    const { ivy, runId } = state();
    const page = await as(ivy, /\/app/);
    await page.getByRole('link', { name: 'My codes' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'My discount codes' })).toBeVisible();
    await expect(page.getByTestId('my-code')).toHaveText(c.ivy);
    await expect(page.getByRole('heading', { name: `Glow ${runId}` })).toBeVisible();
    await expect(page.getByText('10% of net')).toBeVisible();
    await expect(page.getByText('Personal code')).toBeVisible();
    await expect(page.getByLabel(/Share link/)).toHaveValue(new RegExp(`code=${c.ivy}`));
    // Only her own code: the shared squad code and the spare code are not listed.
    await expect(page.getByText(c.squad)).toHaveCount(0);
    await expect(page.getByText(c.spare)).toHaveCount(0);

    await page.getByRole('button', { name: 'Report a sale' }).first().click();
    const dialog = modal(page, 'Report a sale');
    await field(dialog, 'Order number').fill(`GC-${runId}-1`);
    await field(dialog, 'Order value').fill('120');
    await field(dialog, 'Discount given').fill('21.18');
    await dialog.getByRole('button', { name: 'Report sale' }).click();
    await expect(toast(page, 'Sale reported')).toBeVisible();
    await expect(page.getByRole('heading', { level: 1, name: `Order GC-${runId}-1` })).toBeVisible();
    await expect(page.getByText('Pending review').first()).toBeVisible();
    await expect(page.getByText('Estimated commission')).toBeVisible();
    remember('saleId', page.url().split('/').pop());

    // A second sale that the brand will later report (arranged through the API).
    const session = await api(ivy);
    const mine = await session.get<{ codeId: string; code: string }[]>('/me/codes');
    const form = new FormData();
    form.append('codeId', mine.find((x) => x.code === c.ivy)!.codeId);
    form.append('orderReference', `GC-${runId}-2`);
    form.append('orderDate', new Date(Date.now() - 3600_000).toISOString());
    form.append('netAmount', '80');
    form.append('discountAmount', '14.12');
    form.append('currency', 'USD');
    const second = await session.upload<{ id: string }>('/me/code-sales', form);
    remember('secondSaleId', second.id);
    remember('ivyCodeId', mine.find((x) => x.code === c.ivy)!.codeId);
  });

  test('Milo sees the shared squad code; the same order can’t be claimed twice', async ({ as }) => {
    const c = codes();
    const { milo, runId } = state();
    const page = await as(milo, /\/app/);
    await page.goto('/app/codes');
    await expect(page.getByTestId('my-code')).toHaveText(c.squad);
    await expect(page.getByText('Shared with your group')).toBeVisible();
    await expect(page.getByText(c.ivy)).toHaveCount(0);

    await page.getByRole('button', { name: 'Report a sale' }).first().click();
    const dialog = modal(page, 'Report a sale');
    await field(dialog, 'Order number').fill(`SQ-${runId}-1`);
    await field(dialog, 'Order value').fill('45');
    await dialog.getByRole('button', { name: 'Report sale' }).click();
    await expect(page.getByRole('heading', { level: 1, name: `Order SQ-${runId}-1` })).toBeVisible();

    // Reporting it again is refused with a clear message, and someone else's code is invisible (404).
    await page.goto('/app/codes');
    await page.getByRole('button', { name: 'Report a sale' }).first().click();
    const again = modal(page, 'Report a sale');
    await field(again, 'Order number').fill(`sq-${runId}-1`);
    await field(again, 'Order value').fill('45');
    await again.getByRole('button', { name: 'Report sale' }).click();
    await expect(again.getByText('You already reported this order.')).toBeVisible();
    await again.getByRole('button', { name: 'Cancel' }).click();

    const session = await api(milo);
    const form = new FormData();
    form.append('codeId', recall<string>('ivyCodeId'));
    form.append('orderReference', `X-${runId}`);
    form.append('orderDate', new Date(Date.now() - 3600_000).toISOString());
    form.append('netAmount', '10');
    form.append('currency', 'USD');
    const error = await refused(session.upload('/me/code-sales', form));
    expect(error.status).toBe(404);
    const other = await refused(session.get(`/me/code-sales/${recall<string>('saleId')}`));
    expect(other.status).toBe(404);
  });
});
