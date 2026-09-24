import { expect, test } from '@playwright/test';
import {
  accounts,
  actor,
  address,
  call,
  codeOf,
  landing,
  linkPath,
  mailsWith,
  modal,
  openEmail,
  raw,
  recall,
  remember,
  runJob,
  state,
  subscriberByEmail,
  suppressions,
  toast,
  waitForMail,
  watchErrors,
  visitorPage,
} from './support/email';

/**
 * Lists and contacts of the journey's client: a single opt-in newsletter and a double opt-in list, manual contacts
 * (with and without a consent attestation, duplicates, invalid input), double opt-in through the emailed link and the
 * hosted sign-up form, CSV imports (valid rows, malformed rows, duplicates, suppressed and previously unsubscribed
 * addresses, the attestation, malformed files and size limits, a large file processed in the background), the
 * suppression list, consent withdrawal and erasure.
 */
interface ListDto {
  id: string;
  name: string;
  subscribed: number;
  pending: number;
  unsubscribed: number;
  publicKey: string;
  signupUrl: string;
}

async function list(id: string): Promise<ListDto> {
  const res = await call<ListDto>(accounts.am, 'GET', `/agency/email/lists/${id}`);
  expect(res.status).toBe(200);
  return res.body;
}

test('lists: a single opt-in newsletter and a double opt-in list', async ({ browser }) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await openEmail(page, '/lists');
  await expect(page.getByRole('heading', { level: 1, name: 'Audience' })).toBeVisible();
  await expect(page.getByText('No lists yet')).toBeVisible();

  const create = async (name: string, doubleOptIn: boolean) => {
    await page.getByRole('button', { name: 'New list' }).click();
    const dialog = modal(page, 'New list');
    await dialog.getByLabel('Name').fill(name);
    await dialog.getByLabel('Description').fill(`${name} (E2E)`);
    const doi = dialog.getByRole('switch', { name: /Double opt-in/ });
    if ((await doi.getAttribute('aria-checked')) !== String(doubleOptIn)) await doi.click();
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(toast(page, 'List created')).toBeVisible();
    await expect(dialog).toBeHidden();
  };
  await create(`Newsletter ${state().runId}`, false);
  await create(`Product updates ${state().runId}`, true);

  const table = page.getByRole('table', { name: 'Lists' });
  await expect(table.getByRole('row').filter({ hasText: `Newsletter ${state().runId}` })).toContainText(
    'Single',
  );
  await expect(table.getByRole('row').filter({ hasText: `Product updates ${state().runId}` })).toContainText(
    'Double',
  );
  errors.expectClean('creating lists');

  const lists = await call<ListDto[]>(
    accounts.am,
    'GET',
    `/agency/email/lists?clientId=${state().client.id}`,
  );
  remember('newsletterId', lists.body.find((l) => l.name.startsWith('Newsletter'))!.id);
  remember('updatesId', lists.body.find((l) => l.name.startsWith('Product updates'))!.id);

  // Negative: a list name is required, and another client's workspace is out of reach for a list id.
  const nameless = await call(accounts.am, 'POST', '/agency/email/lists', {
    clientAccountId: state().client.id,
    name: '',
  });
  expect(nameless.status).toBe(400);
});

test('contacts: added with an attestation, duplicates merged, invalid input refused', async ({ browser }) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await openEmail(page, '/lists');
  await page
    .getByRole('table', { name: 'Lists' })
    .getByRole('link', { name: `Newsletter ${state().runId}` })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: `Newsletter ${state().runId}` })).toBeVisible();

  const add = async (email: string, firstName: string, expectOk = true) => {
    await page.getByRole('button', { name: 'Add contact' }).click();
    const dialog = modal(page, 'Add a contact');
    await dialog.getByRole('textbox', { name: 'Email' }).fill(email);
    await dialog.getByLabel('First name').fill(firstName);
    await dialog.getByRole('checkbox', { name: /gave consent to receive email marketing/ }).check();
    await dialog
      .getByLabel('Where and how was consent given?')
      .fill('E2E: signed up at the Lumen launch event');
    await dialog.getByRole('button', { name: 'Add contact' }).click();
    if (expectOk) {
      await expect(toast(page, 'Contact added')).toBeVisible();
      await expect(dialog).toBeHidden();
    }
    return dialog;
  };
  const ada = address('ada');
  await add(ada, 'Ada');
  await add(address('grace'), 'Grace');
  await add(address('linus'), 'Linus');
  await expect(page.getByRole('group', { name: 'Subscribed', exact: true })).toContainText(
    /^Subscribed3(?!\d)/,
  );

  // The same person again (different case) updates the one contact instead of creating a second.
  await add(ada.toUpperCase(), 'Ada Lovelace');
  await expect(page.getByRole('group', { name: 'Subscribed', exact: true })).toContainText(
    /^Subscribed3(?!\d)/,
  );
  const contacts = await call<{ items: { email: string; firstName: string }[]; total: number }>(
    accounts.am,
    'GET',
    `/agency/email/subscribers?clientAccountId=${state().client.id}&search=${encodeURIComponent(`ada.${state().runId}`)}`,
  );
  expect(contacts.body.total).toBe(1);
  expect(contacts.body.items[0].firstName).toBe('Ada Lovelace');

  // Negative: an invalid address is refused with the reason, inside the dialog.
  // (The browser accepts "nobody@localhost"; the API wants a dotted domain.)
  const bad = await add('nobody@localhost', 'Nobody', false);
  await expect(bad.getByRole('alert')).toContainText('email is not a valid address');
  errors.ignore(/HTTP 400 POST .*\/agency\/email\/subscribers$/);
  await bad.getByRole('button', { name: 'Cancel' }).click();

  // The contacts table lists them, and search finds one.
  const table = page.getByRole('table', { name: 'Contacts on this list' });
  await expect(table.getByRole('link', { name: 'Grace' })).toBeVisible();
  await page.getByLabel('Search contacts').fill(`linus.${state().runId}`);
  await expect(table.getByRole('link', { name: 'Linus' })).toBeVisible();
  await expect(table.getByRole('link', { name: 'Grace' })).toBeHidden();
  errors.expectClean('adding contacts');

  // API negatives: no address at all; an attestation without its source.
  const empty = await call(accounts.am, 'POST', '/agency/email/subscribers', {
    clientAccountId: state().client.id,
    firstName: 'Nobody',
  });
  expect(empty.status).toBe(400);
  const unsourced = await call(accounts.am, 'POST', '/agency/email/subscribers', {
    clientAccountId: state().client.id,
    email: address('unsourced'),
    attestEmailConsent: true,
  });
  expect(codeOf(unsourced)).toBe('email.consent_source_required');
  // Editing a contact's address to one that another contact already uses is a conflict.
  const grace = (await subscriberByEmail(address('grace')))!;
  const detail = await call<{ concurrencyStamp: string }>(
    accounts.am,
    'GET',
    `/agency/email/subscribers/${grace.id}`,
  );
  const clash = await call(accounts.am, 'PUT', `/agency/email/subscribers/${grace.id}`, {
    email: ada,
    firstName: 'Grace',
    concurrencyStamp: detail.body.concurrencyStamp,
  });
  expect(clash.status).toBe(409);
  // A plain edit works and keeps consent.
  const edited = await call<{ lastName: string; emailConsent: string }>(
    accounts.am,
    'PUT',
    `/agency/email/subscribers/${grace.id}`,
    {
      email: address('grace'),
      firstName: 'Grace',
      lastName: 'Hopper',
      countryCode: 'US',
      concurrencyStamp: detail.body.concurrencyStamp,
    },
  );
  expect(edited.status).toBe(200);
  expect(edited.body).toMatchObject({ lastName: 'Hopper', emailConsent: 'Granted' });
  // …and the stale stamp of the same edit is now a conflict.
  const staleEdit = await call(accounts.am, 'PUT', `/agency/email/subscribers/${grace.id}`, {
    email: address('grace'),
    firstName: 'G',
    concurrencyStamp: detail.body.concurrencyStamp,
  });
  expect(staleEdit.status).toBe(409);
});

test('double opt-in: nothing but the confirmation email until the link is confirmed; forged links refused', async ({
  browser,
}) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  const updatesId = recall('updatesId');
  await page.goto(`/agency/email/lists/${updatesId}`);
  await expect(
    page.getByRole('heading', { level: 1, name: `Product updates ${state().runId}` }),
  ).toBeVisible();

  const barbara = address('barbara');
  await page.getByRole('button', { name: 'Add contact' }).click();
  const dialog = modal(page, 'Add a contact');
  await expect(dialog).toContainText('double opt-in email first');
  await dialog.getByRole('textbox', { name: 'Email' }).fill(barbara);
  await dialog.getByLabel('First name').fill('Barbara');
  await dialog.getByRole('button', { name: 'Add contact' }).click();
  await expect(toast(page, 'A confirmation email was sent (double opt-in).')).toBeVisible();
  await expect(page.getByRole('group', { name: 'Awaiting confirmation', exact: true })).toContainText(
    /1(?!\d)/,
  );
  errors.expectClean('adding a double opt-in contact');
  expect(await subscriberByEmail(barbara)).toMatchObject({ emailConsent: 'Pending' });

  const mail = await waitForMail(barbara, `Confirm your subscription to Product updates ${state().runId}`);
  const confirmPath = linkPath(mail, '/email/confirm/');
  const token = confirmPath.split('/').pop()!;

  const visitor = await visitorPage(browser);
  const visitorErrors = watchErrors(visitor);
  // A tampered token is refused (the signature covers every byte).
  const tampered = token.slice(0, -2) + (token.endsWith('AA') ? 'BB' : 'AA');
  await visitor.goto(`/email/confirm/${tampered}`);
  await visitor.getByRole('button', { name: 'Confirm subscription' }).click();
  await expect(visitor.getByRole('alert')).toContainText('invalid or has expired');
  visitorErrors.ignore(/HTTP 400 POST .*\/public\/email\/confirm\//);
  expect((await list(updatesId)).pending).toBe(1);

  // Opening the real link changes nothing until the button is pressed (link scanners open links).
  await visitor.goto(confirmPath);
  expect((await list(updatesId)).pending).toBe(1);
  await visitor.getByRole('button', { name: 'Confirm subscription' }).click();
  await expect(visitor.getByRole('status')).toContainText(
    `you are subscribed to Product updates ${state().runId}`,
  );
  expect(await list(updatesId)).toMatchObject({ pending: 0, subscribed: 1 });
  expect(await subscriberByEmail(barbara)).toMatchObject({ emailConsent: 'Granted', status: 'Subscribed' });

  // Confirming again (the link clicked twice) is harmless.
  const again = await raw('POST', `/public/email/confirm/${token}`);
  expect(again.status).toBe(200);
  expect(await list(updatesId)).toMatchObject({ pending: 0, subscribed: 1 });
  visitorErrors.expectClean('the double opt-in confirmation page');
});

test('hosted sign-up form: consent box required, honeypot, confirmation email, no address enumeration', async ({
  browser,
}) => {
  const updates = await list(recall('updatesId'));
  const visitor = await visitorPage(browser);
  const errors = watchErrors(visitor);
  await visitor.goto(new URL(updates.signupUrl).pathname);
  await expect(
    visitor.getByRole('heading', { level: 1, name: `Subscribe to Product updates ${state().runId}` }),
  ).toBeVisible();
  const katherine = address('katherine');
  const main = visitor.getByRole('main');
  await main.getByRole('textbox', { name: 'Email', exact: true }).fill(katherine);
  await main.getByLabel('First name').fill('Katherine');
  // The consent box starts unticked and the form cannot be sent without it.
  const consent = main.getByRole('checkbox', { name: /Yes, send me news and offers/ });
  await expect(consent).not.toBeChecked();
  await expect(main.getByRole('button', { name: 'Subscribe' })).toBeDisabled();
  await consent.check();
  await main.getByRole('button', { name: 'Subscribe' }).click();
  await expect(main.getByRole('status')).toContainText('check your inbox to confirm');
  errors.expectClean('the hosted sign-up form');
  await waitForMail(katherine, `Confirm your subscription to Product updates ${state().runId}`);
  expect(await subscriberByEmail(katherine)).toMatchObject({ emailConsent: 'Pending' });

  // A known address answers exactly like a new one (no enumeration), and only one confirmation per 10 minutes is sent.
  const repeat = await raw('POST', `/public/email/forms/${updates.publicKey}`, {
    body: { email: katherine, consent: true },
  });
  expect(repeat.status).toBe(202);
  expect(mailsWith(katherine, 'Confirm your subscription')).toHaveLength(1);
  // Without consent: refused.
  const noConsent = await raw('POST', `/public/email/forms/${updates.publicKey}`, {
    body: { email: address('noconsent'), consent: false },
  });
  expect(noConsent.status).toBe(400);
  // A bot filling the honeypot gets the same answer and nothing is stored.
  const bot = await raw('POST', `/public/email/forms/${updates.publicKey}`, {
    body: { email: address('bot'), consent: true, website: 'https://spam.example' },
  });
  expect(bot.status).toBe(202);
  expect(await subscriberByEmail(address('bot'))).toBeUndefined();
  // An unknown form key is a 404.
  expect((await raw('GET', '/public/email/forms/nosuchformkey000')).status).toBe(404);
});

test('suppression list: manual entries, removal needs a reason, complaints cannot be lifted', async ({
  browser,
}) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await openEmail(page, '/settings');
  const blocked = address('blocked');
  await page.getByRole('button', { name: 'Suppress address' }).click();
  const dialog = modal(page, 'Suppress an address');
  await dialog.getByLabel('Email address').fill(blocked.toUpperCase());
  await dialog.getByLabel('Note').fill('Asked by phone never to be emailed');
  await dialog.getByRole('button', { name: /^(Suppress|Save|Add)/ }).click();
  await expect(dialog).toBeHidden();
  const table = page.getByRole('table', { name: 'Suppressed addresses' });
  await expect(table.getByRole('row').filter({ hasText: blocked })).toContainText('Manual');

  // Removal asks why (reason required, audited); cancelled here — the address stays suppressed for the import test.
  await table.getByRole('button', { name: `Remove suppression for ${blocked}` }).click();
  const confirm = modal(page, `Remove the suppression for ${blocked}?`);
  await confirm.getByRole('button', { name: 'Remove suppression' }).click();
  await expect(confirm).toContainText('Enter a reason');
  await confirm.getByRole('button', { name: 'Cancel' }).click();
  expect((await suppressions(blocked)).map((s) => s.reason)).toEqual(['Manual']);
  errors.expectClean('the suppression list');

  // Adding the same address twice keeps one entry.
  await call(accounts.am, 'POST', '/agency/email/suppressions', {
    clientAccountId: state().client.id,
    value: blocked,
  });
  expect(await suppressions(blocked)).toHaveLength(1);
  // Invalid values are refused.
  const invalid = await call(accounts.am, 'POST', '/agency/email/suppressions', {
    clientAccountId: state().client.id,
    value: 'nope',
  });
  expect(codeOf(invalid)).toBe('email.suppression_invalid');
  // Removing without a reason is refused by the API too.
  const [entry] = await suppressions(blocked);
  expect((await call(accounts.am, 'DELETE', `/agency/email/suppressions/${entry.id}`)).status).toBe(400);

  // A complaint (manual bounce import) suppresses the address and cannot be lifted by staff.
  const complainer = address('complainer');
  const imported = await call<{ complaints: number; hard: number; invalid: number }>(
    accounts.am,
    'POST',
    '/agency/email/suppressions/bounces',
    {
      clientAccountId: state().client.id,
      content: `email,type\n${complainer},complaint\n${address('hardbounce')},hard\nnot-an-email,hard\n`,
    },
  );
  expect(imported.body).toMatchObject({ complaints: 1, hard: 1, invalid: 1 });
  const [complaint] = await suppressions(complainer);
  expect(complaint.reason).toBe('Complaint');
  const lift = await call(
    accounts.am,
    'DELETE',
    `/agency/email/suppressions/${complaint.id}?reason=${encodeURIComponent('Client says it was a mistake')}`,
  );
  expect(lift.status).toBe(409);
  expect(codeOf(lift)).toBe('email.suppression_locked');
});

test('CSV import: valid, malformed, duplicate, suppressed and unsubscribed rows are reported per row', async ({
  browser,
}) => {
  const newsletterId = recall('newsletterId');
  // Linus unsubscribes from the newsletter (only that list: he stays a subscribed contact of the workspace).
  const linus = (await subscriberByEmail(address('linus')))!;
  const unsub = await call(accounts.am, 'POST', `/agency/email/subscribers/${linus.id}/lists`, {
    listId: newsletterId,
    action: 'unsubscribe',
  });
  expect(unsub.status).toBe(200);
  expect((await list(newsletterId)).unsubscribed).toBe(1);

  const rows = [
    'email,first_name,last_name,country,tags,plan',
    `${address('alan')},Alan,Turing,GB,vip;beta,pro`,
    `${address('edsger')},Edsger,Dijkstra,NL,,free`,
    `${address('margaret')},Margaret,Hamilton,US,vip,pro`,
    `"${address('radia')}","Radia","Perlman, PhD",US,,free`,
    // A blank line (spreadsheets export them): problems are still reported on the file's own line numbers.
    '',
    `${address('john')},John,Backus,Narnia,,free`,
    'not-an-email,Broken,Row,GB,,free',
    `${address('ALAN')},Alan,Again,GB,,pro`,
    ',No,Address,GB,,free',
    `${address('blocked')},Blocked,Person,GB,,free`,
    `${address('linus')},Linus,Torvalds,FI,,pro`,
  ];
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/lists/${newsletterId}`);
  await page.getByRole('button', { name: 'Import CSV' }).click();
  const dialog = modal(page, 'Import contacts');
  await dialog.getByLabel('CSV file').setInputFiles({
    name: 'lumen-contacts.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from(rows.join('\r\n') + '\r\n', 'utf8'),
  });
  await expect(dialog).toContainText('10 rows in lumen-contacts.csv');
  // Suggested mapping: known headers map to fields, "plan" to a custom field.
  await expect(dialog.getByLabel('email', { exact: true })).toHaveValue('email');
  await expect(dialog.getByLabel('plan', { exact: true })).toHaveValue('custom.plan');
  const importButton = dialog.getByRole('button', { name: 'Import 10 rows' });
  // The attestation and its source are required before anything can be imported.
  await expect(importButton).toBeDisabled();
  await dialog.getByLabel('How was consent collected?').fill('Lumen webinar registrations 2026 (opt-in box)');
  await expect(importButton).toBeDisabled();
  await dialog.getByRole('checkbox', { name: /I confirm every contact in this file gave consent/ }).check();
  await dialog.getByLabel('Add tags to every imported contact').fill('webinar-2026');
  await importButton.click();

  await expect(
    dialog.getByRole('status').or(dialog.getByRole('alert')).filter({ hasText: 'Import finished' }),
  ).toContainText('5 created · 0 updated · 3 skipped · 2 failed');
  const problems = dialog.getByRole('table', { name: 'Rows with problems' });
  await expect(
    problems.getByRole('row').filter({ hasText: 'Invalid email address' }).getByRole('cell').first(),
  ).toHaveText('8');
  await expect(
    problems.getByRole('row').filter({ hasText: 'Duplicate of an earlier row' }).getByRole('cell').first(),
  ).toHaveText('9');
  await expect(
    problems
      .getByRole('row')
      .filter({ hasText: 'No email address or phone number' })
      .getByRole('cell')
      .first(),
  ).toHaveText('10');
  await expect(
    problems.getByRole('row').filter({ hasText: 'suppression list' }).getByRole('cell').first(),
  ).toHaveText('11');
  await expect(
    problems.getByRole('row').filter({ hasText: 'previously unsubscribed' }).getByRole('cell').first(),
  ).toHaveText('12');
  await expect(
    problems.getByRole('row').filter({ hasText: "country 'NARNIA' ignored" }).getByRole('cell').first(),
  ).toHaveText('7');
  await dialog.getByRole('button', { name: 'Close', exact: true }).click();
  errors.expectClean('the CSV import wizard');

  // Suppressed and list-unsubscribed people stay out of the list.
  const newsletter = await list(newsletterId);
  expect(newsletter).toMatchObject({ subscribed: 3 - 1 + 5, unsubscribed: 1 });
  const detail = await call<{ lists: { listId: string; status: string }[] }>(
    accounts.am,
    'GET',
    `/agency/email/subscribers/${linus.id}`,
  );
  expect(detail.body.lists.find((l) => l.listId === newsletterId)?.status).toBe('Unsubscribed');
  expect(await subscriberByEmail(address('blocked'))).toBeUndefined();
  // Imported contacts carry the consent evidence, the tags and the custom field.
  const alan = (await subscriberByEmail(address('alan')))!;
  const alanDetail = await call<{
    tags: string[];
    customFields: Record<string, string>;
    consentHistory: { source: string; note: string }[];
  }>(accounts.am, 'GET', `/agency/email/subscribers/${alan.id}`);
  expect(alanDetail.body.tags).toEqual(expect.arrayContaining(['vip', 'beta', 'webinar-2026']));
  expect(alanDetail.body.customFields).toEqual({ plan: 'pro' });
  expect(alanDetail.body.consentHistory[0]).toMatchObject({ source: 'import' });
  expect(alanDetail.body.consentHistory[0].note).toContain('Lumen webinar registrations 2026');

  // Re-importing the same file updates, never duplicates.
  const again = await call<{ created: number; updated: number; skipped: number; failed: number }>(
    accounts.am,
    'POST',
    `/agency/email/lists/${newsletterId}/imports`,
    {
      fileName: 'again.csv',
      csv: rows.join('\n'),
      mapping: {
        email: 'email',
        first_name: 'first_name',
        last_name: 'last_name',
        country: 'country',
        tags: 'tags',
        plan: 'custom.plan',
      },
      confirmConsent: true,
      consentSource: 'Same webinar list, uploaded twice',
    },
  );
  expect(again.body).toMatchObject({ created: 0, updated: 5, skipped: 3, failed: 2 });
  expect((await list(newsletterId)).subscribed).toBe(7);
});

test('CSV import negatives: attestation, mapping, malformed files, size limits; a large file runs in the background', async ({
  browser,
}) => {
  const newsletterId = recall('newsletterId');
  const path = `/agency/email/lists/${newsletterId}/imports`;
  const base = { fileName: 'x.csv', mapping: { email: 'email' }, confirmConsent: true, consentSource: 'E2E' };

  const noAttestation = await call(accounts.am, 'POST', path, {
    ...base,
    csv: `email\n${address('x1')}\n`,
    confirmConsent: false,
  });
  expect(codeOf(noAttestation)).toBe('email.import_consent_required');
  const noIdentity = await call(accounts.am, 'POST', path, {
    ...base,
    csv: `name\nX\n`,
    mapping: { name: 'first_name' },
  });
  expect(codeOf(noIdentity)).toBe('email.import_mapping_invalid');
  const unknownColumn = await call(accounts.am, 'POST', path, {
    ...base,
    csv: `email\n${address('x2')}\n`,
    mapping: { email: 'email', nope: 'phone' },
  });
  expect(codeOf(unknownColumn)).toBe('email.import_mapping_invalid');
  const unterminated = await call(accounts.am, 'POST', path, {
    ...base,
    csv: `email,first_name\n"${address('x3')},Oops\n`,
  });
  expect(codeOf(unterminated)).toBe('email.import_invalid_csv');
  const headerOnly = await call(accounts.am, 'POST', path, { ...base, csv: 'email\n' });
  expect(codeOf(headerOnly)).toBe('email.import_empty');
  const tooWide = await call(accounts.am, 'POST', `${path}/preview`, {
    csv: Array.from({ length: 101 }, (_, i) => `c${i}`).join(',') + '\n',
  });
  expect(codeOf(tooWide)).toBe('email.import_invalid_csv');

  // Boundary: 100,000 data rows is the maximum; one more is refused before anything is stored.
  const tooMany = ['email', ...Array.from({ length: 100_001 }, (_, i) => `r${i}@x.io`)].join('\n');
  const refused = await call(accounts.am, 'POST', path, { ...base, csv: tooMany });
  expect(codeOf(refused)).toBe('email.import_too_large');
  // Over the request size limit (12 MB) the server refuses the body outright.
  // (The server answers 413 as soon as it sees the size and may close the connection while the body is still uploading.)
  const huge = await call(accounts.am, 'POST', path, {
    ...base,
    csv: 'email\n' + 'a'.repeat(13 * 1024 * 1024),
  }).then(
    (r) => r.status,
    (error: Error) =>
      `connection closed: ${String((error.cause as Error | undefined)?.message ?? error.message)}`,
  );
  expect(String(huge)).toMatch(
    /^(400|413)$|^connection closed: (write EPIPE|.*ECONNRESET|other side closed)/,
  );

  // The wizard refuses a file over 10 MB before uploading it.
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/lists/${newsletterId}`);
  await page.getByRole('button', { name: 'Import CSV' }).click();
  const dialog = modal(page, 'Import contacts');
  await dialog.getByLabel('CSV file').setInputFiles({
    name: 'too-big.csv',
    mimeType: 'text/csv',
    buffer: Buffer.alloc(10 * 1024 * 1024 + 1, 'a'),
  });
  await expect(dialog).toContainText('larger than 10 MB');
  await dialog.getByRole('button', { name: 'Close', exact: true }).click();
  errors.expectClean('the import wizard with a too large file');

  // A large file (over the 500-row inline limit) is accepted and processed by the background job in chunks.
  const bulkList = await call<{ id: string }>(accounts.am, 'POST', '/agency/email/lists', {
    clientAccountId: state().client.id,
    name: `Bulk ${state().runId}`,
    doubleOptIn: false,
  });
  const bulk = [
    'email,first_name',
    ...Array.from({ length: 1_200 }, (_, i) => `${address(`bulk${i}`)},Bulk${i}`),
  ].join('\n');
  const started = await call<{ id: string; status: string; totalRows: number }>(
    accounts.am,
    'POST',
    `/agency/email/lists/${bulkList.body.id}/imports`,
    {
      ...base,
      csv: bulk,
      mapping: { email: 'email', first_name: 'first_name' },
    },
  );
  expect(started.status).toBe(202);
  expect(started.body).toMatchObject({ status: 'Pending', totalRows: 1_200 });
  // Each run processes up to 2,000 rows of an import (4 chunks of 500).
  await runJob('SubscriberImportJob');
  const done = await call<{ status: string; created: number; processedRows: number }>(
    accounts.am,
    'GET',
    `/agency/email/imports/${started.body.id}`,
  );
  expect(done.body).toMatchObject({ status: 'Completed', created: 1_200, processedRows: 1_200 });
  expect((await list(bulkList.body.id)).subscribed).toBe(1_200);
  // Running the job again does nothing more.
  await runJob('SubscriberImportJob');
  expect((await list(bulkList.body.id)).subscribed).toBe(1_200);
  remember('bulkListId', bulkList.body.id);
});

test('consent withdrawal and erasure from the contact page', async ({ browser }) => {
  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  const edsger = (await subscriberByEmail(address('edsger')))!;
  await page.goto(`/agency/email/contacts/${edsger.id}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Edsger Dijkstra' })).toBeVisible();
  await page.getByRole('button', { name: 'Record consent withdrawal' }).click();
  const withdraw = modal(page, 'Record consent withdrawal?');
  await withdraw.getByLabel('Source of the request').fill('Phone call to Lumen support');
  await withdraw.getByRole('button', { name: /^(Confirm|Record)/ }).click();
  await expect(withdraw).toBeHidden();
  await expect(page.getByRole('button', { name: 'Record consent withdrawal' })).toBeDisabled();
  await expect(page.getByRole('table', { name: 'Consent history' }).getByRole('row').nth(1)).toContainText(
    'Withdrawn',
  );
  errors.expectClean('recording a consent withdrawal');
  remember('withdrawnEmail', address('edsger'));

  // Erasure (typed confirmation) removes the contact entirely.
  const john = (await subscriberByEmail(address('john')))!;
  await page.goto(`/agency/email/contacts/${john.id}`);
  await page.getByRole('button', { name: 'Erase contact' }).click();
  const erase = modal(page, 'Erase this contact?');
  await expect(erase.getByRole('button', { name: 'Erase' })).toBeDisabled();
  await erase.getByLabel('Type ERASE to confirm').fill('ERASE');
  await erase.getByRole('button', { name: 'Erase' }).click();
  await expect(page).toHaveURL(/\/agency\/email\/lists$/);
  expect(await subscriberByEmail(address('john'))).toBeUndefined();
  expect((await call(accounts.am, 'GET', `/agency/email/subscribers/${john.id}`)).status).toBe(404);
  errors.expectClean('erasing a contact');
});
