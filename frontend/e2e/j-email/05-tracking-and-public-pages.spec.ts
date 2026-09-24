import { expect, test } from '@playwright/test';
import {
  APPLE_MPP_UA,
  HUMAN_UA,
  type Mail,
  accounts,
  actor,
  address,
  call,
  landing,
  linkPath,
  mailsWith,
  raw,
  recall,
  remember,
  report,
  state,
  subscriberByEmail,
  suppressions,
  watchErrors,
  visitorPage,
} from './support/email';

/**
 * What recipients do with the spring campaign (04-campaigns) and what the agency sees of it: open pixels (human and
 * machine), click redirects that only ever go to the stored link (forged, tampered and purpose-swapped tokens refused),
 * the public unsubscribe page (valid, tampered, reused), RFC 8058 one-click unsubscribe, the preference center
 * (topics, frequency, unsubscribe from all, opting back in), and a report whose numbers match exactly these events.
 */
const AUDIENCE = ['ada', 'grace', 'alan', 'margaret', 'radia'];

function springMails(): { who: string; mail: Mail }[] {
  const marker = `spring is here ${state().runId}`;
  return AUDIENCE.flatMap((who) => mailsWith(address(who), marker).map((mail) => ({ who, mail })));
}

/** The spring recipients, in a fixed order, each with their one email. */
function recipients() {
  const all = springMails();
  expect(all).toHaveLength(4);
  return all;
}

test('open pixel: human opens counted once per person, machine opens apart, forged tokens change nothing', async () => {
  const springId = recall('springId');
  const [r1, r2] = recipients();
  const pixel = linkPath(r1.mail, '/e/o/');
  const before = await report(springId);
  expect(before).toMatchObject({ uniqueOpens: 0, totalOpens: 0, machineOpens: 0 });

  for (let i = 0; i < 2; i++) {
    const res = await raw('GET', pixel, { headers: { 'User-Agent': HUMAN_UA } });
    expect(res.status).toBe(200);
    expect(res.headers.get('content-type')).toBe('image/gif');
    expect(res.headers.get('cache-control')).toContain('no-store');
  }
  // Apple Mail Privacy Protection prefetch: stored, but never a human open.
  expect(
    (await raw('GET', linkPath(r2.mail, '/e/o/'), { headers: { 'User-Agent': APPLE_MPP_UA } })).status,
  ).toBe(200);
  // A forged or tampered token still answers the pixel (validity is not revealed) and records nothing.
  const token = pixel.slice('/e/o/'.length, -'.gif'.length);
  const tampered = `${token.slice(0, -3)}${token.endsWith('AAA') ? 'BBB' : 'AAA'}`;
  expect((await raw('GET', `/e/o/${tampered}.gif`, { headers: { 'User-Agent': HUMAN_UA } })).status).toBe(
    200,
  );
  expect((await raw('GET', '/e/o/not-a-token.gif', { headers: { 'User-Agent': HUMAN_UA } })).status).toBe(
    200,
  );
  // An open token is not a click token (the purpose is signed).
  expect((await raw('GET', `/e/c/${token}`, { headers: { 'User-Agent': HUMAN_UA } })).status).toBe(404);

  expect(await report(springId)).toMatchObject({ uniqueOpens: 1, totalOpens: 2, machineOpens: 1 });
  remember('openedBy', r1.who);
});

test('click redirect: only to the stored link, through the web origin; scanners and forged tokens do not count', async ({
  browser,
}) => {
  const springId = recall('springId');
  const [r1, , r3] = recipients();
  const click = linkPath(r1.mail, '/e/c/');

  const res = await raw('GET', click, { headers: { 'User-Agent': HUMAN_UA } });
  expect(res.status).toBe(302);
  expect(res.headers.get('location')).toBe('https://lumen.example/spring?src=email');
  expect(res.headers.get('referrer-policy')).toBe('no-referrer');
  expect(res.headers.get('cache-control')).toContain('no-store');

  // A link scanner follows it too: redirected, but not a human click.
  const scanner = await raw('GET', linkPath(r3.mail, '/e/c/'), { headers: { 'User-Agent': 'curl/8.5.0' } });
  expect(scanner.status).toBe(302);

  // Open-redirect attempts: forged tokens, a URL instead of a token, a truncated token — never a redirect.
  const token = click.slice('/e/c/'.length);
  for (const forged of [
    `${token.slice(0, -2)}${token.endsWith('AA') ? 'BB' : 'AA'}`,
    token.split('.')[0],
    encodeURIComponent('https://evil.example/phish'),
    'x'.repeat(200),
  ]) {
    const r = await raw('GET', `/e/c/${forged}`, { headers: { 'User-Agent': HUMAN_UA } });
    expect(r.status, forged).toBe(404);
    expect(r.headers.get('location')).toBeNull();
  }

  // The recipient clicks in a real browser (desktop mail client): the web origin's /e/ route redirects the browser to
  // the landing page (which is not reachable from the test machine, so only the redirected request is checked).
  const reader = await visitorPage(browser, { userAgent: HUMAN_UA });
  const landingRequest = reader.waitForRequest('https://lumen.example/spring?src=email');
  await reader.goto(click).catch(() => undefined);
  const followed = await landingRequest;
  expect(followed.redirectedFrom()?.url()).toMatch(new RegExp(`${click.replace(/[.]/g, '\\.')}$`));
  await reader.context().close();

  const r = await report(springId);
  // r1 clicked twice (fetch + browser): one unique human click, two in total; the scanner's click is excluded.
  expect(r).toMatchObject({ uniqueClicks: 1, totalClicks: 2 });
  expect(r.links).toEqual([
    expect.objectContaining({
      url: 'https://lumen.example/spring?src=email',
      uniqueClicks: 1,
      totalClicks: 2,
    }),
  ]);
  // A click is also an open for that person (still one unique open).
  expect(r.uniqueOpens).toBe(1);
});

test('unsubscribe page: opening changes nothing, a tampered link is refused, the button unsubscribes, reuse is harmless', async ({
  browser,
}) => {
  const [, r2] = recipients();
  const unsubscribe = linkPath(r2.mail, '/e/u/');
  const token = unsubscribe.slice('/e/u/'.length);
  // The tracking route only redirects to the confirmation page (link scanners must not unsubscribe people).
  const redirect = await raw('GET', unsubscribe);
  expect(redirect.status).toBe(302);
  expect(new URL(redirect.headers.get('location')!).pathname).toBe(`/email/unsubscribe/${token}`);
  expect(await subscriberByEmail(address(r2.who))).toMatchObject({ status: 'Subscribed' });

  const visitor = await visitorPage(browser);
  const errors = watchErrors(visitor);
  const tampered = `${token.slice(0, -2)}${token.endsWith('AA') ? 'BB' : 'AA'}`;
  await visitor.goto(`/email/unsubscribe/${tampered}`);
  await visitor.getByRole('button', { name: 'Unsubscribe' }).click();
  await expect(visitor.getByRole('alert')).toContainText('This link is invalid or has expired.');
  errors.ignore(/HTTP 404 POST .*\/public\/email\/unsubscribe\//);
  expect(await subscriberByEmail(address(r2.who))).toMatchObject({ status: 'Subscribed' });

  await visitor.goto(unsubscribe);
  await expect(visitor).toHaveURL(new RegExp(`/email/unsubscribe/${token.replace(/[.]/g, '\\.')}$`));
  await expect(visitor.getByRole('heading', { level: 1, name: 'Unsubscribe' })).toBeVisible();
  expect(await subscriberByEmail(address(r2.who))).toMatchObject({ status: 'Subscribed' });
  await visitor.getByRole('button', { name: 'Unsubscribe' }).click();
  await expect(visitor.getByRole('heading', { level: 1, name: 'You are unsubscribed' })).toBeVisible();
  expect(await subscriberByEmail(address(r2.who))).toMatchObject({
    status: 'Unsubscribed',
    emailConsent: 'Withdrawn',
  });
  expect((await suppressions(address(r2.who))).map((s) => s.reason)).toEqual(['Unsubscribed']);

  // The same link again (reused): still a success, nothing duplicated.
  await visitor.goto(`/email/unsubscribe/${token}`);
  await visitor.getByRole('button', { name: 'Unsubscribe' }).click();
  await expect(visitor.getByRole('heading', { level: 1, name: 'You are unsubscribed' })).toBeVisible();
  expect(await suppressions(address(r2.who))).toHaveLength(1);
  errors.expectClean('the public unsubscribe page');
  remember('unsubscribedByPage', r2.who);
});

test('RFC 8058 one-click unsubscribe (List-Unsubscribe-Post) from the mail client', async () => {
  const [, , r3] = recipients();
  const header = r3.mail.headers['list-unsubscribe'];
  const url = new URL(header.replace(/^<|>$/g, ''));
  const tampered = await raw('POST', `${url.pathname.slice(0, -2)}xx`, {
    text: 'List-Unsubscribe=One-Click',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  });
  expect(tampered.status).toBe(404);
  const res = await raw('POST', url.pathname, {
    text: 'List-Unsubscribe=One-Click',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  });
  expect(res.status).toBe(200);
  expect(res.body).toBe('You have been unsubscribed.');
  expect(res.headers.get('location')).toBeNull();
  expect(await subscriberByEmail(address(r3.who))).toMatchObject({ status: 'Unsubscribed' });
  // Mail clients retry: a second POST is harmless.
  expect((await raw('POST', url.pathname, { text: 'List-Unsubscribe=One-Click' })).status).toBe(200);
  expect(await suppressions(address(r3.who))).toHaveLength(1);
  remember('unsubscribedByOneClick', r3.who);
});

test('preference center: topics and frequency, unsubscribe from everything, opt back in; forged links refused', async ({
  browser,
}) => {
  const [, , , r4] = recipients();
  const prefsPath = linkPath(r4.mail, '/email/preferences/');
  const visitor = await visitorPage(browser);
  const errors = watchErrors(visitor);

  await visitor.goto(`${prefsPath.slice(0, -2)}zz`);
  await expect(visitor.getByRole('alert')).toContainText('This link is invalid or has expired.');
  errors.ignore(/HTTP 404 GET .*\/public\/email\/preferences\//);

  await visitor.goto(prefsPath);
  await expect(visitor.getByRole('heading', { level: 1, name: 'Email preferences' })).toBeVisible();
  // The address is masked; the workspace is named.
  await expect(visitor.getByRole('main')).toContainText(`Lumen Labs Ltd ${state().runId}`);
  await expect(visitor.getByRole('main')).toContainText(`${address(r4.who).slice(0, 2)}•`);
  const newsletter = visitor.getByRole('switch', { name: `Newsletter ${state().runId}` });
  const updates = visitor.getByRole('switch', { name: `Product updates ${state().runId}` });
  await expect(newsletter).toHaveAttribute('aria-checked', 'true');
  await expect(updates).toHaveAttribute('aria-checked', 'false');

  await updates.click();
  await visitor.getByRole('radio', { name: 'At most one a week' }).check();
  await visitor.getByRole('button', { name: 'Save preferences' }).click();
  await expect(visitor.getByRole('status').filter({ hasText: 'Your preferences were saved.' })).toBeVisible();
  const contact = (await subscriberByEmail(address(r4.who)))!;
  const detail = await call<{ frequency: string; lists: { listName: string; status: string }[] }>(
    accounts.am,
    'GET',
    `/agency/email/subscribers/${contact.id}`,
  );
  expect(detail.body.frequency).toBe('Weekly');
  expect(detail.body.lists.find((l) => l.listName === `Product updates ${state().runId}`)?.status).toBe(
    'Subscribed',
  );
  remember('weeklyContact', r4.who);

  // Unsubscribe from everything, then opt back into one topic (the contact proved control of the inbox).
  await visitor.getByRole('button', { name: 'Unsubscribe from all' }).click();
  await expect(visitor.getByText('You are unsubscribed from all marketing email.')).toBeVisible();
  await expect(newsletter).toHaveAttribute('aria-checked', 'false');
  expect(await subscriberByEmail(address(r4.who))).toMatchObject({
    status: 'Unsubscribed',
    emailConsent: 'Withdrawn',
  });
  expect(await suppressions(address(r4.who))).toHaveLength(1);

  await newsletter.click();
  await visitor.getByRole('button', { name: 'Save preferences' }).click();
  await expect(visitor.getByText('You are unsubscribed from all marketing email.')).toBeHidden();
  await expect(newsletter).toHaveAttribute('aria-checked', 'true');
  expect(await subscriberByEmail(address(r4.who))).toMatchObject({
    status: 'Subscribed',
    emailConsent: 'Granted',
  });
  expect(await suppressions(address(r4.who))).toHaveLength(0);
  errors.expectClean('the preference center');
});

test('the campaign report matches exactly what the recipients did', async ({ browser }) => {
  const springId = recall('springId');
  const r = await report(springId);
  expect(r).toMatchObject({
    recipients: 5,
    sent: 4,
    skipped: 1,
    failed: 0,
    delivered: 4,
    deliveredIsEstimated: true,
    uniqueOpens: 1,
    totalOpens: 2,
    machineOpens: 1,
    uniqueClicks: 1,
    totalClicks: 2,
    // The unsubscribe page, the one-click unsubscribe and the preference center's "Unsubscribe from all" (all three
    // reached from this campaign's email; the last contact opted back in afterwards, which does not undo the event).
    unsubscribes: 3,
    complaints: 0,
    hardBounces: 0,
  });

  const page = await actor(browser, accounts.am, landing.agency);
  const errors = watchErrors(page);
  await page.goto(`/agency/email/campaigns/${springId}/report`);
  await expect(page.getByRole('group', { name: 'Open rate', exact: true })).toContainText('25%');
  await expect(page.getByRole('group', { name: 'Open rate', exact: true })).toContainText(
    '1 unique · 2 total',
  );
  await expect(page.getByRole('group', { name: 'Click rate', exact: true })).toContainText(
    '1 unique · 2 total',
  );
  await expect(page.getByRole('group', { name: 'Unsubscribes', exact: true })).toContainText('3');
  await expect(page.getByRole('table', { name: 'Link clicks' })).toContainText(
    'https://lumen.example/spring?src=email',
  );
  errors.expectClean('the campaign report page');

  // The recipients' activity is on their contact page.
  const opener = (await subscriberByEmail(address(recall('openedBy'))))!;
  const activity = await call<{ activity: { type: string; isMachine: boolean }[] }>(
    accounts.am,
    'GET',
    `/agency/email/subscribers/${opener.id}`,
  );
  expect(activity.body.activity.filter((a) => a.type === 'Open' && !a.isMachine)).toHaveLength(2);
  expect(activity.body.activity.filter((a) => a.type === 'Click' && !a.isMachine)).toHaveLength(2);
});
