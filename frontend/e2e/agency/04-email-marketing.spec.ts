import { expect, test } from '@playwright/test';
import {
  API_URL,
  ApiSession,
  accounts,
  actor,
  clients,
  landing,
  latestMail,
  modal,
  runId,
  toast,
  watchErrors,
} from './support/agency';

/**
 * Email marketing with client approval, in the Aurora Skincare workspace (the demo seed makes Aurora require client
 * approval before any campaign is sent):
 *   the account manager creates a list, adds a consenting contact, drafts a campaign, checks the rendered preview and
 *   confirms the send → it waits for the client → the Aurora owner approves it in the client portal → the send job runs
 *   (triggered through the admin jobs API: background jobs are off in e2e) → the contact's email carries an unsubscribe
 *   link → the public unsubscribe page does nothing until the button is pressed.
 */
test('campaign draft → preview → client approval → public unsubscribe needs a button press', async ({
  browser,
}) => {
  const id = runId();
  const listName = `E2E list ${id}`;
  const campaignName = `E2E autumn launch ${id}`;
  const subject = `Autumn skincare launch ${id}`;
  const contactEmail = `reader.${id}@e2e.optimizeall.test`;

  // ---------------------------------------------------------------- staff: list + contact
  const am = await actor(browser, accounts.am, landing.agency);
  const amErrors = watchErrors(am);
  await am
    .getByRole('navigation', { name: 'Agency navigation' })
    .getByRole('link', { name: 'Email marketing' })
    .click();
  await am
    .getByRole('navigation', { name: 'Email marketing sections' })
    .getByRole('link', { name: 'Audience' })
    .click();
  await am.getByLabel('Workspace').selectOption({ label: clients.aurora.name });
  await expect(am.getByRole('heading', { level: 1, name: 'Audience' })).toBeVisible();
  await am.getByRole('button', { name: 'New list' }).click();
  const listDialog = modal(am, 'New list');
  await listDialog.getByLabel('Name').fill(listName);
  const doubleOptIn = listDialog.getByRole('switch', { name: /Double opt-in/ });
  if ((await doubleOptIn.getAttribute('aria-checked')) === 'true') await doubleOptIn.click();
  await expect(doubleOptIn).toHaveAttribute('aria-checked', 'false');
  await listDialog.getByRole('button', { name: /^(Create|Save)/ }).click();
  await expect(toast(am, 'List created')).toBeVisible();
  await am.getByRole('table', { name: 'Lists' }).getByRole('link', { name: listName }).click();
  await expect(am.getByRole('heading', { level: 1, name: listName })).toBeVisible();

  await am.getByRole('button', { name: 'Add contact' }).click();
  const contactDialog = modal(am, 'Add a contact');
  await contactDialog.getByRole('textbox', { name: 'Email' }).fill(contactEmail);
  await contactDialog.getByLabel('First name').fill('Robin');
  await contactDialog.getByRole('checkbox', { name: /gave consent to receive email marketing/ }).check();
  await contactDialog
    .getByLabel('Where and how was consent given?')
    .fill('E2E: signed up at the Aurora pop-up store');
  await contactDialog.getByRole('button', { name: /^(Add|Save)/ }).click();
  await expect(toast(am, 'Contact added')).toBeVisible();
  await expect(am.getByRole('group', { name: 'Subscribed', exact: true })).toContainText(
    /^Subscribed1(?!\d)/,
  );

  // ---------------------------------------------------------------- staff: campaign draft + preview
  await am
    .getByRole('navigation', { name: 'Email marketing sections' })
    .getByRole('link', { name: 'Campaigns' })
    .click();
  await am.getByRole('link', { name: 'New campaign' }).click();
  await expect(am.getByRole('heading', { level: 1, name: 'New email campaign' })).toBeVisible();
  const settings = am.getByRole('form', { name: 'Campaign settings' });
  await settings.getByLabel('Campaign name', { exact: true }).fill(campaignName);
  await settings.getByLabel('List', { exact: true }).selectOption({ label: listName });
  const from = settings.getByLabel('From (verified sender)');
  await from.selectOption({ index: 1 });
  await settings.getByLabel('Subject line', { exact: true }).fill(subject);
  await settings.getByLabel('Preview text (optional)').fill('New serums, just in time for autumn.');
  await settings.getByLabel('Title', { exact: true }).first().fill('Meet the autumn collection');
  // The default design's button starts with a placeholder "https://" link that must be completed before saving.
  const button = settings
    .getByRole('region', { name: 'Content blocks' })
    .getByRole('listitem')
    .filter({ has: am.getByRole('heading', { name: /^\d+\. Button$/ }) });
  await button.getByLabel('Link', { exact: true }).fill('https://auroraskin.example/autumn');
  await am.getByRole('button', { name: 'Save draft' }).click();
  await expect(toast(am, 'Campaign saved')).toBeVisible();
  await expect(am).toHaveURL(/\/agency\/email\/campaigns\/[0-9a-f-]{36}$/);
  await expect(am.getByRole('heading', { level: 1, name: campaignName })).toBeVisible();

  const preview = am.getByRole('region', { name: 'Preview' });
  await expect(preview.getByText(`Subject: ${subject}`)).toBeVisible();
  await expect(
    am.frameLocator('iframe[title="Email preview (desktop)"]').getByText('Meet the autumn collection'),
  ).toBeVisible();
  await preview.getByRole('radio', { name: 'Mobile (375 px)' }).check();
  await expect(
    am.frameLocator('iframe[title="Email preview (mobile)"]').getByText('Meet the autumn collection'),
  ).toBeVisible();

  // Account managers have email.send: they confirm the send themselves. Aurora requires client approval, so it waits.
  await am.getByRole('button', { name: 'Review & send' }).click();
  const send = modal(am, `Send “${campaignName}”?`);
  await expect(send.getByText('Required before the send job starts')).toBeVisible();
  await send.getByLabel(`Type ${campaignName} to confirm`).fill(campaignName);
  await send.getByRole('button', { name: 'Send now' }).click();
  await expect(toast(am, 'Waiting for client approval')).toBeVisible();
  amErrors.expectClean('the email campaign editor and send confirmation');

  // ---------------------------------------------------------------- client: approve in the portal
  const owner = await actor(browser, accounts.auroraOwner, landing.client);
  const ownerErrors = watchErrors(owner);
  await owner.goto('/client/email');
  const waiting = owner.getByRole('table', { name: 'Campaigns awaiting approval' });
  await waiting.getByRole('link', { name: campaignName }).click();
  await expect(owner.getByRole('heading', { level: 1, name: campaignName })).toBeVisible();
  await owner.getByRole('button', { name: 'Approve', exact: true }).click();
  await modal(owner, 'Approve this campaign?').getByRole('button', { name: 'Approve' }).click();
  await expect(toast(owner, 'Campaign approved')).toBeVisible();
  ownerErrors.expectClean('the client email approval');

  // ---------------------------------------------------------------- the send job runs (arranged through the API)
  const admin = await ApiSession.login(accounts.admin.email, accounts.admin.password);
  await admin.post('/admin/jobs/CampaignSendJob/run');
  const mail = await latestMail(contactEmail, new RegExp(subject));
  const unsubscribeLink = mail.links.map((l) => new URL(l)).find((u) => u.pathname.startsWith('/e/u/'));
  expect(unsubscribeLink, `unsubscribe link in "${mail.subject}"`).toBeDefined();
  const token = unsubscribeLink!.pathname.split('/').pop()!;

  // The tracking route only redirects to the confirmation page (link scanners must not unsubscribe people).
  const redirect = await fetch(`${API_URL}/e/u/${token}`, { redirect: 'manual' });
  expect(redirect.status).toBe(302);
  expect(new URL(redirect.headers.get('location')!).pathname).toBe(`/email/unsubscribe/${token}`);

  // ---------------------------------------------------------------- public unsubscribe page
  const amApi = await ApiSession.login(accounts.am.email, accounts.am.password);
  const listStats = async () => {
    const lists = await amApi.get<{ name: string; subscribed: number; unsubscribed: number }[]>(
      `/agency/email/lists?clientId=${await auroraId(amApi)}`,
    );
    return lists.find((l) => l.name === listName)!;
  };

  const visitor = await (await browser.newContext()).newPage();
  const visitorErrors = watchErrors(visitor);
  await visitor.goto(`/email/unsubscribe/${token}`);
  await expect(visitor.getByRole('heading', { level: 1, name: 'Unsubscribe' })).toBeVisible();
  await expect(visitor.getByRole('button', { name: 'Unsubscribe' })).toBeVisible();
  // Opening the page changed nothing.
  expect(await listStats()).toMatchObject({ subscribed: 1, unsubscribed: 0 });

  await visitor.getByRole('button', { name: 'Unsubscribe' }).click();
  await expect(visitor.getByRole('heading', { level: 1, name: 'You are unsubscribed' })).toBeVisible();
  await expect.poll(async () => (await listStats()).unsubscribed).toBe(1);
  visitorErrors.expectClean('the public unsubscribe page');
});

async function auroraId(api: ApiSession): Promise<string> {
  const workspaces =
    await api.get<{ clientAccountId: string | null; name: string }[]>('/agency/email/workspaces');
  const aurora = workspaces.find((w) => w.name === clients.aurora.name);
  if (!aurora?.clientAccountId) throw new Error('No Aurora Skincare email workspace');
  return aurora.clientAccountId;
}
