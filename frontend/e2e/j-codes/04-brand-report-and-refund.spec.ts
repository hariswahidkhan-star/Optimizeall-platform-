import { codes, csvFile, expect, ledgerOf, modal, recall, state, test, toast } from './support/codes';

/**
 * The manager imports the brand's sales report: Ivy's second order is matched, Milo's differs (flagged), an order nobody
 * claimed becomes a pending sale for the code's holder, and the first (approved) order is reported refunded, which
 * reverses its commission in the ledger. A reviewer then bulk-approves the matched sale.
 */
test.describe.serial('brand report reconciliation and refunds', () => {
  test('the brand’s sales report matches, flags, creates and refunds', async ({ as }) => {
    const c = codes();
    const { runId } = state();
    const programId = recall<string>('programId');
    const day = new Date().toISOString().slice(0, 10);
    const csv =
      'Order ID,Coupon,Total,Order Date,Status\n' +
      `GC-${runId}-2,${c.ivy},80.00,${day},completed\n` +
      `SQ-${runId}-1,${c.squad},60.00,${day},completed\n` +
      `GC-${runId}-3,${c.ivy},33.50,${day},paid\n` +
      `GC-${runId}-1,${c.ivy},120.00,${day},refunded\n` +
      `ZZ-${runId},NO-SUCH-CODE,10.00,${day},completed\n`;

    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/codes/${programId}`);
    await page.getByRole('tab', { name: /^Sales/ }).click();
    await page.getByRole('button', { name: 'Import brand sales report' }).click();
    const dialog = modal(page, /sales report/);
    await dialog.getByLabel('Sales report (CSV)').setInputFiles(csvFile('glow-report.csv', csv));
    await dialog.getByRole('button', { name: 'Check file' }).click();
    await expect(dialog.getByText(/5 rows · 1 matched · 1 differ · 1 new · 1 refunded/)).toBeVisible();
    const issues = dialog.getByRole('table', { name: 'Rows needing attention' });
    await expect(issues.getByRole('row', { name: new RegExp(`SQ-${runId}-1.*Differs`) })).toBeVisible();
    await expect(issues.getByRole('row', { name: /ZZ-.*Rejected row/ })).toBeVisible();
    await dialog.getByRole('button', { name: 'Apply 4 changes' }).click();
    await expect(toast(page, 'Sales report imported')).toBeVisible();
    await dialog.getByRole('button', { name: 'Done' }).click();

    const table = page.getByRole('table', { name: /^Sales of/ });
    await expect(table.getByRole('row', { name: new RegExp(`GC-${runId}-1.*Refunded`) })).toBeVisible();
    await expect(
      table.getByRole('row', { name: new RegExp(`GC-${runId}-2.*Matched by brand`) }),
    ).toBeVisible();
    await expect(
      table.getByRole('row', { name: new RegExp(`SQ-${runId}-1.*Differs from brand report`) }),
    ).toBeVisible();
    await expect(table.getByRole('row', { name: new RegExp(`GC-${runId}-3.*Brand report`) })).toBeVisible();
  });

  test('the refund reversed the commission; finance sees the sale refunded', async ({ as }) => {
    const { ivy, runId } = state();
    const saleId = recall<string>('saleId');
    const rows = await ledgerOf(ivy.id);
    const commission = rows.find((r) => r.codeSaleId === saleId && r.type === 'SaleCommission')!;
    expect(commission.status).toBe('Reversed');
    const reversal = rows.find((r) => r.type === 'Reversal' && r.reversesEntryId === commission.id);
    expect(reversal?.originalAmount).toBe(-12);
    expect(reversal?.rateSource).toBe('CodeProgramRules');

    const finance = await as(state().finance, /\/finance/);
    await finance.getByRole('link', { name: 'Code sales' }).first().click();
    await expect(finance.getByRole('heading', { level: 1, name: 'Code sales' })).toBeVisible();
    await finance.goto(`/finance/code-sales/${saleId}`);
    await expect(finance.getByRole('heading', { level: 1, name: `Order GC-${runId}-1` })).toBeVisible();
    await expect(finance.getByText('Refunded').first()).toBeVisible();
    await expect(finance.getByRole('table', { name: 'Ledger entries' })).toContainText('Reversal');
  });

  test('a reviewer bulk-approves the sale the brand matched', async ({ as }) => {
    const { runId } = state();
    const page = await as(state().reviewer, /\/review/);
    await page.goto('/review/code-sales');
    const queue = page.getByRole('table', { name: 'Code sales to review' });
    await queue.getByRole('checkbox', { name: `Select Order GC-${runId}-2` }).check();
    await page.getByRole('button', { name: 'Approve matched (1)' }).click();
    await expect(toast(page, '1 approved')).toBeVisible();
    await expect(queue.getByRole('link', { name: `GC-${runId}-2` })).toHaveCount(0);
  });
});
