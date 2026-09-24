import { mkdirSync, writeFileSync } from 'node:fs';
import type { Route } from '@playwright/test';
import { join } from 'node:path';
import {
  MAX_UPLOAD_BYTES,
  accounts,
  clientUser,
  clients,
  expect,
  landing,
  login,
  need,
  png,
  raw,
  runId,
  sizedPng,
  statusOf,
  test,
  watchErrors,
} from './support/delivery';

/**
 * Negatives across the journey — tenancy (another client's organization, deliverables and files are 404 both ways), file
 * size limits, an upload interrupted by a reload, an expired session — and the agency home dashboard, whose numbers must
 * match the underlying data.
 */

test('tenancy: the new client and Nimbus Fitness never see each other’s work', async ({ as }) => {
  const { id: lumenId } = need('client');
  const { id: deliverableId } = need('deliverable');
  const { id: projectId } = need('project');
  const amApi = await login(accounts.am);
  const all = await amApi.get<{ items: { id: string; clientId: string; clientName: string }[] }>('/agency/deliverables?pageSize=100');
  const nimbus = all.items.find((d) => d.clientName === clients.nimbus.name)!;
  expect(nimbus, 'the demo seed has Nimbus deliverables').toBeDefined();

  const lumenOwner = await login(clientUser('Owner'));
  const orgs = await lumenOwner.get<{ clientId: string }[]>('/client/orgs');
  expect(orgs.map((o) => o.clientId)).toEqual([lumenId]);
  for (const path of [
    `/client/orgs/${nimbus.clientId}/home`,
    `/client/orgs/${nimbus.clientId}/deliverables`,
    `/client/orgs/${nimbus.clientId}/threads`,
    `/client/orgs/${lumenId}/deliverables/${nimbus.id}`,
  ])
    expect(await statusOf(lumenOwner.get(path)), path).toBe(404);

  const nimbusApprover = await login(accounts.nimbusApprover);
  for (const path of [
    `/client/orgs/${lumenId}/deliverables/${deliverableId}`,
    `/client/orgs/${lumenId}/projects/${projectId}`,
    `/client/orgs/${lumenId}/brand-kit`,
  ])
    expect(await statusOf(nimbusApprover.get(path)), path).toBe(404);
  const nimbusOrg = (await nimbusApprover.get<{ clientId: string }[]>('/client/orgs'))[0]!.clientId;
  expect(await statusOf(nimbusApprover.get(`/client/orgs/${nimbusOrg}/deliverables/${deliverableId}`))).toBe(404);
  expect(await statusOf(nimbusApprover.post(`/client/orgs/${lumenId}/deliverables/${deliverableId}/approve`, { version: 2 }))).toBe(404);
  // Lumen's files are not served through Nimbus's organization either.
  const lumenFiles = await amApi.get<{ file: { id: string } }[]>(`/agency/projects/${projectId}/files`);
  expect(lumenFiles.length).toBeGreaterThan(0);
  expect((await raw(nimbusApprover, 'GET', `/client/orgs/${nimbusOrg}/files/${lumenFiles[0]!.file.id}`)).status).toBe(404);

  // In the UI: the Lumen user sees only Lumen work.
  const owner = await as(clientUser('Owner'), landing.client);
  const errors = watchErrors(owner);
  await owner.goto('/client/approvals');
  await owner.getByRole('tab', { name: 'All' }).click();
  await expect(owner.getByRole('list', { name: 'Deliverables' })).toContainText(`Lumen homepage hero ${runId()}`);
  await expect(owner.getByText('Ramadan campaign post set')).toHaveCount(0);
  errors.ignore(new RegExp(`HTTP 404 GET \\S+/deliverables/${nimbus.id}$`));
  await owner.goto(`/client/approvals/${nimbus.id}`);
  await expect(owner.getByRole('region', { name: 'Your decision' })).toHaveCount(0);
  errors.expectClean('the client portal across tenants');
});

test('file limits: 50 MB is the maximum, over it is refused in the API and in the composer', async ({ as }, testInfo) => {
  const { id: clientId } = need('client');
  const api = await login(clientUser('Approver'));
  const over = sizedPng(MAX_UPLOAD_BYTES + 1, 'too-big.png');
  const form = new FormData();
  form.append('file', new Blob([over.buffer], { type: 'image/png' }), over.name);
  const refused = await raw(api, 'POST', `/client/orgs/${clientId}/files`, { form });
  expect(refused.status).toBe(400);
  expect(JSON.stringify(refused.body)).toContain('50 MB');
  const empty = new FormData();
  empty.append('file', new Blob([], { type: 'image/png' }), 'empty.png');
  expect((await raw(api, 'POST', `/client/orgs/${clientId}/files`, { form: empty })).status).toBe(400);

  // The composer tells the user a file is too large instead of silently sending the message without it.
  const dir = testInfo.outputPath('uploads');
  mkdirSync(dir, { recursive: true });
  const bigPath = join(dir, 'campaign-video.png');
  writeFileSync(bigPath, over.buffer);
  const approver = await as(clientUser('Approver'), landing.client);
  const errors = watchErrors(approver);
  await approver.goto('/client/messages');
  const compose = approver.getByRole('form', { name: 'New conversation' });
  const subject = `Big file ${runId()}`;
  await compose.getByLabel('Subject').fill(subject);
  await compose.getByLabel('Message').fill('Video attached.');
  await compose.getByLabel('Attachments').setInputFiles(bigPath);
  await expect(compose.getByText(/campaign-video\.png is larger than 50 MB/)).toBeVisible();
  await expect(compose.getByRole('button', { name: 'Start conversation' })).toBeDisabled();
  await compose.getByLabel('Attachments').setInputFiles(png(51, 'still.png'));
  await expect(compose.getByText(/larger than 50 MB/)).toHaveCount(0);
  await compose.getByRole('button', { name: 'Start conversation' }).click();
  await expect(approver.getByRole('list', { name: 'Messages, oldest first' }).getByRole('img', { name: 'still.png' })).toBeVisible();
  errors.expectClean('the composer’s file limit');
});

test('an upload interrupted by a reload leaves nothing behind and can be redone', async ({ as }) => {
  const { id: clientId } = need('client');
  const owner = await as(clientUser('Owner'), landing.client);
  const api = await login(clientUser('Owner'));
  const before = await api.get<{ subject: string }[]>(`/client/orgs/${clientId}/threads`);
  const subject = `Interrupted ${runId()}`;
  await owner.goto('/client/messages');
  // Hold the file upload so the reload happens mid-upload.
  let held: Route | null = null;
  await owner.route('**/api/v1/client/orgs/*/files', (route) => {
    held = route; // not answered: the reload cancels it
  });
  const compose = owner.getByRole('form', { name: 'New conversation' });
  await compose.getByLabel('Subject').fill(subject);
  await compose.getByLabel('Message').fill('Logo pack attached.');
  await compose.getByLabel('Attachments').setInputFiles(png(52, 'logo-pack.png'));
  await compose.getByRole('button', { name: 'Start conversation' }).click();
  await expect.poll(() => held !== null).toBe(true);
  const reloading = owner.reload();
  // The browser drops the in-flight upload when the page goes away.
  await (held as Route | null)?.abort('aborted').catch(() => undefined);
  await reloading;
  await owner.unroute('**/api/v1/client/orgs/*/files');
  const after = await api.get<{ subject: string }[]>(`/client/orgs/${clientId}/threads`);
  expect(after.length, 'no half-sent conversation').toBe(before.length);
  // The form is fresh; sending again works once.
  await expect(compose.getByLabel('Subject')).toHaveValue('');
  await compose.getByLabel('Subject').fill(subject);
  await compose.getByLabel('Message').fill('Logo pack attached.');
  await compose.getByLabel('Attachments').setInputFiles(png(52, 'logo-pack.png'));
  await compose.getByRole('button', { name: 'Start conversation' }).click();
  await expect(owner.getByRole('list', { name: 'Messages, oldest first' }).getByRole('img', { name: 'logo-pack.png' })).toBeVisible();
  const final = await api.get<{ subject: string }[]>(`/client/orgs/${clientId}/threads`);
  expect(final.filter((t) => t.subject === subject)).toHaveLength(1);
});

test('an expired session sends the user to sign in and back to the page they were on', async ({ as }) => {
  const owner = await as(clientUser('Owner'), landing.client);
  const errors = watchErrors(owner);
  await owner.goto('/client/briefs');
  await expect(owner.getByRole('heading', { level: 1, name: 'Briefs' })).toBeVisible();
  // The refresh cookie is gone and the access token is rejected: the session is over.
  await owner.context().clearCookies();
  await owner.route('**/api/v1/client/**', (route) =>
    route.fulfill({ status: 401, contentType: 'application/json', body: '{"title":"Unauthorized"}' }),
  );
  errors.ignore(/HTTP 401 /);
  errors.ignore(/console: .*401/);
  await owner.getByRole('navigation').getByRole('link', { name: 'Reports' }).first().click();
  await expect(owner).toHaveURL(/\/login/);
  await expect(owner.getByText(/session has expired/i)).toBeVisible();
  await owner.unroute('**/api/v1/client/**');
  await owner.getByLabel('Email', { exact: true }).fill(clientUser('Owner').email);
  await owner.getByLabel('Password', { exact: true }).fill(clientUser('Owner').password);
  await owner.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(owner).toHaveURL(/\/client\/reports/);
  await expect(owner.getByRole('heading', { level: 1, name: 'Reports' })).toBeVisible();
  errors.expectClean('the expired session');
});

test('agency home dashboard numbers match the data', async ({ as }) => {
  const { id: projectId } = need('project');
  const api = await login(accounts.am);
  // More deliverables awaiting review than the dashboard lists (it shows 10), so the count must not come from the list.
  const current = await api.get<{ total: number }>('/agency/deliverables?view=review&pageSize=1');
  for (let i = current.total; i < 12; i++) {
    const d = await api.post<{ deliverable: { id: string } }>('/agency/deliverables', { projectId, title: `Review load ${runId()} #${i}`, type: 'Copy' });
    const form = new FormData();
    form.append('body', `Draft copy ${i}`);
    await api.upload(`/agency/deliverables/${d.deliverable.id}/versions`, form);
    await api.post(`/agency/deliverables/${d.deliverable.id}/submit`, { version: 1 });
  }
  const review = await api.get<{ total: number }>('/agency/deliverables?view=review&pageSize=1');
  const open = await api.get<unknown[]>('/agency/tasks/mine?filter=open');
  const overdue = await api.get<unknown[]>('/agency/tasks/mine?filter=overdue');
  const week = await api.get<{ totalMinutes: number }>('/agency/time/timesheets/week');

  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto('/agency');
  const stat = (name: string) => am.getByRole('group', { name });
  await expect(stat('Open tasks')).toContainText(String(open.length));
  await expect(stat('Overdue')).toContainText(String(overdue.length));
  await expect(stat('Awaiting my review')).toContainText(String(review.total));
  const minutes = week.totalMinutes;
  const time = `${Math.floor(minutes / 60)}h${minutes % 60 ? ` ${minutes % 60}m` : ''}`;
  await expect(stat('My time this week')).toContainText(time);
  await expect(am.getByRole('list', { name: 'Deliverables awaiting my review' }).getByRole('listitem')).toHaveCount(10);
  const pending = await api.get<{ total: number }>('/agency/deliverables?view=client&pageSize=1');
  expect(pending.total).toBeGreaterThan(0);
  errors.expectClean('the agency dashboard');
});
