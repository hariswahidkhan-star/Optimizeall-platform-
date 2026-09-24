import type { Page } from '@playwright/test';
import {
  type ApiError,
  api,
  campaignBody,
  decide,
  expect,
  field,
  modal,
  refused,
  rememberCampaign,
  state,
  submissionOf,
  submitPost,
  test,
} from './support/campaigns';

/**
 * The review side: claim + claim expiry, the three decisions (reason required for correction/rejection), two
 * reviewers deciding the same submission at the same moment (exactly one wins), self-review forbidden, budget
 * exhaustion and per-participant caps at approval time, live checks (confirm → earnings approved; removed → reversal
 * with ledger entries), an appeal resolved by a different reviewer, and the reviewer's stats.
 */
interface ReviewDetail {
  submission: {
    status: string;
    concurrencyStamp: string;
    claim: { claimedBy: { displayName: string } | null };
  };
  earnings: { type: string; amount: number; status: string }[];
}

test.describe.serial('reviewer queue and decisions', () => {
  let reviewCampaignId = '';
  /** Omar's rejected submission (appealed later) and the reviewer-participant's own submission. */
  let rejectedId = '';
  let dualSubmissionId = '';
  const reviewCampaignTitle = () => `Review Desk ${state().runId}`;

  test.beforeAll(async () => {
    const manager = await api(state().manager);
    // Budget 10 USD, base 4, campaign cap per participant 6: caps and the budget are hit within a few approvals.
    const created = await manager.post<{ id: string; slug: string; title: string }>(
      '/admin/campaigns',
      campaignBody({
        title: reviewCampaignTitle(),
        budgetAmount: 10,
        budgetCurrency: 'USD',
        rewardRules: {
          currency: 'USD',
          campaignCapPerParticipant: 6,
          rules: [{ type: 'BaseRate', amount: 4 }],
        },
      }),
    );
    await manager.post(`/admin/campaigns/${created.id}/publish`);
    reviewCampaignId = created.id;
    rememberCampaign('review', created);
  });

  async function openWorkspace(page: Page, submissionId: string) {
    await page.goto(`/review/queue/${submissionId}`);
    await expect(page.getByRole('region', { name: 'Campaign requirements' })).toBeVisible();
    await page.getByRole('button', { name: 'Claim to review' }).click();
    await expect(page.getByText('You’re reviewing this submission')).toBeVisible();
  }

  async function decideInUi(
    page: Page,
    decision: 'Approve' | 'Request correction' | 'Reject',
    reason?: string,
  ) {
    const panel = page.getByRole('region', { name: 'Decision' });
    await panel.getByRole('button', { name: decision, exact: true }).click();
    await panel.getByRole('switch', { name: 'Open the next submission afterwards' }).setChecked(false);
    const submit = {
      Approve: 'Confirm approval',
      'Request correction': 'Send correction request',
      Reject: 'Confirm rejection',
    }[decision];
    if (decision !== 'Approve') {
      const box = field(panel, decision === 'Reject' ? 'Reason for rejection' : 'What needs correcting');
      await box.fill('');
      await panel.getByRole('button', { name: submit }).click();
      await expect(panel.getByText(/at least 5 characters/i).first()).toBeVisible(); // reason required
      await box.fill(reason!);
    }
    await panel.getByRole('button', { name: submit }).click();
    await expect(page.getByText('This submission has been decided')).toBeVisible();
  }

  test('claim holds the submission; the claim expires and another reviewer can take it', async () => {
    const [paula] = state().participants;
    const admin = await api(state().admin);
    const id = await submitPost(paula!, reviewCampaignId);
    const r1 = await api(state().reviewer1);
    const r2 = await api(state().reviewer2);

    await admin.put('/admin/settings/review.claimMinutes', {
      value: 1,
      reason: 'E2E: short claims',
      confirm: true,
    });
    try {
      await r1.post(`/review/submissions/${id}/claim`);
      const held = await refused(r2.post(`/review/submissions/${id}/claim`));
      expect([held.status, held.code]).toEqual([409, 'review.claimed_by_other']);
      expect(held.message).toContain(state().reviewer1.displayName);
      // The second reviewer also can't decide while the claim is live.
      const detail = await r2.get<ReviewDetail>(`/review/submissions/${id}`);
      const blocked = await refused(
        r2.post(`/review/submissions/${id}/decision`, {
          decision: 'Approve',
          concurrencyStamp: detail.submission.concurrencyStamp,
        }),
      );
      expect(blocked.code).toBe('review.claimed_by_other');

      // After the claim expires (1 minute) the second reviewer can claim it.
      await expect
        .poll(
          async () => {
            try {
              await r2.post(`/review/submissions/${id}/claim`);
              return 'claimed';
            } catch (error) {
              return (error as ApiError).code;
            }
          },
          { timeout: 100_000, intervals: [5_000] },
        )
        .toBe('claimed');
    } finally {
      await admin.post('/admin/settings/review.claimMinutes/reset', {
        reason: 'E2E: restore default',
        confirm: true,
      });
    }
    // The first reviewer's stale claim is gone.
    expect((await refused(r1.post(`/review/submissions/${id}/release`))).code).toBe('review.not_claimed');
    await r2.post(`/review/submissions/${id}/release`);
  });

  test('approve, request a correction and reject in the workspace (reasons required)', async ({ as }) => {
    const [paula, omar] = state().participants;
    const queued = await r1Queue();
    const page = await as(state().reviewer1, /\/review$/);

    const approveId = queued; // the submission from the claim test (released, still pending)
    await openWorkspace(page, approveId);
    await decideInUi(page, 'Approve');
    expect((await submissionOf(paula!, approveId)).status).toBe('Approved');

    const correctionId = await submitPost(omar!, reviewCampaignId);
    await openWorkspace(page, correctionId);
    await decideInUi(page, 'Request correction', 'Please add #ad at the start of the caption.');
    expect((await submissionOf(omar!, correctionId)).status).toBe('NeedsCorrection');

    const rejectId = await submitPost(omar!, reviewCampaignId);
    await openWorkspace(page, rejectId);
    await decideInUi(page, 'Reject', 'The post does not show the product.');
    expect((await submissionOf(omar!, rejectId)).status).toBe('Rejected');

    // API: a rejection without a (5+ character) reason is refused.
    const another = await submitPost(omar!, reviewCampaignId);
    const r1 = await api(state().reviewer1);
    const claim = await r1.post<{ concurrencyStamp: string }>(`/review/submissions/${another}/claim`);
    for (const reason of [undefined, '   ', 'no']) {
      const error = await refused(
        r1.post(`/review/submissions/${another}/decision`, {
          decision: 'Reject',
          reason,
          concurrencyStamp: claim.concurrencyStamp,
        }),
      );
      expect(error.status).toBe(400);
      expect(error.code).toMatch(/^review\.reason_(required|too_short)$/);
    }
    await r1.post(`/review/submissions/${another}/release`);
    rejectedId = rejectId;

    async function r1Queue() {
      const list = await (
        await api(state().reviewer1)
      ).get<{ items: { id: string; campaign: { id: string } }[] }>(
        `/review/queue?campaignId=${reviewCampaignId}`,
      );
      expect(list.items).toHaveLength(1);
      return list.items[0]!.id;
    }
  });

  test('two reviewers decide the same submission at the same moment: exactly one wins', async () => {
    const tess = state().participants[2]!;
    const id = await submitPost(tess, reviewCampaignId);
    const r1 = await api(state().reviewer1);
    const r2 = await api(state().reviewer2);
    const { submission } = await r1.get<ReviewDetail>(`/review/submissions/${id}`);
    const results = await Promise.allSettled([
      r1.post(`/review/submissions/${id}/decision`, {
        decision: 'Approve',
        concurrencyStamp: submission.concurrencyStamp,
      }),
      r2.post(`/review/submissions/${id}/decision`, {
        decision: 'Reject',
        reason: 'Rejected by the second reviewer',
        concurrencyStamp: submission.concurrencyStamp,
      }),
    ]);
    const won = results.filter((r) => r.status === 'fulfilled');
    const lost = results.filter((r): r is PromiseRejectedResult => r.status === 'rejected');
    expect(won).toHaveLength(1);
    expect(lost).toHaveLength(1);
    expect((lost[0]!.reason as ApiError).status).toBe(409);
    expect((lost[0]!.reason as ApiError).code).toBe('review.already_decided');

    const final = await r1.get<ReviewDetail>(`/review/submissions/${id}`);
    const approvedEarnings = final.earnings.filter((e) => e.type === 'PostReward');
    if (final.submission.status === 'Approved') expect(approvedEarnings).toHaveLength(1);
    else expect([final.submission.status, approvedEarnings.length]).toEqual(['Rejected', 0]);
  });

  test('a reviewer can never review their own submission', async ({ as }) => {
    const dual = state().dual;
    const id = await submitPost(dual, reviewCampaignId);
    const self = await api(dual);
    const claim = await refused(self.post(`/review/submissions/${id}/claim`));
    expect([claim.status, claim.code]).toEqual([403, 'review.self_review']);
    const detail = await (await api(state().reviewer1)).get<ReviewDetail>(`/review/submissions/${id}`);
    const decision = await refused(
      self.post(`/review/submissions/${id}/decision`, {
        decision: 'Approve',
        concurrencyStamp: detail.submission.concurrencyStamp,
      }),
    );
    expect([decision.status, decision.code]).toEqual([403, 'review.self_review']);

    const page = await as(dual, /\/(review|app)$/);
    await page.goto(`/review/queue/${id}`);
    await expect(
      page.getByRole('region', { name: 'Decision' }).getByRole('button', { name: 'Approve', exact: true }),
    ).toHaveCount(0);
    dualSubmissionId = id;
  });

  test('budget exhaustion and the per-participant campaign cap reduce rewards at approval', async () => {
    // Spent so far on this campaign: the approval above (4) and the race winner (4 if approved).
    const manager = await api(state().manager);
    const before = await manager.get<{ spent: number; budgetRemaining: number }>(
      `/admin/campaigns/${reviewCampaignId}`,
    );
    const [paula] = state().participants;

    // Paula already earned 4 here; her campaign cap is 6, so her next post earns 2 (campaign_cap).
    const second = await submitPost(paula!, reviewCampaignId);
    const capped = await decide(state().reviewer2, second, 'Approve');
    expect(capped.reward!.total).toBe(Math.min(2, before.budgetRemaining));
    expect(capped.reward!.appliedCaps).toContain(
      before.budgetRemaining < 2 ? 'campaign_budget' : 'campaign_cap',
    );

    // Dana's post: whatever budget is left, then nothing — approved with the budget cap reported.
    const dualId = dualSubmissionId;
    let spent = (await manager.get<{ spent: number }>(`/admin/campaigns/${reviewCampaignId}`)).spent;
    const first = await decide(state().reviewer2, dualId, 'Approve');
    expect(first.reward!.total).toBe(Math.min(4, 10 - spent));
    spent = (await manager.get<{ spent: number }>(`/admin/campaigns/${reviewCampaignId}`)).spent;
    if (spent < 10) {
      // Top up with one more post from Dana until the budget is exhausted.
      const extra = await submitPost(state().dual, reviewCampaignId);
      const r = await decide(state().reviewer2, extra, 'Approve');
      expect(r.reward!.total).toBe(Math.min(2, 10 - spent));
    }
    const exhausted = await manager.get<{ spent: number; budgetRemaining: number }>(
      `/admin/campaigns/${reviewCampaignId}`,
    );
    expect(exhausted).toMatchObject({ spent: 10, budgetRemaining: 0 });

    const after = await submitPost(state().participants[2]!, reviewCampaignId);
    const zero = await decide(state().reviewer2, after, 'Approve');
    expect(zero.status).toBe('Approved');
    expect(zero.reward!.total).toBe(0);
    expect(zero.reward!.appliedCaps).toContain('campaign_budget');
    const final = await manager.get<{ spent: number }>(`/admin/campaigns/${reviewCampaignId}`);
    expect(final.spent).toBe(10); // never over budget
  });

  test('two approvals racing for the last of the budget never overspend it', async () => {
    const manager = await api(state().manager);
    const tight = await manager.post<{ id: string }>(
      '/admin/campaigns',
      campaignBody({
        title: `Last Dollars ${state().runId}`,
        budgetAmount: 4,
        budgetCurrency: 'USD',
        rewardRules: { currency: 'USD', rules: [{ type: 'BaseRate', amount: 4 }] },
      }),
    );
    await manager.post(`/admin/campaigns/${tight.id}/publish`);
    const [paula, omar] = state().participants;
    const a = await submitPost(paula!, tight.id);
    const b = await submitPost(omar!, tight.id);
    const [first, second] = await Promise.all([
      decide(state().reviewer1, a, 'Approve'),
      decide(state().reviewer2, b, 'Approve'),
    ]);
    expect([first.status, second.status]).toEqual(['Approved', 'Approved']);
    expect([first.reward!.total, second.reward!.total].sort()).toEqual([0, 4]);
    const after = await manager.get<{ spent: number; budgetRemaining: number }>(
      `/admin/campaigns/${tight.id}`,
    );
    expect(after).toMatchObject({ spent: 4, budgetRemaining: 0 });
  });

  test('live checks: confirm approves pending earnings; removed reverses them in the ledger', async ({
    as,
  }) => {
    const r1 = await api(state().reviewer1);
    const due = await r1.get<{
      items: { submissionId: string; campaign: { title: string }; participant: { displayName: string } }[];
    }>('/review/live-checks?due=true&pageSize=50');
    expect(due.items.length).toBeGreaterThanOrEqual(2);
    const [toConfirm, toRemove] = due.items;

    const page = await as(state().reviewer1, /\/review$/);
    await page.goto('/review/live-checks');
    await expect(page.getByRole('heading', { level: 1, name: 'Live checks' })).toBeVisible();

    const row = (item: typeof toConfirm) =>
      page
        .getByRole('row')
        .filter({ has: page.getByRole('link', { name: item!.campaign.title }) })
        .filter({
          hasText: item!.participant.displayName,
        });
    // Actions unlock only after the post was opened.
    const confirmButton = page
      .getByRole('button', {
        name: `Confirm live: ${toConfirm!.campaign.title} by ${toConfirm!.participant.displayName}`,
      })
      .first();
    await expect(confirmButton).toBeDisabled();
    const popup = page.waitForEvent('popup');
    await row(toConfirm)
      .first()
      .getByRole('link', { name: /Open post/ })
      .click();
    await (await popup).close();
    await expect(confirmButton).toBeEnabled();
    await confirmButton.click();
    await modal(page, 'Confirm the post is still live').getByRole('button', { name: 'Confirm live' }).click();
    await expect(page.getByText('Post confirmed live').first()).toBeVisible();
    const confirmed = await r1.get<ReviewDetail>(`/review/submissions/${toConfirm!.submissionId}`);
    expect(confirmed.submission.status).toBe('Approved');
    const confirmedPending = confirmed.earnings.filter((e) => e.status === 'PendingApproval');
    // Only manual-approval bonuses may stay pending for finance.
    expect(
      confirmedPending.every(
        (e) => e.type === 'QualityBonus' || e.type === 'TimeLimitedBonus' || e.type === 'FirstPostBonus',
      ),
    ).toBe(true);
    expect(confirmed.earnings.some((e) => e.type === 'PostReward' && e.status === 'Approved')).toBe(true);

    const before = await r1.get<ReviewDetail>(`/review/submissions/${toRemove!.submissionId}`);
    const live = before.earnings.filter(
      (e) => e.type !== 'Reversal' && e.status !== 'Reversed' && e.status !== 'Declined',
    );
    expect(live.length).toBeGreaterThan(0);
    const popup2 = page.waitForEvent('popup');
    await row(toRemove)
      .first()
      .getByRole('link', { name: /Open post/ })
      .click();
    await (await popup2).close();
    await page
      .getByRole('button', {
        name: `Mark removed: ${toRemove!.campaign.title} by ${toRemove!.participant.displayName}`,
      })
      .first()
      .click();
    const dialog = modal(page, 'Mark this post as removed?');
    await field(dialog, 'What did you find?').fill('Post deleted from the profile');
    await dialog.getByRole('button', { name: 'Mark removed & reverse' }).click();
    await expect(page.getByText('Marked as removed').first()).toBeVisible();

    const after = await r1.get<ReviewDetail>(`/review/submissions/${toRemove!.submissionId}`);
    expect(after.submission.status).toBe('Reversed');
    const reversals = after.earnings.filter((e) => e.type === 'Reversal');
    expect(reversals).toHaveLength(live.length);
    const liveTotal = live.reduce((s, e) => s + e.amount, 0);
    expect(reversals.reduce((s, e) => s + e.amount, 0)).toBeCloseTo(-liveTotal, 2);
    expect(
      after.earnings
        .filter((e) => e.type !== 'Reversal')
        .every((e) => e.status === 'Reversed' || e.status === 'Declined' || e.status === 'Paid'),
    ).toBe(true);

    // A second removal of the same check is refused (no double clawback).
    const again = await refused(
      r1.post(`/review/submissions/${toRemove!.submissionId}/live-check`, {
        result: 'Removed',
        note: 'Again removed',
      }),
    );
    expect(again.code).toBe('review.live_check_not_pending');
  });

  test('the participant appeals; the original reviewer is blocked and a different reviewer upholds it', async ({
    as,
  }) => {
    const omar = state().participants[1]!;
    const rejectId = rejectedId;
    const tooShort = await refused(
      (await api(omar)).post(`/me/submissions/${rejectId}/appeal`, { reason: 'Please' }),
    );
    expect(tooShort.status).toBe(400);
    await (
      await api(omar)
    ).post(`/me/submissions/${rejectId}/appeal`, {
      reason: 'The product is visible in the second slide of the carousel.',
    });

    const appeals = await (
      await api(state().reviewer2)
    ).get<{ items: { id: string; submissionId: string; concurrencyStamp: string }[] }>('/review/appeals');
    const appeal = appeals.items.find((a) => a.submissionId === rejectId)!;
    const same = await refused(
      (await api(state().reviewer1)).post(`/review/appeals/${appeal.id}/resolve`, {
        outcome: 'Upheld',
        note: 'I stand by my decision',
        concurrencyStamp: appeal.concurrencyStamp,
      }),
    );
    expect([same.status, same.code]).toEqual([403, 'appeal.same_reviewer']);

    const page = await as(state().reviewer2, /\/review$/);
    await page.goto(`/review/appeals/${appeal.id}`);
    await page.getByRole('radio', { name: /Uphold the original decision/ }).check();
    await field(page, 'Resolution note').fill('Checked every slide; the product is not shown.');
    await page.getByRole('button', { name: 'Resolve appeal' }).click();
    await expect(page.getByText('Upheld', { exact: true }).first()).toBeVisible();
    const after = await submissionOf(omar, rejectId);
    expect(after.status).toBe('Rejected');
  });

  test('reviewer stats count today’s decisions', async ({ as }) => {
    const stats = await (
      await api(state().reviewer1)
    ).get<{ myDecisionsToday: number; openAppeals: number }>('/review/stats');
    expect(stats.myDecisionsToday).toBeGreaterThanOrEqual(3);
    const page = await as(state().reviewer1, /\/review$/);
    const tile = page.getByRole('group', { name: 'My decisions today' });
    await expect(tile).toContainText(String(stats.myDecisionsToday));
  });
});
