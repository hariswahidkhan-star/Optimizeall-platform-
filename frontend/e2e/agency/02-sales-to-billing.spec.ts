import { expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  clients,
  landing,
  modal,
  pathOf,
  runId,
  toast,
  watchErrors,
} from './support/agency';

/**
 * Sales → billing, as the account manager and finance:
 *   sign in → agency home → CRM: create a contact and a deal, move the deal to "Qualified" → proposals: create from
 *   the deal and send → the client opens the public /p/:token link (anonymous context) and accepts → billing (finance):
 *   create a draft invoice and issue it → the client opens the public /i/:token link.
 */
test.describe.configure({ mode: 'serial' });

test('account manager takes a lead from contact to accepted proposal; finance issues the invoice', async ({
  browser,
}) => {
  const id = runId();
  const contactFirst = `Quinn`;
  const contactLast = `Prospect ${id}`;
  const dealTitle = `E2E deal ${id} — SEO retainer`;
  const recipientEmail = `quinn.${id}@e2e.optimizeall.test`;

  // ---------------------------------------------------------------- sign in → agency portal home
  const am = await actor(browser, accounts.am, landing.agency);
  const amErrors = watchErrors(am);
  await expect(
    am.getByRole('heading', { level: 1, name: /^Good (morning|afternoon|evening), / }),
  ).toBeVisible();
  await expect(am.getByRole('navigation', { name: 'Agency navigation' })).toBeVisible();

  // ---------------------------------------------------------------- CRM: contact
  await am
    .getByRole('navigation', { name: 'Agency navigation' })
    .getByRole('link', { name: 'Sales CRM' })
    .click();
  await expect(am.getByRole('heading', { level: 1, name: 'Sales CRM' })).toBeVisible();
  await am.getByRole('main').getByRole('link', { name: 'Contacts' }).click();
  await am.getByRole('button', { name: 'New contact' }).click();
  const contactDialog = modal(am, 'New contact');
  await contactDialog.getByLabel('First name').fill(contactFirst);
  await contactDialog.getByLabel('Last name').fill(contactLast);
  await contactDialog.getByLabel('Email').fill(recipientEmail);
  await contactDialog.getByLabel('Job title').fill('Head of Growth');
  await contactDialog.getByRole('button', { name: 'Create contact' }).click();
  await expect(toast(am, 'Contact created')).toBeVisible();
  await expect(contactDialog).toBeHidden();
  await expect(
    am.getByRole('main').getByRole('link', { name: new RegExp(`${contactFirst} ${contactLast}`) }),
  ).toBeVisible();

  // ---------------------------------------------------------------- CRM: deal, then move its stage
  await am.goto('/agency/crm/deals');
  await am.getByRole('button', { name: 'New deal' }).click();
  const dealDialog = modal(am, 'New deal');
  await dealDialog.getByLabel('Title').fill(dealTitle);
  await dealDialog.getByLabel('Value').fill('12000');
  await dealDialog.getByLabel('Currency').selectOption('USD');
  await dealDialog.getByRole('button', { name: 'Create deal' }).click();
  await expect(am).toHaveURL(/\/agency\/crm\/deals\/[0-9a-f-]{36}$/);
  await expect(am.getByRole('heading', { level: 1, name: dealTitle })).toBeVisible();
  await expect(am.getByText('$12,000.00 · New (5%)')).toBeVisible();

  const stage = am.getByLabel('Move to stage');
  await stage.selectOption({ label: 'Qualified (25%)' });
  await expect(toast(am, 'Stage updated')).toBeVisible();
  await expect(am.getByText('$12,000.00 · Qualified (25%)')).toBeVisible();
  const dealUrl = am.url();

  // The board shows the deal in its new column.
  await am.goto('/agency/crm/deals');
  await expect(
    am.getByRole('region', { name: /^Qualified \(\d+\)$/ }).getByRole('link', { name: dealTitle }),
  ).toBeVisible();

  // ---------------------------------------------------------------- proposal from the deal
  await am.goto(dealUrl);
  await am.getByRole('link', { name: 'New proposal' }).click();
  await expect(am.getByRole('heading', { level: 1, name: 'New proposal' })).toBeVisible();
  await expect(am.getByLabel('Title', { exact: true })).toHaveValue(`${dealTitle} proposal`);
  await am.getByLabel('Existing client').selectOption({ label: clients.nimbus.name });
  await am.getByLabel('Recipient name').fill(`${contactFirst} ${contactLast}`);
  await am.getByLabel('Recipient email').fill(recipientEmail);
  await am.getByLabel('Line 1 description').fill('SEO retainer — technical audit and content');
  await am.getByLabel('Quantity').fill('1');
  await am.getByLabel('Unit price (USD)').fill('3000');
  await am.getByLabel('Billing').selectOption({ label: 'One-time' });
  await expect(am.getByRole('definition').filter({ hasText: '$3,000.00' }).first()).toBeVisible();
  await am.getByRole('button', { name: 'Create proposal' }).click();
  await expect(am).toHaveURL(/\/agency\/proposals\/[0-9a-f-]{36}$/);
  const proposalHeading = am.getByRole('heading', { level: 1, name: new RegExp(`· ${dealTitle} proposal$`) });
  await expect(proposalHeading).toBeVisible();

  await am.getByRole('button', { name: 'Send', exact: true }).click();
  const sendDialog = modal(am, /^Send .* v1$/);
  await expect(sendDialog.getByRole('checkbox', { name: `Email it to ${recipientEmail}` })).toBeChecked();
  await sendDialog.getByLabel('Message').fill('Here is our proposal — happy to walk you through it.');
  await sendDialog.getByRole('button', { name: 'Send proposal' }).click();
  await expect(toast(am, `Emailed to ${recipientEmail}`)).toBeVisible();
  const proposalLink = await sendDialog.getByLabel('Proposal link').inputValue();
  expect(proposalLink).toMatch(/\/p\/[\w-]+$/);
  await sendDialog
    .getByRole('button', { name: /^(Close|Cancel)$/ })
    .first()
    .click();
  const proposalUrl = am.url();

  // ---------------------------------------------------------------- the client accepts on the public link
  const clientContext = await browser.newContext();
  const client = await clientContext.newPage();
  const clientErrors = watchErrors(client);
  await client.goto(pathOf(proposalLink));
  await expect(client.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(client.getByText('SEO retainer — technical audit and content')).toBeVisible();
  const accept = client.getByRole('form', { name: 'Accept proposal' });
  await accept.getByLabel('Full name').fill(`${contactFirst} ${contactLast}`);
  await accept.getByLabel('Job title').fill('Head of Growth');
  await accept.getByRole('checkbox', { name: 'I agree to the terms of this proposal' }).check();
  await accept.getByRole('button', { name: 'Accept proposal' }).click();
  await expect(client.getByText('Proposal accepted')).toBeVisible();
  await expect(client.getByText(`Accepted by ${contactFirst} ${contactLast}`)).toBeVisible();
  clientErrors.expectClean('the public proposal page');
  await clientContext.close();

  // The account manager sees the acceptance.
  await am.goto(proposalUrl);
  await expect(am.getByText('Accepted', { exact: true }).first()).toBeVisible();
  await expect(
    am.getByText(new RegExp(`${contactFirst} ${contactLast} \\(Head of Growth\\) · v1`)),
  ).toBeVisible();
  amErrors.expectClean('the account manager journey');
  await am.context().close();

  // ---------------------------------------------------------------- billing: create & issue an invoice
  const finance = await actor(browser, accounts.finance, /\/(finance|agency)(\/|$)/);
  const financeErrors = watchErrors(finance);
  await finance.goto('/agency/billing/invoices');
  await finance.getByRole('link', { name: 'New invoice' }).click();
  await expect(finance.getByRole('heading', { level: 1, name: 'New invoice' })).toBeVisible();
  await finance.getByLabel('Client').selectOption({ label: `${clients.nimbus.name} (USD)` });
  await finance.getByLabel('Reference').fill(`PO-${id}`);
  await finance.getByLabel('Line 1 description').fill(`Paid social management ${id}`);
  await finance.getByLabel('Quantity').fill('2');
  await finance.getByLabel(/^Unit price/).fill('750');
  await finance.getByRole('button', { name: 'Create draft' }).click();
  await expect(toast(finance, 'Draft invoice created')).toBeVisible();
  await expect(finance).toHaveURL(/\/agency\/billing\/invoices\/[0-9a-f-]{36}$/);
  await expect(finance.getByRole('heading', { level: 1, name: 'Draft invoice' })).toBeVisible();

  await finance.getByRole('button', { name: 'Issue', exact: true }).click();
  await expect(toast(finance, 'Invoice issued')).toBeVisible();
  await expect(finance.getByRole('heading', { level: 1, name: /^Invoice \S+$/ })).toBeVisible();
  const invoiceNumber = (await finance.getByRole('heading', { level: 1 }).textContent())!
    .replace('Invoice ', '')
    .trim();
  await expect(finance.getByRole('button', { name: 'Record payment' })).toBeVisible();
  const invoiceLink = await finance.getByLabel('Client view link').inputValue();
  expect(invoiceLink).toMatch(/\/i\/[\w-]+$/);
  financeErrors.expectClean('the finance journey');
  await finance.context().close();

  // ---------------------------------------------------------------- the client opens the public invoice
  const viewerContext = await browser.newContext();
  const viewer = await viewerContext.newPage();
  const viewerErrors = watchErrors(viewer);
  await viewer.goto(pathOf(invoiceLink));
  await expect(viewer.getByText(invoiceNumber).first()).toBeVisible();
  await expect(viewer.getByText(`Paid social management ${id}`)).toBeVisible();
  await expect(viewer.getByText('$1,500.00').first()).toBeVisible();
  viewerErrors.expectClean('the public invoice page');
  await viewerContext.close();
});
