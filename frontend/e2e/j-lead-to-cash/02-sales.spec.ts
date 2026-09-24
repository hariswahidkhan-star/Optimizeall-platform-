import type { Page } from '@playwright/test';
import {
  accounts,
  apiAs,
  expect,
  isoDate,
  landing,
  modal,
  prospect,
  recall,
  remember,
  runId,
  statusOf,
  test,
  toast,
  watchErrors,
} from './support/journey';

/**
 * Lead to cash, part 2 — the agency works the lead:
 *   the admin (site.manage) sees the inquiry in the notification inbox, assigns it to the sales rep and sets it in
 *   progress (a stale second tab is told someone else changed it) → the sales rep finds the CRM contact the website
 *   created (company, deal, lead score with the inquiry and the booked meeting), moves the deal through the pipeline and
 *   adds a follow-up task → a second lead is lost with a configured reason and archived (read-only, no proposals) → the
 *   sales rep builds a proposal from a template, with a service-catalog line and tax, and sends it.
 */
test.describe.configure({ mode: 'serial' });

/** Opens the top-bar notification inbox and returns the drawer. */
async function openInbox(page: Page) {
  await page.getByRole('button', { name: /^Notifications( \(\d+ unread\))?$/ }).click();
  return page.getByRole('dialog', { name: 'Notifications' });
}

test('the inquiry reaches staff: notification, assignment and status', async ({ as }) => {
  const lead = prospect();
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);

  // The website inquiry notified site managers in-app, with a link to the inquiry.
  const inbox = await openInbox(admin);
  const note = inbox.getByRole('link', { name: `New contact message from ${lead.name} (${lead.company})` });
  await expect(note).toBeVisible();
  await note.click();
  await expect(admin).toHaveURL(/\/agency\/website\/inquiries\/[0-9a-f-]{36}$/);
  await expect(admin.getByRole('heading', { level: 1, name: `${lead.name} — ${lead.company}` })).toBeVisible();
  await expect(admin.getByText('We need SEO and paid social for our spring launch')).toBeVisible();
  // Attribution from the landing visit (?utm_…) was carried to the form.
  await expect(admin.getByText('l2c-' + runId())).toBeVisible();
  const inquiryUrl = admin.url();

  // A second tab on the same inquiry, to provoke a concurrent edit.
  const stale = await admin.context().newPage();
  const staleErrors = watchErrors(stale);
  await stale.goto(inquiryUrl);
  await expect(stale.getByRole('heading', { level: 1, name: `${lead.name} — ${lead.company}` })).toBeVisible();

  await admin.getByLabel('Status').selectOption('InProgress');
  await admin.getByLabel('Assigned to').selectOption({ label: accounts.sales.displayName });
  await admin.getByLabel('Internal notes').fill('Hot lead — spring launch, booked a call.');
  await admin.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(toast(admin, 'Inquiry updated')).toBeVisible();

  staleErrors.ignore(/HTTP 409 PUT .*\/agency\/website\/inquiries\//);
  await stale.getByLabel('Status').selectOption('Closed');
  await stale.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(toast(stale, 'Someone else updated this inquiry. Reload the page to see their changes.')).toBeVisible();
  await stale.reload();
  await expect(stale.getByLabel('Status')).toHaveValue('InProgress');
  await expect(stale.getByLabel('Assigned to')).toHaveValue(/[0-9a-f-]{36}/);

  // The inbox filters by assignee.
  await admin.goto('/agency/website/inquiries');
  const filtered = admin.waitForResponse((r) => new URL(r.url()).pathname.endsWith('/agency/website/inquiries') && new URL(r.url()).searchParams.has('assignedTo') && r.ok());
  await admin.getByRole('combobox', { name: 'Assigned to' }).selectOption({ label: accounts.sales.displayName });
  await filtered;
  // Of this lead's four inquiries, only the assigned one is listed.
  await expect(admin.getByRole('table', { name: 'Website inquiries' }).getByRole('link', { name: `${lead.name} — ${lead.company}` })).toHaveCount(1);

  errors.expectClean('the inquiry handling');
  staleErrors.expectClean('the stale inquiry tab');
});

test('the sales rep works the CRM lead: contact, company, score, pipeline and a task', async ({ as }) => {
  const lead = prospect();
  const sales = await as(accounts.sales, landing.agency);
  const errors = watchErrors(sales);

  // Sales reps can read the inquiry but not change it (site.manage).
  const salesApi = await apiAs(accounts.sales);
  const inquiries = await salesApi.get<{ items: { id: string; concurrencyStamp?: string }[] }>(
    `/agency/website/inquiries?search=${encodeURIComponent(lead.email)}`,
  );
  expect(inquiries.items.length).toBeGreaterThanOrEqual(4); // contact, audit, quote, consultation
  expect(await statusOf(salesApi.put(`/agency/website/inquiries/${inquiries.items[0]!.id}`, { status: 'Closed' }))).toMatchObject({ status: 403 });

  // ---------------------------------------------------------------- contact → company, lead score
  await sales.goto('/agency/crm/contacts');
  await sales.getByRole('searchbox').first().fill(lead.email);
  await sales.getByRole('main').getByRole('link', { name: new RegExp(lead.name) }).click();
  await expect(sales.getByRole('heading', { level: 1, name: lead.name })).toBeVisible();
  const scoreHeading = sales.getByRole('heading', { name: /^Lead score: \d+$/ });
  await expect(scoreHeading).toBeVisible();
  const score = Number((await scoreHeading.textContent())!.replace(/\D/g, ''));
  // Four website inquiries (capped at 3 × 20) plus the booked consultation (25).
  const breakdown = sales.getByRole('listitem');
  await expect(breakdown.filter({ hasText: 'Website inquiry' }).filter({ hasText: '+60' })).toBeVisible();
  await expect(breakdown.filter({ hasText: 'Meeting booked' }).filter({ hasText: '+25' })).toBeVisible();
  expect(score).toBeGreaterThanOrEqual(85);
  await expect(sales.getByRole('main').getByRole('link', { name: lead.company, exact: true })).toBeVisible();

  // ---------------------------------------------------------------- the deal: pipeline moves and a task
  await sales.goto('/agency/crm/deals');
  const dealLink = sales.getByRole('main').getByRole('link', { name: new RegExp(`^${lead.company} — `) }).first();
  await dealLink.click();
  await expect(sales).toHaveURL(/\/agency\/crm\/deals\/[0-9a-f-]{36}$/);
  const dealUrl = sales.url();
  await expect(sales.getByText(/ · New \(5%\)$/)).toBeVisible();
  // The inquiry was assigned to this rep; the round-robin may have given the CRM deal to someone else: take it over,
  // with a value for the pipeline.
  await sales.getByRole('button', { name: 'Edit' }).click();
  const editDeal = modal(sales, 'Edit deal');
  await editDeal.getByLabel('Owner').selectOption({ label: accounts.sales.displayName });
  await editDeal.getByLabel('Value').fill('18000');
  await editDeal.getByRole('button', { name: 'Save deal' }).click();
  await expect(toast(sales, 'Deal saved')).toBeVisible();
  await expect(sales.getByText(/^\$18,000\.00 · New \(5%\)$/)).toBeVisible();

  const stage = sales.getByLabel('Move to stage');
  await stage.selectOption({ label: 'Qualified (25%)' });
  await expect(toast(sales, 'Stage updated')).toBeVisible();
  await expect(sales.getByText(/ · Qualified \(25%\)$/)).toBeVisible();
  await stage.selectOption({ label: 'Discovery call (40%)' });
  await expect(sales.getByText(/ · Discovery call \(40%\)$/)).toBeVisible();

  const log = sales.getByRole('form', { name: 'Log activity' });
  await log.getByLabel('Type').selectOption({ label: 'Task' });
  await log.getByLabel('Subject').fill(`Send proposal to ${lead.name}`);
  await log.getByLabel('Due').fill(`${isoDate(1)}T10:00`);
  await log.getByRole('button', { name: 'Add task' }).click();
  await expect(toast(sales, 'Task added')).toBeVisible();
  await expect(sales.getByRole('list', { name: 'Activity timeline' }).getByText(`Send proposal to ${lead.name}`, { exact: true })).toBeVisible();
  // The inquiry itself is on the deal's timeline.
  await expect(sales.getByRole('list', { name: 'Activity timeline' }).getByText(/received$/).first()).toBeVisible();

  await sales.goto('/agency/crm/tasks');
  await expect(sales.getByText(`Send proposal to ${lead.name}`, { exact: true })).toBeVisible();

  errors.expectClean('the CRM work');
  remember({ dealUrl });
});

test('a second lead is lost with a configured reason, archived and read-only', async ({ as }) => {
  const id = runId();
  const sales = await as(accounts.sales, landing.agency);
  const errors = watchErrors(sales);

  await sales.goto('/agency/crm/deals');
  // The public-form checks in part 1 created this lead ("Spam Check <run>").
  await sales.getByRole('main').getByRole('link', { name: new RegExp(`^spamcheck-${id}\\.test — `) }).first().click();
  await expect(sales).toHaveURL(/\/agency\/crm\/deals\/[0-9a-f-]{36}$/);
  const dealId = sales.url().split('/').pop()!;

  await sales.getByLabel('Move to stage').selectOption({ label: 'Lost (0%)' });
  const lost = modal(sales, /^Mark “.*” as lost$/);
  await lost.getByRole('button', { name: 'Mark as lost' }).click();
  await expect(lost.getByText('Say why the deal was lost.')).toBeVisible(); // a reason is required
  await lost.getByLabel('Reason').selectOption({ label: 'Budget' });
  await lost.getByLabel('Details').fill('Only $500/month available');
  await lost.getByRole('button', { name: 'Mark as lost' }).click();
  await expect(toast(sales, 'Stage updated')).toBeVisible();
  await expect(sales.getByRole('status', { name: 'Lost' })).toContainText('Budget: Only $500/month available');

  await sales.getByRole('button', { name: 'Archive' }).click();
  await sales.getByRole('alertdialog').or(sales.getByRole('dialog')).getByRole('button', { name: 'Archive' }).click();
  await expect(sales.getByRole('link', { name: 'New proposal' })).toHaveCount(0);
  await expect(sales.getByLabel('Move to stage')).toHaveCount(0);

  // The server refuses proposals on an archived deal too.
  const salesApi = await apiAs(accounts.sales);
  const refused = await statusOf(
    salesApi.post('/agency/proposals', {
      title: `Archived ${id}`,
      dealId,
      validUntil: isoDate(14),
      lines: [{ description: 'SEO', quantity: 1, unitPrice: 100, recurrence: 'Monthly' }],
    }),
  );
  expect(refused.status).toBe(409);
  errors.expectClean('the lost deal');
});

test('the sales rep builds a proposal from a template, the service catalog and tax, and sends it', async ({ as }) => {
  const lead = prospect();
  const dealUrl = recall('dealUrl');
  const sales = await as(accounts.sales, landing.agency);
  const errors = watchErrors(sales);

  await sales.goto(dealUrl);
  await sales.getByRole('link', { name: 'New proposal' }).click();
  await expect(sales.getByRole('heading', { level: 1, name: 'New proposal' })).toBeVisible();
  await expect(sales.getByLabel('Recipient email')).toHaveValue(lead.email);

  // Template: sections and lines; then keep only recurring work and add a catalog line with tax.
  await sales.getByLabel('Start from a template').selectOption({ label: 'SEO retainer' });
  await expect(toast(sales, 'Started from “SEO retainer”')).toBeVisible();
  await expect(sales.getByLabel('Executive summary')).not.toHaveValue('');
  await sales.getByLabel('Title', { exact: true }).fill(`${lead.company} growth retainer`);
  const lines = sales.getByRole('list', { name: 'Line items' }).getByRole('listitem');
  // Remove the template's one-time lines: this retainer bills monthly from the contract.
  for (let i = (await lines.count()) - 1; i >= 0; i--) {
    const billing = lines.nth(i).getByLabel('Billing');
    if ((await billing.inputValue()) === 'OneTime') await lines.nth(i).getByRole('button', { name: /^Remove line/ }).click();
  }
  await sales.getByRole('button', { name: 'Add from catalog' }).click();
  await sales.getByRole('menuitem', { name: /^Social media management/ }).click();
  const added = lines.last();
  await expect(added.getByLabel('Billing')).toHaveValue('Monthly');
  await added.getByLabel('Tax rate').selectOption({ label: 'Sindh sales tax on services 13% — review' });
  // No first invoice on acceptance: the retainer contract's recurring invoice job bills the first month.
  await sales.getByRole('checkbox', { name: 'Create the first invoice on acceptance' }).uncheck();
  const totals = sales.getByRole('definition').filter({ hasText: /\$/ });
  await expect(sales.getByText('Sindh sales tax on services 13%', { exact: true })).toBeVisible(); // tax line in the totals
  await expect(totals.first()).toBeVisible();

  // Boundary: a validity date in the past is refused by the server.
  await sales.getByLabel('Valid until').fill(isoDate(-1));
  errors.ignore(/HTTP 400 POST .*\/agency\/proposals$/);
  await sales.getByRole('button', { name: 'Create proposal' }).click();
  await expect(sales.getByRole('alert').filter({ hasText: 'Choose today or a later date.' })).toBeVisible();
  await sales.getByLabel('Valid until').fill(isoDate(21));

  // A double click creates one proposal.
  await sales.getByRole('button', { name: 'Create proposal' }).dblclick();
  await expect(sales).toHaveURL(/\/agency\/proposals\/[0-9a-f-]{36}$/);
  const proposalUrl = sales.url();
  const salesApi = await apiAs(accounts.sales);
  const created = await salesApi.get<{ items: unknown[] }>(`/agency/proposals?search=${encodeURIComponent(`${lead.company} growth retainer`)}`);
  expect(created.items).toHaveLength(1);

  await sales.getByRole('button', { name: 'Send', exact: true }).click();
  const send = modal(sales, /^Send .* v1$/);
  await expect(send.getByRole('checkbox', { name: `Email it to ${lead.email}` })).toBeChecked();
  await send.getByRole('button', { name: 'Send proposal' }).click();
  await expect(toast(sales, `Emailed to ${lead.email}`)).toBeVisible();
  const proposalLink = await send.getByLabel('Proposal link').inputValue();
  expect(proposalLink).toMatch(/\/p\/[\w-]+$/);
  await send.getByRole('button', { name: /^(Close|Cancel)$/ }).first().click();

  // Finance can't build proposals (proposals.manage); the account manager can.
  const financeApi = await apiAs(accounts.finance);
  expect(await statusOf(financeApi.get('/agency/proposals'))).toMatchObject({ status: 403 });

  errors.expectClean('the proposal builder');
  remember({ proposalUrl, proposalLink });
});
