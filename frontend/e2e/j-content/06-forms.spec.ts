import { readFileSync } from 'node:fs';
import { expect, test } from '@playwright/test';
import {
  API_URL,
  accounts,
  actor,
  api,
  landing,
  recall,
  refused,
  runId,
  visitor,
  watchErrors,
} from './support/content';

/**
 * Form builder and submissions, as the designer:
 *   a client's contact form gets a new required "Budget" field and a minimum fill time → the embeddable /f/:id page
 *   renders it and enforces the required fields → a visitor submits values that look like spreadsheet formulas → the
 *   submissions list shows it → Export CSV neutralises the formulas → spam handling: the honeypot is accepted but never
 *   stored, a too-fast or token-less submission is refused, spam is excluded from the list and the export → archiving
 *   the form takes it offline; stale form edits are 409.
 */

interface FormDetail {
  id: string;
  name: string;
  status: string;
  schema: { steps: { id: string; title?: string; fields: Record<string, unknown>[] }[] };
  submitLabel: string;
  successMessage: string;
  redirectUrl: string | null;
  notifyUserIds: string[];
  autoresponderEnabled: boolean;
  autoresponderSubject: string | null;
  autoresponderBody: string | null;
  allowedOrigins: string[];
  consentText: string | null;
  captcha: string;
  minFillSeconds: number;
  concurrencyStamp: string;
}

const update = (f: FormDetail, patch: Partial<FormDetail> = {}) => {
  const m = { ...f, ...patch };
  return {
    name: m.name,
    status: m.status,
    schema: m.schema,
    submitLabel: m.submitLabel,
    successMessage: m.successMessage,
    redirectUrl: m.redirectUrl,
    notifyUserIds: m.notifyUserIds,
    autoresponderEnabled: m.autoresponderEnabled,
    autoresponderSubject: m.autoresponderSubject,
    autoresponderBody: m.autoresponderBody,
    allowedOrigins: m.allowedOrigins,
    consentText: m.consentText,
    captcha: m.captcha,
    minFillSeconds: m.minFillSeconds,
    concurrencyStamp: m.concurrencyStamp,
  };
};

async function submit(formId: string, body: Record<string, unknown>) {
  const res = await fetch(`${API_URL}/api/v1/public/forms/${formId}/submissions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
    body: JSON.stringify(body),
  });
  return {
    status: res.status,
    body: (await res.json().catch(() => null)) as { code?: string; errors?: Record<string, string[]> } | null,
  };
}

async function renderToken(formId: string): Promise<string> {
  const res = await fetch(`${API_URL}/api/v1/public/forms/${formId}`, {
    headers: { 'X-Requested-With': 'fetch' },
  });
  return ((await res.json()) as { token: string }).token;
}

test('form builder → embeddable form → submissions → CSV export with formula escaping → spam → archive', async ({
  browser,
}) => {
  test.setTimeout(180_000);
  const id = runId();
  const designerApi = await api(accounts.designer);
  const { clientId } = recall<{ clientId: string }>('bootcampForm');
  const created = await designerApi.post<FormDetail>('/agency/pages/forms', {
    clientAccountId: clientId,
    name: `Corporate enquiry ${id}`,
    templateKey: 'contact',
  });

  // ---------------------------------------------------------------- the builder: a new required field and a minimum fill time
  const schema = structuredClone(created.schema);
  const fields = schema.steps[0]!.fields;
  fields.splice(
    fields.findIndex((f) => f.key === 'message'),
    0,
    {
      key: 'budget',
      type: 'select',
      label: 'Monthly budget',
      required: true,
      options: [
        { value: 'under-5k', label: 'Under $5k' },
        { value: '5k-plus', label: '$5k or more' },
      ],
    },
  );
  let form = await designerApi.put<FormDetail>(
    `/agency/pages/forms/${created.id}`,
    update(created, { schema, minFillSeconds: 2, submitLabel: 'Send enquiry' }),
  );
  expect(form.schema.steps[0]!.fields.some((f) => f.key === 'budget')).toBe(true);
  // A stale edit (the stamp from before the change) is refused.
  const stale = await refused(
    designerApi.put(`/agency/pages/forms/${created.id}`, update(created, { name: 'Stale rename' })),
  );
  expect(stale.status).toBe(409);
  // So is an edit without a stamp (it is never treated as "skip the check").
  const noStamp = await refused(
    designerApi.put(`/agency/pages/forms/${created.id}`, {
      ...update(form, { name: 'No stamp' }),
      concurrencyStamp: undefined,
    }),
  );
  expect(noStamp.status).toBe(409);
  // An invalid schema (duplicate field key) is refused.
  const dupSchema = structuredClone(form.schema);
  dupSchema.steps[0]!.fields.push({ key: 'budget', type: 'text', label: 'Budget again' });
  expect(
    (await refused(designerApi.put(`/agency/pages/forms/${created.id}`, update(form, { schema: dupSchema }))))
      .status,
  ).toBe(400);

  // ---------------------------------------------------------------- the embeddable form: required fields, then a formula-looking submission
  const page = await visitor(browser);
  const errors = watchErrors(page);
  await page.goto(`/f/${form.id}`);
  const main = page.getByRole('main');
  await expect(main.getByLabel('Monthly budget')).toBeVisible();
  const openedAt = Date.now();
  await main.getByRole('button', { name: 'Send enquiry' }).click();
  await expect(main.getByText('Monthly budget is required.')).toBeVisible();
  await expect(main.getByText('Full name is required.')).toBeVisible();
  await main.getByLabel('Full name').fill('=HYPERLINK("http://evil.example/x","Click")');
  await main.getByLabel('Work email').fill(`csv.${id}@e2e.optimizeall.test`);
  await main.getByLabel(/^Company/).fill("+cmd|' /C calc'!A0");
  await main.getByLabel('How can we help?').selectOption({ label: 'Sales enquiry' });
  await main.getByLabel('Monthly budget').selectOption({ label: '$5k or more' });
  await main.getByLabel('Message').fill('@SUM(1+1) please call us back');
  await main.getByRole('checkbox', { name: /^I agree to be contacted/ }).check();
  await expect.poll(() => Date.now() - openedAt).toBeGreaterThanOrEqual(2_500);
  await main.getByRole('button', { name: 'Send enquiry' }).click();
  await expect(main.getByRole('status')).toContainText(form.successMessage);
  errors.expectClean('the embeddable form');

  // ---------------------------------------------------------------- spam handling
  const values = {
    name: 'Spam Bot',
    email: `bot.${id}@e2e.optimizeall.test`,
    topic: 'sales-enquiry',
    budget: 'under-5k',
    message: 'Buy now',
    consent: true,
  };
  await new Promise((r) => setTimeout(r, 2_500));
  // The honeypot looks like a success to the bot, but nothing is stored.
  const honeypot = await submit(form.id, { token: await renderToken(form.id), hp: 'I am a bot', values });
  expect(honeypot.status).toBe(201);
  // No render token, a forged token, or submitting faster than the minimum fill time: refused.
  expect((await submit(form.id, { values })).body?.code).toBe('forms.token_invalid');
  expect((await submit(form.id, { token: 'forged.token', values })).body?.code).toBe('forms.token_invalid');
  expect((await submit(form.id, { token: await renderToken(form.id), values })).body?.code).toBe(
    'forms.too_fast',
  );
  // Server-side required-field validation (a bot skipping the browser).
  const token = await renderToken(form.id);
  await new Promise((r) => setTimeout(r, 2_500));
  const missing = await submit(form.id, { token, values: { ...values, budget: '' } });
  expect(missing.status).toBe(400);
  expect(Object.keys(missing.body?.errors ?? {})).toContain('budget');
  // A real-looking submission that staff will mark as spam.
  const spam = await submit(form.id, { token, values });
  expect(spam.status).toBe(201);
  // Render tokens are single use: replaying the accepted submission is refused (only one row is stored).
  const replay = await submit(form.id, { token, values });
  expect(replay.status).toBe(409);
  expect(replay.body?.code).toBe('forms.already_submitted');

  // ---------------------------------------------------------------- staff: submissions list, spam, CSV export
  type Row = { id: string; email: string; name: string; values: Record<string, string>; status: string };
  const list = await designerApi.get<{ items: Row[]; total: number }>(
    `/agency/pages/forms/${form.id}/submissions`,
  );
  expect(list.items.map((s) => s.email).sort()).toEqual(
    [`bot.${id}@e2e.optimizeall.test`, `csv.${id}@e2e.optimizeall.test`].sort(),
  );
  const spamRow = list.items.find((s) => s.email === `bot.${id}@e2e.optimizeall.test`)!;
  await designerApi.post(`/agency/pages/forms/${form.id}/submissions/bulk`, {
    ids: [spamRow.id],
    status: 'Spam',
  });

  const designer = await actor(browser, accounts.designer, landing.agency);
  await designer.goto(`/agency/pages/forms/${form.id}/submissions`);
  await expect(
    designer.getByRole('heading', { level: 1, name: `Corporate enquiry ${id} submissions` }),
  ).toBeVisible();
  const table = designer.getByRole('table', { name: `Submissions for Corporate enquiry ${id}` });
  await expect(table.getByText(`csv.${id}@e2e.optimizeall.test`)).toBeVisible();
  await expect(table.getByText(`bot.${id}@e2e.optimizeall.test`)).toHaveCount(0);
  const downloadPromise = designer.waitForEvent('download');
  await designer.getByRole('button', { name: 'Export CSV' }).click();
  const download = await downloadPromise;
  const csv = readFileSync((await download.path())!, 'utf8').replace(/^\uFEFF/, ''); // Excel BOM
  const [header, ...rows] = csv.trim().split('\r\n');
  expect(header).toContain('budget');
  expect(rows).toHaveLength(1); // spam is not exported by default
  const row = rows[0]!;
  // Every formula-looking value is prefixed with ' so spreadsheets show it as text.
  expect(row).toContain(`"'=HYPERLINK(""http://evil.example/x"",""Click"")"`);
  expect(row).toContain(`'+cmd|' /C calc'!A0`);
  expect(row).toContain(`'@SUM(1+1) please call us back`);
  expect(row).not.toMatch(/(^|,)[=+@]/);
  expect(row).toContain('5k-plus');

  // ---------------------------------------------------------------- archive: the form goes offline
  await designerApi.delete(`/agency/pages/forms/${form.id}`);
  const offline = await visitor(browser);
  await offline.goto(`/f/${form.id}`);
  await expect(offline.getByText('This form isn’t available here.')).toBeVisible();
  expect((await submit(form.id, { token: await renderToken(form.id).catch(() => 'x'), values })).status).toBe(
    404,
  );
  form = await designerApi.get<FormDetail>(`/agency/pages/forms/${form.id}`);
  expect(form.status).toBe('Archived');
  // Submissions are kept for staff.
  expect((await designerApi.get<{ total: number }>(`/agency/pages/forms/${form.id}/submissions`)).total).toBe(
    1,
  );
});
