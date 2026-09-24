import type { Page } from '@playwright/test';
import {
  DAY,
  api,
  campaign,
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
  toast,
} from './support/campaigns';

/**
 * Reward rules versioning after submissions exist (historical rates preserved), concurrent edits by two managers
 * (409 with a friendly message), the audited budget change, boundary values, 403s for a reviewer on manager actions,
 * the scheduler (Scheduled → Active), pause/resume/end/archive/restore with submissions refused while closed, and
 * duplicating a campaign.
 */
const tab = (page: Page, name: string) => page.getByRole('tab', { name: new RegExp(`^${name}`) });
const ruleCard = (page: Page, type: string) =>
  page
    .getByRole('list', { name: 'Reward rules' })
    .getByRole('listitem')
    .filter({ has: page.getByRole('heading', { level: 3, name: new RegExp(`^${type}`) }) });

test.describe.serial('reward rules, concurrency and lifecycle', () => {
  let v1SubmissionId = '';
  let v2SubmissionId = '';

  test('rules edited after a submission: v2 applies to new posts, the old post keeps its v1 rate', async ({
    as,
  }) => {
    const glow = campaign('glow');
    const [paula, omar] = state().participants;
    v1SubmissionId = await submitPost(paula!, glow.id, 'TikTok');
    expect((await submissionOf(paula!, v1SubmissionId)).rewardRuleSetVersion).toBe(1);

    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/campaigns/${glow.id}`);
    await tab(page, 'Rewards').click();
    await expect(page.getByText('Locked: the campaign already has submissions.')).toBeVisible();
    await field(ruleCard(page, 'Base rate'), 'Amount (USD)').fill('6');
    await field(ruleCard(page, 'Rate override'), 'Amount (USD)').fill('9');
    await expect(page.getByRole('button', { name: 'Discard rule changes' })).toBeVisible();
    await page.getByRole('button', { name: 'Save as new version' }).click();
    const dialog = modal(page, 'Save reward rules as a new version?');
    const confirm = dialog.getByRole('button', { name: 'Save new version' });
    await field(dialog, 'Reason for the change').fill('Hi');
    await confirm.click();
    await expect(dialog.getByText('Enter a reason (at least 5 characters).')).toBeVisible();
    await field(dialog, 'Reason for the change').fill('Launch week: higher TikTok rate');
    await confirm.click();
    await expect(toast(page, 'Reward rules saved as version 2')).toBeVisible();

    const history = page.getByRole('region', { name: 'Version history' });
    await expect(history).toContainText('Version 2');
    await expect(history).toContainText('Reason: Launch week: higher TikTok rate');
    await expect(history.getByRole('listitem').filter({ hasText: 'Version 1' })).toContainText(
      '1 submission priced with it',
    );

    v2SubmissionId = await submitPost(omar!, glow.id, 'TikTok');
    expect((await submissionOf(omar!, v2SubmissionId)).rewardRuleSetVersion).toBe(2);

    const oldDecision = await decide(state().reviewer1, v1SubmissionId, 'Approve');
    const newDecision = await decide(state().reviewer1, v2SubmissionId, 'Approve');
    expect(oldDecision.reward!.total).toBe(8); // v1: TikTok 7 + first post 1
    expect(newDecision.reward!.total).toBe(10); // v2: TikTok 9 + first post 1
    const earnings = (await submissionOf(paula!, v1SubmissionId)).earnings!;
    expect(earnings.map((e) => [e.type, e.amount]).sort()).toEqual([
      ['FirstPostBonus', 1],
      ['PostReward', 7],
    ]);
  });

  test('invalid rules and an unconfirmed change are refused with explanations', async () => {
    const manager = await api(state().manager);
    const glow = campaign('glow');
    const twoBase = await refused(
      manager.post(`/admin/campaigns/${glow.id}/reward-rules`, {
        currency: 'USD',
        rules: [
          { type: 'BaseRate', amount: 5 },
          { type: 'BaseRate', amount: 6 },
        ],
        reason: 'Two base rates',
        confirm: true,
      }),
    );
    expect([twoBase.status, twoBase.code]).toEqual([400, 'reward.invalid_rules']);
    const unconfirmed = await refused(
      manager.post(`/admin/campaigns/${glow.id}/reward-rules`, {
        currency: 'USD',
        rules: [{ type: 'BaseRate', amount: 5 }],
        reason: 'Forgot to confirm',
      }),
    );
    expect([unconfirmed.status, unconfirmed.code]).toEqual([400, 'confirmation.required']);
    const currency = await refused(
      manager.post(`/admin/campaigns/${glow.id}/reward-rules`, {
        currency: 'EUR',
        rules: [{ type: 'BaseRate', amount: 5 }],
        reason: 'Switch to euros',
        confirm: true,
      }),
    );
    expect(currency.status).toBe(409);
    expect(currency.code).toBe('reward.currency_locked');
  });

  test('two managers edit at once: the second save gets a friendly conflict and can reload', async ({
    as,
  }) => {
    const glow = campaign('glow');
    const first = await as(state().manager, /\/manage$/);
    const second = await as(state().manager2, /\/manage$/);
    for (const page of [first, second]) {
      await page.goto(`/manage/campaigns/${glow.id}`);
      await expect(field(page, 'Summary')).toBeVisible();
    }
    await field(second, 'Summary').fill('Edited by the second manager.');
    await second.getByRole('button', { name: 'Save changes' }).click();
    await expect(toast(second, 'Campaign saved')).toBeVisible();

    await field(first, 'Summary').fill('Edited by the first manager.');
    await first.getByRole('button', { name: 'Save changes' }).click();
    const conflict = first.getByRole('alert').filter({ hasText: 'Someone else changed this campaign' });
    await expect(conflict).toBeVisible();
    await expect(conflict).toContainText('Your changes were not saved');
    await conflict.getByRole('button', { name: 'Reload latest version' }).click();
    await expect(field(first, 'Summary')).toHaveValue('Edited by the second manager.');
    await expect(conflict).toHaveCount(0);
  });

  test('a refused disclosure override does not turn the next save into a false conflict', async ({ as }) => {
    const glow = campaign('glow');
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/campaigns/${glow.id}`);
    await field(page, 'Summary').fill('Show your autumn routine — now with a duplicate override.');
    await tab(page, 'Content').click();
    const overrides = page.getByRole('region', { name: 'Disclosure overrides' });
    await overrides.getByRole('button', { name: 'Add override' }).click();
    const rows = overrides.getByRole('row');
    await field(rows.last(), 'Platform').selectOption('Instagram');
    await field(rows.last(), 'Country code').fill('PK');
    await field(rows.last(), 'Disclosure text').fill('#ad duplicate');
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(
      page
        .getByRole('alert')
        .filter({ hasText: 'Each platform/country combination can only have one disclosure.' })
        .first(),
    ).toBeVisible();

    await overrides.getByRole('button', { name: 'Remove override Instagram, PK' }).last().click();
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(toast(page, 'Campaign saved')).toBeVisible();
    await expect(
      page.getByRole('alert').filter({ hasText: 'Someone else changed this campaign' }),
    ).toHaveCount(0);
    const saved = await (
      await api(state().manager)
    ).get<{ summary: string; disclosures: unknown[] }>(`/admin/campaigns/${glow.id}`);
    expect(saved.summary).toBe('Show your autumn routine — now with a duplicate override.');
    expect(saved.disclosures).toHaveLength(1);
  });

  test('two managers change the reward rules at once: the second is told, nothing is silently lost', async ({
    as,
  }) => {
    const draft = await (
      await api(state().manager)
    ).post<{ id: string }>('/admin/campaigns', campaignBody({ title: `Rules Race ${state().runId}` }));
    const first = await as(state().manager, /\/manage$/);
    const second = await as(state().manager2, /\/manage$/);
    for (const page of [first, second]) {
      await page.goto(`/manage/campaigns/${draft.id}`);
      await tab(page, 'Rewards').click();
      await expect(field(ruleCard(page, 'Base rate'), 'Amount (USD)')).toHaveValue('5');
    }
    const saveRules = async (page: Page, amount: string, reason: string) => {
      await field(ruleCard(page, 'Base rate'), 'Amount (USD)').fill(amount);
      await page.getByRole('button', { name: 'Save as new version' }).click();
      const dialog = modal(page, 'Save reward rules as a new version?');
      await field(dialog, 'Reason for the change').fill(reason);
      await dialog.getByRole('button', { name: 'Save new version' }).click();
      return dialog;
    };
    await saveRules(first, '6', 'First manager raises the base rate');
    await expect(toast(first, 'Reward rules saved as version 2')).toBeVisible();

    // The second manager still edits version 1: saving must not silently replace the first manager's version 2.
    const dialog = await saveRules(second, '7', 'Second manager raises the base rate');
    await expect(dialog.getByRole('alert')).toContainText('Someone else saved new reward rules');
    const versions = await (
      await api(state().manager)
    ).get<{ version: number }[]>(`/admin/campaigns/${draft.id}/reward-rules`);
    expect(versions.map((v) => v.version)).toEqual([2, 1]);

    // After a reload the second manager sees version 2 and can build on it.
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await second.reload();
    await tab(second, 'Rewards').click();
    await expect(field(ruleCard(second, 'Base rate'), 'Amount (USD)')).toHaveValue('6');
    await saveRules(second, '7', 'Second manager, on top of version 2');
    await expect(toast(second, 'Reward rules saved as version 3')).toBeVisible();

    // The API refuses a stale base version too; without one (older clients) it still appends.
    const stale = await refused(
      (await api(state().manager)).post(`/admin/campaigns/${draft.id}/reward-rules`, {
        currency: 'USD',
        rules: [{ type: 'BaseRate', amount: 8 }],
        reason: 'Stale editor',
        confirm: true,
        baseVersion: 2,
      }),
    );
    expect([stale.status, stale.code]).toEqual([409, 'reward.version_conflict']);
  });

  test('changing the budget asks for an audited reason', async ({ as }) => {
    const glow = campaign('glow');
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/campaigns/${glow.id}`);
    await tab(page, 'Rewards').click();
    // 18 USD was already earned on this campaign: a lower budget is refused on the budget field.
    await page.getByLabel(/^Campaign budget \(USD\)/).fill('10');
    await page.getByRole('button', { name: 'Save changes' }).click();
    const lower = modal(page, 'Change the campaign budget?');
    await field(lower, 'Reason').fill('Client cut the budget');
    await lower.getByRole('button', { name: 'Save with new budget' }).click();
    await expect(lower.getByRole('alert')).toContainText(
      "can't be lower than what has already been spent (18.00 USD)",
    );
    await lower.getByRole('button', { name: 'Cancel' }).click();
    await expect(
      page.getByRole('alert').filter({ hasText: 'The campaign could not be saved' }),
    ).toBeVisible();

    await page.getByLabel(/^Campaign budget \(USD\)/).fill('60');
    await page.getByRole('button', { name: 'Save changes' }).click();
    const dialog = modal(page, 'Change the campaign budget?');
    await expect(dialog).toContainText('from 50 to 60 USD');
    await field(dialog, 'Reason').fill('More demand than expected');
    await dialog.getByRole('button', { name: 'Save with new budget' }).click();
    await expect(toast(page, 'Campaign saved')).toBeVisible();
    const saved = await (
      await api(state().manager)
    ).get<{ budgetAmount: number; spent: number }>(`/admin/campaigns/${glow.id}`);
    expect(saved).toMatchObject({ budgetAmount: 60, spent: 18 });

    // Without confirm + reason the API refuses the change.
    const current = await (
      await api(state().manager)
    ).get<Record<string, unknown>>(`/admin/campaigns/${glow.id}`);
    const error = await refused(
      (await api(state().manager)).put(`/admin/campaigns/${glow.id}`, {
        ...(current as object),
        eligibility: current.eligibility,
        budgetAmount: 70,
      }),
    );
    expect([error.status, error.code]).toEqual([400, 'campaign.budget_change_unconfirmed']);
  });

  test('boundary values are refused with the field named', async () => {
    const manager = await api(state().manager);
    const cases: [Record<string, unknown>, number, string][] = [
      [{ maxSubmissionsPerParticipant: 0 }, 400, 'maxSubmissionsPerParticipant'],
      [{ maxSubmissionsPerParticipant: 101 }, 400, 'maxSubmissionsPerParticipant'],
      [{ minPostLiveHours: 721 }, 400, 'minPostLiveHours'],
      [{ minPostLiveHours: -1 }, 400, 'minPostLiveHours'],
      [{ budgetAmount: 0, budgetCurrency: 'USD' }, 400, 'budgetAmount'],
      [{ title: 'ab' }, 400, 'title'],
      [{ platforms: [] }, 400, 'platforms'],
      [{ endsAt: new Date(Date.now() - 3600_000).toISOString() }, 400, 'endsAt'],
      [{ submissionDeadline: new Date(Date.now() + DAY).toISOString() }, 400, 'submissionDeadline'],
      [{ timeZone: 'Mars/Olympus' }, 400, 'timeZone'],
      [{ eligibility: { minFollowers: 0, countries: ['PAK'] } }, 400, 'eligibility.countries'],
      [{ slug: campaign('glow').slug }, 409, 'slug'],
      [
        { rewardRules: { currency: 'USD', rules: [{ type: 'BaseRate', amount: -1 }] } },
        400,
        'rewardRules.rules[0].amount',
      ],
    ];
    for (const [overrides, status, fieldName] of cases) {
      const error = await refused(manager.post('/admin/campaigns', campaignBody(overrides)));
      expect(error.status, JSON.stringify(overrides)).toBe(status);
      const errors = (error.body as { errors?: Record<string, string[]> }).errors ?? {};
      expect(
        Object.keys(errors).map((k) => k.toLowerCase()),
        `${JSON.stringify(overrides)} → ${JSON.stringify(error.body)}`,
      ).toContain(fieldName.toLowerCase());
    }
    // The largest allowed values are accepted.
    const edge = await manager.post<{ maxSubmissionsPerParticipant: number; minPostLiveHours: number }>(
      '/admin/campaigns',
      campaignBody({
        maxSubmissionsPerParticipant: 100,
        minPostLiveHours: 720,
        budgetAmount: 0.01,
        budgetCurrency: 'USD',
      }),
    );
    expect(edge).toMatchObject({ maxSubmissionsPerParticipant: 100, minPostLiveHours: 720 });
  });

  test('a reviewer is refused every manager action (403) and the manager portal', async ({ as }) => {
    const reviewer = await api(state().reviewer1);
    const glow = campaign('glow');
    const calls: [string, () => Promise<unknown>][] = [
      ['create', () => reviewer.post('/admin/campaigns', campaignBody())],
      [
        'update',
        () => reviewer.put(`/admin/campaigns/${glow.id}`, campaignBody({ concurrencyStamp: glow.id })),
      ],
      ['publish', () => reviewer.post(`/admin/campaigns/${glow.id}/publish`)],
      ['pause', () => reviewer.post(`/admin/campaigns/${glow.id}/pause`, { reason: 'Reviewer pause' })],
      ['end', () => reviewer.post(`/admin/campaigns/${glow.id}/end`, { reason: 'Reviewer end' })],
      ['archive', () => reviewer.post(`/admin/campaigns/${glow.id}/archive`)],
      ['duplicate', () => reviewer.post(`/admin/campaigns/${glow.id}/duplicate`)],
      [
        'rules',
        () =>
          reviewer.post(`/admin/campaigns/${glow.id}/reward-rules`, {
            currency: 'USD',
            rules: [{ type: 'BaseRate', amount: 50 }],
            reason: 'Reviewer raises rates',
            confirm: true,
          }),
      ],
      [
        'preview',
        () =>
          reviewer.post(`/admin/campaigns/${glow.id}/reward-rules/preview`, {
            platform: 'Instagram',
            countryCode: 'GB',
            tier: 'Standard',
          }),
      ],
      [
        'invitation',
        () => reviewer.post('/marketing/invitations', { name: 'Reviewer link', campaignId: glow.id }),
      ],
      ['experiment', () => reviewer.get('/marketing/experiments')],
      [
        'assets',
        () => reviewer.post(`/admin/campaigns/${glow.id}/assets`, { type: 'Caption', title: 'x', body: 'y' }),
      ],
      [
        'assign',
        () => reviewer.post('/review/assign', { reviewerId: state().reviewer1.id, submissionIds: [] }),
      ],
    ];
    for (const [name, call] of calls) {
      const error = await refused(call());
      expect(error.status, name).toBe(403);
    }
    // Reading the campaign's rules is fine (campaigns.view).
    await reviewer.get(`/admin/campaigns/${glow.id}/reward-rules`);

    const page = await as(state().reviewer1, /\/review$/);
    await page.goto(`/manage/campaigns/${glow.id}`);
    await expect(
      page.getByRole('heading', { level: 1, name: 'You don’t have access to this page' }),
    ).toBeVisible();
    await expect(page.getByText('403', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save changes' })).toHaveCount(0);
  });

  test('scheduled → active by the scheduler job', async () => {
    const manager = await api(state().manager);
    const admin = await api(state().admin);
    const created = await manager.post<{ id: string; slug: string; title: string }>(
      '/admin/campaigns',
      campaignBody({
        title: `Orbit Launch ${state().runId}`,
        startsAt: new Date(Date.now() + 20_000).toISOString(),
      }),
    );
    const published = await manager.post<{ status: string }>(`/admin/campaigns/${created.id}/publish`);
    expect(published.status).toBe('Scheduled');
    // Submissions are refused before the start.
    const early = await refused(submitPost(state().participants[2]!, created.id));
    expect([early.status, early.code]).toEqual([409, 'submission.campaign_closed']);

    await expect
      .poll(
        async () => {
          await admin.post('/admin/jobs/campaign-schedule/run');
          return (await manager.get<{ status: string }>(`/admin/campaigns/${created.id}`)).status;
        },
        { timeout: 60_000, intervals: [2_000] },
      )
      .toBe('Active');
    const audit = await admin.get<{ items: { action: string }[] }>(
      `/admin/audit-logs?entityId=${created.id}&pageSize=50`,
    );
    expect(audit.items.map((a) => a.action)).toContain('campaign.activated');
    rememberCampaign('orbit', created);
  });

  test('pause, resume, end, archive and restore — submissions refused while closed', async ({ as }) => {
    const orbit = campaign('orbit');
    const tess = state().participants[2]!;
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/campaigns/${orbit.id}`);
    const more = page.getByRole('button', { name: 'More campaign actions' });
    const run = async (item: string, dialogTitle: RegExp, confirmLabel: string, reason?: string) => {
      await more.click();
      await page.getByRole('menuitem', { name: item }).click();
      const dialog = modal(page, dialogTitle);
      if (reason !== undefined) {
        await dialog.getByRole('button', { name: confirmLabel }).click(); // no reason yet: refused in the dialog
        await expect(dialog.getByText('Enter a reason (at least 5 characters).')).toBeVisible();
        await field(dialog, 'Reason').fill(reason);
      }
      await dialog.getByRole('button', { name: confirmLabel }).click();
      await expect(dialog).toHaveCount(0);
    };
    const status = (s: string) => expect(page.getByText(s, { exact: true }).first()).toBeVisible();

    await run('Pause…', /^Pause/, 'Pause campaign', 'Client asked for a pause');
    await status('Paused');
    expect((await refused(submitPost(tess, orbit.id))).code).toBe('submission.campaign_closed');

    await run('Resume…', /^Resume/, 'Resume campaign');
    await status('Active');
    const accepted = await submitPost(tess, orbit.id);

    await run('End…', /^End/, 'End campaign', 'Campaign finished early');
    await status('Ended');
    expect((await refused(submitPost(tess, orbit.id))).code).toBe('submission.campaign_closed');
    // Pending reviews continue after the end.
    expect((await decide(state().reviewer2, accepted, 'Approve')).status).toBe('Approved');

    await run('Archive…', /^Archive/, 'Archive campaign');
    await status('Archived');
    await expect(page.getByText('This campaign is archived and can no longer be changed.')).toBeVisible();
    await expect(field(page, 'Title')).toBeDisabled();
    const archivedSubmit = await refused(submitPost(tess, orbit.id));
    expect(archivedSubmit.status).toBe(404);
    const manager = await api(state().manager);
    const full = await manager.get<Record<string, unknown>>(`/admin/campaigns/${orbit.id}`);
    expect((await refused(manager.put(`/admin/campaigns/${orbit.id}`, full))).code).toBe('campaign.archived');
    expect(
      (
        await refused(
          manager.post(`/admin/campaigns/${orbit.id}/reward-rules`, {
            currency: 'USD',
            rules: [{ type: 'BaseRate', amount: 1 }],
            reason: 'Archived change',
            confirm: true,
          }),
        )
      ).code,
    ).toBe('campaign.archived');
    // Not on the public landing page or the participant catalogue.
    expect((await fetch(`${process.env.E2E_API_URL}/api/v1/public/campaigns/${orbit.slug}`)).status).toBe(
      404,
    );

    await run('Restore from archive…', /^Restore/, 'Restore campaign');
    await status('Ended'); // it was published, so it returns to Ended (not Draft)
  });

  test('duplicates a campaign as a draft with its assets, disclosures and latest rules as v1', async ({
    as,
  }) => {
    const glow = campaign('glow');
    const page = await as(state().manager, /\/manage$/);
    await page.goto(`/manage/campaigns/${glow.id}`);
    await page.getByRole('button', { name: 'More campaign actions' }).click();
    await page.getByRole('menuitem', { name: 'Duplicate…' }).click();
    await modal(page, /^Duplicate/)
      .getByRole('button', { name: 'Duplicate' })
      .click();
    await expect(page.getByRole('heading', { level: 1, name: `${glow.title} (copy)` })).toBeVisible();
    await expect(page.getByText('Draft', { exact: true }).first()).toBeVisible();
    const copyId = page.url().split('/').pop()!;
    expect(copyId).not.toBe(glow.id);

    const copy = await (
      await api(state().manager)
    ).get<{
      slug: string;
      assets: unknown[];
      disclosures: unknown[];
      submissions: { total: number };
      spent: number;
      currentRuleSet: { version: number; rules: { type: string; amount: number }[] };
    }>(`/admin/campaigns/${copyId}`);
    expect(copy.slug).toBe(`${glow.slug}-copy`);
    expect(copy.assets).toHaveLength(2);
    expect(copy.disclosures).toHaveLength(1);
    expect(copy.submissions.total).toBe(0);
    expect(copy.spent).toBe(0);
    expect(copy.currentRuleSet.version).toBe(1);
    expect(copy.currentRuleSet.rules.find((r) => r.type === 'BaseRate')!.amount).toBe(6); // the latest (v2) rules
  });
});
