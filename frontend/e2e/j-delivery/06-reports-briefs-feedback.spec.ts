import {
  accounts,
  clientUser,
  errorOf,
  expect,
  landing,
  login,
  modal,
  need,
  runId,
  statusOf,
  test,
  watchErrors,
} from './support/delivery';

/**
 * Reports, briefs and feedback: the account manager builds a monthly report from the template, fills one section with a
 * KPI and publishes it; the client sees only the published report and only the sections with content. The client
 * Approver submits a brief (the Viewer can't); the account manager reviews and converts it into tasks. The Owner answers
 * the NPS survey and the account team sees it with the CSAT rating.
 */

interface Section {
  key: string;
  title: string;
  body: string | null;
  kpis: unknown[];
}

const clientText = (body: string | null) => (body ?? '').replace(/<!--[\s\S]*?-->/g, '').trim();

test('a monthly report from the template is published; the client sees only sections with content', async ({ as }) => {
  const { id: clientId, name: clientName } = need('client');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto('/agency/reports');
  await am.getByRole('button', { name: 'New report' }).click();
  const dialog = modal(am, 'New client report');
  await dialog.getByLabel('Client').selectOption({ label: clientName });
  await expect(dialog.getByLabel('Template')).toHaveValue('monthly-performance');
  await dialog.getByRole('button', { name: 'Create draft' }).click();
  await expect(am).toHaveURL(/\/agency\/reports\/[0-9a-f-]{36}/);
  const reportId = am.url().split('/').pop()!;

  const api = await login(accounts.am);
  const draft = await api.get<{ title: string; sections: Section[] }>(`/agency/reports/${reportId}`);
  const filled = draft.sections[0]!;
  // The client can't see a draft.
  const ownerApi = await login(clientUser('Owner'));
  expect(await statusOf(ownerApi.get(`/client/orgs/${clientId}/reports/${reportId}`))).toBe(404);

  const editor = am.getByRole('region', { name: filled.title });
  await editor.getByLabel('Text').fill(`Organic sessions grew on the back of the technical fixes (${runId()}).`);
  await editor.getByRole('button', { name: 'Add KPI' }).click();
  const kpi = editor.getByRole('group', { name: /^KPI / }).last();
  await kpi.getByLabel('Label').fill('Organic sessions');
  await kpi.getByLabel('Value').fill('12840');
  await kpi.getByLabel('Previous').fill('11020');
  await kpi.getByLabel('Source').fill('Google Analytics 4 (export)');
  await expect(am.getByRole('button', { name: 'Publish to client' })).toBeDisabled();
  await am.getByRole('button', { name: 'Save draft' }).click();
  await expect(am.getByText('You have unsaved changes.')).toHaveCount(0);
  await am.getByRole('button', { name: 'Publish to client' }).click();
  await modal(am, 'Publish this report?').getByRole('button', { name: 'Publish' }).click();
  await expect(am.getByText('Published', { exact: true }).first()).toBeVisible();
  await expect(am.getByRole('button', { name: 'Save draft' })).toHaveCount(0);

  // Published reports are read-only.
  const published = await api.get<{ title: string; sections: Section[]; concurrencyStamp: string }>(`/agency/reports/${reportId}`);
  expect(
    (await errorOf(api.put(`/agency/reports/${reportId}`, { title: 'Edited', sections: published.sections, concurrencyStamp: published.concurrencyStamp }))).status,
  ).toBe(409);
  errors.expectClean('building and publishing the report');

  const hidden = published.sections.filter((s) => s.kpis.length === 0 && !clientText(s.body));
  expect(hidden.length, 'the template leaves some sections empty').toBeGreaterThan(0);
  const owner = await as(clientUser('Owner'), landing.client);
  const ownerErrors = watchErrors(owner);
  await owner.goto('/client/reports');
  await owner.getByRole('link', { name: published.title }).click();
  const view = owner.getByRole('article', { name: published.title });
  await expect(view.getByRole('heading', { level: 2, name: filled.title })).toBeVisible();
  await expect(view).toContainText('Organic sessions');
  await expect(view).toContainText('Google Analytics 4 (export)');
  for (const s of hidden) await expect(view.getByRole('heading', { level: 2, name: s.title, exact: true })).toHaveCount(0);
  await expect(view).not.toContainText('<!--');
  const clientCopy = await ownerApi.get<{ sections: Section[] }>(`/client/orgs/${clientId}/reports/${reportId}`);
  expect(clientCopy.sections.map((s) => s.key)).not.toContain(hidden[0]!.key);
  // Home shows it as the latest report.
  await owner.goto('/client');
  await expect(owner.getByRole('region', { name: 'Latest report' })).toContainText(published.title);
  ownerErrors.expectClean('reading the published report');
});

interface BriefTemplate {
  key: string;
  name: string;
  fields: { key: string; label: string; type: string; required: boolean; options: string[] }[];
}

test('the Approver submits a brief; the Viewer cannot; staff convert it into tasks', async ({ as }) => {
  const { id: clientId } = need('client');
  const { name: projectName, id: projectId } = need('project');
  const approverApi = await login(clientUser('Approver'));
  const templates = await approverApi.get<BriefTemplate[]>(`/client/orgs/${clientId}/brief-templates`);
  const template = templates.find((t) => /blog/i.test(t.name)) ?? templates[0]!;
  const briefTitle = `Spring lighting guide ${runId()}`;

  // Missing required answers are refused per field.
  const missing = await errorOf(approverApi.post(`/client/orgs/${clientId}/briefs`, { templateKey: template.key, title: briefTitle, answers: {} }));
  expect(missing.status).toBe(400);
  // The Viewer can't submit.
  const viewerApi = await login(clientUser('Viewer'));
  expect(await statusOf(viewerApi.post(`/client/orgs/${clientId}/briefs`, { templateKey: template.key, title: briefTitle, answers: {} }))).toBe(403);
  const viewer = await as(clientUser('Viewer'), landing.client);
  await viewer.goto('/client/briefs');
  await expect(viewer.getByText('Approvers and Owners can submit briefs.')).toBeVisible();
  await expect(viewer.getByRole('button', { name: 'Submit a brief' })).toHaveCount(0);

  const approver = await as(clientUser('Approver'), landing.client);
  const errors = watchErrors(approver);
  await approver.goto('/client/briefs');
  await approver.getByRole('button', { name: 'Submit a brief' }).click();
  const dialog = modal(approver, 'Submit a brief');
  await dialog.getByLabel('Type of work').selectOption(template.key);
  await dialog.getByLabel('Title', { exact: true }).fill(briefTitle);
  for (const field of template.fields.filter((f) => f.required)) {
    const input = dialog.getByLabel(field.label, { exact: true });
    if (field.type === 'Select') await input.selectOption(field.options[0]!);
    else if (field.type === 'Date') await input.fill('2026-12-01');
    else if (field.type === 'Url') await input.fill('https://lumen-labs.example.com/blog');
    else await input.fill(`${field.label}: smart lighting for small flats.`);
  }
  await dialog.getByRole('button', { name: 'Submit brief' }).dblclick();
  await expect(dialog).toBeHidden();
  await expect(approver.getByRole('article', { name: briefTitle })).toHaveCount(1);
  errors.expectClean('submitting a brief');

  const am = await as(accounts.am, landing.agency);
  const amErrors = watchErrors(am);
  await am.goto(`/agency/clients/${clientId}?tab=briefs`);
  const card = am.getByRole('article', { name: briefTitle });
  await expect(card).toContainText('from the client');
  await card.getByLabel(`Status of ${briefTitle}`).selectOption('InReview');
  await expect(card.getByLabel(`Status of ${briefTitle}`)).toHaveValue('InReview');
  await card.getByRole('button', { name: 'Convert to tasks' }).click();
  const convert = modal(am, 'Convert brief into tasks');
  await convert.getByLabel('Project').selectOption({ label: projectName });
  await convert.getByLabel('Tasks').fill('Outline the spring lighting guide\nWrite the spring lighting guide');
  await convert.getByRole('button', { name: 'Convert' }).click();
  await expect(convert).toBeHidden();
  await expect(card).toContainText('Converted');
  const api = await login(accounts.am);
  const tasks = await api.get<{ title: string }[]>(`/agency/projects/${projectId}/tasks`);
  expect(tasks.map((t) => t.title)).toEqual(expect.arrayContaining(['Outline the spring lighting guide', 'Write the spring lighting guide']));
  await approver.reload();
  await expect(approver.getByRole('article', { name: briefTitle })).toContainText('In progress');
  amErrors.expectClean('converting the brief');
});

test('the Owner answers the NPS survey; staff see NPS and CSAT', async ({ as }) => {
  const { id: clientId } = need('client');
  const owner = await as(clientUser('Owner'), landing.client);
  const errors = watchErrors(owner);
  await owner.goto('/client/feedback');
  const survey = owner.getByRole('region', { name: 'Quarterly survey' });
  await survey.getByRole('radio', { name: '9' }).check();
  await survey.getByLabel("What's the main reason for your score?").fill('Responsive team.');
  await survey.getByRole('button', { name: 'Send feedback' }).click();
  // The page switches to the answered state for this quarter.
  await expect(owner.getByText(/with 9\/10/)).toBeVisible();
  await owner.reload();
  await expect(owner.getByText(/with 9\/10/)).toBeVisible();
  // Out-of-range and repeated answers are refused.
  const api = await login(clientUser('Owner'));
  expect(await statusOf(api.post(`/client/orgs/${clientId}/feedback/nps`, { score: 11 }))).toBe(400);
  expect(await statusOf(api.post(`/client/orgs/${clientId}/feedback/nps`, { score: 7 }))).toBeGreaterThanOrEqual(400);
  errors.expectClean('answering the survey');

  const am = await as(accounts.am, landing.agency);
  await am.goto(`/agency/clients/${clientId}?tab=feedback`);
  await expect(am.getByRole('group', { name: 'Average CSAT' })).toContainText('5.0 / 5');
  const recent = am.getByRole('list', { name: 'Recent feedback' });
  await expect(recent).toContainText('NPS 9/10');
  await expect(recent).toContainText('CSAT 5/5');
  await expect(recent).toContainText('Responsive team.');
});
