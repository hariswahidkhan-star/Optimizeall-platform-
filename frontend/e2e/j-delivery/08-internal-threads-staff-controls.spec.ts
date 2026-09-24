import {
  type ApiSession,
  accounts,
  clientUser,
  errorOf,
  expect,
  landing,
  login,
  modal,
  need,
  png,
  raw,
  runId,
  staffNames,
  statusOf,
  test,
  watchErrors,
} from './support/delivery';

/**
 * Staff-side controls around the client relationship: an internal (staff-only) thread the client's users never see
 * (not listed, 404 by id, no notification, file not served); client Viewer and Billing members read messages but
 * can't post; the account manager ticks and un-ticks a client-owned onboarding step on the client's behalf (the client
 * sees who did it); staff remove a brand asset and a task attachment (confirmed, permission-gated, file deleted); none of
 * these destructive or on-behalf actions work while an admin impersonates the account manager.
 */

const internalSubject = () => `Internal: pricing ${runId()}`;

interface ThreadSummary {
  id: string;
  subject: string;
  isInternal: boolean;
}

test('staff keep an internal thread that the client never sees', async ({ as }) => {
  const { id: clientId } = need('client');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/clients/${clientId}?tab=messages`);
  const compose = am.getByRole('form', { name: 'New conversation' });
  await compose.getByLabel('Subject').fill(internalSubject());
  await compose.getByLabel('Message').fill('They are price sensitive; hold the discount until the renewal.');
  await compose.getByLabel('Attachments').setInputFiles(png(81, 'margin-notes.png'));
  await compose.getByRole('checkbox', { name: /Internal/ }).check();
  await compose.getByRole('button', { name: 'Start conversation' }).click();
  await expect(am.getByText('Internal — not visible to the client')).toBeVisible();
  const item = am
    .getByRole('list', { name: 'Conversations' })
    .getByRole('listitem')
    .filter({ hasText: internalSubject() });
  await expect(item).toContainText('Internal');
  errors.expectClean('starting an internal thread');

  const amApi = await login(accounts.am);
  const staffThreads = await amApi.get<ThreadSummary[]>(`/agency/clients/${clientId}/threads`);
  const internal = staffThreads.find((t) => t.subject === internalSubject())!;
  expect(internal.isInternal).toBe(true);
  const detail = await amApi.get<{ messages: { attachments: { id: string }[] }[] }>(
    `/agency/clients/${clientId}/threads/${internal.id}`,
  );
  const fileId = detail.messages[0]!.attachments[0]!.id;

  // The client's users: not in the list (UI and API), 404 by id, can't reply, the file isn't served, no notification.
  const owner = await as(clientUser('Owner'), landing.client);
  const ownerErrors = watchErrors(owner);
  await owner.goto('/client/messages');
  await expect(owner.getByRole('list', { name: 'Conversations' })).toBeVisible();
  await expect(owner.getByRole('list', { name: 'Conversations' })).not.toContainText(internalSubject());
  ownerErrors.expectClean('the client message list');
  for (const duty of ['Owner', 'Approver', 'Billing', 'Viewer'] as const) {
    const member = await login(clientUser(duty));
    const list = await member.get<ThreadSummary[]>(`/client/orgs/${clientId}/threads`);
    expect(
      list.map((t) => t.subject),
      duty,
    ).not.toContain(internalSubject());
    expect(await statusOf(member.get(`/client/orgs/${clientId}/threads/${internal.id}`)), duty).toBe(404);
    expect(
      await statusOf(member.post(`/client/orgs/${clientId}/threads/${internal.id}/messages`, { body: 'hi' })),
      duty,
    ).toBe(404);
    expect((await raw(member, 'GET', `/client/orgs/${clientId}/files/${fileId}`)).status, duty).toBe(404);
    const home = await member.get<{ threads: ThreadSummary[] }>(`/client/orgs/${clientId}/home`);
    expect(
      home.threads.map((t) => t.id),
      duty,
    ).not.toContain(internal.id);
    const notifications = await member.get<{ items: { title: string }[] }>('/me/notifications?pageSize=100');
    expect(notifications.items.map((n) => n.title).join('\n'), duty).not.toContain(internalSubject());
  }
  // Another tenant can't reach it either, and a client can't create an internal thread.
  const nimbus = await login(accounts.nimbusOwner);
  expect(await statusOf(nimbus.get(`/client/orgs/${clientId}/threads/${internal.id}`))).toBe(404);
  const ownerApi = await login(clientUser('Owner'));
  expect(
    await errorOf(
      ownerApi.post(`/client/orgs/${clientId}/threads`, {
        subject: 'A question',
        body: 'Hello',
        isInternal: true,
      }),
    ),
  ).toEqual({
    status: 400,
    code: 'message.internal_not_allowed',
  });
});

test('Viewer and Billing members read messages but cannot post', async ({ as }) => {
  const { id: clientId } = need('client');
  const viewer = await as(clientUser('Viewer'), landing.client);
  const errors = watchErrors(viewer);
  await viewer.goto('/client/messages');
  await expect(viewer.getByText(/Your role is read-only here/)).toBeVisible();
  await expect(viewer.getByRole('form', { name: 'New conversation' })).toHaveCount(0);
  const first = viewer.getByRole('list', { name: 'Conversations' }).getByRole('button').first();
  await first.click();
  await expect(viewer.getByRole('list', { name: 'Messages, oldest first' })).toBeVisible();
  await expect(viewer.getByRole('form', { name: 'Reply' })).toHaveCount(0);
  errors.expectClean('reading messages as a Viewer');

  const approver = await login(clientUser('Approver'));
  const threads = await approver.get<ThreadSummary[]>(`/client/orgs/${clientId}/threads`);
  const target = threads[0]!;
  for (const duty of ['Viewer', 'Billing'] as const) {
    const member = await login(clientUser(duty));
    const read = await member.get<{ canReply: boolean }>(`/client/orgs/${clientId}/threads/${target.id}`);
    expect(read.canReply, duty).toBe(false);
    expect(
      await errorOf(member.post(`/client/orgs/${clientId}/threads/${target.id}/messages`, { body: 'x' })),
    ).toEqual({
      status: 403,
      code: 'client.insufficient_role',
    });
    expect(
      await statusOf(
        member.post(`/client/orgs/${clientId}/threads`, { subject: 'A question', body: 'Hello' }),
      ),
      duty,
    ).toBe(403);
  }
  // The Approver can.
  const replied = await approver.post<{ canReply: boolean; messages: unknown[] }>(
    `/client/orgs/${clientId}/threads/${target.id}/messages`,
    {
      body: `Approver reply ${runId()}`,
    },
  );
  expect(replied.canReply).toBe(true);
});

test('the account manager ticks and un-ticks a client step on the client’s behalf; the client sees who did it', async ({
  as,
}) => {
  const { id: clientId } = need('client');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/clients/${clientId}?tab=onboarding`);
  const checklist = am.getByRole('list', { name: 'Onboarding checklist' });
  const step = checklist
    .getByRole('listitem')
    .filter({ hasText: `Share the Meta Business Manager (${runId()})` });
  await expect(step).toContainText('Client action');
  const title = (await step.locator('.dl-list__title').innerText()).trim();
  await am.getByLabel(`Status of ${title}`).selectOption('Done');
  const confirm = modal(am, /done on the client’s behalf\?$/);
  await expect(confirm).toContainText('The client will see that you completed it for them');
  await confirm.getByRole('button', { name: 'Mark done for the client' }).click();
  await expect(step).toContainText(`by ${staffNames.am} on behalf of the client`);
  errors.expectClean('ticking a client step on their behalf');

  // The client's home names the step as done on their behalf, by whom.
  const owner = await as(clientUser('Owner'), landing.client);
  const doneForYou = owner.getByRole('list', { name: 'Steps done on your behalf' });
  await expect(doneForYou).toContainText(`${title} — marked done by ${staffNames.am}`);

  // Guarded: the plain status update refuses it, a strategist (no clients.manage) and the client can't use it.
  const api = await login(accounts.am);
  const onboarding = await api.get<{
    items: { id: string; title: string; completedOnBehalfOfClient: boolean }[];
  }>(`/agency/clients/${clientId}/onboarding`);
  const item = onboarding.items.find((i) => i.title === title)!;
  expect(item.completedOnBehalfOfClient).toBe(true);
  expect(
    await errorOf(api.put(`/agency/clients/${clientId}/onboarding/${item.id}`, { status: 'Done' })),
  ).toEqual({
    status: 400,
    code: 'onboarding.client_item',
  });
  const strategist = await login(accounts.strategist);
  expect(
    await statusOf(
      strategist.post(`/agency/clients/${clientId}/onboarding/${item.id}/on-behalf`, { done: false }),
    ),
  ).toBe(403);
  const ownerApi = await login(clientUser('Owner'));
  expect(
    await statusOf(
      ownerApi.post(`/agency/clients/${clientId}/onboarding/${item.id}/on-behalf`, { done: false }),
    ),
  ).toBe(403);

  // Un-tick on their behalf: it goes back to the client's to-do list.
  await am.getByLabel(`Status of ${title}`).selectOption('Pending');
  await modal(am, /not done again\?$/)
    .getByRole('button', { name: 'Mark not done' })
    .click();
  await expect(step).not.toContainText('on behalf of the client');
  await expect(am.getByLabel(`Status of ${title}`)).toHaveValue('Pending');
  await owner.reload();
  await expect(owner.getByRole('region', { name: 'Onboarding' })).toContainText(title);
  errors.expectClean('un-ticking a client step');
});

test('staff remove a brand asset and a task attachment; the files are deleted', async ({ as }) => {
  const { id: clientId } = need('client');
  const { id: projectId } = need('project');
  const internalTask = need('internalTask');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  const amApi = await login(accounts.am);

  // Brand asset: upload, then remove with confirmation.
  await am.goto(`/agency/clients/${clientId}?tab=brand`);
  const upload = am.getByRole('form', { name: 'Upload brand asset' });
  await upload.getByLabel('Upload an asset').setInputFiles(png(82, 'old-logo.png'));
  await upload.getByRole('button', { name: 'Upload', exact: true }).click();
  const assets = am.getByRole('list', { name: 'Brand assets' });
  await expect(assets).toContainText('old-logo.png');
  const kit = await amApi.get<{ assets: { id: string; label: string; fileId: string }[] }>(
    `/agency/clients/${clientId}/brand-kit`,
  );
  const asset = kit.assets.find((a) => a.label === 'old-logo.png')!;
  // Staff without clients.manage get no remove action (and a 403 from the API).
  const designer = await as(accounts.designerStaff, landing.agency);
  await designer.goto(`/agency/clients/${clientId}?tab=brand`);
  await expect(designer.getByRole('list', { name: 'Brand assets' })).toContainText('old-logo.png');
  await expect(designer.getByRole('button', { name: 'Remove old-logo.png' })).toHaveCount(0);
  const designerApi = await login(accounts.designerStaff);
  expect(await statusOf(designerApi.delete(`/agency/clients/${clientId}/brand-kit/assets/${asset.id}`))).toBe(
    403,
  );

  await am.getByRole('button', { name: 'Remove old-logo.png' }).click();
  await modal(am, /Remove “old-logo.png” from the brand kit\?$/)
    .getByRole('button', { name: 'Remove asset' })
    .click();
  await expect(assets).not.toContainText('old-logo.png');
  expect((await raw(amApi, 'GET', `/agency/files/${asset.fileId}`)).status, 'the file is deleted').toBe(404);
  expect(
    await statusOf(amApi.delete(`/agency/clients/${clientId}/brand-kit/assets/${asset.id}`)),
    'a second removal',
  ).toBe(404);
  const ownerKit = await (
    await login(clientUser('Owner'))
  ).get<{ assets: { id: string }[] }>(`/client/orgs/${clientId}/brand-kit`);
  expect(ownerKit.assets.map((a) => a.id)).not.toContain(asset.id);

  // Task attachment: attach a scratch file to the internal task, then remove it.
  await am.goto(`/agency/projects/${projectId}?task=${internalTask.id}`);
  const drawer = modal(am, internalTask.title);
  await drawer.getByLabel('Attach a file').setInputFiles(png(83, 'scratch.png'));
  await drawer.getByRole('button', { name: 'Upload', exact: true }).click();
  await expect(drawer.getByRole('img', { name: 'scratch.png' })).toBeVisible();
  const before = await amApi.get<{ attachments: { id: string; file: { id: string; fileName: string } }[] }>(
    `/agency/tasks/${internalTask.id}`,
  );
  const scratch = before.attachments.find((a) => a.file.fileName === 'scratch.png')!;
  await drawer.getByRole('button', { name: 'Remove attachment scratch.png' }).click();
  await modal(am, /Remove “scratch.png” from this task\?$/)
    .getByRole('button', { name: 'Remove attachment' })
    .click();
  await expect(drawer.getByRole('img', { name: 'scratch.png' })).toHaveCount(0);
  // The journey's other attachment stays.
  await expect(drawer.getByRole('img', { name: 'keyword-gap.png' })).toBeVisible();
  await am.reload();
  await expect(modal(am, internalTask.title).getByRole('img', { name: 'keyword-gap.png' })).toBeVisible();
  await expect(modal(am, internalTask.title).getByRole('img', { name: 'scratch.png' })).toHaveCount(0);
  expect((await raw(amApi, 'GET', `/agency/files/${scratch.file.id}`)).status, 'the file is deleted').toBe(
    404,
  );
  expect(
    await statusOf(amApi.delete(`/agency/tasks/${internalTask.id}/attachments/${scratch.id}`)),
    'a second removal',
  ).toBe(404);
  errors.expectClean('removing a brand asset and a task attachment');
});

test('while impersonating the account manager, nothing is removed or done on the client’s behalf', async () => {
  const { id: clientId } = need('client');
  const internalTask = need('internalTask');
  const amApi = await login(accounts.am);
  const admin = await login(accounts.admin);
  const started = await admin.post<{ accessToken: string }>(`/admin/users/${amApi.user.id}/impersonate`, {
    reason: `E2E ${runId()}: checking destructive actions are blocked`,
    confirm: true,
  });
  const asAm = { token: started.accessToken } as ApiSession;

  const onboarding = await amApi.get<{ items: { id: string; owner: string; status: string }[] }>(
    `/agency/clients/${clientId}/onboarding`,
  );
  const clientStep = onboarding.items.find((i) => i.owner === 'Client' && i.status === 'Pending')!;
  const kit = await amApi.get<{ assets: { id: string }[] }>(`/agency/clients/${clientId}/brand-kit`);
  const task = await amApi.get<{ attachments: { id: string }[] }>(`/agency/tasks/${internalTask.id}`);
  expect(kit.assets.length, 'the journey uploaded brand assets').toBeGreaterThan(0);

  const blocked = [
    await raw(asAm, 'POST', `/agency/clients/${clientId}/onboarding/${clientStep.id}/on-behalf`, {
      json: { done: true },
    }),
    await raw(asAm, 'DELETE', `/agency/clients/${clientId}/brand-kit/assets/${kit.assets[0]!.id}`),
    await raw(asAm, 'DELETE', `/agency/tasks/${internalTask.id}/attachments/${task.attachments[0]!.id}`),
  ];
  for (const r of blocked) {
    expect(r.status).toBe(403);
    expect((r.body as { code?: string }).code).toBe('auth.impersonation_forbidden_action');
  }
  // Reads still work while impersonating; nothing changed.
  expect((await raw(asAm, 'GET', `/agency/clients/${clientId}/onboarding`)).status).toBe(200);
  const after = await amApi.get<{ assets: { id: string }[] }>(`/agency/clients/${clientId}/brand-kit`);
  expect(after.assets.length).toBe(kit.assets.length);
  const stillPending = await amApi.get<{ items: { id: string; status: string }[] }>(
    `/agency/clients/${clientId}/onboarding`,
  );
  expect(stillPending.items.find((i) => i.id === clientStep.id)!.status).toBe('Pending');
});
