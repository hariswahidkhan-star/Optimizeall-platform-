import type { Page } from '@playwright/test';
import {
  DEMO_ADMIN_NAME,
  accounts,
  adminApi,
  arrangeTestUser,
  codeOf,
  expect,
  landing,
  modal,
  runId,
  test,
  toast,
  watchErrors,
} from './support/jadmin';

/**
 * Settings and content, each change checked where it shows and in the audit log:
 *   settings (edit with a reason → boundary values refused → restore the default with a reason → audit before/after),
 *   portal texts (help centre headline → public /faq → reset), email templates (edit, preview, required and unknown
 *   variables refused, reset), CMS pages (version history, restore an older version, scheduled go-live hides the page),
 *   announcements (created once despite a double-click, shown on the participant home, stale edit → 409 prompt),
 *   FAQ reorder (saved order is the public order).
 */
test.describe.configure({ mode: 'serial' });

async function openAudit(page: Page, action: string) {
  await page.goto('/admin/audit');
  const filters = page.getByRole('search', { name: 'Audit log filters' });
  await filters.getByLabel('Action').fill(action);
  await filters.getByRole('button', { name: 'Apply filters' }).click();
  return page.getByRole('region', { name: 'Audit entries' }).getByRole('listitem');
}

test('settings: edit with a reason, boundary values refused, restore the default; audited with before/after', async ({
  as,
}) => {
  const id = runId();
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/settings');
  const setting = admin.getByRole('region', { name: 'Reviewer claim duration' });
  await expect(setting.getByText('Default', { exact: true })).toBeVisible();
  const input = setting.getByLabel('New value');
  const original = await input.inputValue();

  // Out of range (1–240) and unchanged values are refused before anything is sent.
  for (const bad of ['0', '241', '-5']) {
    await input.fill(bad);
    await setting.getByRole('button', { name: 'Save…' }).click();
    await expect(setting.getByText(/between 1 and 240|1 to 240|at least 1|at most 240/i).first()).toBeVisible();
    await expect(modal(admin, 'Change Reviewer claim duration?')).toHaveCount(0);
  }
  await input.fill(original);
  await setting.getByRole('button', { name: 'Save…' }).click();
  await expect(setting.getByRole('alert')).toContainText('Nothing to save');

  // The boundary itself is accepted.
  const reason = `E2E ${id}: longer claims during the audit week`;
  await input.fill('240');
  await setting.getByRole('button', { name: 'Save…' }).click();
  const dialog = modal(admin, 'Change Reviewer claim duration?');
  await expect(dialog).toContainText(`${original}`);
  await expect(dialog).toContainText('240');
  await dialog.getByLabel('Reason').fill(reason);
  await dialog.getByRole('button', { name: 'Save setting' }).click();
  await expect(toast(admin, 'Setting saved')).toBeVisible();
  await expect(setting.getByText('Customised')).toBeVisible();
  await expect(setting).toContainText(`by ${DEMO_ADMIN_NAME}`);

  // The API enforces the range on its own.
  const api = await adminApi();
  expect(
    await codeOf(api.put('/admin/settings/review.claimMinutes', { value: 241, reason: 'E2E out of range', confirm: true })),
  ).toMatch(/^400\b/);

  // Restore the default (reason required).
  const resetReason = `E2E ${id}: back to normal`;
  await setting.getByRole('button', { name: 'Restore default…' }).click();
  const reset = modal(admin, 'Restore the default for Reviewer claim duration?');
  await reset.getByLabel('Reason').fill(resetReason);
  await reset.getByRole('button', { name: 'Restore default' }).click();
  await expect(toast(admin, 'Default restored')).toBeVisible();
  await expect(setting.getByText('Default', { exact: true })).toBeVisible();
  await expect(input).toHaveValue(original);

  // Audit: both changes with their reasons and before/after values.
  const changed = (await openAudit(admin, 'admin.setting_changed')).filter({ hasText: reason });
  await expect(changed).toHaveCount(1);
  await changed.getByRole('button', { name: /admin\.setting_changed/ }).click();
  await expect(changed.getByText('Before')).toBeVisible();
  await expect(changed.getByText('After')).toBeVisible();
  await expect(changed).toContainText('240');
  const restored = (await openAudit(admin, 'admin.setting_reset')).filter({ hasText: resetReason });
  await expect(restored).toHaveCount(1);
  errors.expectClean('editing and restoring a setting');
});

test('portal texts: the help centre headline changes on the public FAQ page, then resets', async ({
  as,
  anonymous,
}) => {
  const id = runId();
  const headline = `Answers for creators (${id})`;
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/content?tab=copy');
  await expect(admin.getByLabel('Page')).toHaveValue('helpCentre');
  const field = admin.getByRole('textbox', { name: 'Headline', exact: true });
  await expect(field).toHaveValue('Frequently asked questions');
  await field.fill(headline);
  await admin.getByRole('button', { name: 'Save 1 change' }).click();
  await expect(toast(admin, 'Texts saved')).toBeVisible();

  const visitor = await anonymous();
  await visitor.goto('/faq');
  await expect(visitor.getByRole('heading', { level: 1, name: headline })).toBeVisible();

  // A text that is too long is refused by the API (boundary: 300 characters for a one-line text).
  const api = await adminApi();
  const catalog = await api.get<{ groups: { entries: { key: string; concurrencyStamp: string | null }[] }[] }>(
    '/admin/content/copy',
  );
  const entry = catalog.groups.flatMap((g) => g.entries).find((e) => e.key === 'faq.hero.title')!;
  expect(
    await codeOf(
      api.put('/admin/content/copy', {
        changes: [{ key: 'faq.hero.title', value: 'x'.repeat(301), concurrencyStamp: entry.concurrencyStamp }],
      }),
    ),
  ).toMatch(/^400\b/);
  // A stale stamp is refused with 409.
  expect(
    await codeOf(
      api.put('/admin/content/copy', {
        changes: [{ key: 'faq.hero.title', value: 'Stale', concurrencyStamp: '00000000-0000-0000-0000-000000000000' }],
      }),
    ),
  ).toBe('409 concurrency.conflict');

  await admin.reload();
  const item = admin.getByRole('listitem').filter({ has: field });
  await expect(item.getByText('Customized')).toBeVisible();
  await item.getByRole('button', { name: 'Reset to default' }).click();
  await admin.getByRole('button', { name: 'Save 1 change' }).click();
  await expect(toast(admin, 'Texts saved')).toBeVisible();
  await visitor.reload();
  await expect(visitor.getByRole('heading', { level: 1, name: 'Frequently asked questions' })).toBeVisible();

  await expect((await openAudit(admin, 'content.copy_updated')).first()).toBeVisible();
  errors.expectClean('editing portal texts');
});

test('email templates: required and unknown variables are enforced; preview; reset', async ({ as }) => {
  const id = runId();
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  errors.ignore(/HTTP 400 (PUT|POST) .*\/api\/v1\/admin\/email-templates\//);
  await admin.goto('/admin/content?tab=emails');
  await admin.getByRole('table', { name: 'Email templates' }).getByRole('button', { name: 'Password reset' }).click();
  const form = admin.getByRole('form', { name: 'Edit Password reset' });
  const preview = admin.getByRole('region', { name: 'Preview with sample values' });
  const body = form.getByLabel('Email text');
  const original = await body.inputValue();
  expect(original).toContain('{{resetUrl}}');

  // Dropping the required reset link is refused (on preview and on save).
  await body.fill(original.replaceAll('{{resetUrl}}', ''));
  await form.getByRole('button', { name: 'Save template' }).click();
  await expect(form.getByText(/Keep \{\{resetUrl\}\} in the email/)).toBeVisible();
  // An unknown variable is refused too.
  await body.fill(`${original}\n\nHello {{favouriteColour}}`);
  await form.getByRole('button', { name: 'Save template' }).click();
  await expect(form.getByText(/Unknown variable \{\{favouriteColour\}\}/)).toBeVisible();

  // A valid edit: insert a variable at the cursor, preview, save.
  await body.fill(`${original}\n\nRequested for ${id}: `);
  await form.getByRole('button', { name: /^\{\{displayName\}\}|^\{\{name\}\}/ }).first().click();
  await expect(body).toHaveValue(new RegExp(`Requested for ${id}: \\{\\{\\w+\\}\\}`));
  await form.getByRole('button', { name: 'Preview' }).click();
  await preview.getByText('Plain-text version').click();
  await expect(preview.locator('pre')).toContainText(`Requested for ${id}: `);
  await expect(preview.locator('pre')).not.toContainText('{{');
  await form.getByRole('button', { name: 'Save template' }).click();
  await expect(toast(admin, 'Template saved')).toBeVisible();
  await expect(form.getByText('Customized', { exact: true })).toBeVisible();

  // Reset to the default wording.
  await form.getByRole('button', { name: 'Reset to default' }).click();
  await modal(admin, 'Reset to the default wording?').getByRole('button', { name: 'Reset template' }).click();
  await expect(toast(admin, 'Template reset')).toBeVisible();
  await expect(body).toHaveValue(original);
  await expect((await openAudit(admin, 'content.email_template_reset')).first()).toBeVisible();
  errors.expectClean('editing an email template');
});

test('CMS page: version history, restore an older version, scheduled go-live hides the page until then', async ({
  as,
  anonymous,
}) => {
  const id = runId();
  const slug = `e2e-policy-${id}`;
  const v1 = `Our policy, first wording ${id}.`;
  const v2 = `Our policy, second wording ${id}.`;
  const api = await adminApi();
  const created = await api.post<{ id: string }>('/agency/website/pages', {
    slug,
    title: `E2E policy ${id}`,
    summary: null,
    kind: 'Legal',
    blocks: [{ id: 'b1', type: 'richText', data: { markdown: v1 } }],
    seo: { title: null, description: null, ogImageUrl: null, canonicalUrl: null, noIndex: false },
    isPublished: true,
    sortOrder: 0,
    publishAt: null,
  });

  const visitor = await anonymous();
  await visitor.goto(`/${slug}`);
  await expect(visitor.getByText(v1)).toBeVisible();

  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto(`/agency/website/pages/${created.id}`);
  const editor = admin.getByRole('form', { name: 'Page editor' });
  await editor.getByLabel('Content').fill(v2);
  await editor.getByLabel('Change note').fill(`E2E ${id}: new wording`);
  await editor.getByRole('button', { name: 'Save page' }).click();
  await expect(toast(admin, 'Page saved')).toBeVisible();
  await visitor.reload();
  await expect(visitor.getByText(v2)).toBeVisible();

  // History lists both versions; restore the first.
  const history = admin.getByRole('region', { name: 'Version history' });
  await expect(history.getByText(`E2E ${id}: new wording`)).toBeVisible();
  const first = history.getByRole('listitem').filter({ hasText: /Version (0|1) / }).last();
  await first.getByRole('button', { name: /^Preview version/ }).click();
  await expect(admin.getByRole('complementary', { name: /^Preview of version/ })).toContainText(v1);
  await first.getByRole('button', { name: /^Restore version/ }).click();
  const confirm = modal(admin, /^Restore version \d+\?$/);
  await confirm.getByRole('button', { name: 'Restore version' }).click();
  await expect(toast(admin, /Version \d+ restored/)).toBeVisible();
  await visitor.reload();
  await expect(visitor.getByText(v1)).toBeVisible();

  // Scheduled go-live: a future time hides the page (404) until then.
  await admin.reload();
  const goLive = editor.getByLabel('Go live at');
  const future = new Date(Date.now() + 3 * 86_400_000);
  const local = new Date(future.getTime() - future.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
  await goLive.fill(local);
  await editor.getByRole('button', { name: 'Save page' }).click();
  await expect(toast(admin, 'Page saved')).toBeVisible();
  await visitor.reload();
  await expect(visitor.getByText(v1)).toHaveCount(0);
  await expect(visitor.getByRole('heading', { level: 1 })).toContainText(/not found|can’t find|doesn’t exist/i);
  await goLive.fill('');
  await editor.getByRole('button', { name: 'Save page' }).click();
  await expect(toast(admin, 'Page saved')).toBeVisible();
  await visitor.reload();
  await expect(visitor.getByText(v1)).toBeVisible();
  errors.expectClean('versioning a CMS page');
});

test('announcements: a double-click creates one, it shows on the participant home; a stale edit is refused', async ({
  as,
}) => {
  const id = runId();
  const title = `E2E maintenance window ${id}`;
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  errors.ignore(/HTTP 409 PUT .*\/api\/v1\/admin\/content\/announcements\//);
  await admin.goto('/admin/content?tab=announcements');
  await admin.getByRole('button', { name: 'New announcement' }).click();
  const dialog = modal(admin, 'New announcement');
  await dialog.getByLabel('Title').fill(title);
  await dialog.getByLabel('Message').fill(`Payouts run late this week (${id}).`);
  await dialog.getByRole('button', { name: 'Create announcement' }).dblclick();
  await expect(toast(admin, 'Announcement created')).toBeVisible();
  await expect(dialog).toBeHidden();
  const api = await adminApi();
  const list = await api.get<{ items: { id: string; title: string }[] }>(
    `/admin/content/announcements?search=${encodeURIComponent(title)}`,
  );
  expect(list.items.filter((a) => a.title === title)).toHaveLength(1);

  const participant = await arrangeTestUser(`E2E announcement reader ${id}`);
  const home = await as(participant, landing.participant);
  await expect(home.getByText(title)).toBeVisible();

  // Stale edit: someone else saves first; this dialog's save is refused and offers to load the latest version.
  await admin.getByRole('searchbox', { name: 'Search announcements' }).fill(title);
  await admin.getByRole('button', { name: `Actions for ${title}` }).click();
  await admin.getByRole('menuitem', { name: 'Edit' }).click();
  const edit = modal(admin, 'Edit announcement');
  const item = await api.get<Record<string, unknown>>(`/admin/content/announcements/${list.items[0]!.id}`);
  await api.put(`/admin/content/announcements/${list.items[0]!.id}`, { ...item, body: `Changed elsewhere (${id}).` });
  await edit.getByLabel('Message').fill(`My edit (${id}).`);
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit.getByRole('alert')).toContainText('Someone else changed this item');
  await edit.getByRole('button', { name: 'Load latest version' }).click();
  await expect(edit.getByLabel('Message')).toHaveValue(`Changed elsewhere (${id}).`);
  await edit.getByLabel('Active').click();
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(toast(admin, 'Changes saved')).toBeVisible();
  await home.reload();
  await expect(home.getByRole('heading', { level: 1 }).first()).toBeVisible();
  await expect(home.getByText(title)).toHaveCount(0);
  errors.expectClean('announcements');
});

test('FAQ reorder: the saved order is the public order', async ({ as }) => {
  const admin = await as(accounts.admin, landing.admin);
  const errors = watchErrors(admin);
  await admin.goto('/admin/content?tab=faqs');
  await admin.getByRole('button', { name: 'Reorder' }).click();
  const list = admin.getByRole('list', { name: 'FAQs order' });
  const handles = list.getByRole('button', { name: /^Reorder / });
  await expect(handles.nth(1)).toBeVisible();
  const second = (await handles.nth(1).getAttribute('aria-label'))!.replace(/^Reorder (.*), position .*$/, '$1');
  await admin.getByRole('button', { name: `Move ${second} up` }).click();
  await expect(admin.getByText('Unsaved order changes')).toBeVisible();
  await admin.getByRole('button', { name: 'Save order' }).click();
  await expect(toast(admin, 'Order saved')).toBeVisible();
  await admin.reload();
  await admin.getByRole('button', { name: 'Reorder' }).click();
  await expect(list.getByRole('button', { name: /^Reorder / }).first()).toHaveAttribute(
    'aria-label',
    `Reorder ${second}, position 1 of ${await handles.count()}`,
  );
  // The public FAQ endpoint answers in the new order.
  const res = await fetch(`${process.env.E2E_API_URL}/api/v1/content/faqs`);
  const faqs = (await res.json()) as { question: string }[] | { items: { question: string }[] };
  const questions = (Array.isArray(faqs) ? faqs : faqs.items).map((f) => f.question);
  expect(questions[0]).toBe(second);
  errors.expectClean('reordering FAQs');
});
