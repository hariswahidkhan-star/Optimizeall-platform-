import { type Page, expect, test } from '@playwright/test';
import { ApiError, ApiSession } from '../journeys/support/api';
import { makePng, pngFile } from '../journeys/support/png';
import { modal, signIn } from '../journeys/support/ui';
import { as, decide, resolveAppeal } from './support/staff';
import { remember, state } from './support/state';

/**
 * Participant lifecycle, part 3 — submissions through every outcome:
 *   A: submitted in the UI → the reviewer asks for a correction → resubmitted (after a reload that drops the edit) →
 *      approved: earnings of 5 + 1 USD move from pending to approved
 *   B: withdrawn with a reason → the same post submitted again (the post is released) → rejected
 *   C: submitted with a double click (one submission) → rejected → appealed (the 20-character minimum at its boundary)
 *      → upheld by a second reviewer
 *   D: two identical requests in parallel (one wins, the other is a duplicate) → approved
 * plus the negatives: client validation, a link from the wrong platform, a reload in the middle of the form, the same
 * post with www./utm as a duplicate (then fixed and retried in the same dialog), and another participant's submission
 * (404 in the API and "isn't available" in the UI).
 */
test.describe.serial('submissions', () => {
  let page: Page;
  let api: ApiSession;
  const s = () => state();
  const code = (suffix: string) => `${s().pat.postPrefix}${suffix}`;
  const ids: Record<string, string> = {};

  test.beforeAll(async ({ browser }) => {
    page = await (await browser.newContext()).newPage();
    await signIn(page, s().pat, /\/app$/);
    api = await as(s().pat);
  });
  test.afterAll(async () => {
    await page.context().close();
  });

  const proofDialog = () => page.getByRole('dialog', { name: 'Submit proof of your post' });
  const mine = async () =>
    (await api.get<{ items: { id: string; postUrl: string; status: string }[] }>('/me/submissions?pageSize=50'))
      .items;
  const withCode = async (suffix: string) => (await mine()).filter((x) => x.postUrl.includes(`/${code(suffix)}/`));

  async function openProof() {
    await page.goto(`/app/campaigns/${s().main.slug}`);
    await page.getByRole('button', { name: 'Submit proof' }).click();
    await expect(proofDialog()).toBeVisible();
    return proofDialog();
  }

  test('client validation: no link, not a link, no screenshot', async () => {
    const d = await openProof();
    await d.getByRole('button', { name: 'Submit proof' }).click();
    const url = d.getByLabel('Link to your post');
    await expect(url).toBeFocused();
    await expect(url).toHaveAccessibleDescription(/Paste the public link to your post/);
    await expect(d.getByText('This campaign needs a screenshot of your post.')).toBeVisible();

    await url.fill('instagram post A');
    await d.getByRole('button', { name: 'Submit proof' }).click();
    await expect(url).toHaveAccessibleDescription(/starting with https:\/\//);
    expect(await mine()).toEqual([]);
  });

  test('a TikTok link for the Instagram profile is refused on the link field', async () => {
    const d = proofDialog();
    const url = d.getByLabel('Link to your post');
    await url.fill(`https://www.tiktok.com/@${s().pat.tiktok}/video/7300000000000000001`);
    await d.getByLabel('Screenshot of your post').setInputFiles(pngFile(201));
    await d.getByRole('button', { name: 'Submit proof' }).click();
    await expect(url).toHaveAttribute('aria-invalid', 'true');
    await expect(url).toHaveAccessibleDescription(/not a Instagram post link|not an Instagram post link/);
    expect(await mine()).toEqual([]);
  });

  test('a reload in the middle of the form submits nothing', async () => {
    const d = proofDialog();
    await d.getByLabel('Link to your post').fill(`https://www.instagram.com/p/${code('A')}/`);
    await d.getByLabel('Caption you used').fill('Half-finished caption');
    await page.reload();
    await expect(page.getByRole('heading', { level: 1, name: s().main.title })).toBeVisible();
    await expect(proofDialog()).toBeHidden();
    expect(await mine()).toEqual([]);
  });

  test('A: submits proof with a screenshot; it is pending with an estimate of 6 USD', async () => {
    const d = await openProof();
    await d.getByLabel('Link to your post').fill(`https://instagram.com/p/${code('A')}/`);
    await d.getByLabel('Caption you used').fill(`Loving it! ${s().main.hashtag}`);
    await d.getByLabel('Screenshot of your post').setInputFiles(pngFile(202));
    await d.getByRole('button', { name: 'Submit proof' }).click();

    await expect(page).toHaveURL(/\/app\/submissions\/[0-9a-f-]{36}$/);
    ids.A = page.url().split('/').pop()!;
    remember('submissionA', ids.A);
    await expect(page.getByText('Pending', { exact: true }).first()).toBeVisible();
    const reward = page.getByRole('region', { name: 'Reward' });
    await expect(reward).toContainText(/Estimated reward\s*\$6\.00/);
    await expect(reward).toContainText('Earnings are created when the post is approved.');
    const shot = page.getByRole('img', { name: 'Screenshot you submitted of the post' });
    await expect.poll(() => shot.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(400);
  });

  test('the same post with www. and ?utm= is a duplicate; fixing the link and retrying works', async () => {
    const d = await openProof();
    const url = d.getByLabel('Link to your post');
    await url.fill(`https://www.instagram.com/p/${code('A')}/?utm_source=x`);
    await d.getByLabel('Screenshot of your post').setInputFiles(pngFile(203));
    await d.getByRole('button', { name: 'Submit proof' }).click();
    await expect(url).toHaveAttribute('aria-invalid', 'true');
    await expect(url).toHaveAccessibleDescription(/already been submitted/i);
    await expect(url).toBeFocused();

    // Retry after the failure, in the same dialog: the other fields (and the screenshot) are kept.
    await url.fill(`https://www.instagram.com/p/${code('B')}/`);
    await expect(url).not.toHaveAttribute('aria-invalid', 'true');
    await d.getByRole('button', { name: 'Submit proof' }).click();
    await expect(page).toHaveURL(/\/app\/submissions\/[0-9a-f-]{36}$/);
    ids.B = page.url().split('/').pop()!;
    expect(await withCode('A')).toHaveLength(1);
    expect(await withCode('B')).toHaveLength(1);
  });

  test('C: a double click on "Submit proof" creates exactly one submission', async () => {
    const d = await openProof();
    await d.getByLabel('Link to your post').fill(`https://www.instagram.com/p/${code('C')}/`);
    await d.getByLabel('Screenshot of your post').setInputFiles(pngFile(204));
    await d.getByRole('button', { name: 'Submit proof' }).dblclick();
    await expect(page).toHaveURL(/\/app\/submissions\/[0-9a-f-]{36}$/);
    ids.C = page.url().split('/').pop()!;
    const created = await withCode('C');
    expect(created.map((x) => x.id)).toEqual([ids.C]);
  });

  test('D: two identical requests in parallel — one is created, the other is a duplicate', async () => {
    const accounts = await api.get<{ items: { id: string; platform: string }[] }>('/me/social-accounts');
    const instagram = accounts.items.find((a) => a.platform === 'Instagram')!;
    const send = (seed: number) => {
      const form = new FormData();
      form.append('campaignId', s().main.id);
      form.append('socialAccountId', instagram.id);
      form.append('platform', 'Instagram');
      form.append('postUrl', `https://www.instagram.com/p/${code('D')}/`);
      form.append('postedAt', new Date(Date.now() - 120_000).toISOString());
      form.append('screenshot', new Blob([makePng(seed)], { type: 'image/png' }), 'd.png');
      return api.upload<{ id: string }>('/me/submissions', form);
    };
    const results = await Promise.allSettled([send(205), send(206)]);
    const created = results.filter((r) => r.status === 'fulfilled');
    const refused = results.filter((r) => r.status === 'rejected').map((r) => (r as PromiseRejectedResult).reason);
    expect(created).toHaveLength(1);
    expect(refused).toHaveLength(1);
    expect(refused[0]).toBeInstanceOf(ApiError);
    expect(refused[0]).toMatchObject({ status: 409, code: 'submission.duplicate_url' });
    ids.D = (created[0] as PromiseFulfilledResult<{ id: string }>).value.id;
    expect((await withCode('D')).map((x) => x.id)).toEqual([ids.D]);
  });

  test('another participant’s submission is not found — in the API and in the UI', async () => {
    const { stranger } = s();
    await expect(api.get(`/me/submissions/${stranger.submissionId}`)).rejects.toMatchObject({ status: 404 });
    await expect(
      api.post(`/me/submissions/${stranger.submissionId}/withdraw`, { confirm: true }),
    ).rejects.toMatchObject({ status: 404 });
    const strangerApi = await ApiSession.login(stranger.email, stranger.password);
    await expect(strangerApi.get(`/me/submissions/${ids.A}`)).rejects.toMatchObject({ status: 404 });

    await page.goto(`/app/submissions/${stranger.submissionId}`);
    await expect(page.getByText('This submission isn’t available')).toBeVisible();
    await expect(page.getByText('Stranger post')).toHaveCount(0);
  });

  test('B: withdrawn with a reason; the same post can then be submitted again', async () => {
    await page.goto(`/app/submissions/${ids.B}`);
    await page.getByRole('button', { name: 'Withdraw submission' }).click();
    const confirm = modal(page, 'Withdraw this submission?');
    await confirm.getByLabel('Reason (optional)').fill('Posted the wrong photo.');
    await confirm.getByRole('button', { name: 'Withdraw', exact: true }).click();
    await expect(confirm).toBeHidden();
    await expect(page.getByText('You withdrew this submission')).toBeVisible();
    await expect(page.getByText('Withdrawn', { exact: true }).first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Withdraw submission' })).toHaveCount(0);
    // A retried withdrawal is refused, not applied twice.
    await expect(api.post(`/me/submissions/${ids.B}/withdraw`, { confirm: true })).rejects.toMatchObject({
      status: 409,
      code: 'submission.not_withdrawable',
    });

    const d = await openProof();
    await d.getByLabel('Link to your post').fill(`https://www.instagram.com/p/${code('B')}/`);
    await d.getByLabel('Screenshot of your post').setInputFiles(pngFile(207));
    await d.getByRole('button', { name: 'Submit proof' }).click();
    await expect(page).toHaveURL(/\/app\/submissions\/[0-9a-f-]{36}$/);
    ids.B2 = page.url().split('/').pop()!;
    expect(ids.B2).not.toBe(ids.B);
    expect((await withCode('B')).map((x) => x.status).sort()).toEqual(['Pending', 'Withdrawn']);
  });

  test('A: the reviewer asks for a correction; a reload drops the edit; the participant resubmits', async () => {
    await decide(s().reviewer1, ids.A, 'RequestCorrection', `Please add ${s().main.hashtag} to the caption.`);

    await page.goto(`/app/submissions/${ids.A}`);
    await expect(page.getByRole('status', { name: 'The reviewer asked for a correction' })).toContainText(
      `Please add ${s().main.hashtag} to the caption.`,
    );
    const form = page.getByRole('region', { name: 'Edit & resubmit' });
    const caption = form.getByLabel('Caption you used');
    await expect(caption).toHaveValue(`Loving it! ${s().main.hashtag}`);
    await caption.fill('Edited but not sent');
    await page.reload();
    await expect(page.getByRole('region', { name: 'Edit & resubmit' }).getByLabel('Caption you used')).toHaveValue(
      `Loving it! ${s().main.hashtag}`,
    );

    // A link that is not a link is caught; the fixed one goes through.
    const fresh = page.getByRole('region', { name: 'Edit & resubmit' });
    await fresh.getByLabel('Link to your post').fill('instagram.com');
    await fresh.getByRole('button', { name: 'Resubmit for review' }).click();
    await expect(fresh.getByLabel('Link to your post')).toHaveAccessibleDescription(/starting with https:\/\//);
    await fresh.getByLabel('Link to your post').fill(`https://instagram.com/p/${code('A')}/`);
    await fresh.getByLabel('Caption you used').fill(`Now with ${s().main.hashtag} and #ad`);
    await fresh.getByLabel('New screenshot (optional)').setInputFiles(pngFile(208));
    await fresh.getByRole('button', { name: 'Resubmit for review' }).click();
    await expect(page.getByText('Corrected 1×')).toBeVisible();
    await expect(page.getByText('Pending', { exact: true }).first()).toBeVisible();
    await expect(page.getByRole('region', { name: 'Edit & resubmit' })).toHaveCount(0);
  });

  test('A approved: earnings of 5 + 1 USD move from pending to approved', async () => {
    await page.goto('/app/earnings');
    await expect(page.getByRole('group', { name: /^Approved\b/ })).toContainText('$0.00');

    await decide(s().reviewer1, ids.B2, 'Reject', 'The screenshot shows a different post.');
    await decide(s().reviewer1, ids.C, 'Reject', 'The campaign hashtag is missing from the caption.');
    await decide(s().reviewer1, ids.A, 'Approve');

    await page.goto(`/app/submissions/${ids.A}`);
    await expect(page.getByText('Approved', { exact: true }).first()).toBeVisible();
    const reward = page.getByRole('region', { name: 'Reward' });
    await expect(reward.getByRole('listitem').filter({ hasText: 'Post reward' })).toContainText('$5.00');
    await expect(reward.getByRole('listitem').filter({ hasText: 'First-post bonus' })).toContainText('$1.00');

    await page.goto('/app/earnings');
    // D is still pending. Its estimate is the quote captured when it was submitted — before A was approved, so it
    // still includes the first-post bonus (documented: "the estimate can change with caps and bonuses"). A's 6 USD is
    // approved.
    await expect(page.getByRole('group', { name: /^Pending\b/ })).toContainText('$6.00');
    await expect(page.getByRole('group', { name: /^Approved\b/ })).toContainText('$6.00');
  });

  test('C: the appeal needs at least 20 characters; exactly 20 is accepted, and only once', async () => {
    await page.goto(`/app/submissions/${ids.C}`);
    await expect(page.getByText('Rejected', { exact: true }).first()).toBeVisible();
    await expect(page.getByRole('status', { name: 'Reason for the decision' })).toContainText(
      'The campaign hashtag is missing from the caption.',
    );
    const appeal = page.getByRole('region', { name: 'Appeal this decision' });
    const reason = appeal.getByLabel('Why should the decision be reviewed again?');
    const nineteen = 'Hashtag is in reply';
    expect(nineteen).toHaveLength(19);
    await reason.fill(nineteen);
    await appeal.getByRole('button', { name: 'Submit appeal' }).click();
    await expect(reason).toHaveAccessibleDescription(/at least 20 characters/);
    await expect(reason).toBeFocused();

    const twenty = 'Hashtag is in replys';
    expect(twenty).toHaveLength(20);
    await reason.fill(twenty);
    await appeal.getByRole('button', { name: 'Submit appeal' }).click();
    const status = page.getByRole('region', { name: 'Your appeal' });
    await expect(status).toContainText('Under review');
    await expect(status).toContainText(twenty);
    await expect(page.getByRole('region', { name: 'Appeal this decision' })).toHaveCount(0);
    await expect(
      api.post(`/me/submissions/${ids.C}/appeal`, { reason: 'A second appeal for the same decision.' }),
    ).rejects.toMatchObject({ status: 409 });
  });

  test('C: a second reviewer upholds the decision; the participant sees the note', async () => {
    await resolveAppeal(s().reviewer2, ids.C, 'Upheld', 'The hashtag must be in the caption itself.');
    await page.reload();
    const status = page.getByRole('region', { name: 'Your appeal' });
    await expect(status).toContainText('Decision upheld');
    await expect(status).toContainText('The hashtag must be in the caption itself.');
    await expect(page.getByText('Rejected', { exact: true }).first()).toBeVisible();
    await expect(page.getByRole('region', { name: 'Appeal this decision' })).toHaveCount(0);
  });

  test('D approved: nothing pending, 11 USD approved', async () => {
    await decide(s().reviewer1, ids.D, 'Approve');
    // The first-post bonus is paid once per campaign: D earns the 5 USD post reward only.
    await page.goto(`/app/submissions/${ids.D}`);
    const reward = page.getByRole('region', { name: 'Reward' });
    await expect(reward.getByRole('listitem').filter({ hasText: 'Post reward' })).toContainText('$5.00');
    await expect(reward.getByRole('listitem').filter({ hasText: 'First-post bonus' })).toHaveCount(0);
    await page.goto('/app/earnings');
    await expect(page.getByRole('group', { name: /^Pending\b/ })).toContainText('$0.00');
    await expect(page.getByRole('group', { name: /^Approved\b/ })).toContainText('$11.00');

    await page.goto('/app/submissions');
    await expect(page.getByRole('heading', { level: 1, name: 'My submissions' })).toBeVisible();
    for (const id of Object.values(ids)) remember(`submission:${id}`, id);
  });
});
