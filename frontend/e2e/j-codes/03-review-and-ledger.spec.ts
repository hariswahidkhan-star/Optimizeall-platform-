import { api, expect, ledgerOf, modal, recall, refused, state, test, toast } from './support/codes';

/**
 * A reviewer approves Ivy's sale from the code-sales queue; the commission lands in the ledger as a SaleCommission with
 * its payout source (program, version and rate); Ivy sees it approved. Deciding it again is refused.
 */
test.describe.serial('review and ledger', () => {
  test('a reviewer approves the sale from the queue', async ({ as }) => {
    const { runId } = state();
    const saleId = recall<string>('saleId');
    const page = await as(state().reviewer, /\/review/);
    await page.getByRole('link', { name: 'Code sales' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'Code sales' })).toBeVisible();
    const queue = page.getByRole('table', { name: 'Code sales to review' });
    await queue.getByRole('link', { name: `GC-${runId}-1` }).click();
    await expect(page.getByRole('heading', { level: 1, name: `Order GC-${runId}-1` })).toBeVisible();
    await expect(page.getByText(/Estimate \(before caps\)/)).toBeVisible();
    await page.getByRole('button', { name: 'Approve' }).click();
    await modal(page, 'Approve this sale?').getByRole('button', { name: 'Approve sale' }).click();
    await expect(toast(page, 'Sale approved')).toBeVisible();
    await expect(page.getByText('Approved').first()).toBeVisible();
    await expect(page.getByRole('table', { name: 'Ledger entries' })).toContainText('Sale commission');
    await expect(page.getByText(/Program rate · 10% of net/)).toBeVisible();

    // Deciding again (a second tab) is refused.
    const reviewer = await api(state().reviewer);
    const sale = await reviewer.get<{ concurrencyStamp: string }>(`/admin/code-sales/${saleId}`);
    const error = await refused(
      reviewer.post(`/admin/code-sales/${saleId}/decision`, {
        decision: 'Approve',
        concurrencyStamp: sale.concurrencyStamp,
      }),
    );
    expect(error.status).toBe(409);
    expect(error.code).toBe('code_sale.already_decided');
  });

  test('finance sees the commission with its payout source; Ivy sees it approved', async ({ as }) => {
    const { ivy, runId } = state();
    const saleId = recall<string>('saleId');
    const rows = await ledgerOf(ivy.id);
    const commission = rows.find((r) => r.codeSaleId === saleId && r.type === 'SaleCommission');
    expect(commission).toBeTruthy();
    expect(commission!.originalAmount).toBe(12);
    expect(commission!.rateSource).toBe('CodeProgramRules');
    expect(commission!.rateSourceLabel).toContain(`Summer ${runId} v1`);

    const finance = await as(state().finance, /\/finance/);
    await finance.goto(`/finance/ledger/users/${ivy.id}`);
    await expect(finance.getByText(/Sale commission — Glow/).first()).toBeVisible();

    const page = await as(ivy, /\/app/);
    await page.goto(`/app/codes/sales/${saleId}`);
    await expect(page.getByText('You earned').first()).toBeVisible();
    await expect(page.getByText(/12\.00/).first()).toBeVisible();
    await page.goto('/app/earnings');
    await expect(page.getByText('Code sale commission').first()).toBeVisible();
  });
});
