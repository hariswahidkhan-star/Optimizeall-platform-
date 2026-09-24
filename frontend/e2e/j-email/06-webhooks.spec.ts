import { expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  address,
  call,
  codeOf,
  design,
  landing,
  mailgunEvent,
  mailsWith,
  modal,
  openEmail,
  raw,
  recall,
  remember,
  report,
  runSendJob,
  state,
  subscriberByEmail,
  suppressions,
  toast,
  watchErrors,
} from './support/email';

/**
 * Provider webhooks for the journey's client: the admin connects Mailgun on the Integrations page (with the webhook
 * signing key), then a campaign's delivery, hard-bounce and complaint events arrive signed. Events reach the report and
 * the suppression list; bad or stale signatures, unconfigured providers and unknown workspaces are refused; duplicate
 * deliveries of the same event are applied once.
 */
const SIGNING_KEY = `mg-webhook-signing-${Date.now().toString(36)}`;
const webhook = () => `/api/v1/public/email/webhooks/mailgun/${state().client.id}`;

test('the admin connects Mailgun with its webhook signing key on the Integrations page', async ({
  browser,
}) => {
  // Before: nothing configured for the workspace → every event is refused (503), none applied.
  const before = await raw('POST', webhook(), {
    body: mailgunEvent('whatever', { event: 'delivered', recipient: address('ada'), id: 'early' }),
  });
  expect(before.status).toBe(503);
  expect(codeOf(before)).toBe('email.webhook_not_configured');

  const page = await actor(browser, accounts.admin, landing.admin);
  const errors = watchErrors(page);
  await page.goto('/agency/integrations');
  await expect(page.getByRole('heading', { level: 1, name: 'Integrations' })).toBeVisible();
  await page.getByLabel('Connections for').selectOption({ label: state().client.name });
  await page.getByRole('button', { name: 'Connect Mailgun' }).click();
  const dialog = modal(page, 'Connect Mailgun');
  await dialog.getByLabel('Connection name').fill(`Lumen Mailgun ${state().runId}`);
  await dialog.getByLabel('Sending domain').fill('mg.lumen.example');
  await dialog.getByLabel('Region (us or eu)').fill('eu');
  await dialog.getByLabel('From address').fill(recall('senderEmail'));
  await dialog.getByLabel('API key', { exact: true }).fill('key-e2e-not-used');
  // The webhook signing key is what verifies bounce/complaint events (it used to be impossible to enter).
  await dialog.getByLabel(/^HTTP webhook signing key/).fill(SIGNING_KEY);
  await dialog.getByRole('button', { name: 'Save connection' }).click();
  await expect(toast(page, 'Mailgun connected')).toBeVisible();
  await expect(dialog).toBeHidden();
  errors.expectClean('connecting Mailgun on the integrations page');

  const connections = await call<
    {
      id: string;
      provider: string;
      settings: Record<string, string>;
      secrets: { key: string; saved: boolean }[];
    }[]
  >(accounts.admin, 'GET', `/agency/integrations/connections?clientId=${state().client.id}&provider=mailgun`);
  const created = connections.body[0];
  expect(created.settings).toMatchObject({ domain: 'mg.lumen.example', region: 'eu' });
  // Secrets are write-only: the API only says they are saved.
  expect(created.secrets).toEqual(
    expect.arrayContaining([
      expect.objectContaining({ key: 'webhookSigningKey', saved: true }),
      expect.objectContaining({ key: 'apiKey', saved: true }),
    ]),
  );
  expect(JSON.stringify(connections.body)).not.toContain(SIGNING_KEY);
  // Only integrations.manage may do this (the account manager may not).
  expect((await call(accounts.am, 'GET', '/agency/integrations/connections')).status).toBe(403);
  remember('mailgunConnectionId', created.id);
});

test('delivered, hard bounce and complaint events: report and suppression list; bad signatures and replays refused', async () => {
  test.setTimeout(3 * 60_000);
  // A fresh campaign to the three contacts the events are about.
  const name = `Webhook check ${state().runId}`;
  const listRes = await call<{ id: string }>(accounts.am, 'POST', '/agency/email/lists', {
    clientAccountId: state().client.id,
    name: `Webhook list ${state().runId}`,
    doubleOptIn: false,
  });
  const people = ['dora', 'hugo', 'cleo'];
  for (const who of people) {
    const r = await call(accounts.am, 'POST', '/agency/email/subscribers', {
      clientAccountId: state().client.id,
      email: address(who),
      firstName: who,
      attestEmailConsent: true,
      consentSource: 'E2E webhook journey',
      listIds: [listRes.body.id],
    });
    expect(r.status).toBe(200);
  }
  const c = await call<{ id: string; name: string; concurrencyStamp: string }>(
    accounts.am,
    'POST',
    '/agency/email/campaigns',
    {
      clientAccountId: state().client.id,
      name,
      channel: 'Email',
      listId: listRes.body.id,
      senderProfileId: recall('senderId'),
      subject: `Webhooks ${state().runId}`,
      design: design('Webhooks', 'https://lumen.example/webhooks'),
    },
  );
  expect(
    (
      await call(accounts.am, 'POST', `/agency/email/campaigns/${c.body.id}/send`, {
        confirm: true,
        confirmName: name,
        concurrencyStamp: c.body.concurrencyStamp,
      })
    ).status,
  ).toBe(200);
  await runSendJob();
  for (const who of people)
    await expect.poll(() => mailsWith(address(who), `Webhooks ${state().runId}`).length).toBe(1);
  remember('webhookCampaignId', c.body.id);

  const post = (body: unknown) => raw<{ applied: number }>('POST', webhook(), { body });
  // Mailgun echoes the message's custom variables (v:oa_ref, sent as X-OA-oa_ref) with every event.
  const ref = (who: string) => mailsWith(address(who), `Webhooks ${state().runId}`)[0].headers['x-oa-oa_ref'];
  const ev = (event: string, who: string, id: string, extra: Record<string, unknown> = {}) =>
    mailgunEvent(SIGNING_KEY, {
      event,
      recipient: address(who),
      id,
      timestamp: Date.now() / 1000,
      'user-variables': { oa_ref: ref(who) },
      ...extra,
    });

  // Refused: wrong key, stale timestamp (a captured request replayed later), tampered body fields, no signature.
  expect(
    (await post(mailgunEvent('wrong-key', { event: 'complained', recipient: address('dora'), id: 'x1' })))
      .status,
  ).toBe(401);
  expect(
    (
      await post(
        mailgunEvent(
          SIGNING_KEY,
          { event: 'complained', recipient: address('dora'), id: 'x2' },
          { timestamp: Math.floor(Date.now() / 1000) - 3_600 },
        ),
      )
    ).status,
  ).toBe(401);
  const good = ev('delivered', 'dora', `del-dora-${state().runId}`);
  expect((await post({ ...good, signature: { ...good.signature, token: 'swapped-token' } })).status).toBe(
    401,
  );
  expect((await post({ 'event-data': good['event-data'] })).status).toBe(401);
  expect(
    (await raw('POST', webhook(), { text: '{not json', headers: { 'Content-Type': 'application/json' } }))
      .status,
  ).toBe(400);
  // Unknown or malformed workspaces are 404s; SendGrid is not configured for this workspace (503).
  expect(
    (await raw('POST', '/api/v1/public/email/webhooks/mailgun/not-a-workspace', { body: good })).status,
  ).toBe(404);
  expect(
    (await raw('POST', `/api/v1/public/email/webhooks/sendgrid/${state().client.id}`, { body: [] })).status,
  ).toBe(503);
  expect(await suppressions(address('dora'))).toHaveLength(0);

  // Delivered (dora), permanent failure (hugo), complaint (cleo).
  expect((await post(good)).body.applied).toBe(1);
  expect(
    (
      await post(
        ev('failed', 'hugo', `fail-hugo-${state().runId}`, {
          severity: 'permanent',
          'delivery-status': { description: '550 No such user' },
        }),
      )
    ).body.applied,
  ).toBe(1);
  expect((await post(ev('complained', 'cleo', `cmp-cleo-${state().runId}`))).body.applied).toBe(1);
  // The same deliveries again (providers retry): nothing applied twice.
  expect((await post(good)).body.applied).toBe(0);
  expect((await post(ev('complained', 'cleo', `cmp-cleo-${state().runId}`))).body.applied).toBe(0);
  // The same new event delivered five times at once (provider retries racing): applied exactly once — one soft
  // bounce, not five (three would clean the address).
  const soft = ev('failed', 'dora', `soft-dora-${state().runId}`, { severity: 'temporary' });
  const burst = await Promise.all(Array.from({ length: 5 }, () => post(soft)));
  for (const r of burst) expect(r.status).toBe(200);
  expect(burst.reduce((n, r) => n + (r.body?.applied ?? 0), 0)).toBe(1);
  // An ignored event type is accepted but changes nothing.
  expect((await post(ev('opened', 'dora', `open-dora-${state().runId}`))).body.applied).toBe(0);

  expect((await suppressions(address('hugo'))).map((s) => s.reason)).toEqual(['HardBounce']);
  expect((await suppressions(address('cleo'))).map((s) => s.reason)).toEqual(['Complaint']);
  expect(await suppressions(address('dora'))).toHaveLength(0);
  expect(await subscriberByEmail(address('hugo'))).toMatchObject({ status: 'Bounced' });
  expect(await subscriberByEmail(address('dora'))).toMatchObject({ status: 'Subscribed' });
  expect(await subscriberByEmail(address('cleo'))).toMatchObject({
    status: 'Complained',
    emailConsent: 'Withdrawn',
  });

  // Workspace KPIs (overview, client portal): the webhook campaign's deliveries are measured (1 of its 3), every other
  // campaign's are estimated (sent − bounces) — not "1 delivered" for the whole workspace, which made rates exceed 100%.
  const kpis = await call<{ emailsSent: number; delivered: number; uniqueOpens: number; openRate: number }>(
    accounts.am,
    'GET',
    `/agency/email/kpis?clientId=${state().client.id}`,
  );
  expect(kpis.body.delivered).toBe(kpis.body.emailsSent - 2);
  expect(kpis.body.openRate).toBeLessThanOrEqual(1);
  expect(kpis.body.openRate).toBeCloseTo(kpis.body.uniqueOpens / kpis.body.delivered, 3);

  const r = await report(c.body.id);
  expect(r).toMatchObject({
    recipients: 3,
    sent: 3,
    hardBounces: 1,
    softBounces: 1,
    complaints: 1,
    deliveredIsEstimated: false,
    delivered: 1,
  });

  // Suppressed people are out of the next campaign's audience.
  const next = await call<{ id: string }>(accounts.am, 'POST', '/agency/email/campaigns', {
    clientAccountId: state().client.id,
    name: `After webhooks ${state().runId}`,
    channel: 'Email',
    listId: listRes.body.id,
    senderProfileId: recall('senderId'),
    subject: 'Again',
    design: design('Again', 'https://lumen.example/again'),
  });
  const checklist = await call<{ audienceCount: number }>(
    accounts.am,
    'GET',
    `/agency/email/campaigns/${next.body.id}/checklist`,
  );
  expect(checklist.body.audienceCount).toBe(1);
  expect((await call(accounts.am, 'DELETE', `/agency/email/campaigns/${next.body.id}`)).status).toBe(204);
});

test('the report shows the provider-measured numbers; a complaint cannot be lifted from the suppression list UI', async ({
  browser,
}) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/campaigns/${recall('webhookCampaignId')}/report`);
  await expect(page.getByRole('group', { name: 'Delivered', exact: true })).toContainText('Measured');
  await expect(page.getByRole('group', { name: 'Bounces', exact: true })).toContainText('1 hard · 1 soft');
  await expect(page.getByRole('group', { name: 'Complaints', exact: true })).toContainText('1');

  await openEmail(page, '/settings');
  await page.getByLabel('Search suppressions').fill(address('cleo'));
  const table = page.getByRole('table', { name: 'Suppressed addresses' });
  await expect(table.getByRole('row').filter({ hasText: address('cleo') })).toContainText('Complaint');
  await table.getByRole('button', { name: `Remove suppression for ${address('cleo')}` }).click();
  const dialog = modal(page, `Remove the suppression for ${address('cleo')}?`);
  await dialog
    .getByLabel('Why is it safe to message this address again?')
    .fill('The client says the complaint was a mistake');
  await dialog.getByRole('button', { name: 'Remove suppression' }).click();
  await expect(dialog.getByRole('alert')).toContainText('can only be lifted by the contact');
  errors.ignore(/HTTP 409 DELETE .*\/agency\/email\/suppressions\//);
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(table.getByRole('row').filter({ hasText: address('cleo') })).toBeVisible();

  // A hard bounce, on the other hand, can be lifted with a reason (e.g. the mailbox was re-created).
  await page.getByLabel('Search suppressions').fill(address('hugo'));
  await table.getByRole('button', { name: `Remove suppression for ${address('hugo')}` }).click();
  const lift = modal(page, `Remove the suppression for ${address('hugo')}?`);
  await lift
    .getByLabel('Why is it safe to message this address again?')
    .fill('Hugo confirmed by phone that the mailbox exists again');
  await lift.getByRole('button', { name: 'Remove suppression' }).click();
  await expect(lift).toBeHidden();
  await expect(table.getByRole('row').filter({ hasText: address('hugo') })).toBeHidden();
  errors.expectClean('the suppression list after webhooks');
});
