import { type Page, expect, test } from '@playwright/test';
import { mailLink } from '../journeys/support/api';
import { signIn } from '../journeys/support/ui';
import { as, decide, submitViaApi } from './support/staff';
import { state } from './support/state';

/**
 * Participant lifecycle, part 4 — referrals: Pat shares the referral link → a friend signs up with it (the invitation is
 * shown, the referral appears for Pat as "Registered" and pays nothing yet) → the friend's first post is approved (the
 * qualifying action) → the referral qualifies and Pat's 5 USD reward waits for approval → finance approves it and it
 * joins Pat's approved earnings. Verifying the email alone does not qualify.
 */
test.describe.serial('referral', () => {
  let pat: Page;
  let friend: Page;
  let link = '';
  const s = () => state();

  test.beforeAll(async ({ browser }) => {
    pat = await (await browser.newContext()).newPage();
    await signIn(pat, s().pat, /\/app$/);
    friend = await (await browser.newContext()).newPage();
  });
  test.afterAll(async () => {
    await pat.context().close();
    await friend.context().close();
  });

  const referralRows = () =>
    pat.getByRole('table', { name: 'People you referred' }).getByRole('rowgroup').nth(1).getByRole('row');

  test('Pat sees the referral link, the terms and no referrals yet', async () => {
    await pat.goto('/app/referrals');
    await expect(pat.getByRole('heading', { level: 1, name: 'Referrals' })).toBeVisible();
    link = await pat.getByLabel('Referral link', { exact: true }).inputValue();
    const code = await pat.getByLabel('Referral code', { exact: true }).inputValue();
    expect(new URL(link).pathname).toBe('/register');
    expect(new URL(link).searchParams.get('ref')).toBe(code);
    const terms = pat.getByRole('region', { name: 'Program terms' });
    await expect(terms).toContainText('$5.00');
    await expect(terms).toContainText('only after they get their first post approved');
    await expect(terms).toContainText('Signing up alone doesn’t earn a reward.');
    await expect(pat.getByText('No referrals yet')).toBeVisible();
  });

  test('the friend signs up with the link and verifies the email', async () => {
    const f = s().friend;
    const url = new URL(link);
    await friend.goto(`${url.pathname}${url.search}`);
    await expect(friend.getByText('You were invited')).toBeVisible();
    await expect(friend.getByText(url.searchParams.get('ref')!, { exact: true })).toBeVisible();
    await friend.getByLabel('Email', { exact: true }).fill(f.email);
    await friend.getByLabel('Password', { exact: true }).fill(f.password);
    await friend.getByLabel('Display name').fill(f.displayName);
    await friend.getByLabel('Country').selectOption('GB');
    await friend.getByRole('checkbox', { name: /accept the participant rules/i }).check();
    await friend.getByRole('button', { name: 'Create account' }).click();
    await expect(friend.getByRole('heading', { name: 'Check your email' })).toBeVisible();

    const verify = await mailLink(f.email, '/verify-email', /verify/i);
    await friend.goto(`${verify.pathname}${verify.search}`);
    await expect(friend.getByRole('heading', { name: 'Your email is verified' })).toBeVisible();
  });

  test('Pat sees the friend as signed up — no reward for signing up', async () => {
    await pat.reload();
    await expect(referralRows()).toHaveCount(1);
    const row = referralRows().first();
    await expect(row).toContainText('Registered');
    await expect(row).not.toContainText(s().friend.email);
    await expect(pat.getByRole('group', { name: /^Signed up\b/ })).toContainText('1');
    await expect(pat.getByRole('group', { name: /^Qualified\b/ })).toContainText('0');

    await pat.goto('/app/earnings');
    await expect(pat.getByRole('group', { name: /^Pending\b/ })).toContainText('$0.00');
    await expect(pat.getByRole('group', { name: /^Approved\b/ })).toContainText('$11.00');
  });

  test('the friend’s first approved post qualifies the referral; the reward waits for approval', async () => {
    const f = s().friend;
    const friendApi = await as(f);
    await friendApi.post('/me/social-accounts', {
      platform: 'Instagram',
      handle: f.instagram,
      profileUrl: `https://www.instagram.com/${f.instagram}`,
      accountCreatedAt: new Date(Date.now() - 500 * 86_400_000).toISOString(),
      followerCount: 700,
    });
    const submission = await submitViaApi(f, s().main.id, `${f.postPrefix}A`, 301);
    await decide(s().reviewer2, submission.id, 'Approve');

    await pat.goto('/app/referrals');
    const row = referralRows().first();
    await expect(row).toContainText('Qualified');
    await expect(row).toContainText('Pending approval');
    await expect(pat.getByRole('group', { name: /^Qualified\b/ })).toContainText('1');
    await expect(pat.getByRole('group', { name: /^Reward pending\b/ })).toContainText('1');

    await pat.goto('/app/earnings');
    await expect(pat.getByRole('group', { name: /^Pending\b/ })).toContainText('$5.00');
    await expect(pat.getByRole('group', { name: /^Approved\b/ })).toContainText('$11.00');
  });

  test('finance approves the reward; it joins Pat’s approved earnings', async () => {
    const finance = await as(s().finance1);
    const pending = await finance.get<{
      items: { id: string; type: string; user: { email: string }; amount: number; concurrencyStamp: string }[];
    }>('/finance/pending-earnings?type=ReferralReward&pageSize=100');
    const reward = pending.items.find((e) => e.user.email.toLowerCase() === s().pat.email.toLowerCase());
    expect(reward, 'Pat’s referral reward is pending approval').toBeDefined();
    expect(reward!.amount).toBe(5);
    await finance.post(`/finance/pending-earnings/${reward!.id}/approve`, { concurrencyStamp: reward!.concurrencyStamp });
    // Approving twice (a retried request) is refused, not applied twice.
    await expect(
      finance.post(`/finance/pending-earnings/${reward!.id}/approve`, { concurrencyStamp: reward!.concurrencyStamp }),
    ).rejects.toMatchObject({ status: 409 });

    await pat.reload();
    await expect(pat.getByRole('group', { name: /^Pending\b/ })).toContainText('$0.00');
    await expect(pat.getByRole('group', { name: /^Approved\b/ })).toContainText('$16.00');
    await pat.goto('/app/referrals');
    await expect(referralRows().first()).toContainText('Approved');
    await expect(pat.getByRole('group', { name: /^Rewarded\b/ })).toContainText('1');
  });
});
