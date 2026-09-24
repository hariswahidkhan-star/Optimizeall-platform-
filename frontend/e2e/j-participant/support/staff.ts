import { expect } from '@playwright/test';
import { ApiSession } from '../../journeys/support/api';
import { makePng } from '../../journeys/support/png';
import type { Credentials } from '../../journeys/support/fixtures';
import { state } from './state';

/**
 * The staff side of the participant lifecycle, driven through the real API (the reviewer, finance and support UIs
 * have suites of their own). Every call goes through the same endpoints and permission checks the staff portals use.
 */
const sessions = new Map<string, ApiSession>();
export async function as(user: Credentials): Promise<ApiSession> {
  const key = `${user.email}\n${user.password}`;
  let session = sessions.get(key);
  if (!session) {
    session = await ApiSession.login(user.email, user.password);
    sessions.set(key, session);
  }
  return session;
}

type Decision = 'Approve' | 'RequestCorrection' | 'Reject';

/** Claims a submission as `reviewer` and records the decision (claim stamp → decision, as the workspace does). */
export async function decide(reviewer: Credentials, submissionId: string, decision: Decision, reason?: string) {
  const api = await as(reviewer);
  const claim = await api.post<{ concurrencyStamp: string }>(`/review/submissions/${submissionId}/claim`);
  return api.post<{ status: string }>(`/review/submissions/${submissionId}/decision`, {
    decision,
    reason: reason ?? null,
    concurrencyStamp: claim.concurrencyStamp,
  });
}

/** Resolves the open appeal of a submission as `reviewer` (must not be the original decider). */
export async function resolveAppeal(
  reviewer: Credentials,
  submissionId: string,
  outcome: 'Upheld' | 'Overturned',
  note: string,
) {
  const api = await as(reviewer);
  const appeals = await api.get<{ items: { id: string; submissionId: string; concurrencyStamp: string }[] }>(
    '/review/appeals?pageSize=100',
  );
  const appeal = appeals.items.find((a) => a.submissionId === submissionId);
  if (!appeal) throw new Error(`No open appeal for submission ${submissionId}`);
  return api.post(`/review/appeals/${appeal.id}/resolve`, {
    outcome,
    note,
    concurrencyStamp: appeal.concurrencyStamp,
  });
}

/** Runs a background job now (Jobs:Enabled is false in the e2e harness), as the admin. */
export async function runJob(name: string) {
  const admin = await as(state().admin);
  const run = await admin.post<{ status: string; summary: string | null; error: string | null }>(
    `/admin/jobs/${name}/run`,
  );
  expect(run.error, `${name}: ${run.summary}`).toBeNull();
  return run;
}

/** Delivers the notification outbox (email) now. */
export const dispatchNotifications = () => runJob('NotificationDispatchJob');

/** Adds a social profile and submits a post through the participant API (arrangement for staff-side steps). */
export async function submitViaApi(
  participant: Credentials,
  campaignId: string,
  postCode: string,
  seed: number,
  platform: 'Instagram' | 'TikTok' = 'Instagram',
) {
  const api = await as(participant);
  const accounts = await api.get<{ items: { id: string; platform: string }[] }>('/me/social-accounts');
  const account = accounts.items.find((a) => a.platform === platform);
  if (!account) throw new Error(`${participant.email} has no ${platform} profile`);
  const form = new FormData();
  form.append('campaignId', campaignId);
  form.append('socialAccountId', account.id);
  form.append('platform', platform);
  form.append('postUrl', `https://www.instagram.com/p/${postCode}/`);
  form.append('postedAt', new Date(Date.now() - 60_000).toISOString());
  form.append('captionText', `Post ${postCode}`);
  form.append('screenshot', new Blob([makePng(seed)], { type: 'image/png' }), `${postCode}.png`);
  return api.upload<{ id: string; status: string }>('/me/submissions', form);
}

/** Registers and verifies a participant through the API (dev mailbox), with a qualifying Instagram profile. */
export async function registerParticipantViaApi(
  who: Credentials & { instagram: string },
  options: { referralCode?: string } = {},
) {
  const { publicApi, mailLink } = await import('../../journeys/support/api');
  await publicApi.post('/auth/register', {
    email: who.email,
    password: who.password,
    displayName: who.displayName,
    countryCode: 'GB',
    languageCode: 'en',
    timeZone: 'UTC',
    acceptTerms: true,
    referralCode: options.referralCode,
  });
  const verify = await mailLink(who.email, '/verify-email', /verify/i);
  await publicApi.post('/auth/verify-email', { token: verify.searchParams.get('token') });
  const api = await as(who);
  await api.post('/me/social-accounts', {
    platform: 'Instagram',
    handle: who.instagram,
    profileUrl: `https://www.instagram.com/${who.instagram}`,
    accountCreatedAt: new Date(Date.now() - 400 * 86_400_000).toISOString(),
    followerCount: 900,
  });
  return api;
}

/**
 * Pays every approved earning that is due: moves the weekly cutoff to one second ago (after every earning's
 * availableAt), finance 1 prepares the batch, finance 2 finalizes it (four-eyes) and finance 1 records the payment of
 * `userId`'s item. Returns the batch reference and the item.
 */
export async function payUser(userId: string, paymentReference: string) {
  const { finance1, finance2 } = state();
  const f1 = await as(finance1);
  const f2 = await as(finance2);

  const ledger = await f1.get<{ items: { availableAt: string | null }[] }>(
    `/finance/ledger?userId=${userId}&status=Approved&pageSize=100`,
  );
  expect(ledger.items.length, 'approved earnings to pay').toBeGreaterThan(0);
  const lastAvailable = Math.max(...ledger.items.map((e) => Date.parse(e.availableAt!)));
  let cutoff = 0;
  await expect
    .poll(() => {
      cutoff = Math.floor(Date.now() / 1000) * 1000 - 1000;
      return cutoff > lastAvailable;
    })
    .toBe(true);
  const iso = new Date(cutoff).toISOString();
  const periodKey = iso.slice(0, 10);
  await f1.put('/finance/payout-schedule', {
    frequency: 'Weekly',
    anchorCutoffDate: periodKey,
    cutoffLocalTime: iso.slice(11, 19),
    timeZone: 'UTC',
    paymentDelayDays: 2,
    minimumPayoutAmount: 1,
    settlementCurrency: 'USD',
    earningHoldDays: 0,
    autoPrepareBatches: false,
    effectiveFrom: new Date().toISOString(),
    reason: 'E2E participant lifecycle: close a payout period right after the approvals',
    confirm: true,
  });

  const prepared = await f1.post<{ batch: { id: string; reference: string } }>('/finance/payout-batches/prepare', {
    periodKey,
  });
  const batchId = prepared.batch.id;
  const detail = await f2.get<{ concurrencyStamp: string }>(`/finance/payout-batches/${batchId}`);
  await f2.post(`/finance/payout-batches/${batchId}/finalize`, {
    confirm: true,
    reason: 'Checked the items',
    concurrencyStamp: detail.concurrencyStamp,
  });
  const items = await f1.get<{
    items: { items: { itemId: string; user: { id: string }; amount: number; status: string }[] };
  }>(`/finance/payout-batches/${batchId}?pageSize=100`);
  const item = items.items.items.find((i) => i.user.id === userId);
  if (!item) throw new Error(`User ${userId} has no item in batch ${prepared.batch.reference}`);
  await f1.post(`/finance/payout-batches/${batchId}/items/${item.itemId}/record-payment`, {
    paymentReference,
    paidAt: new Date().toISOString(),
  });
  return { batchId, reference: prepared.batch.reference, item };
}
