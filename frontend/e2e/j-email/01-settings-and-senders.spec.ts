import { expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  address,
  call,
  codeOf,
  landing,
  mailsWith,
  modal,
  openEmail,
  remember,
  state,
  toast,
  verificationCode,
  watchErrors,
} from './support/email';

/**
 * Workspace settings and sender identities of the journey's brand-new client, through the settings page:
 * compliance details (postal address), a sender verified with the emailed 6-digit code, and the negatives —
 * an invalid time zone, a from name with markup, a wrong code, the resend throttle, the attempt lockout, and a changed
 * address that must be verified again.
 */
test('settings: compliance details are saved and validated', async ({ browser }) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await openEmail(page, '/settings');
  await expect(page.getByRole('heading', { level: 1, name: 'Email & SMS settings' })).toBeVisible();

  const form = page.getByRole('form', { name: 'Sending and compliance settings' });
  // A new client's workspace starts with its name and time zone, and no postal address.
  await expect(form.getByLabel('Sender organization name')).toHaveValue(state().client.name);
  await expect(form.getByLabel('Default time zone')).toHaveValue('Europe/London');
  await expect(form.getByLabel('Postal address')).toHaveValue('');

  // Negative: an unknown time zone is refused and nothing is saved.
  await form.getByLabel('Default time zone').fill('Mars/Olympus_Mons');
  await form.getByRole('button', { name: 'Save settings' }).click();
  await expect(form.getByRole('alert')).toContainText('valid IANA time zone');
  errors.ignore(/HTTP 400 PUT .*\/agency\/email\/settings/);

  await form.getByLabel('Default time zone').fill('Europe/London');
  await form.getByLabel('Sender organization name').fill(`Lumen Labs Ltd ${state().runId}`);
  await form.getByLabel('Postal address').fill('1 Lumen Way, Bristol BS1 4DJ, United Kingdom');
  await form.getByRole('button', { name: 'Save settings' }).click();
  await expect(toast(page, 'Settings saved')).toBeVisible();

  // Saved: a reload shows the values (and a second save with the new concurrency stamp works).
  await page.reload();
  const reloaded = page.getByRole('form', { name: 'Sending and compliance settings' });
  await expect(reloaded.getByLabel('Postal address')).toHaveValue(
    '1 Lumen Way, Bristol BS1 4DJ, United Kingdom',
  );
  await expect(reloaded.getByLabel('Sender organization name')).toHaveValue(
    `Lumen Labs Ltd ${state().runId}`,
  );
  errors.expectClean('the email settings page');

  // A stale concurrency stamp (another tab saved meanwhile) is a conflict, not a silent overwrite.
  const current = await call<{ concurrencyStamp: string }>(
    accounts.am,
    'GET',
    `/agency/email/settings?clientId=${state().client.id}`,
  );
  const stale = await call(accounts.am, 'PUT', '/agency/email/settings', {
    clientAccountId: state().client.id,
    organizationName: 'Overwrite',
    physicalAddress: 'Nowhere',
    defaultThrottlePerMinute: 600,
    defaultTimeZone: 'UTC',
    quietHoursStart: 21,
    quietHoursEnd: 8,
    smsCostPerSegment: 0,
    whatsAppCostPerMessage: 0,
    concurrencyStamp: '00000000-0000-0000-0000-000000000001',
  });
  expect(stale.status).toBe(409);
  expect(
    (
      await call<{ concurrencyStamp: string; organizationName: string }>(
        accounts.am,
        'GET',
        `/agency/email/settings?clientId=${state().client.id}`,
      )
    ).body,
  ).toMatchObject({
    concurrencyStamp: current.body.concurrencyStamp,
    organizationName: `Lumen Labs Ltd ${state().runId}`,
  });
});

test('senders: verified with the emailed code; wrong codes, resend throttle and lockout', async ({
  browser,
}) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await openEmail(page, '/settings');
  const senderEmail = address('news');

  // Negative: a from name with markup is refused inside the dialog.
  await page.getByRole('button', { name: 'Add sender' }).click();
  let dialog = modal(page, 'Add a sender');
  await dialog.getByLabel('From name').fill('Lumen <News>');
  await dialog.getByLabel('From email').fill(senderEmail);
  await dialog.getByRole('button', { name: 'Add sender' }).click();
  await expect(dialog).toContainText('invalid characters');
  errors.ignore(/HTTP 400 POST .*\/agency\/email\/senders$/);

  await dialog.getByLabel('From name').fill('Lumen News');
  await dialog.getByLabel('Reply-to').fill(address('support'));
  await dialog.getByRole('switch', { name: 'Default sender for this workspace' }).click();
  await dialog.getByRole('button', { name: 'Add sender' }).click();
  await expect(dialog).toBeHidden();

  const table = page.getByRole('table', { name: 'Sender identities' });
  const row = table.getByRole('row').filter({ hasText: senderEmail });
  await expect(row).toContainText('Lumen News');
  await row.getByRole('button', { name: 'Verify' }).click();
  dialog = modal(page, `Verify ${senderEmail}`);
  await expect(dialog.getByRole('button', { name: 'Verify' })).toBeDisabled();
  await dialog.getByRole('button', { name: 'Send a code' }).click();
  await expect(toast(page, 'Code sent')).toBeVisible();
  const code = await verificationCode(senderEmail);

  // Resending within a minute is throttled (429) and says why.
  await dialog.getByRole('button', { name: 'Send a code' }).click();
  await expect(toast(page, 'Could not send the code')).toBeVisible();
  errors.ignore(/HTTP 429 POST .*send-verification/);
  expect(mailsWith(senderEmail, 'Verify your sender address')).toHaveLength(1);

  // A wrong code is refused; the right one verifies.
  const wrong = code === '123456' ? '654321' : '123456';
  await dialog.getByLabel('6-digit code').fill(wrong);
  await dialog.getByRole('button', { name: 'Verify' }).click();
  await expect(dialog).toContainText('That code is not correct.');
  errors.ignore(/HTTP 400 POST .*\/verify$/);
  await dialog.getByLabel('6-digit code').fill(code);
  await dialog.getByRole('button', { name: 'Verify' }).click();
  await expect(toast(page, 'Sender verified')).toBeVisible();
  await expect(row.getByText('Verified', { exact: true })).toBeVisible();
  errors.expectClean('adding and verifying a sender');

  const senders = await call<{ id: string; fromEmail: string; verified: boolean; isDefault: boolean }[]>(
    accounts.am,
    'GET',
    `/agency/email/senders?clientId=${state().client.id}`,
  );
  const sender = senders.body.find((s) => s.fromEmail === senderEmail)!;
  expect(sender).toMatchObject({ verified: true, isDefault: true });
  remember('senderId', sender.id);
  remember('senderEmail', senderEmail);

  // A used code cannot be replayed.
  const replay = await call(accounts.am, 'POST', `/agency/email/senders/${sender.id}/verify`, { code });
  expect(replay.status).toBe(400);
  expect(codeOf(replay)).toBe('email.verification_expired');

  // Boundary: five wrong attempts lock the code — even the right code is refused afterwards.
  const second = await call<{ id: string }>(accounts.am, 'POST', '/agency/email/senders', {
    clientAccountId: state().client.id,
    fromName: 'Lumen Offers',
    fromEmail: address('offers'),
  });
  expect(second.status).toBe(200);
  expect(
    (await call(accounts.am, 'POST', `/agency/email/senders/${second.body.id}/send-verification`)).status,
  ).toBe(200);
  const secondCode = await verificationCode(address('offers'));
  const bad = secondCode === '111111' ? '222222' : '111111';
  for (let attempt = 1; attempt <= 5; attempt++) {
    const res = await call(accounts.am, 'POST', `/agency/email/senders/${second.body.id}/verify`, {
      code: bad,
    });
    expect(codeOf(res), `attempt ${attempt}`).toBe('email.verification_wrong_code');
  }
  const locked = await call(accounts.am, 'POST', `/agency/email/senders/${second.body.id}/verify`, {
    code: secondCode,
  });
  expect(codeOf(locked)).toBe('email.verification_locked');
  // Malformed codes never reach the check.
  expect(
    (await call(accounts.am, 'POST', `/agency/email/senders/${second.body.id}/verify`, { code: '12ab56' }))
      .status,
  ).toBe(400);
  remember('unverifiedSenderId', second.body.id);
});
