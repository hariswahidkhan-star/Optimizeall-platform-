import { expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  address,
  as,
  call,
  codeOf,
  landing,
  mailsWith,
  raw,
  recall,
  runJob,
  state,
  toast,
  watchErrors,
} from './support/email';

/**
 * Journeys (automations) and SMS for the journey's client: a welcome journey triggered by a list subscription sends
 * its email exactly once per contact (reruns and concurrent job runs included) and locks its sender's address while
 * active; SMS campaigns count GSM-7/UCS-2 segments and cannot be sent without a connected SMS provider; Twilio
 * webhooks refuse unconfigured workspaces.
 */
test('welcome journey: list subscription → one email per contact, never twice; the sender is locked while it runs', async () => {
  test.setTimeout(3 * 60_000);
  const welcomeList = await call<{ id: string }>(accounts.am, 'POST', '/agency/email/lists', {
    clientAccountId: state().client.id,
    name: `Welcome ${state().runId}`,
    doubleOptIn: false,
  });
  const subject = `Welcome to Lumen ${state().runId}`;
  const journey = await call<{ id: string; status: string; concurrencyStamp: string }>(
    accounts.am,
    'POST',
    '/agency/email/automations',
    {
      clientAccountId: state().client.id,
      name: `Welcome journey ${state().runId}`,
      trigger: 'ListSubscribed',
      triggerConfig: { listId: welcomeList.body.id },
      reentry: 'Never',
      senderProfileId: recall('senderId'),
      entryStepKey: 'welcome',
      steps: [
        {
          key: 'welcome',
          type: 'SendEmail',
          config: { templateId: recall('templateId'), subject },
          next: null,
        },
      ],
    },
  );
  expect(journey.status, JSON.stringify(journey.body)).toBe(200);
  // Negative: a journey with an unverified sender cannot be activated.
  const unverified = await call<{ id: string }>(accounts.am, 'POST', '/agency/email/automations', {
    clientAccountId: state().client.id,
    name: `Unverified journey ${state().runId}`,
    trigger: 'ListSubscribed',
    triggerConfig: { listId: welcomeList.body.id },
    senderProfileId: recall('unverifiedSenderId'),
    entryStepKey: 'welcome',
    steps: [
      { key: 'welcome', type: 'SendEmail', config: { templateId: recall('templateId'), subject: 'Nope' } },
    ],
  });
  const refused = await call(accounts.am, 'POST', `/agency/email/automations/${unverified.body.id}/activate`);
  expect(codeOf(refused)).toBe('email.automation_invalid');

  expect(
    (await call(accounts.am, 'POST', `/agency/email/automations/${journey.body.id}/activate`)).status,
  ).toBe(200);

  // While the journey is active its sender's address cannot change.
  const senders = await call<{ id: string; fromName: string; concurrencyStamp: string }[]>(
    accounts.am,
    'GET',
    `/agency/email/senders?clientId=${state().client.id}`,
  );
  const sender = senders.body.find((s) => s.id === recall('senderId'))!;
  const move = await call(accounts.am, 'PUT', `/agency/email/senders/${sender.id}`, {
    clientAccountId: state().client.id,
    fromName: sender.fromName,
    fromEmail: address('moved-again'),
    isDefault: true,
    concurrencyStamp: sender.concurrencyStamp,
  });
  expect(codeOf(move)).toBe('email.sender_in_use');

  for (const who of ['wes', 'zoe']) {
    const r = await call(accounts.am, 'POST', '/agency/email/subscribers', {
      clientAccountId: state().client.id,
      email: address(who),
      firstName: who === 'wes' ? 'Wes' : 'Zoe',
      attestEmailConsent: true,
      consentSource: 'E2E welcome journey',
      listIds: [welcomeList.body.id],
    });
    expect(r.status).toBe(200);
  }
  // Two job runs at once, then another: each contact gets the welcome email exactly once.
  const token = (await as(accounts.admin)).token;
  const runs = await Promise.all([0, 1].map(() => raw('POST', '/admin/jobs/AutomationJob/run', { token })));
  for (const r of runs) expect([200, 409]).toContain(r.status);
  await runJob('AutomationJob');
  for (const who of ['wes', 'zoe'])
    await expect.poll(() => mailsWith(address(who), subject).length, { message: who }).toBe(1);
  const wes = mailsWith(address('wes'), subject)[0];
  expect(wes.html).toContain('Hi Wes, your free plan is live.');
  expect(wes.headers['list-unsubscribe']).toMatch(/\/e\/u\//);
  const enrollments = await call<{ status: string }[]>(
    accounts.am,
    'GET',
    `/agency/email/automations/${journey.body.id}/enrollments`,
  );
  expect(enrollments.body.map((e) => e.status).sort()).toEqual(['Completed', 'Completed']);

  // Re-subscribing the same contact does not re-enter a "never" journey.
  const zoeId = (
    await call<{ items: { id: string }[] }>(
      accounts.am,
      'GET',
      `/agency/email/subscribers?clientAccountId=${state().client.id}&search=${encodeURIComponent(address('zoe'))}`,
    )
  ).body.items[0].id;
  await call(accounts.am, 'POST', `/agency/email/subscribers/${zoeId}/lists`, {
    listId: welcomeList.body.id,
    action: 'unsubscribe',
  });
  await call(accounts.am, 'POST', `/agency/email/subscribers/${zoeId}/lists`, {
    listId: welcomeList.body.id,
    action: 'subscribe',
  });
  await runJob('AutomationJob');
  expect(mailsWith(address('zoe'), subject)).toHaveLength(1);

  // Paused: the sender can be re-addressed again (nothing is going out from it).
  expect((await call(accounts.am, 'POST', `/agency/email/automations/${journey.body.id}/pause`)).status).toBe(
    200,
  );
});

test('SMS: segment counter (GSM-7 vs UCS-2), no send without a connected provider, Twilio webhooks refused when unconfigured', async ({
  browser,
}) => {
  const seg = async (text: string) =>
    (
      await call<{ encoding: string; segments: number; characters: number }>(
        accounts.am,
        'POST',
        '/agency/email/sms/campaigns/segments',
        { text, recipients: 10, costPerSegment: 0.01 },
      )
    ).body;
  expect(await seg('Hello Lumen')).toMatchObject({ encoding: 'Gsm7', segments: 1 });
  expect(await seg('x'.repeat(160))).toMatchObject({ encoding: 'Gsm7', segments: 1 });
  expect(await seg('x'.repeat(161))).toMatchObject({ encoding: 'Gsm7', segments: 2 });
  expect(await seg('Sale 🎉')).toMatchObject({ encoding: 'Ucs2', segments: 1 });
  expect(await seg('é'.repeat(70) + '🎉')).toMatchObject({ encoding: 'Ucs2', segments: 2 });

  // A contact with SMS consent, on the newsletter.
  const phoneContact = await call<{ id: string }>(accounts.am, 'POST', '/agency/email/subscribers', {
    clientAccountId: state().client.id,
    email: address('sms'),
    phone: '+447700900123',
    firstName: 'Sam',
    attestEmailConsent: true,
    attestSmsConsent: true,
    consentSource: 'E2E: SMS opt-in at checkout',
    listIds: [recall('newsletterId')],
  });
  expect(phoneContact.status).toBe(200);

  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto('/agency/sms');
  const picker = page.getByLabel('Workspace');
  await picker.selectOption({ label: state().client.name });
  await expect(page.getByRole('heading', { level: 1, name: 'SMS & WhatsApp campaigns' })).toBeVisible();
  await page.getByRole('link', { name: 'New campaign' }).click();
  const form = page.getByRole('form', { name: 'Campaign settings' });
  const name = `Flash SMS ${state().runId}`;
  await form.getByLabel('Campaign name', { exact: true }).fill(name);
  await form.getByLabel('List', { exact: true }).selectOption({ label: `Newsletter ${state().runId}` });
  await form
    .getByLabel('SMS text')
    .fill('Hi {{first_name|there}}, Lumen spring sale today only. Reply STOP to opt out');
  await expect(page.getByText(/GSM-7 characters · 1 segment/)).toBeVisible();
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect(toast(page, 'Campaign saved')).toBeVisible();
  await expect(page).toHaveURL(/\/agency\/sms\/[0-9a-f-]{36}$/);
  const checklist = page.getByRole('list', { name: 'Pre-send checklist' });
  await expect(checklist).toContainText('Twilio is not connected for this workspace');
  await expect(checklist).toContainText('1 contacts with consent will receive it');
  await expect(page.getByRole('button', { name: 'Review & send' })).toBeDisabled();
  errors.expectClean('an SMS campaign draft');

  const id = page.url().split('/').pop()!;
  const c = await call<{ name: string; concurrencyStamp: string }>(
    accounts.am,
    'GET',
    `/agency/email/sms/campaigns/${id}`,
  );
  const send = await call(accounts.am, 'POST', `/agency/email/sms/campaigns/${id}/send`, {
    confirm: true,
    confirmName: c.body.name,
    concurrencyStamp: c.body.concurrencyStamp,
  });
  expect(codeOf(send)).toBe('email.checklist_failed');
  // SMS campaigns are not reachable through the email campaign API (and vice versa).
  expect((await call(accounts.am, 'GET', `/agency/email/campaigns/${id}`)).status).toBe(404);
  expect((await call(accounts.am, 'GET', `/agency/email/sms/campaigns/${recall('springId')}`)).status).toBe(
    404,
  );

  const form2 = new URLSearchParams({ From: '+447700900123', Body: 'STOP' }).toString();
  const inbound = await raw('POST', `/api/v1/public/sms/webhooks/twilio/${state().client.id}/inbound`, {
    text: form2,
    headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Twilio-Signature': 'forged' },
  });
  expect(inbound.status).toBe(503);
  expect(
    (
      await raw('POST', '/api/v1/public/sms/webhooks/twilio/not-a-workspace/inbound', {
        text: form2,
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      })
    ).status,
  ).toBe(404);
  // Deleting the draft.
  const del = await call(accounts.am, 'DELETE', `/agency/email/sms/campaigns/${id}`);
  expect(del.status).toBe(204);
});
