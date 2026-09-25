import { expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  address,
  as,
  call,
  codeOf,
  design,
  landing,
  mailsWith,
  modal,
  raw,
  recall,
  report,
  runSendJob,
  state,
  toast,
  watchErrors,
} from './support/email';

/**
 * Who may do what with the journey's email: client approval in the client portal (reject with a note → back to draft
 * → approve → sent), the client portal's tenancy (another organisation sees nothing), staff without email.send (author
 * only), staff without email.manage (no area at all), anonymous callers, and an admin viewing as the account manager
 * (reads and drafts only: the send to the audience is refused, 403 auth.impersonation_forbidden_action, in the API and UI).
 */
interface Campaign {
  id: string;
  name: string;
  status: string;
  approvalStatus: string;
  concurrencyStamp: string;
}

async function settings(requireClientApproval: boolean) {
  const current = await call<Record<string, unknown> & { concurrencyStamp: string }>(
    accounts.am,
    'GET',
    `/agency/email/settings?clientId=${state().client.id}`,
  );
  const res = await call(accounts.am, 'PUT', '/agency/email/settings', {
    ...current.body,
    clientAccountId: state().client.id,
    requireClientApproval,
  });
  expect(res.status, JSON.stringify(res.body)).toBe(200);
}

async function draft(name: string, by = accounts.am): Promise<Campaign> {
  const res = await call<Campaign>(by, 'POST', '/agency/email/campaigns', {
    clientAccountId: state().client.id,
    name,
    channel: 'Email',
    listId: recall('updatesId'),
    senderProfileId: recall('senderId'),
    subject: `${name} for {{first_name|you}}`,
    design: design(name, 'https://lumen.example/approval'),
  });
  expect(res.status, JSON.stringify(res.body)).toBe(200);
  return res.body;
}

const confirm = (c: Campaign, by = accounts.am) =>
  call<Campaign>(by, 'POST', `/agency/email/campaigns/${c.id}/send`, {
    confirm: true,
    confirmName: c.name,
    concurrencyStamp: c.concurrencyStamp,
  });

test('client approval: the owner requests changes, the agency edits, the owner approves, then it is sent', async ({
  browser,
}) => {
  test.setTimeout(4 * 60_000);
  await settings(true);
  const name = `Summer sale ${state().runId}`;
  let c = await draft(name);
  const confirmed = await confirm(c);
  expect(confirmed.body).toMatchObject({ status: 'Scheduled', approvalStatus: 'Pending' });
  // Not approved: the send job never starts it.
  await runSendJob();
  expect(mailsWith(address('barbara'), name)).toHaveLength(0);

  // Another organisation's owner cannot see or decide it.
  const other = await call(accounts.nimbusOwner, 'POST', `/client/email/campaigns/${c.id}/approval`, {
    approve: true,
  });
  expect(other.status).toBe(404);
  expect((await call(accounts.nimbusOwner, 'GET', `/client/email/campaigns/${c.id}/report`)).status).toBe(
    404,
  );
  const nimbusList = await call<{ items: { id: string }[] }>(
    accounts.nimbusOwner,
    'GET',
    '/client/email/campaigns?pageSize=100',
  );
  expect(nimbusList.body.items.some((i) => i.id === c.id)).toBe(false);

  // The Lumen owner requests changes in the portal.
  const owner = await actor(browser, state().owner, landing.client);
  const ownerErrors = watchErrors(owner);
  await owner.goto('/client/email');
  await expect(owner.getByRole('heading', { level: 1, name: 'Email marketing' })).toBeVisible();
  await owner.getByRole('table', { name: 'Campaigns awaiting approval' }).getByRole('link', { name }).click();
  await expect(owner.getByRole('heading', { level: 1, name })).toBeVisible();
  await expect(owner.frameLocator('iframe[title="Email preview"]').getByText(name).first()).toBeVisible();
  await owner.getByRole('button', { name: 'Request changes' }).click();
  const reject = modal(owner, 'Request changes?');
  await reject.getByLabel('What should change?').fill('Please mention the 20% discount in the subject line.');
  await reject.getByRole('button', { name: 'Request changes' }).click();
  await expect(toast(owner, 'Changes requested')).toBeVisible();
  // The campaign is the agency's draft again (hidden from the portal): the owner is taken back to the list.
  await expect(owner).toHaveURL(/\/client\/email$/);
  await expect(
    owner.getByRole('table', { name: 'Campaigns awaiting approval' }).getByRole('link', { name }),
  ).toHaveCount(0);
  ownerErrors.expectClean('requesting changes in the client portal');

  // Back at the agency: a draft again, with the client's note.
  const am = await actor(browser, accounts.am, landing.agency);
  const amErrors = watchErrors(am);
  await am.goto(`/agency/email/campaigns/${c.id}`);
  await expect(am.getByRole('alert').filter({ hasText: 'The client requested changes' })).toContainText(
    '20% discount',
  );
  const form = am.getByRole('form', { name: 'Campaign settings' });
  await form.getByLabel('Subject line', { exact: true }).fill(`20% off: ${name} for {{first_name|you}}`);
  await am.getByRole('button', { name: 'Save draft' }).click();
  await expect(toast(am, 'Campaign saved')).toBeVisible();
  await expect(am.getByRole('button', { name: 'Review & send' })).toBeEnabled();
  await am.getByRole('button', { name: 'Review & send' }).click();
  const send = modal(am, `Send “${name}”?`);
  await expect(send).toContainText('Required before the send job starts');
  await send.getByLabel(`Type ${name} to confirm`).fill(name);
  await send.getByRole('button', { name: 'Send now' }).click();
  await expect(toast(am, 'Waiting for client approval')).toBeVisible();
  amErrors.expectClean('re-sending after the client asked for changes');

  // The owner approves; approving twice is refused.
  await owner.goto('/client/email');
  await owner.getByRole('table', { name: 'Campaigns awaiting approval' }).getByRole('link', { name }).click();
  await owner.getByRole('button', { name: 'Approve', exact: true }).click();
  await modal(owner, 'Approve this campaign?').getByRole('button', { name: 'Approve' }).click();
  await expect(toast(owner, 'Campaign approved')).toBeVisible();
  expect(
    (await call(state().owner, 'POST', `/client/email/campaigns/${c.id}/approval`, { approve: true })).status,
  ).toBe(409);
  ownerErrors.expectClean('approving in the client portal');

  await runSendJob();
  await expect.poll(() => mailsWith(address('barbara'), `20% off: ${name}`).length).toBe(1);
  c = (await call<Campaign>(accounts.am, 'GET', `/agency/email/campaigns/${c.id}`)).body;
  expect(c).toMatchObject({ status: 'Sent', approvalStatus: 'Approved' });

  // The client sees the result in the portal (drafts never).
  await owner.goto('/client/email');
  const table = owner.getByRole('table', { name: 'Scheduled and sent campaigns' });
  await expect(table.getByRole('link', { name })).toBeVisible();
  await expect(table.getByRole('link', { name: `Spring launch ${state().runId}` })).toBeVisible();
  const portalCampaigns = await call<{ items: { name: string; status: string }[] }>(
    state().owner,
    'GET',
    '/client/email/campaigns?pageSize=100',
  );
  expect(portalCampaigns.body.items.every((i) => i.status !== 'Draft')).toBe(true);
  const portalReport = await call<{ sent: number; uniqueOpens: number }>(
    state().owner,
    'GET',
    `/client/email/campaigns/${recall('springId')}/report`,
  );
  const agencyReport = await report(recall('springId'));
  expect(portalReport.body).toMatchObject({ sent: agencyReport.sent, uniqueOpens: agencyReport.uniqueOpens });
  ownerErrors.expectClean('the client email portal');
  await settings(false);
});

test('content writer (email.manage without email.send): authors drafts but cannot send, pause or cancel', async ({
  browser,
}) => {
  const name = `Writer draft ${state().runId}`;
  const c = await draft(name, accounts.content);
  expect(c.status).toBe('Draft');
  const page = await actor(browser, accounts.content, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/campaigns/${c.id}`);
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
  await expect(page.getByText('Sending needs the email.send permission.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Review & send' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Save draft' })).toBeVisible();
  // No SMS area without sms.manage.
  await expect(
    page
      .getByRole('navigation', { name: 'Email marketing sections' })
      .getByRole('link', { name: 'SMS & WhatsApp' }),
  ).toHaveCount(0);
  errors.expectClean('a draft for a writer without email.send');

  const refused = await confirm(c, accounts.content);
  expect(refused.status).toBe(403);
  // The account manager confirms it; the writer still cannot pause or cancel it.
  const scheduled = (await confirm(c)).body;
  expect(scheduled.status).toBe('Scheduled');
  expect(
    (
      await call(accounts.content, 'POST', `/agency/email/campaigns/${c.id}/pause`, {
        concurrencyStamp: scheduled.concurrencyStamp,
      })
    ).status,
  ).toBe(403);
  expect(
    (
      await call(accounts.content, 'POST', `/agency/email/campaigns/${c.id}/cancel`, {
        concurrencyStamp: scheduled.concurrencyStamp,
      })
    ).status,
  ).toBe(403);
  expect((await call(accounts.content, 'GET', '/agency/email/sms/campaigns')).status).toBe(403);
  expect(
    (
      await call(accounts.am, 'POST', `/agency/email/campaigns/${c.id}/cancel`, {
        concurrencyStamp: scheduled.concurrencyStamp,
        reason: 'E2E',
      })
    ).status,
  ).toBe(200);
});

test('designer (no email permission), client users and anonymous callers are kept out', async ({
  browser,
}) => {
  const page = await actor(browser, accounts.designerStaff, landing.agency);
  const errors = watchErrors(page);
  await expect(
    page
      .getByRole('navigation', { name: 'Agency navigation' })
      .getByRole('link', { name: 'Email marketing' }),
  ).toHaveCount(0);
  await page.goto('/agency/email/campaigns');
  await expect(page.getByRole('heading', { level: 1 })).toContainText(
    /access|permission|not allowed|forbidden/i,
  );
  await expect(page.getByRole('heading', { level: 1, name: 'Email campaigns' })).toHaveCount(0);
  errors.ignore(/HTTP 403/);
  errors.expectClean('a designer on an email page');

  for (const path of [
    '/agency/email/lists',
    '/agency/email/campaigns',
    `/agency/email/campaigns/${recall('springId')}/report`,
    '/agency/email/suppressions',
  ]) {
    expect((await call(accounts.designerStaff, 'GET', path)).status, path).toBe(403);
    expect((await call(state().owner, 'GET', path)).status, `client on ${path}`).toBe(403);
    expect((await raw('GET', path)).status, `anonymous on ${path}`).toBe(401);
  }
  expect(
    (
      await call(accounts.designerStaff, 'POST', '/agency/email/lists', {
        clientAccountId: state().client.id,
        name: 'Nope',
      })
    ).status,
  ).toBe(403);
  // Staff have no client-portal email (the portal answers only to client members).
  expect((await call(accounts.designerStaff, 'GET', '/client/email/campaigns')).status).toBe(403);
});

const BLOCKED =
  'This action is not available while you are viewing as another user. Exit the impersonation session first.';

test('an admin viewing as the account manager: reads and drafts work, sending to the audience is refused (403 in the API and the UI)', async ({
  browser,
}) => {
  test.setTimeout(4 * 60_000);
  const admin = await as(accounts.admin);
  const users = await admin.get<{ items: { id: string; email: string }[] }>(
    `/admin/users?search=${encodeURIComponent(accounts.am.email)}`,
  );
  const amId = users.items.find((u) => u.email === accounts.am.email)!.id;
  const started = await raw<{ accessToken: string }>('POST', `/admin/users/${amId}/impersonate`, {
    token: admin.token,
    body: { reason: 'E2E: checking the email journey as Amira', confirm: true },
  });
  expect(started.status, JSON.stringify(started.body)).toBe(200);
  const token = started.body.accessToken;

  expect((await raw('GET', `/agency/email/lists?clientId=${state().client.id}`, { token })).status).toBe(200);
  // Not available to the account manager, and never while impersonating: the email provider choice and manual job runs.
  const provider = await raw('PUT', '/agency/email/settings/provider', {
    token,
    body: { clientAccountId: state().client.id, emailProvider: 'smtp', confirm: true },
  });
  expect(provider.status).toBe(403);
  const job = await raw('POST', '/admin/jobs/CampaignSendJob/run', { token });
  expect(job.status).toBe(403);

  const name = `Viewed-as draft ${state().runId}`;
  const created = await raw<Campaign>('POST', '/agency/email/campaigns', {
    token,
    body: {
      clientAccountId: state().client.id,
      name,
      channel: 'Email',
      listId: recall('updatesId'),
      senderProfileId: recall('senderId'),
      subject: 'As AM',
      design: design(name, 'https://lumen.example/as'),
    },
  });
  expect(created.status).toBe(200);
  // Sending (or scheduling) reaches the client's whole audience: refused while impersonating, like payouts.
  const sent = await raw<{ code?: string; title?: string }>(
    'POST',
    `/agency/email/campaigns/${created.body.id}/send`,
    {
      token,
      body: { confirm: true, confirmName: name, concurrencyStamp: created.body.concurrencyStamp },
    },
  );
  expect(`${sent.status} ${codeOf(sent)}`).toBe('403 auth.impersonation_forbidden_action');
  expect(sent.body.title).toBe(BLOCKED);
  // Nothing was confirmed: still a draft, no send_confirmed audit row; the refused request is on record for the session.
  const draftNow = await call<Campaign>(accounts.am, 'GET', `/agency/email/campaigns/${created.body.id}`);
  expect(draftNow.body.status).toBe('Draft');
  const confirmed = await admin.get<{ items: unknown[] }>(
    `/admin/audit-logs?action=email.campaign.send_confirmed&entityId=${created.body.id}`,
  );
  expect(confirmed.items).toHaveLength(0);
  const refused = await admin.get<{
    items: { actorUserId: string; impersonatorUserId: string | null; after: unknown }[];
  }>(`/admin/audit-logs?action=impersonation.request&entityId=${amId}&pageSize=50`);
  const refusedRow = refused.items.find((i) =>
    JSON.stringify(i.after).includes(`/agency/email/campaigns/${created.body.id}/send`),
  );
  expect(refusedRow).toMatchObject({ actorUserId: amId, impersonatorUserId: admin.user.id });
  expect(JSON.stringify(refusedRow!.after)).toContain('403');
  // The same in the browser: the admin logs in as the account manager and tries to send the draft.
  const page = await actor(browser, accounts.admin, landing.admin);
  const errors = watchErrors(page);
  await page.goto(`/admin/users/${amId}`);
  await page.getByRole('button', { name: 'Log in as' }).click();
  const login = page.getByRole('alertdialog', { name: /^Log in as / });
  await login.getByLabel(`Type ${accounts.am.email} to confirm`).fill(accounts.am.email);
  await login.getByLabel(/Why do you need to view this account/).fill('E2E: checking a send as Amira');
  await login.getByRole('button', { name: 'Log in as user' }).click();
  await expect(page).toHaveURL(landing.agency, { timeout: 60_000 });
  await expect(page.getByRole('region', { name: 'Impersonation' })).toContainText('You are viewing as');
  errors.ignore(/HTTP 403 POST /);
  await page.goto(`/agency/email/campaigns/${created.body.id}`);
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
  await page.getByRole('button', { name: 'Review & send' }).click();
  const send = modal(page, `Send “${name}”?`);
  await send.getByLabel(`Type ${name} to confirm`).fill(name);
  await send.getByRole('button', { name: 'Send now' }).click();
  await expect(send.getByRole('alert')).toContainText(BLOCKED);
  expect(
    (await call<Campaign>(accounts.am, 'GET', `/agency/email/campaigns/${created.body.id}`)).body.status,
  ).toBe('Draft');
  errors.expectClean('a refused send while viewing as the account manager');
  await send.getByRole('button', { name: 'Cancel' }).click();
  await expect(send).toBeHidden();
  await page.getByRole('region', { name: 'Impersonation' }).getByRole('button', { name: 'Exit' }).click();
  await expect(page).toHaveURL(/\/admin\/users$/);

  // The account manager (not impersonated) can still send it; cancel it again to leave the journey as it was.
  const own = await call<Campaign>(accounts.am, 'POST', `/agency/email/campaigns/${created.body.id}/send`, {
    confirm: true,
    confirmName: name,
    concurrencyStamp: draftNow.body.concurrencyStamp,
  });
  expect(own.status, JSON.stringify(own.body)).toBe(200);
  expect(
    (
      await call(accounts.am, 'POST', `/agency/email/campaigns/${created.body.id}/cancel`, {
        concurrencyStamp: own.body.concurrencyStamp,
        reason: 'E2E',
      })
    ).status,
  ).toBe(200);
});
