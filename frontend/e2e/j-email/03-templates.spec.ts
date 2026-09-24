import { type Page, expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  address,
  call,
  codeOf,
  landing,
  mailsTo,
  mailsWith,
  modal,
  openEmail,
  remember,
  recall,
  state,
  toast,
  waitForMail,
  watchErrors,
} from './support/email';

/**
 * Templates in the journey's client workspace: the block editor with personalisation tokens and a live server-side
 * preview (sample data), sanitised rich text, unknown or malformed merge tags refused, test sends only to verified
 * staff, duplicate and archive.
 */
const block = (page: Page, title: RegExp) =>
  page
    .getByRole('form', { name: 'Template editor' })
    .getByRole('listitem')
    .filter({ has: page.getByRole('heading', { name: title }) });

test('template editor: personalisation tokens, live preview, sanitised content, invalid tags refused', async ({
  browser,
}) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await openEmail(page, '/templates');
  await expect(page.getByRole('heading', { level: 1, name: 'Templates' })).toBeVisible();
  // The agency's starter library is offered in every workspace.
  await expect(
    page.getByRole('table', { name: 'Email templates' }).getByText('Agency library').first(),
  ).toBeVisible();
  await page.getByRole('link', { name: 'New template' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'New template' })).toBeVisible();

  const name = `Lumen welcome ${state().runId}`;
  const editor = page.getByRole('form', { name: 'Template editor' });
  await editor.getByLabel('Template name').fill(name);
  await editor.getByLabel('Category').selectOption('welcome');
  await editor.getByLabel('Subject line').fill('Welcome {{first_name|there}} to {{org_name}}');
  await editor.getByLabel('Preview text').fill('Your {{custom.plan|free}} plan is ready');
  await block(page, /^1\. Header$/)
    .getByLabel('Title', { exact: true })
    .fill('Welcome aboard');
  await block(page, /^2\. Text$/)
    .getByLabel('Content')
    .fill(
      '<p>Hi {{first_name|there}}, your {{custom.plan|free}} plan is live.</p><script>alert("x")</script><p onclick="steal()">Enjoy.</p>',
    );
  const button = block(page, /^3\. Button$/);
  await button.getByLabel('Button text').fill('Open Lumen');
  await button.getByLabel('Link', { exact: true }).fill('https://lumen.example/start?who={{email}}');

  // The preview renders on the server with sample data (Alex).
  const preview = page.getByRole('region', { name: 'Preview' });
  await expect(preview.getByText(/Subject: Welcome Alex to Lumen Labs Ltd/)).toBeVisible();
  const frame = page.frameLocator('iframe[title="Email preview (desktop)"]');
  await expect(frame.getByText('Hi Alex, your free plan is live.')).toBeVisible();
  await expect(frame.locator('script')).toHaveCount(0);

  // Negative: an unknown merge tag is refused with the allowed list.
  await editor.getByLabel('Subject line').fill('Welcome {{favourite_colour}}');
  await page.getByRole('button', { name: 'Save template' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not save the template' })).toContainText(
    'Unknown merge tags: {{favourite_colour}}',
  );
  // Negative: a malformed tag.
  await editor.getByLabel('Subject line').fill('Welcome {{first_name');
  await page.getByRole('button', { name: 'Save template' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not save the template' })).toContainText(
    'not closed or is malformed',
  );
  // Negative: a javascript: link on the button.
  await editor.getByLabel('Subject line').fill('Welcome {{first_name|there}} to {{org_name}}');
  await button.getByLabel('Link', { exact: true }).fill('javascript:alert(1)');
  await page.getByRole('button', { name: 'Save template' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not save the template' })).toContainText(
    'href must be',
  );
  errors.ignore(/HTTP 400 (POST|PUT) .*\/agency\/email\/templates/);
  errors.ignore(/HTTP 400 POST .*\/agency\/email\/templates\/render/);

  await button.getByLabel('Link', { exact: true }).fill('https://lumen.example/start?who={{email}}');
  await page.getByRole('button', { name: 'Save template' }).click();
  await expect(toast(page, 'Template saved')).toBeVisible();
  await expect(page).toHaveURL(/\/agency\/email\/templates\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
  errors.expectClean('the template editor');

  // What is saved is sanitised: no script, no event handler.
  const id = page.url().split('/').pop()!;
  const saved = await call<{ design: { blocks: { type: string; html?: string }[] }; subject: string }>(
    accounts.am,
    'GET',
    `/agency/email/templates/${id}`,
  );
  const html = saved.body.design.blocks.find((b) => b.type === 'text')!.html!;
  expect(html).not.toContain('<script');
  expect(html).not.toContain('onclick');
  expect(html).toContain('{{custom.plan|free}}');
  remember('templateId', id);
  remember('templateName', name);
});

test('test sends: only to verified staff, with sample data and a [Test] subject', async ({ browser }) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/templates/${recall('templateId')}`);
  await expect(page.getByRole('heading', { level: 1, name: recall('templateName') })).toBeVisible();
  const before = mailsTo(accounts.am.email).length;

  // Negative: an address that is not a verified staff account is refused, and nothing is sent to it.
  await page.getByRole('button', { name: 'Send test' }).click();
  const dialog = modal(page, 'Send a test email');
  await expect(dialog.getByLabel('Send to')).toHaveValue(accounts.am.email);
  const outsider = address('outsider');
  await dialog.getByLabel('Send to').fill(outsider);
  await dialog.getByRole('button', { name: 'Send test' }).click();
  await expect(dialog.getByRole('alert')).toContainText('verified staff email address');
  errors.ignore(/HTTP 400 POST .*\/test$/);
  expect(mailsTo(outsider)).toHaveLength(0);
  // A client user is not staff either.
  const client = await call(accounts.am, 'POST', `/agency/email/templates/${recall('templateId')}/test`, {
    to: state().owner.email,
    clientAccountId: state().client.id,
  });
  expect(codeOf(client)).toBe('email.test_recipient_not_allowed');

  await dialog.getByLabel('Send to').fill(accounts.am.email);
  await dialog.getByRole('button', { name: 'Send test' }).click();
  await expect(toast(page, 'Test email sent')).toBeVisible();
  const mail = await waitForMail(accounts.am.email, '[Test] Welcome Alex to Lumen Labs Ltd');
  expect(mailsTo(accounts.am.email).length).toBe(before + 1);
  expect(mail.html).toContain('Hi Alex, your free plan is live.');
  // Sent from the workspace's verified default sender.
  expect(mail.headers.from).toContain(recall('senderEmail'));
  errors.expectClean('test sends');
  expect(mailsWith(accounts.am.email, '[Test]').length).toBeGreaterThan(0);
});

test('duplicate and archive; archived templates leave the picker but can be restored', async ({
  browser,
}) => {
  const id = recall('templateId');
  const copy = await call<{ id: string; name: string }>(
    accounts.am,
    'POST',
    `/agency/email/templates/${id}/duplicate?clientId=${state().client.id}`,
  );
  expect(copy.status).toBe(200);
  expect(copy.body.name).toBe(`${recall('templateName')} (copy)`);

  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await openEmail(page, '/templates');
  const table = page.getByRole('table', { name: 'Email templates' });
  const row = table.getByRole('row').filter({ hasText: `${recall('templateName')} (copy)` });
  await row.getByRole('button', { name: `Actions for ${recall('templateName')} (copy)` }).click();
  await page.getByRole('menuitem', { name: 'Archive' }).click();
  await modal(page, 'Archive this template?').getByRole('button', { name: 'Archive' }).click();
  await expect(row).toBeHidden();
  await page.getByLabel('Show archived templates').check();
  await expect(table.getByRole('row').filter({ hasText: `${recall('templateName')} (copy)` })).toContainText(
    'Archived',
  );
  errors.expectClean('archiving a template');
  const listed = await call<{ id: string }[]>(
    accounts.am,
    'GET',
    `/agency/email/templates?clientId=${state().client.id}`,
  );
  expect(listed.body.some((t) => t.id === copy.body.id)).toBe(false);
});
