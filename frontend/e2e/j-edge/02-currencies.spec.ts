import { expect, test } from '@playwright/test';
import { adminAt, runId, staff, watchErrors } from './support/edge';

/**
 * Invoices in currencies with 0, 2 and 3 minor units render exactly what the API computed. PKR is the trap: the API
 * (ISO 4217) keeps two decimals, while the browser's CLDR data formats PKR with none, which used to show PKR 1,235 for
 * an invoice of PKR 1,234.56.
 */
test.use({ locale: 'en-US' });

const cases = [
  { currency: 'PKR', country: 'PK', timeZone: 'Asia/Karachi', unitPrice: 1234.56, total: /PKR\s1,234\.56/ },
  { currency: 'KWD', country: 'KW', timeZone: 'Asia/Kuwait', unitPrice: 12.345, total: /KWD\s12\.345/ },
  { currency: 'JPY', country: 'JP', timeZone: 'Asia/Tokyo', unitPrice: 1234.56, total: /¥1,235(?![.\d])/ },
] as const;

for (const c of cases) {
  test(`a ${c.currency} invoice shows the API's amounts with ${c.currency}'s minor units`, async ({
    page,
  }) => {
    const errors = watchErrors(page);
    const am = await staff('am');
    const finance = await staff('finance');
    const client = await am.post<{ id: string }>('/agency/clients', {
      name: `Edge ${c.currency} ${runId()}`,
      countryCode: c.country,
      timeZone: c.timeZone,
      currency: c.currency,
      industry: 'Testing',
      status: 'Active',
    });
    const invoice = await finance.post<{ id: string; total: number; currency: string }>(
      '/agency/billing/invoices',
      {
        clientAccountId: client.id,
        lines: [
          {
            description: 'Edge retainer',
            quantity: 1,
            unitPrice: c.unitPrice,
            recurrence: 'OneTime',
            discountType: 'None',
            discountValue: 0,
          },
        ],
      },
    );
    expect(invoice.currency).toBe(c.currency);

    await adminAt(page, `/agency/billing/invoices/${invoice.id}`);
    const line = page.getByRole('row').filter({ hasText: 'Edge retainer' });
    await expect(line).toContainText(c.total);
    await expect(page.getByText(c.total).first()).toBeVisible();
    errors.expectClean(`${c.currency} invoice`);
  });
}
