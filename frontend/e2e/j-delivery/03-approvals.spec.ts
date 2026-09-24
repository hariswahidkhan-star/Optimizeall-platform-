import {
  type Duty,
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
  saveState,
  statusOf,
  test,
  watchErrors,
} from './support/delivery';

/**
 * Deliverable approvals: version 1 (a file) → internal review → sent to the client → the client Approver requests
 * changes → version 2 → internal review → sent → Viewer and Billing can't decide (UI and API 403) → the Approver
 * approves version 2 (a stale decision on version 1 is refused) → the approved version is locked → the client rates it.
 */

const title = () => `Lumen homepage hero ${runId()}`;

test('staff prepare version 1 and send it to the client after internal review', async ({ as }) => {
  const { id: projectId } = need('project');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/projects/${projectId}?tab=deliverables`);
  await am.getByRole('button', { name: 'New deliverable' }).click();
  const dialog = modal(am, 'New deliverable');
  await dialog.getByLabel('Title').fill(title());
  await dialog.getByLabel('Type').selectOption('Design');
  await dialog.getByRole('button', { name: 'Create' }).dblclick();
  await expect(dialog).toBeHidden();
  const list = am.getByRole('list', { name: 'Deliverables' });
  await expect(list.getByRole('link', { name: title() })).toHaveCount(1);
  await list.getByRole('link', { name: title() }).click();
  await expect(am.getByRole('heading', { level: 1, name: title() })).toBeVisible();
  const id = am.url().split('/').pop()!;
  saveState({ deliverable: { id, title: title() } });

  const version = am.getByRole('region', { name: 'New version' });
  await version.getByLabel('File').setInputFiles(png(31, 'hero-v1.png'));
  await version.getByLabel('Notes for reviewers').fill('First pass, warm palette.');
  await version.getByRole('button', { name: 'Add version' }).click();
  await expect(am.getByText(/Design · v1/)).toBeVisible();

  // The client can't see an unsent version (neither the deliverable nor its file).
  const { id: clientId } = need('client');
  const approverApi = await login(clientUser('Approver'));
  expect(await statusOf(approverApi.get(`/client/orgs/${clientId}/deliverables/${id}`))).toBe(404);

  const review = am.getByRole('region', { name: 'Review' });
  // Double-clicking a workflow action sends it once (no "invalid transition" error for the second click).
  await review.getByRole('button', { name: 'Submit for internal review' }).dblclick();
  await expect(am.getByText('Internal review', { exact: true }).first()).toBeVisible();
  await expect(am.getByRole('alert')).toHaveCount(0);
  await review.getByLabel('Comment').fill('Internal: check contrast on the CTA.');
  await review.getByRole('button', { name: 'Add comment' }).click();
  await review.getByRole('button', { name: 'Approve & send to client' }).click();
  await expect(am.getByText('Awaiting client', { exact: true }).first()).toBeVisible();
  errors.expectClean('sending version 1 to the client');
});

test('the client Approver requests changes; staff send version 2', async ({ as }) => {
  const { id } = need('deliverable');
  const approver = await as(clientUser('Approver'), landing.client);
  const errors = watchErrors(approver);
  const awaiting = approver.getByRole('list', { name: 'Deliverables awaiting your approval' });
  await awaiting.getByRole('link', { name: title() }).click();
  await expect(approver).toHaveURL(new RegExp(`/client/approvals/${id}`));
  // Internal comments never reach the client.
  await expect(approver.getByText('Internal: check contrast on the CTA.')).toHaveCount(0);
  const decision = approver.getByRole('region', { name: 'Your decision' });
  await expect(decision.getByRole('button', { name: 'Request changes' })).toBeDisabled();
  await decision.getByLabel('Comment').fill('Please use the cooler palette from the brand kit.');
  await decision.getByRole('button', { name: 'Request changes' }).click();
  await expect(approver.getByRole('region', { name: 'History' })).toContainText('Please use the cooler palette');
  errors.expectClean('requesting changes');

  const am = await as(accounts.am, landing.agency);
  const amErrors = watchErrors(am);
  await am.goto(`/agency/deliverables/${id}`);
  await expect(am.getByText('Changes requested', { exact: true }).first()).toBeVisible();
  const version = am.getByRole('region', { name: 'New version' });
  await version.getByLabel('File').setInputFiles(png(32, 'hero-v2.png'));
  await version.getByLabel('Notes for reviewers').fill('Cooler palette.');
  await version.getByRole('button', { name: 'Add version' }).click();
  await expect(am.getByText(/Design · v2/)).toBeVisible();
  const review = am.getByRole('region', { name: 'Review' });
  await review.getByRole('button', { name: 'Submit for internal review' }).click();
  await review.getByRole('button', { name: 'Approve & send to client' }).click();
  await expect(am.getByText('Awaiting client', { exact: true }).first()).toBeVisible();
  amErrors.expectClean('sending version 2');
});

test('Viewer and Billing members cannot approve; the Approver approves version 2', async ({ as }) => {
  const { id } = need('deliverable');
  const { id: clientId } = need('client');
  for (const duty of ['Viewer', 'Billing'] as Duty[]) {
    const page = await as(clientUser(duty), landing.client);
    await page.goto(`/client/approvals/${id}`);
    const decision = page.getByRole('region', { name: 'Your decision' });
    await expect(decision.getByText('View only')).toBeVisible();
    await expect(decision.getByRole('button', { name: /Approve/ })).toHaveCount(0);
    const api = await login(clientUser(duty));
    for (const action of ['approve', 'request-changes'])
      expect(
        await statusOf(api.post(`/client/orgs/${clientId}/deliverables/${id}/${action}`, { version: 2, comment: 'Trying anyway' })),
        `${duty} ${action}`,
      ).toBe(403);
  }

  const approver = await as(clientUser('Approver'), landing.client);
  const errors = watchErrors(approver);
  // A decision on the outdated version 1 is refused.
  const api = await login(clientUser('Approver'));
  expect((await errorOf(api.post(`/client/orgs/${clientId}/deliverables/${id}/approve`, { version: 1 }))).status).toBe(409);

  await approver.goto(`/client/approvals/${id}`);
  const decision = approver.getByRole('region', { name: 'Your decision' });
  await decision.getByLabel('Comment').fill('Perfect, ship it.');
  await decision.getByRole('button', { name: 'Approve version 2' }).dblclick();
  await expect(approver.getByText(/Version 2 was approved by/)).toBeVisible();
  await expect(approver.getByRole('alert')).toHaveCount(0);
  // A second approver acting afterwards: only the first decision counts.
  expect((await errorOf(api.post(`/client/orgs/${clientId}/deliverables/${id}/approve`, { version: 2 }))).status).toBe(409);

  // CSAT rating.
  const rate = approver.getByRole('form', { name: 'Rate this deliverable' });
  await rate.getByRole('radio', { name: '5' }).check();
  await rate.getByLabel('Anything we could do better?').fill('Fast turnaround.');
  await rate.getByRole('button', { name: 'Send rating' }).click();
  await expect(approver.getByText('Thanks for rating this deliverable.')).toBeVisible();
  errors.expectClean('approving version 2');
});

test('the approved version is locked for staff', async ({ as }) => {
  const { id } = need('deliverable');
  const am = await as(accounts.am, landing.agency);
  const errors = watchErrors(am);
  await am.goto(`/agency/deliverables/${id}`);
  await expect(am.getByText('Approved by the client')).toBeVisible();
  await expect(am.getByText('Approved versions are locked')).toBeVisible();
  await expect(am.getByRole('region', { name: 'New version' })).toHaveCount(0);
  const api = await login(accounts.am);
  const form = new FormData();
  form.append('linkUrl', 'https://example.com/sneaky-v3');
  expect((await raw(api, 'POST', `/agency/deliverables/${id}/versions`, { form })).status).toBe(409);
  const detail = await api.get<{ deliverable: { status: string; currentVersion: number }; approvedVersion: number }>(`/agency/deliverables/${id}`);
  expect(detail.deliverable.status).toBe('Approved');
  expect(detail.approvedVersion).toBe(2);
  expect(detail.deliverable.currentVersion).toBe(2);
  // Once the client has seen it, it can't be deleted.
  expect((await raw(api, 'DELETE', `/agency/deliverables/${id}?concurrencyStamp=00000000-0000-0000-0000-000000000000`)).status).toBeGreaterThanOrEqual(400);
  const after = await api.get<{ deliverable: { status: string } }>(`/agency/deliverables/${id}`);
  expect(after.deliverable.status).toBe('Approved');
  await am.getByRole('region', { name: 'Review' }).getByRole('button', { name: 'Mark published / delivered' }).click();
  await expect(am.getByText('Published', { exact: true }).first()).toBeVisible();
  errors.expectClean('the locked approved deliverable');
});
