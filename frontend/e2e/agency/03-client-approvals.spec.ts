import { expect, test } from '@playwright/test';
import { ApiSession, accounts, actor, clients, landing, modal, runId, watchErrors } from './support/agency';

/**
 * Delivery approvals across the agency and the client portal:
 *   the account manager creates a project for Nimbus Fitness and a deliverable, adds a version, submits it for internal
 *   review and approves it to the client → the Nimbus Approver signs in to the client portal, sees only Nimbus work
 *   (no org switcher, another client's deliverable is not reachable) and approves → staff sees "Approved".
 */
test('account manager sends a deliverable to the client; the Nimbus approver approves it', async ({
  browser,
}) => {
  const id = runId();
  const projectName = `E2E launch ${id}`;
  const deliverableTitle = `Launch hero banner ${id}`;

  // ---------------------------------------------------------------- staff: project + deliverable
  const am = await actor(browser, accounts.am, landing.agency);
  const amErrors = watchErrors(am);
  await am
    .getByRole('navigation', { name: 'Agency navigation' })
    .getByRole('link', { name: 'Projects' })
    .click();
  await expect(am.getByRole('heading', { level: 1, name: 'Projects' })).toBeVisible();
  await am.getByRole('button', { name: 'New project' }).click();
  const projectDialog = modal(am, 'New project');
  await projectDialog.getByLabel('Client').selectOption({ label: clients.nimbus.name });
  await projectDialog.getByLabel('Name').fill(projectName);
  await projectDialog.getByRole('button', { name: 'Create project' }).click();
  await expect(am).toHaveURL(/\/agency\/projects\/[0-9a-f-]{36}/);
  await expect(am.getByRole('heading', { level: 1, name: projectName })).toBeVisible();

  await am.getByRole('tab', { name: 'Deliverables' }).click();
  await am.getByRole('button', { name: 'New deliverable' }).click();
  const deliverableDialog = modal(am, 'New deliverable');
  await deliverableDialog.getByLabel('Title').fill(deliverableTitle);
  await deliverableDialog.getByLabel('Type').selectOption('Design');
  await deliverableDialog.getByRole('button', { name: 'Create' }).click();
  await expect(deliverableDialog).toBeHidden();
  await am.getByRole('list', { name: 'Deliverables' }).getByRole('link', { name: deliverableTitle }).click();
  await expect(am.getByRole('heading', { level: 1, name: deliverableTitle })).toBeVisible();
  const deliverableUrl = am.url();
  const deliverableId = deliverableUrl.split('/').pop()!;

  const newVersion = am.getByRole('region', { name: 'New version' });
  await newVersion.getByLabel('Link').fill('https://www.figma.com/file/e2e-launch-hero');
  await newVersion
    .getByLabel('Notes for reviewers')
    .fill('Two colourways; the brief asked for the bold one.');
  await newVersion.getByRole('button', { name: 'Add version' }).click();
  await expect(am.getByText(/Design · v1/)).toBeVisible();

  const review = am.getByRole('region', { name: 'Review' });
  await review.getByRole('button', { name: 'Submit for internal review' }).click();
  await expect(am.getByText('Internal review', { exact: true }).first()).toBeVisible();
  await review.getByRole('button', { name: 'Approve & send to client' }).click();
  await expect(am.getByText('Awaiting client', { exact: true }).first()).toBeVisible();
  amErrors.expectClean('the account manager delivery journey');

  // ---------------------------------------------------------------- client portal: only their organization
  const approver = await actor(browser, accounts.nimbusApprover, landing.client);
  const clientErrors = watchErrors(approver);
  await expect(approver.getByRole('heading', { level: 1, name: 'Welcome back' })).toBeVisible();
  await expect(approver.getByRole('main').getByText(clients.nimbus.name).first()).toBeVisible();
  // One organization: no switcher.
  await expect(approver.getByLabel('Organization')).toHaveCount(0);

  const awaiting = approver.getByRole('list', { name: 'Deliverables awaiting your approval' });
  await expect(awaiting.getByRole('link', { name: deliverableTitle })).toBeVisible();
  await approver.getByRole('link', { name: 'All approvals' }).click();
  const list = approver.getByRole('list', { name: 'Deliverables' });
  await expect(list.getByRole('link', { name: deliverableTitle })).toBeVisible();
  // Aurora Skincare's pending deliverable (demo seed) never shows up for a Nimbus user…
  await expect(approver.getByText('Ramadan campaign post set')).toHaveCount(0);
  // …and neither Aurora's organization nor its deliverable can be reached directly.
  const amApi = await ApiSession.login(accounts.am.email, accounts.am.password);
  const all = await amApi.get<{ items: { id: string; clientId: string; clientName: string }[] }>(
    '/agency/deliverables?pageSize=100',
  );
  const aurora = all.items.find((d) => d.clientName === clients.aurora.name);
  expect(aurora, 'the demo seed has an Aurora Skincare deliverable').toBeDefined();
  const approverApi = await ApiSession.login(accounts.nimbusApprover.email, accounts.nimbusApprover.password);
  const orgs = await approverApi.get<{ clientId: string; name: string }[]>('/client/orgs');
  expect(orgs.map((o) => o.name)).toEqual([clients.nimbus.name]);
  const status = (p: Promise<unknown>) =>
    p.then(
      () => 200,
      (e: { status?: number }) => e.status ?? 0,
    );
  expect([403, 404]).toContain(
    await status(approverApi.get(`/client/orgs/${aurora!.clientId}/deliverables`)),
  );
  expect([403, 404]).toContain(
    await status(approverApi.get(`/client/orgs/${orgs[0]!.clientId}/deliverables/${aurora!.id}`)),
  );
  clientErrors.ignore(new RegExp(`HTTP 404 GET \\S+/deliverables/${aurora!.id}$`));
  await approver.goto(`/client/approvals/${aurora!.id}`);
  await expect(approver.getByRole('heading', { level: 1, name: 'Review deliverable' })).toBeVisible();
  await expect(approver.getByRole('region', { name: 'Your decision' })).toHaveCount(0);
  await approver.goto('/client/approvals');

  // Approve.
  await list.getByRole('link', { name: deliverableTitle }).click();
  await expect(approver.getByText('Awaiting your review').first()).toBeVisible();
  const decision = approver.getByRole('region', { name: 'Your decision' });
  await decision.getByLabel('Comment').fill('Looks great — go with the bold colourway.');
  await decision.getByRole('button', { name: 'Approve version 1' }).click();
  await expect(approver.getByText(/Version 1 was approved by/)).toBeVisible();
  clientErrors.expectClean('the client approval journey');

  // ---------------------------------------------------------------- staff sees the approval
  await am.goto(deliverableUrl);
  await expect(am.getByText('Approved by the client')).toBeVisible();
  await expect(am.getByText(/Version 1 approved by/)).toBeVisible();
  await expect(
    am.getByRole('region', { name: 'Review' }).getByRole('button', { name: 'Mark published / delivered' }),
  ).toBeVisible();
  const detail = await amApi.get<{ deliverable: { status: string } }>(
    `/agency/deliverables/${deliverableId}`,
  );
  expect(detail.deliverable.status).toBe('Approved');
});
