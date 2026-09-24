import type { Page } from '@playwright/test';
import {
  accounts,
  clients,
  expect,
  landing,
  modal,
  runId,
  test,
  toast,
  watchErrors,
} from './support/platform';

/**
 * "Everything is editable" — one sample per area, each through the UI and checked after a reload:
 *   CRM contact archive → restore; project task edit → complete; social post draft edit; SEO audit issue marked fixed
 *   (and reopened); Website → Page texts change shows on the public home page (then reset); an email template edit
 *   with its preview (then reset).
 */

test('CRM: archive a contact, find it under Archived, restore it', async ({ as }) => {
  const id = runId();
  const first = 'Arlo';
  const last = `Archivable ${id}`;
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);

  await am.goto('/agency/crm/contacts');
  await am.getByRole('button', { name: 'New contact' }).click();
  const dialog = modal(am, 'New contact');
  await dialog.getByLabel('First name').fill(first);
  await dialog.getByLabel('Last name').fill(last);
  await dialog.getByLabel('Email').fill(`arlo.${id}@e2e.optimizeall.test`);
  await dialog.getByRole('button', { name: 'Create contact' }).click();
  await expect(toast(am, 'Contact created')).toBeVisible();
  await am
    .getByRole('main')
    .getByRole('link', { name: `${first} ${last}` })
    .click();
  await expect(am.getByRole('heading', { level: 1, name: `${first} ${last}` })).toBeVisible();

  await am.getByRole('button', { name: 'Archive' }).click();
  await modal(am, 'Archive this contact?').getByRole('button', { name: 'Archive' }).click();
  await expect(toast(am, 'Archived the contact')).toBeVisible();
  await expect(am.getByText('This contact is archived')).toBeVisible();
  await expect(am.getByRole('button', { name: 'Edit' })).toHaveCount(0);

  // Hidden from the active list, listed under Archived.
  await am.goto('/agency/crm/contacts');
  await am.getByRole('searchbox', { name: 'Search' }).fill(last);
  await expect(am.getByRole('main').getByRole('link', { name: `${first} ${last}` })).toHaveCount(0);
  await am.getByLabel('Show').selectOption('archived');
  await expect(am.getByRole('table', { name: 'Archived contacts' })).toBeVisible();
  await am
    .getByRole('main')
    .getByRole('link', { name: `${first} ${last}` })
    .click();

  await am.getByRole('button', { name: 'Restore' }).click();
  await expect(toast(am, 'Restored the contact')).toBeVisible();
  await am.reload();
  await expect(am.getByRole('heading', { level: 1, name: `${first} ${last}` })).toBeVisible();
  await expect(am.getByText('This contact is archived')).toHaveCount(0);
  await expect(am.getByRole('button', { name: 'Edit' })).toBeVisible();
  errors.expectClean('archiving and restoring a contact');
});

test('Projects: add a task, edit it, then mark it complete', async ({ as }) => {
  const id = runId();
  const title = `E2E task ${id}`;
  const renamed = `E2E task ${id} (edited)`;
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);

  await am.goto('/agency/projects');
  await am.getByRole('main').getByRole('link', { name: 'SEO retainer' }).first().click();
  await expect(am.getByRole('heading', { level: 1, name: 'SEO retainer' })).toBeVisible();
  const projectUrl = am.url();
  const add = am.getByRole('form', { name: 'Add a task' });
  await add.getByLabel('New task').fill(title);
  await add.getByRole('button', { name: 'Add task' }).click();
  const todo = am.getByRole('region', { name: /^To do \d+$/ });
  await todo.getByRole('button', { name: title }).click();

  // The task editor gives no toast on save; wait for the saved task instead (the drawer is titled from the server copy).
  const saved = () =>
    am.waitForResponse(
      (r) => r.request().method() === 'PUT' && /\/api\/v1\/agency\/tasks\/[0-9a-f-]+$/.test(r.url()),
    );
  await expect(modal(am, title)).toBeVisible();
  const details = am.getByRole('form', { name: 'Task details' });
  await details.getByLabel('Title').fill(renamed);
  await details.getByLabel('Priority').selectOption({ label: 'High' });
  await details.getByLabel('Description').fill('Checked by the platform e2e suite.');
  let response = saved();
  await details.getByRole('button', { name: 'Save task' }).click();
  expect((await response).ok()).toBe(true);
  await expect(modal(am, renamed)).toBeVisible();
  await expect(todo.getByRole('button', { name: renamed })).toBeVisible();

  // Mark it complete.
  await details.getByLabel('Status').selectOption({ label: 'Done' });
  response = saved();
  await details.getByRole('button', { name: 'Save task' }).click();
  expect((await response).ok()).toBe(true);
  await expect(modal(am, renamed).getByText('Done', { exact: true }).first()).toBeVisible();
  await am.keyboard.press('Escape');
  await expect(modal(am, renamed)).toBeHidden();

  await am.goto(projectUrl);
  const done = am.getByRole('region', { name: /^Done \d+$/ });
  await expect(done.getByRole('button', { name: renamed })).toBeVisible();
  await expect(done.getByRole('listitem').filter({ hasText: renamed })).toContainText('High');
  await expect(todo.getByRole('button', { name: renamed })).toHaveCount(0);
  errors.expectClean('editing and completing a task');
});

/** The composer's (debounced) server validation of a body containing `text`. */
function validation(page: Page, text: string) {
  return page.waitForResponse(
    (r) =>
      r.url().endsWith('/api/v1/agency/social/validate') &&
      (r.request().postData() ?? '').includes(text) &&
      r.ok(),
  );
}

test('Social: edit a post draft and keep the change', async ({ as }) => {
  const id = runId();
  const title = `E2E draft ${id}`;
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);

  await am.goto('/agency/social/compose');
  const setup = am.getByRole('region', { name: 'Post' });
  await setup.getByLabel('Client').selectOption({ label: clients.nimbus.name });
  await setup.getByLabel('Internal title').fill(title);
  await setup
    .getByRole('checkbox', { name: /^LinkedIn · @/ })
    .first()
    .check();
  const variants = am.getByRole('region', { name: 'Content per network' });
  // Server-side validation of this text settles (and the layout with it) before saving.
  let validated = validation(am, `First draft ${id}`);
  await variants.getByLabel('LinkedIn text').fill(`First draft ${id}`);
  await validated;
  await expect(am.getByRole('status').filter({ hasText: 'Ready for review' })).toBeVisible();
  await am.getByRole('button', { name: 'Save draft' }).click();
  await expect(toast(am, 'Draft created')).toBeVisible();
  await expect(am).toHaveURL(/\/agency\/social\/posts\/[0-9a-f-]{36}$/);

  // Edit the saved draft: title and text.
  await setup.getByLabel('Internal title').fill(`${title} v2`);
  validated = validation(am, `Second draft ${id}`);
  await variants.getByLabel('LinkedIn text').fill(`Second draft ${id} — now with a stronger hook.`);
  await validated;
  await expect(am.getByRole('status').filter({ hasText: 'Ready for review' })).toBeVisible();
  await am.getByRole('button', { name: 'Save changes' }).click();
  await expect(toast(am, 'Post saved')).toBeVisible();

  await am.reload();
  await expect(am.getByRole('heading', { level: 1, name: `${title} v2` })).toBeVisible();
  await expect(am.getByText('Draft', { exact: true }).first()).toBeVisible();
  await expect(variants.getByLabel('LinkedIn text')).toHaveValue(
    `Second draft ${id} — now with a stronger hook.`,
  );
  await expect(
    am.getByRole('region', { name: 'Preview' }).getByText(`Second draft ${id}`, { exact: false }).first(),
  ).toBeVisible();
  errors.expectClean('editing a social draft');
});

test('SEO: mark an audit issue fixed, then reopen it', async ({ as }) => {
  const seo = await as(accounts.seo, landing.agency);
  const errors = watchErrors(seo);
  await seo.goto('/agency/seo');
  await seo.getByRole('link', { name: 'Karachi Eats', exact: true }).click();
  await seo.getByRole('link', { name: 'View results' }).click();
  await expect(seo.getByRole('heading', { level: 1, name: 'Audit: Karachi Eats' })).toBeVisible();
  const auditUrl = seo.url();

  const issueName = 'Thin content';
  const actions = seo.getByRole('group', { name: `Actions for ${issueName}` });
  await actions.getByRole('button', { name: 'Mark fixed' }).click();
  await expect(toast(seo, 'Issue marked fixed')).toBeVisible();
  await expect(actions.getByRole('button', { name: 'Reopen' })).toBeVisible();

  await seo.goto(auditUrl);
  const show = seo.getByLabel('Show issues');
  await expect(show.locator('option[value="done"]')).toHaveText('Fixed or ignored (1)');
  await show.selectOption('done');
  const toggle = seo.getByRole('button', { name: new RegExp(`^${issueName}`) });
  await expect(toggle).toContainText('Marked fixed');

  await actions.getByRole('button', { name: 'Reopen' }).click();
  await expect(toast(seo, 'Issue reopened')).toBeVisible();
  await seo.goto(auditUrl);
  await expect(seo.getByLabel('Show issues').locator('option[value="done"]')).toHaveText(
    'Fixed or ignored (0)',
  );
  errors.expectClean('triaging an SEO issue');
});

test('Website → Page texts: a changed home headline shows on the public home page', async ({
  as,
  anonymous,
}) => {
  const id = runId();
  const heading = `Every channel, one accountable team (${id})`;
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/agency/website/copy');
  await expect(admin.getByRole('heading', { level: 1, name: 'Page texts' })).toBeVisible();
  await expect(admin.getByLabel('Page')).toHaveValue(/home/i);
  const field = admin.getByRole('textbox', { name: 'Services title', exact: true });
  await expect(field).toHaveValue('Every channel, one accountable team');
  await field.fill(heading);
  await expect(admin.getByRole('listitem').filter({ has: field }).getByText('Unsaved')).toBeVisible();
  await admin.getByRole('button', { name: 'Save 1 change' }).click();
  await expect(toast(admin, 'Texts saved')).toBeVisible();
  await expect(admin.getByRole('button', { name: 'Save changes' })).toBeDisabled();

  // The public site (anonymous visitor) shows it.
  const visitor = await anonymous();
  const visitorErrors = watchErrors(visitor);
  await visitor.goto('/');
  await expect(visitor.getByRole('heading', { name: heading })).toBeVisible();
  visitorErrors.expectClean('the public home page');

  // Reset it to the original wording; the site follows.
  await admin.reload();
  await expect(field).toHaveValue(heading);
  const item = admin.getByRole('listitem').filter({ has: field });
  await expect(item.getByText('Customized')).toBeVisible();
  await item.getByRole('button', { name: 'Reset to default' }).click();
  await expect(field).toHaveValue('Every channel, one accountable team');
  await admin.getByRole('button', { name: 'Save 1 change' }).click();
  await expect(toast(admin, 'Texts saved')).toBeVisible();
  await expect(item.getByText('Customized')).toHaveCount(0);
  await visitor.reload();
  await expect(
    visitor.getByRole('heading', { name: 'Every channel, one accountable team', exact: true }),
  ).toBeVisible();
  errors.expectClean('editing page texts');
});

test('Admin → Content → Email templates: edit a template and preview it', async ({ as }) => {
  const id = runId();
  const subject = `Reset your password (${id})`;
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/content');
  await admin.getByRole('tab', { name: 'Email templates' }).click();
  await admin
    .getByRole('table', { name: 'Email templates' })
    .getByRole('button', { name: 'Password reset' })
    .click();

  const form = admin.getByRole('form', { name: 'Edit Password reset' });
  const preview = admin.getByRole('region', { name: 'Preview with sample values' });
  await expect(preview.getByText(/^Subject:/)).toBeVisible();
  await form.getByLabel('Subject').fill(subject);
  const body = form.getByLabel('Email text');
  await body.fill(`${await body.inputValue()}\n\nQuestions? Reply to this email (${id}).`);
  await form.getByRole('button', { name: 'Preview' }).click();
  await expect(preview).toContainText(`Subject: ${subject}`);
  await preview.getByText('Plain-text version').click();
  await expect(preview.locator('pre')).toContainText(`Questions? Reply to this email (${id}).`);

  await form.getByRole('button', { name: 'Save template' }).click();
  await expect(toast(admin, 'Template saved')).toBeVisible();
  await expect(form.getByText('Customized', { exact: true })).toBeVisible();

  // Kept after a reload; then back to the default wording.
  await admin.reload();
  await admin
    .getByRole('table', { name: 'Email templates' })
    .getByRole('button', { name: 'Password reset' })
    .click();
  await expect(form.getByLabel('Subject')).toHaveValue(subject);
  await form.getByRole('button', { name: 'Reset to default' }).click();
  await modal(admin, 'Reset to the default wording?').getByRole('button', { name: 'Reset template' }).click();
  await expect(toast(admin, 'Template reset')).toBeVisible();
  await expect(form.getByLabel('Subject')).not.toHaveValue(subject);
  errors.expectClean('editing an email template');
});
