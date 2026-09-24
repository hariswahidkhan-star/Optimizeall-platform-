import { type Page, expect, test } from '@playwright/test';
import { signIn } from '../journeys/support/ui';
import { as, payUser } from './support/staff';
import { remember, state } from './support/state';

/**
 * Participant lifecycle, part 5 — getting paid: payout details (client and server validation, an IBAN with a typo, a
 * valid IBAN that is only ever shown masked — not even the API returns it — then a change to PayPal, which needs the
 * destination typed again) → finance prepares, finalizes and records the payment (API, four-eyes) → the participant
 * sees the payout as paid and the balance moves from approved to paid; recording the payment twice is refused.
 */
test.describe.serial('payout details and payout', () => {
  let page: Page;
  const s = () => state();
  const IBAN = 'GB82 WEST 1234 5698 7654 32';
  const paypal = () => `pat.${s().runId}@example.com`;

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
    await signIn(page, s().pat, /\/app$/);
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  const current = () => page.getByRole('region', { name: 'Current payout destination' });

  test('payout details are validated on the client and by the API', async () => {
    await page.goto('/app/profile/payout-details');
    await expect(current().getByText('No payout details yet')).toBeVisible();

    await page.getByRole('button', { name: 'Save payout details' }).click();
    await expect(page.getByText('Choose how you want to be paid.')).toBeVisible();

    await page.getByRole('radio', { name: 'Bank transfer' }).check();
    await page.getByLabel('Account holder name').fill('P');
    await page.getByLabel('IBAN or account number').fill('12-34');
    await page.getByRole('button', { name: 'Save payout details' }).click();
    await expect(page.getByLabel('Account holder name')).toHaveAccessibleDescription(/full name/);
    await expect(page.getByLabel('IBAN or account number')).toHaveAccessibleDescription(
      /8–34 letters and digits/,
    );

    // Passes the client's shape check, but the IBAN checksum is wrong: the API says so on the field.
    await page.getByLabel('Account holder name').fill(s().pat.displayName);
    await page.getByLabel('IBAN or account number').fill('GB82 WEST 1234 5698 7654 33');
    await page.getByRole('button', { name: 'Save payout details' }).click();
    await expect(page.getByLabel('IBAN or account number')).toHaveAccessibleDescription(/IBAN is not valid/);
    await expect(current().getByText('No payout details yet')).toBeVisible();
  });

  test('a valid IBAN is saved encrypted and only shown masked', async () => {
    await page.getByLabel('IBAN or account number').fill(IBAN);
    await page.getByLabel('Preferred currency').selectOption('USD');
    await page.getByLabel('Country of the account').selectOption('GB');
    await page.getByRole('button', { name: 'Save payout details' }).click();

    await expect(current()).toContainText('Bank transfer');
    await expect(current()).toContainText('5432');
    await expect(current()).not.toContainText('WEST');
    // The field is cleared after saving: the saved value is never shown again.
    await expect(page.getByLabel('IBAN or account number')).toHaveValue('');
    await page.reload();
    await expect(current()).toContainText('5432');
    await expect(page.locator('body')).not.toContainText('WEST12345698765432');

    const api = await as(s().pat);
    const profile = await api.get<Record<string, unknown>>('/me/payout-profile');
    expect(JSON.stringify(profile)).not.toContain('WEST');
    expect(profile.destinationHint).toMatch(/5432$/);
  });

  test('switching to PayPal needs the destination again; the email is masked', async () => {
    await page.getByRole('radio', { name: 'PayPal' }).check();
    await page.getByRole('button', { name: 'Save payout details' }).click();
    await expect(page.getByLabel('PayPal email address')).toHaveAccessibleDescription(
      /Enter where we should send/,
    );

    await page.getByLabel('PayPal email address').fill(paypal());
    await page.getByRole('button', { name: 'Save payout details' }).click();
    await expect(current()).toContainText('PayPal');
    await expect(current()).toContainText('@example.com');
    await expect(current()).not.toContainText(paypal());
  });

  test('finance pays; the participant sees the payout as paid and the balance as paid', async () => {
    const api = await as(s().pat);
    const me = await api.get<{ id: string }>('/me/profile');
    const reference = `PAYPAL-${s().runId}-PAT`;
    const paid = await payUser(me.id, reference);
    expect(paid.item.amount).toBe(16);
    remember('batchReference', paid.reference);

    // A retried "record payment" is refused, not recorded twice.
    const finance = await as(s().finance2);
    await expect(
      finance.post(`/finance/payout-batches/${paid.batchId}/items/${paid.item.itemId}/record-payment`, {
        paymentReference: `${reference}-AGAIN`,
        paidAt: new Date().toISOString(),
      }),
    ).rejects.toMatchObject({ status: 409 });

    await page.goto('/app/payouts');
    const row = page.getByRole('row').filter({ hasText: paid.reference });
    await expect(row).toContainText('Paid');
    await expect(row).toContainText('$16.00');

    await page.goto('/app/earnings');
    await expect(page.getByRole('group', { name: /^Approved\b/ })).toContainText('$0.00');
    await expect(page.getByRole('group', { name: /^Paid\b/ })).toContainText('$16.00');
  });
});
