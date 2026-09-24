import { type Page, expect, test } from '@playwright/test';
import type { Credentials } from '../journeys/support/fixtures';
import {
  ApiSession,
  PASSWORD,
  accounts,
  emailFor,
  landing,
  registerUnverified,
  registerVerified,
  raw,
  signedIn,
  statusOf,
} from './support/auth';

/**
 * Object-level access (IDOR): one participant never reads or writes another's records by id, and one client
 * organisation never reaches another's; user-supplied names and texts containing markup are shown as text everywhere
 * they are rendered (the participant's own pages, the admin's user list and support inbox), never executed.
 */
const PAYLOAD = `<img src=x onerror="window.__xss=1"><script>window.__xss=2</script>`;

async function expectNoScriptRan(page: Page, where: string) {
  expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss), where).toBeUndefined();
  expect(await page.locator('img[src="x"]').count(), where).toBe(0);
}

test.describe.serial('IDOR and XSS', () => {
  let alice: Credentials;
  let bob: Credentials;
  let bobTicket: string;
  let xss: Credentials;
  const dialogs: string[] = [];

  test.beforeAll(async () => {
    alice = await registerVerified('alice', 'Alice Owner');
    bob = await registerVerified('bob', 'Bob Other');
    const bobApi = await ApiSession.login(bob.email, bob.password);
    bobTicket = (
      await bobApi.post<{ id: string }>('/me/support/tickets', {
        subject: `Bob private ${Date.now()}`,
        category: 'General',
        body: 'Only Bob and support may read this.',
      })
    ).id;
  });

  test('a participant cannot read or act on another participant’s ticket by id', async ({ browser }) => {
    const aliceApi = await ApiSession.login(alice.email, alice.password);
    expect(await statusOf(aliceApi.get(`/me/support/tickets/${bobTicket}`))).toBe(404);
    expect(
      await statusOf(aliceApi.post(`/me/support/tickets/${bobTicket}/messages`, { body: 'I am in' })),
    ).toBe(404);
    expect(await statusOf(aliceApi.post(`/me/support/tickets/${bobTicket}/close`))).toBe(404);
    const list = await aliceApi.get<{ items: { id: string }[] }>('/me/support/tickets');
    expect(list.items.map((t) => t.id)).not.toContain(bobTicket);

    const { context, page } = await signedIn(browser, alice, landing.participant);
    await page.goto(`/app/support/${bobTicket}`);
    await expect(page.getByText('This ticket isn’t available')).toBeVisible();
    await expect(page.getByText('Only Bob and support may read this.')).toHaveCount(0);
    await expect(page.getByText(/Bob private/)).toHaveCount(0);
    await context.close();

    // Bob's ticket is untouched.
    const bobApi = await ApiSession.login(bob.email, bob.password);
    const ticket = await bobApi.get<{ status: string; messages: unknown[] }>(
      `/me/support/tickets/${bobTicket}`,
    );
    expect(ticket.messages).toHaveLength(1);
  });

  test('participants cannot reach staff records by id either', async () => {
    const aliceApi = await ApiSession.login(alice.email, alice.password);
    const bobId = (await ApiSession.login(bob.email, bob.password)).user.id;
    for (const path of [
      `/admin/users/${bobId}`,
      `/admin/support/tickets/${bobTicket}`,
      `/finance/ledger?userId=${bobId}`,
    ]) {
      expect(await statusOf(aliceApi.get(path)), path).toBe(403);
    }
  });

  test('a client organisation never reaches another organisation’s data', async () => {
    const aurora = await ApiSession.login(accounts.auroraOwner.email, accounts.auroraOwner.password);
    const nimbus = await ApiSession.login(accounts.nimbusApprover.email, accounts.nimbusApprover.password);
    const ids = async (s: ApiSession) =>
      (await s.get<{ clientId: string }[]>('/client/orgs')).map((o) => o.clientId);
    const auroraId = (await ids(aurora))[0]!;
    expect(auroraId).toBeTruthy();
    expect(await ids(nimbus)).not.toContain(auroraId);
    for (const path of ['home', 'projects', 'deliverables']) {
      const status = await statusOf(nimbus.get(`/client/orgs/${auroraId}/${path}`));
      expect([403, 404], `nimbus → aurora ${path}`).toContain(status);
      expect(await statusOf(aurora.get(`/client/orgs/${auroraId}/${path}`)), `aurora ${path}`).toBe(200);
    }
  });

  test('markup in a display name is shown as text to the user, and to the admin', async ({ browser }) => {
    xss = { email: emailFor('xss'), password: PASSWORD, displayName: `Eve ${PAYLOAD}`.slice(0, 100) };
    await registerUnverified(xss);
    const { context, page } = await signedIn(browser, xss, landing.participant);
    page.on('dialog', (d) => {
      dialogs.push(d.message());
      void d.dismiss();
    });
    await expect(page.getByRole('button', { name: `Account menu for ${xss.displayName}` })).toBeVisible();
    await page.goto('/app/profile');
    await expect(page.getByLabel('Display name', { exact: true })).toHaveValue(xss.displayName);
    await expectNoScriptRan(page, 'participant pages');

    // A support ticket whose subject and body carry markup too.
    await page.goto('/app/support/new');
    await page.getByLabel('Category').selectOption('Account');
    await page.getByLabel('Subject').fill(`Help ${PAYLOAD}`.slice(0, 120));
    await page.getByLabel('How can we help?').fill(`Body ${PAYLOAD}`);
    await page.getByRole('button', { name: 'Send ticket' }).click();
    await expect(page.getByText(`Body ${PAYLOAD}`)).toBeVisible();
    await expectNoScriptRan(page, 'the participant ticket');
    await context.close();

    const admin = await signedIn(browser, accounts.admin, landing.admin);
    admin.page.on('dialog', (d) => {
      dialogs.push(d.message());
      void d.dismiss();
    });
    await admin.page.goto('/admin/users');
    await admin.page.getByRole('searchbox', { name: 'Search users' }).fill(xss.email);
    await expect(admin.page.getByRole('row').filter({ hasText: 'Eve <img' })).toBeVisible();
    await expectNoScriptRan(admin.page, 'the admin user list');
    await admin.page.getByRole('row').filter({ hasText: 'Eve <img' }).getByRole('link').first().click();
    await expect(admin.page.getByRole('heading', { level: 1 })).toContainText('Eve <img');
    await expectNoScriptRan(admin.page, 'the admin user page');

    await admin.page.goto('/admin/support');
    await expect(admin.page.getByText(/Help <img/).first()).toBeVisible();
    await admin.page
      .getByText(/Help <img/)
      .first()
      .click();
    await expect(admin.page.getByText(`Body ${PAYLOAD}`)).toBeVisible();
    await expectNoScriptRan(admin.page, 'the admin support ticket');
    await admin.context.close();
    expect(dialogs).toEqual([]);
  });

  test('the API stores the markup as plain text (it is escaped on output, not mangled on input)', async () => {
    const res = await raw('POST', '/auth/login', { body: { email: xss.email, password: xss.password } });
    expect((res.json!.user as { displayName: string }).displayName).toBe(xss.displayName);
    expect(res.headers.get('content-type')).toMatch(/^application\/json/);
  });
});
