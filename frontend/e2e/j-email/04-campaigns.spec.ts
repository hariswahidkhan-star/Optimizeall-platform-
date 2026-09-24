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
  openEmail,
  raw,
  recall,
  remember,
  report,
  runSendJob,
  state,
  toast,
  watchErrors,
} from './support/email';

/**
 * Campaigns from draft to sent in the journey's client workspace: the editor and pre-send checklist, the typed
 * confirmation, the send job under concurrent runs (nobody gets two copies), throttling, pause/resume, suppression that
 * wins at send time, read-only after sending, scheduling/unscheduling/rescheduling, duplicate and stale confirmations,
 * blocked sends (empty segment, unverified sender, past schedule), cancelling a large send midway, duplicate/delete,
 * an A/B subject test, and a sender that cannot change address while a scheduled campaign uses it.
 */
interface Campaign {
  id: string;
  name: string;
  status: string;
  concurrencyStamp: string;
}

/** The Newsletter contacts who may receive marketing email (see 02-audience). */
const NEWSLETTER_AUDIENCE = ['ada', 'grace', 'alan', 'margaret', 'radia'];
/** On the list or in the workspace, but never to be emailed: consent withdrawn, unsubscribed from the list, suppressed. */
const NEVER = ['edsger', 'linus', 'blocked', 'john'];

async function createCampaign(name: string, overrides: Record<string, unknown> = {}): Promise<Campaign> {
  const res = await call<Campaign>(accounts.am, 'POST', '/agency/email/campaigns', {
    clientAccountId: state().client.id,
    name,
    channel: 'Email',
    listId: recall('newsletterId'),
    senderProfileId: recall('senderId'),
    subject: `Hello {{first_name|there}} (${name})`,
    design: design(name, 'https://lumen.example/news'),
    ...overrides,
  });
  expect(res.status, JSON.stringify(res.body)).toBe(200);
  return res.body;
}

async function get(id: string): Promise<Campaign & { recipientCount: number; sendStartedAt: string | null }> {
  const res = await call<Campaign & { recipientCount: number; sendStartedAt: string | null }>(
    accounts.am,
    'GET',
    `/agency/email/campaigns/${id}`,
  );
  expect(res.status).toBe(200);
  return res.body;
}

function confirmSend(c: Campaign, overrides: Record<string, unknown> = {}) {
  return call<Campaign>(accounts.am, 'POST', `/agency/email/campaigns/${c.id}/send`, {
    confirm: true,
    confirmName: c.name,
    concurrencyStamp: c.concurrencyStamp,
    ...overrides,
  });
}

test('draft → checklist → typed confirmation → concurrent send-job runs deliver exactly once; throttle, pause/resume, suppression wins', async ({
  browser,
}) => {
  test.setTimeout(6 * 60_000);
  const name = `Spring launch ${state().runId}`;
  const subjectMarker = `spring is here ${state().runId}`;
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);

  // ------------------------------------------------------------ draft in the editor
  await openEmail(page, '/campaigns');
  await expect(page.getByRole('heading', { level: 1, name: 'Email campaigns' })).toBeVisible();
  await page.getByRole('link', { name: 'New campaign' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'New email campaign' })).toBeVisible();
  const form = page.getByRole('form', { name: 'Campaign settings' });
  await form.getByLabel('Campaign name', { exact: true }).fill(name);
  await form.getByLabel('List', { exact: true }).selectOption({ label: `Newsletter ${state().runId}` });
  await form
    .getByLabel('From (verified sender)')
    .selectOption({ label: `Lumen News <${recall('senderEmail')}>` });
  await form.getByLabel('Subject line', { exact: true }).fill(`{{first_name|Friend}}, ${subjectMarker}`);
  await form.getByLabel('Preview text').fill('New features for {{custom.plan|every}} plan');
  await form.getByLabel('Messages per minute').fill('3');
  const blocks = form.getByRole('region', { name: 'Content blocks' });
  await blocks.getByLabel('Title', { exact: true }).first().fill('Spring at Lumen');
  const button = blocks
    .getByRole('listitem')
    .filter({ has: page.getByRole('heading', { name: /^\d+\. Button$/ }) });
  await button.getByLabel('Link', { exact: true }).fill('https://lumen.example/spring?src=email');
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect(toast(page, 'Campaign saved')).toBeVisible();
  await expect(page).toHaveURL(/\/agency\/email\/campaigns\/[0-9a-f-]{36}$/);
  const id = page.url().split('/').pop()!;
  remember('springId', id);

  // The checklist counts only contacts who may receive it.
  const checklist = page.getByRole('list', { name: 'Pre-send checklist' });
  await expect(checklist).toContainText(
    `${NEWSLETTER_AUDIENCE.length} contacts with consent will receive it`,
  );
  await expect(checklist).toContainText('Lumen News <');
  await expect(checklist).toContainText('1 Lumen Way, Bristol');

  // ------------------------------------------------------------ typed confirmation
  await page.getByRole('button', { name: 'Review & send' }).click();
  const send = modal(page, `Send “${name}”?`);
  await expect(send).toContainText(`Recipients (with consent)${NEWSLETTER_AUDIENCE.length}`);
  await expect(send).toContainText('Up to 3 per minute');
  const confirmButton = send.getByRole('button', { name: 'Send now' });
  await expect(confirmButton).toBeDisabled();
  await send.getByLabel(`Type ${name} to confirm`).fill(name.toUpperCase());
  await expect(confirmButton).toBeDisabled();
  await send.getByLabel(`Type ${name} to confirm`).fill(name);
  await confirmButton.click();
  await expect(toast(page, 'Campaign queued')).toBeVisible();
  // Confirmed: read-only, with sending controls.
  await expect(page.getByRole('button', { name: 'Save draft' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Review & send' })).toBeHidden();
  await expect(form.getByLabel('Campaign name', { exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Pause' })).toBeVisible();
  errors.expectClean('drafting and confirming a campaign');
  expect((await get(id)).status).toBe('Scheduled');

  // ------------------------------------------------------------ three send-job runs at once (several API instances)
  const adminToken = (await as(accounts.admin)).token;
  const runs = await Promise.all(
    [0, 1, 2].map(() => raw('POST', '/admin/jobs/CampaignSendJob/run', { token: adminToken })),
  );
  for (const r of runs) expect([200, 409], JSON.stringify(r.body)).toContain(r.status);
  expect(runs.some((r) => r.status === 200)).toBe(true);
  // Another (sequential) run in the same minute sends nothing more: the throttle allows 3 per minute.
  await runSendJob();

  const received = () =>
    NEWSLETTER_AUDIENCE.filter((who) => mailsWith(address(who), subjectMarker).length > 0);
  await expect.poll(() => received().length).toBe(3);
  for (const who of NEWSLETTER_AUDIENCE)
    expect(mailsWith(address(who), subjectMarker).length, who).toBeLessThanOrEqual(1);
  expect(await report(id)).toMatchObject({ recipients: 5, sent: 3, pending: 2, failed: 0 });
  expect((await get(id)).status).toBe('Sending');
  const firstBatchAt = Date.now();

  // ------------------------------------------------------------ one of the two still waiting is suppressed meanwhile
  const waiting = NEWSLETTER_AUDIENCE.filter((who) => !received().includes(who));
  const [suppressed, lastOne] = waiting;
  remember('suppressedDuringSend', suppressed);
  await call(accounts.am, 'POST', '/agency/email/suppressions', {
    clientAccountId: state().client.id,
    value: address(suppressed),
    note: 'Asked us to stop while the campaign was sending',
  });

  // ------------------------------------------------------------ pause holds everything, even once the throttle allows more
  await page.reload();
  await page.getByRole('button', { name: 'Pause' }).click();
  await expect(toast(page, 'Campaign paused')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Resume' })).toBeVisible();
  await page.waitForTimeout(Math.max(0, firstBatchAt + 62_000 - Date.now()));
  await runSendJob();
  expect(await report(id)).toMatchObject({ sent: 3, pending: 2 });
  expect(mailsWith(address(lastOne), subjectMarker)).toHaveLength(0);

  await page.getByRole('button', { name: 'Resume' }).click();
  await expect(toast(page, 'Campaign sending')).toBeVisible();
  await runSendJob();
  await expect.poll(() => mailsWith(address(lastOne), subjectMarker).length).toBe(1);
  expect(mailsWith(address(suppressed), subjectMarker)).toHaveLength(0);
  expect(await report(id)).toMatchObject({ recipients: 5, sent: 4, skipped: 1, pending: 0, failed: 0 });
  expect((await get(id)).status).toBe('Sent');
  for (const who of NEVER) expect(mailsWith(address(who), subjectMarker), who).toHaveLength(0);
  errors.expectClean('pausing and resuming a campaign');

  // ------------------------------------------------------------ what the recipients got
  const mail = mailsWith(address('alan'), subjectMarker)[0] ?? mailsWith(address('radia'), subjectMarker)[0];
  const who = mail.to.includes(address('alan')) ? 'Alan' : 'Radia';
  expect(mail.subject).toBe(`${who}, ${subjectMarker}`);
  expect(mail.headers['list-unsubscribe']).toMatch(/\/e\/u\//);
  expect(mail.headers['list-unsubscribe-post']).toBe('List-Unsubscribe=One-Click');
  expect(mail.headers.from).toContain(recall('senderEmail'));
  expect(mail.html).toContain('1 Lumen Way, Bristol BS1 4DJ, United Kingdom');
  expect(mail.html).toMatch(/\/e\/o\/[^"]+\.gif/);
  expect(mail.html).toMatch(/\/e\/c\/[^"]+/);
  // Tracked links never expose the destination in the email.
  expect(mail.html).not.toContain('https://lumen.example/spring');

  // ------------------------------------------------------------ read-only after sending
  await page.reload();
  await expect(page.getByRole('button', { name: 'Save draft' })).toBeHidden();
  await expect(page.getByRole('button', { name: 'Pause' })).toBeHidden();
  await expect(page.getByRole('link', { name: 'Report', exact: true })).toBeVisible();
  const sent = await get(id);
  const edit = await call(accounts.am, 'PUT', `/agency/email/campaigns/${id}`, {
    clientAccountId: state().client.id,
    name: 'Changed after sending',
    channel: 'Email',
    listId: recall('newsletterId'),
    subject: 'Changed',
    concurrencyStamp: sent.concurrencyStamp,
  });
  expect(edit.status).toBe(409);
  expect(codeOf(edit)).toBe('email.campaign_not_editable');
  expect((await call(accounts.am, 'DELETE', `/agency/email/campaigns/${id}`)).status).toBe(409);
  expect((await confirmSend(sent)).status).toBe(409);
  expect(
    (
      await call(accounts.am, 'POST', `/agency/email/campaigns/${id}/pause`, {
        concurrencyStamp: sent.concurrencyStamp,
      })
    ).status,
  ).toBe(409);
  expect(
    (
      await call(accounts.am, 'POST', `/agency/email/campaigns/${id}/cancel`, {
        concurrencyStamp: sent.concurrencyStamp,
      })
    ).status,
  ).toBe(409);
  // Nothing more is ever sent for it.
  await runSendJob();
  expect(NEWSLETTER_AUDIENCE.reduce((n, w) => n + mailsWith(address(w), subjectMarker).length, 0)).toBe(4);

  // ------------------------------------------------------------ the report page
  await page.getByRole('link', { name: 'Report', exact: true }).click();
  await expect(page.getByRole('group', { name: 'Sent', exact: true })).toContainText('4');
  await expect(page.getByRole('group', { name: 'Sent', exact: true })).toContainText('5 recipients');
  await expect(page.getByRole('group', { name: 'Delivered', exact: true })).toContainText('Estimated');
  errors.expectClean('the sent campaign and its report');
});

test('schedule, duplicate confirmation, back to draft, past schedule refused, reschedule; sender address locked while scheduled', async ({
  browser,
}) => {
  test.setTimeout(4 * 60_000);
  const name = `Product digest ${state().runId}`;
  const inAnHour = new Date(Date.now() + 3_600_000).toISOString();
  let c = await createCampaign(name, {
    listId: recall('updatesId'),
    scheduleMode: 'FixedTime',
    scheduledAt: inAnHour,
  });

  // A double click (two confirmations with the same stamp at once): exactly one wins.
  const [a, b] = await Promise.all([confirmSend(c), confirmSend(c)]);
  expect([a.status, b.status].sort()).toEqual([200, 409]);
  c = await get(c.id);
  expect(c.status).toBe('Scheduled');

  // Not due yet: the job leaves it alone.
  await runSendJob();
  expect(mailsWith(address('barbara'), name)).toHaveLength(0);
  expect((await get(c.id)).status).toBe('Scheduled');

  // While it is scheduled its sender's address cannot change (it would be unverified when the send starts).
  const senders = await call<{ id: string; concurrencyStamp: string; fromName: string }[]>(
    accounts.am,
    'GET',
    `/agency/email/senders?clientId=${state().client.id}`,
  );
  const sender = senders.body.find((s) => s.id === recall('senderId'))!;
  const move = await call(accounts.am, 'PUT', `/agency/email/senders/${sender.id}`, {
    clientAccountId: state().client.id,
    fromName: sender.fromName,
    fromEmail: address('moved'),
    isDefault: true,
    concurrencyStamp: sender.concurrencyStamp,
  });
  expect(move.status).toBe(409);
  expect(codeOf(move)).toBe('email.sender_in_use');
  expect((await call(accounts.am, 'DELETE', `/agency/email/senders/${sender.id}`)).status).toBe(409);

  // Back to draft from the editor.
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/campaigns/${c.id}`);
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
  await page.getByRole('button', { name: 'Back to draft' }).click();
  await expect(toast(page, 'Campaign draft')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save draft' })).toBeVisible();
  errors.expectClean('unscheduling a campaign');

  // A time in the past is a blocking checklist item.
  c = await get(c.id);
  const past = await call<Campaign>(accounts.am, 'PUT', `/agency/email/campaigns/${c.id}`, {
    clientAccountId: state().client.id,
    name,
    channel: 'Email',
    listId: recall('updatesId'),
    senderProfileId: recall('senderId'),
    subject: `Hello {{first_name|there}} (${name})`,
    design: design(name, 'https://lumen.example/news'),
    scheduleMode: 'FixedTime',
    scheduledAt: new Date(Date.now() - 10 * 60_000).toISOString(),
    concurrencyStamp: c.concurrencyStamp,
  });
  expect(past.status).toBe(200);
  const blocked = await confirmSend(past.body);
  expect(blocked.status).toBe(400);
  expect(codeOf(blocked)).toBe('email.checklist_failed');
  expect(JSON.stringify(blocked.body)).toContain('The scheduled time is in the past');

  // Rescheduled a few seconds ahead: confirmed, then sent once due — only to the confirmed double opt-in contact.
  const soon = await call<Campaign>(accounts.am, 'PUT', `/agency/email/campaigns/${c.id}`, {
    clientAccountId: state().client.id,
    name,
    channel: 'Email',
    listId: recall('updatesId'),
    senderProfileId: recall('senderId'),
    subject: `Hello {{first_name|there}} (${name})`,
    design: design(name, 'https://lumen.example/news'),
    scheduleMode: 'FixedTime',
    scheduledAt: new Date(Date.now() + 5_000).toISOString(),
    concurrencyStamp: past.body.concurrencyStamp,
  });
  // A stale stamp or a mistyped name is refused.
  expect((await confirmSend(soon.body, { concurrencyStamp: past.body.concurrencyStamp })).status).toBe(409);
  expect(codeOf(await confirmSend(soon.body, { confirmName: name.toLowerCase() }))).toBe(
    'email.confirm_name_mismatch',
  );
  expect(codeOf(await confirmSend(soon.body, { concurrencyStamp: undefined }))).toBe(
    'email.concurrency_stamp_required',
  );
  expect((await confirmSend(soon.body)).status).toBe(200);
  await page.waitForTimeout(6_000);
  await runSendJob();
  await expect.poll(() => mailsWith(address('barbara'), name).length).toBe(1);
  expect(mailsWith(address('katherine'), name)).toHaveLength(0);
  expect(await report(c.id)).toMatchObject({ recipients: 1, sent: 1 });
  expect((await get(c.id)).status).toBe('Sent');
});

test('blocked sends: an empty segment, an unverified sender; the UI keeps "Review & send" disabled', async ({
  browser,
}) => {
  // A segment nobody matches.
  const segment = await call<{ id: string }>(accounts.am, 'POST', '/agency/email/segments', {
    clientAccountId: state().client.id,
    name: `Nobody ${state().runId}`,
    definition: { match: 'all', conditions: [{ kind: 'tag', op: 'has', value: 'no-one-has-this-tag' }] },
  });
  expect(segment.status, JSON.stringify(segment.body)).toBe(200);
  const empty = await createCampaign(`Empty segment ${state().runId}`, { segmentId: segment.body.id });
  const checklist = await call<{
    canSend: boolean;
    audienceCount: number;
    items: { id: string; status: string }[];
  }>(accounts.am, 'GET', `/agency/email/campaigns/${empty.id}/checklist`);
  expect(checklist.body).toMatchObject({ canSend: false, audienceCount: 0 });
  expect(checklist.body.items.find((i) => i.id === 'audience')?.status).toBe('Fail');
  const refused = await confirmSend(empty);
  expect(codeOf(refused)).toBe('email.checklist_failed');

  const unverified = await createCampaign(`Unverified sender ${state().runId}`, {
    senderProfileId: recall('unverifiedSenderId'),
  });
  const unverifiedChecklist = await call<{
    canSend: boolean;
    items: { id: string; status: string; detail: string }[];
  }>(accounts.am, 'GET', `/agency/email/campaigns/${unverified.id}/checklist`);
  expect(unverifiedChecklist.body.canSend).toBe(false);
  expect(unverifiedChecklist.body.items.find((i) => i.id === 'sender')).toMatchObject({ status: 'Fail' });

  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/campaigns/${empty.id}`);
  await expect(page.getByRole('list', { name: 'Pre-send checklist' })).toContainText(
    'Nobody in the audience can receive this campaign',
  );
  await expect(page.getByRole('button', { name: 'Review & send' })).toBeDisabled();
  errors.expectClean('a campaign with an empty audience');
  // Drafts can be deleted.
  expect((await call(accounts.am, 'DELETE', `/agency/email/campaigns/${unverified.id}`)).status).toBe(204);
  expect((await call(accounts.am, 'GET', `/agency/email/campaigns/${unverified.id}`)).status).toBe(404);
});

test('cancel a large send midway: 1,200 recipients expanded once, the rest cancelled, nothing more sent', async ({
  browser,
}) => {
  test.setTimeout(5 * 60_000);
  const name = `Bulk announcement ${state().runId}`;
  const c = await createCampaign(name, { listId: recall('bulkListId'), throttlePerMinute: 25 });
  expect((await confirmSend(c)).status).toBe(200);
  await runSendJob();
  const first = await report(c.id);
  expect(first).toMatchObject({ recipients: 1_200, sent: 25, pending: 1_175 });

  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/campaigns/${c.id}`);
  await page.getByRole('button', { name: 'Cancel campaign' }).click();
  const dialog = modal(page, 'Cancel this campaign?');
  await dialog.getByLabel('Reason').fill('Wrong audience: this was meant for the beta list');
  await dialog.getByRole('button', { name: 'Cancel campaign' }).click();
  await expect(toast(page, 'Campaign cancelled')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Resume' })).toBeHidden();
  errors.expectClean('cancelling a campaign');

  await runSendJob();
  expect(await report(c.id)).toMatchObject({ recipients: 1_200, sent: 25, cancelled: 1_175, pending: 0 });
  let delivered = 0;
  for (let i = 0; i < 1_200; i++) delivered += mailsWith(address(`bulk${i}`), name).length;
  expect(delivered).toBe(25);
  // Cancelled is final.
  const cancelled = await get(c.id);
  expect(
    (
      await call(accounts.am, 'POST', `/agency/email/campaigns/${c.id}/resume`, {
        concurrencyStamp: cancelled.concurrencyStamp,
      })
    ).status,
  ).toBe(409);
});

test('duplicate a sent campaign into an editable draft', async ({ browser }) => {
  const springId = recall('springId');
  const copy = await call<Campaign & { listId: string; subject: string }>(
    accounts.am,
    'POST',
    `/agency/email/campaigns/${springId}/duplicate`,
  );
  expect(copy.status).toBe(200);
  expect(copy.body).toMatchObject({
    status: 'Draft',
    name: `Spring launch ${state().runId} (copy)`,
    listId: recall('newsletterId'),
  });
  expect(copy.body.subject).toContain(`spring is here ${state().runId}`);
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/campaigns/${copy.body.id}`);
  await expect(page.getByRole('button', { name: 'Save draft' })).toBeVisible();
  await expect(
    page.getByRole('form', { name: 'Campaign settings' }).getByLabel('Campaign name', { exact: true }),
  ).toBeEnabled();
  errors.expectClean('a duplicated campaign');
  expect((await call(accounts.am, 'DELETE', `/agency/email/campaigns/${copy.body.id}`)).status).toBe(204);
});

test('A/B subject test: the test cohort gets variant A or B, the rest waits for the winner; cancelling releases nobody', async ({
  browser,
}) => {
  const name = `AB subject ${state().runId}`;
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await openEmail(page, '/campaigns/new');
  const form = page.getByRole('form', { name: 'Campaign settings' });
  await form.getByLabel('Campaign name', { exact: true }).fill(name);
  await form.getByLabel('List', { exact: true }).selectOption({ label: `Newsletter ${state().runId}` });
  await form
    .getByLabel('From (verified sender)')
    .selectOption({ label: `Lumen News <${recall('senderEmail')}>` });
  await form.getByLabel('Subject line', { exact: true }).fill(`Variant A ${name}`);
  await form.getByRole('switch', { name: 'A/B test the subject line' }).click();
  await form.getByLabel('Variant B subject line').fill(`Variant B ${name}`);
  await form.getByLabel('Test cohort (%)').fill('50');
  const blocks = form.getByRole('region', { name: 'Content blocks' });
  await blocks
    .getByRole('listitem')
    .filter({ has: page.getByRole('heading', { name: /^\d+\. Button$/ }) })
    .getByLabel('Link', { exact: true })
    .fill('https://lumen.example/ab');
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect(toast(page, 'Campaign saved')).toBeVisible();
  await expect(page).toHaveURL(/\/agency\/email\/campaigns\/[0-9a-f-]{36}$/);
  const id = page.url().split('/').pop()!;
  await expect(page.getByRole('list', { name: 'Pre-send checklist' })).toContainText(
    '2 variants; 50% test cohort',
  );
  errors.expectClean('an A/B campaign draft');

  // Everyone on the newsletter who may still be emailed (one was suppressed during the spring send).
  const audience = NEWSLETTER_AUDIENCE.filter((w) => w !== recall('suppressedDuringSend'));
  expect((await confirmSend(await get(id))).status).toBe(200);
  await runSendJob();
  const r = await report(id);
  const got = (variant: 'A' | 'B') =>
    audience.filter((w) => mailsWith(address(w), `Variant ${variant} ${name}`).length === 1);
  expect(r.recipients).toBe(audience.length);
  expect(r.sent).toBe(got('A').length + got('B').length);
  expect(r.pending).toBe(audience.length - r.sent);
  for (const w of audience) expect(mailsWith(address(w), name).length, w).toBeLessThanOrEqual(1);

  const current = await get(id);
  if (r.pending === 0) {
    // The cohort is drawn per contact (a stable hash), so on a list this small it can cover everyone: then there is no
    // remainder waiting for a winner and the campaign is complete.
    expect(current.status).toBe('Sent');
    return;
  }
  // The remainder waits for the winner (after the wait hours); cancelling now releases nobody.
  expect(current.status).toBe('Sending');
  expect(
    (
      await call(accounts.am, 'POST', `/agency/email/campaigns/${id}/cancel`, {
        concurrencyStamp: current.concurrencyStamp,
        reason: 'E2E',
      })
    ).status,
  ).toBe(200);
  await runSendJob();
  expect(await report(id)).toMatchObject({ sent: r.sent, pending: 0, cancelled: audience.length - r.sent });
});
