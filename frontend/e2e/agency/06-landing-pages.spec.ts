import { expect, test } from '@playwright/test';
import {
  ApiSession,
  accounts,
  actor,
  clients,
  landing,
  modal,
  runId,
  toast,
  watchErrors,
} from './support/agency';

/**
 * Landing pages & forms, as the designer (forms.manage):
 *   create a landing page for Nimbus Fitness from the "Lead generation" template (which also creates its contact form)
 *   → publish → an anonymous visitor opens /lp/:client/:slug → submits the embeddable form at /f/:formId (after the
 *   anti-spam minimum fill time) → the submission shows up for staff.
 */
test('landing page from a template → publish → public page and form → submission for staff', async ({
  browser,
}) => {
  const id = runId();
  const pageName = `E2E spring offer ${id}`;
  const slug = `e2e-spring-offer-${id}`;
  const leadName = `Lee Lead ${id}`;
  const leadEmail = `lee.${id}@e2e.optimizeall.test`;

  // ---------------------------------------------------------------- designer: create from template + publish
  const designer = await actor(browser, accounts.designer, landing.agency);
  const designerErrors = watchErrors(designer);
  await designer
    .getByRole('navigation', { name: 'Agency navigation' })
    .getByRole('link', { name: 'Landing pages' })
    .click();
  await expect(designer.getByRole('heading', { level: 1, name: 'Landing pages' })).toBeVisible();
  await designer.getByRole('main').getByRole('link', { name: 'Templates' }).click();
  await designer.getByRole('button', { name: 'Use the Lead generation template' }).click();
  const create = modal(designer, 'New landing page');
  await create.getByLabel('Client', { exact: true }).selectOption({ label: clients.nimbus.name });
  await create.getByLabel('Page name', { exact: true }).fill(pageName);
  await create.getByLabel(/^URL slug/).fill(slug);
  await expect(create.getByLabel('Template', { exact: true })).toHaveValue('lead-generation');
  await expect(create.getByRole('checkbox', { name: 'Create the template’s form' })).toBeChecked();
  await create.getByRole('button', { name: 'Create page' }).click();
  await expect(toast(designer, 'Page created')).toBeVisible();
  await expect(designer).toHaveURL(/\/agency\/pages\/[0-9a-f-]{36}$/);
  await expect(designer.getByRole('heading', { level: 1, name: pageName })).toBeVisible();
  const publicPath = `/lp/${clients.nimbus.slug}/${slug}`;
  await expect(designer.getByRole('main').getByText(publicPath)).toBeVisible();
  await expect(designer.getByText('Draft', { exact: true }).first()).toBeVisible();

  // Not public before publishing.
  const anon = await (await browser.newContext()).newPage();
  const anonErrors = watchErrors(anon);
  anonErrors.ignore(new RegExp(`HTTP 404 GET \\S+/public/lp/${clients.nimbus.slug}/${slug}`));
  // The document itself is a real 404 until the page is published (docs/SEO_CRO.md § 9.2).
  anonErrors.ignore(new RegExp(`HTTP 404 GET \\S+/lp/${clients.nimbus.slug}/${slug}`));
  // The not-found state asks whether the address has moved (Website → Redirects); 404 = it has not.
  anonErrors.ignore(
    new RegExp(`HTTP 404 GET \\S+/public/redirects\\?path=%2Flp%2F${clients.nimbus.slug}%2F${slug}`),
  );
  await anon.goto(publicPath);
  await expect(anon.getByRole('heading', { level: 1, name: 'This page isn’t available' })).toBeVisible();

  await designer.getByRole('button', { name: 'Publish' }).click();
  await expect(toast(designer, 'Version 1 is live')).toBeVisible();
  await expect(designer.getByText('Published', { exact: true }).first()).toBeVisible();
  await expect(designer.getByRole('link', { name: 'View live' })).toHaveAttribute('href', publicPath);
  designerErrors.expectClean('the page builder');

  // ---------------------------------------------------------------- anonymous visitor: landing page, then the form
  const lpResponse = anon.waitForResponse(
    (r) => r.url().includes(`/public/lp/${clients.nimbus.slug}/${slug}`) && r.ok(),
  );
  await anon.goto(publicPath);
  const lp = (await (await lpResponse).json()) as { forms: { id: string; name: string }[] };
  expect(lp.forms.length, 'the template placed its form on the page').toBeGreaterThan(0);
  const formId = lp.forms[0]!.id;
  await expect(anon.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(anon.getByRole('main').getByRole('button').last()).toBeVisible();
  await expect(anon.getByRole('contentinfo')).toContainText(clients.nimbus.name);

  const formResponse = anon.waitForResponse((r) => r.url().endsWith(`/public/forms/${formId}`) && r.ok());
  await anon.goto(`/f/${formId}`);
  const form = (await (await formResponse).json()) as { submitLabel: string; successMessage: string };
  // The public form does not reveal its anti-spam minimum fill time; staff settings do (arrangement, not under test).
  const designerApi = await ApiSession.login(accounts.designer.email, accounts.designer.password);
  const { minFillSeconds } = await designerApi.get<{ minFillSeconds: number }>(
    `/agency/pages/forms/${formId}`,
  );
  const formOpenedAt = Date.now();
  // The "Lead generation" template's contact form: name, work email, phone, company, topic (+ "Tell us the topic" only
  // for "Something else"), message and consent.
  const main = anon.getByRole('main');
  await main.getByLabel('Full name').fill(leadName);
  await main.getByLabel('Work email').fill(leadEmail);
  await main.getByLabel(/^Company/).fill('Lead Co');
  await expect(main.getByLabel('Tell us the topic')).toHaveCount(0);
  await main.getByLabel('How can we help?').selectOption({ label: 'Something else' });
  await main.getByLabel('Tell us the topic').fill('Corporate wellness plans');
  await main.getByLabel('Message').fill(`E2E enquiry ${id}: interested in the spring offer for 40 staff.`);
  await main.getByRole('checkbox', { name: /^I agree to be contacted about my enquiry/ }).check();
  await expect
    .poll(() => Date.now() - formOpenedAt, { message: 'form open longer than its minimum fill time' })
    .toBeGreaterThanOrEqual(minFillSeconds * 1000);
  await main.getByRole('button', { name: form.submitLabel }).click();
  await expect(main.getByRole('status')).toContainText(form.successMessage);
  anonErrors.expectClean('the public landing page and form');

  // ---------------------------------------------------------------- staff: the submission is listed
  await designer.goto(`/agency/pages/forms/${formId}/submissions`);
  await expect(designer.getByRole('main').getByText(leadEmail).first()).toBeVisible();
});
