import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { type BrowserContext, type Locator, type Page, test as base, expect } from '@playwright/test';
import { API_URL, ApiError, ApiSession, mailLink, publicApi } from '../../journeys/support/api';
import type { Credentials, StaffUser } from '../../journeys/support/fixtures';
import { makePng } from '../../journeys/support/png';
import { signIn } from '../../journeys/support/ui';

export { API_URL, ApiError, ApiSession, makePng, signIn };
export { modal } from '../../journeys/support/ui';
export { pngFile } from '../../journeys/support/png';

/**
 * Campaign-manager + reviewer journey (E2E_SUITE=j-campaigns). Runs against Baseline + Demo (scripts/e2e-journeys.sh
 * picks both for this suite): the journey's own staff, participants and campaigns are created by global-setup.ts
 * through the real API (every name carries the run id); the Demo seed contributes live checks that are already due
 * and a populated analytics dashboard.
 */
export const PASSWORD = 'Campaign-Journey#2026!';
export const DAY = 86_400_000;

export interface Participant extends StaffUser {
  instagramId: string;
  tiktokId: string;
  tiktokHandle: string;
}

export interface State {
  runId: string;
  admin: Credentials;
  manager: StaffUser;
  manager2: StaffUser;
  reviewer1: StaffUser;
  reviewer2: StaffUser;
  /** A reviewer who is also a participant (self-review must be refused). */
  dual: Participant;
  participants: Participant[];
  /** Filled in by the specs as they go (campaign ids by key). */
}

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'state.json');
const CAMPAIGNS_FILE = join(dirname(STATE_FILE), 'campaigns.json');

export function writeState(state: State) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
  writeFileSync(CAMPAIGNS_FILE, '{}');
}

let cached: State | undefined;
export function state(): State {
  cached ??= JSON.parse(readFileSync(STATE_FILE, 'utf8')) as State;
  return cached;
}

/** Ids of campaigns created by earlier specs (the suite runs serially, in file order). */
export function rememberCampaign(key: string, value: { id: string; slug: string; title: string }) {
  const all = JSON.parse(readFileSync(CAMPAIGNS_FILE, 'utf8')) as Record<string, unknown>;
  all[key] = value;
  writeFileSync(CAMPAIGNS_FILE, JSON.stringify(all, null, 2));
}
export function campaign(key: string): { id: string; slug: string; title: string } {
  const all = JSON.parse(readFileSync(CAMPAIGNS_FILE, 'utf8')) as Record<
    string,
    { id: string; slug: string; title: string }
  >;
  const found = all[key];
  if (!found) throw new Error(`Campaign "${key}" was not created by an earlier spec`);
  return found;
}

// ------------------------------------------------------------------ API arrangement helpers

const sessions = new Map<string, Promise<ApiSession>>();
/** A cached API session per user (tokens outlive a spec file). */
export function api(user: Credentials): Promise<ApiSession> {
  let s = sessions.get(user.email);
  if (!s) {
    s = ApiSession.login(user.email, user.password);
    sessions.set(user.email, s);
  }
  return s;
}

/** Calls the API and returns the ApiError it fails with (the test fails if the call succeeds). */
export async function refused(call: Promise<unknown>): Promise<ApiError> {
  try {
    await call;
  } catch (error) {
    if (error instanceof ApiError) return error;
    throw error;
  }
  throw new Error('Expected the API to refuse the request, but it succeeded');
}

export async function createStaff(admin: ApiSession, email: string, displayName: string, roles: string[]) {
  const created = await admin.post<{ profile: { id: string } }>('/admin/users/staff', {
    email,
    displayName,
    countryCode: 'GB',
    roles,
  });
  const link = await mailLink(email, '/reset-password');
  await publicApi.post('/auth/reset-password', {
    token: link.searchParams.get('token'),
    newPassword: PASSWORD,
  });
  return { id: created.profile.id, email, password: PASSWORD, displayName };
}

export async function registerParticipant(email: string, displayName: string, countryCode: string) {
  await publicApi.post('/auth/register', {
    email,
    password: PASSWORD,
    displayName,
    countryCode,
    languageCode: 'en',
    timeZone: 'UTC',
    acceptTerms: true,
  });
  const verify = await mailLink(email, '/verify-email');
  await publicApi.post('/auth/verify-email', { token: verify.searchParams.get('token') });
  const session = await ApiSession.login(email, PASSWORD);
  return { id: session.user.id, email, password: PASSWORD, displayName };
}

/** Adds an Instagram and a TikTok profile declared 400 days old with 5,000 followers (eligible everywhere). */
export async function addProfiles(user: StaffUser, handle: string): Promise<Participant> {
  const session = await ApiSession.login(user.email, user.password);
  const createdAt = new Date(Date.now() - 400 * DAY).toISOString();
  const ig = await session.post<{ id: string }>('/me/social-accounts', {
    platform: 'Instagram',
    handle: `${handle}.ig`,
    profileUrl: `https://www.instagram.com/${handle}.ig/`,
    accountCreatedAt: createdAt,
    followerCount: 5000,
  });
  const tt = await session.post<{ id: string }>('/me/social-accounts', {
    platform: 'TikTok',
    handle: `${handle}.tt`,
    profileUrl: `https://www.tiktok.com/@${handle}.tt`,
    accountCreatedAt: createdAt,
    followerCount: 5000,
  });
  return { ...user, instagramId: ig.id, tiktokId: tt.id, tiktokHandle: `${handle}.tt` };
}

let postSeq = 0;
/**
 * Submits a post as `who` through the API (arrangement: the participant journey itself is covered by the journeys
 * suite). Every call uses a unique post URL and a unique screenshot.
 */
export async function submitPost(
  who: Participant,
  campaignId: string,
  platform: 'Instagram' | 'TikTok' = 'Instagram',
  caption = 'Loving it #ad',
): Promise<string> {
  const session = await api(who);
  postSeq += 1;
  const unique = `${state().runId}${postSeq}${Math.floor(Math.random() * 1e6)}`;
  const form = new FormData();
  form.append('campaignId', campaignId);
  form.append('socialAccountId', platform === 'Instagram' ? who.instagramId : who.tiktokId);
  form.append('platform', platform);
  form.append(
    'postUrl',
    platform === 'Instagram'
      ? `https://www.instagram.com/p/Cj${unique.replace(/[^A-Za-z0-9]/g, '')}/`
      : `https://www.tiktok.com/@${who.tiktokHandle}/video/${(7_300_000_000_000_000_000n + BigInt(Date.now()) * 1000n + BigInt(postSeq)).toString()}`,
  );
  form.append('postedAt', new Date(Date.now() - 60_000).toISOString());
  form.append('captionText', caption);
  form.append(
    'screenshot',
    new Blob([makePng(postSeq * 7 + (Date.now() % 997))], { type: 'image/png' }),
    'post.png',
  );
  return (await session.upload<{ id: string }>('/me/submissions', form)).id;
}

export interface ReviewDetail {
  submission: { id: string; status: string; concurrencyStamp: string };
}

/** Claims and decides a submission through the API as `reviewer` (arrangement for later steps). */
export async function decide(
  reviewer: Credentials,
  submissionId: string,
  decision: 'Approve' | 'RequestCorrection' | 'Reject',
  reason?: string,
) {
  const session = await api(reviewer);
  const claim = await session.post<{ concurrencyStamp: string }>(`/review/submissions/${submissionId}/claim`);
  return session.post<{ status: string; reward: { total: number; appliedCaps: string[] } | null }>(
    `/review/submissions/${submissionId}/decision`,
    { decision, reason, concurrencyStamp: claim.concurrencyStamp },
  );
}

/** Earnings of a submission as the participant sees them (GET /me/submissions/{id}). */
export async function submissionOf(who: Credentials, id: string) {
  return (await api(who)).get<{
    status: string;
    rewardRuleSetVersion?: number;
    earnings?: { type: string; amount: number; status: string }[];
  }>(`/me/submissions/${id}`);
}

// ------------------------------------------------------------------ fixtures

export interface Fixtures {
  /** A fresh browser context signed in as `user`; closed when the test ends. */
  as: (user: Credentials, landing: RegExp) => Promise<Page>;
  anonymous: () => Promise<Page>;
}

export const test = base.extend<Fixtures & { contexts: BrowserContext[] }>({
  contexts: async ({ browser: _browser }, provide) => {
    const contexts: BrowserContext[] = [];
    await provide(contexts);
    for (const context of contexts) await context.close();
  },
  as: async ({ browser, contexts }, provide) => {
    await provide(async (user, landing) => {
      const context = await browser.newContext();
      contexts.push(context);
      const page = await context.newPage();
      await signIn(page, user, landing);
      return page;
    });
  },
  anonymous: async ({ browser, contexts }, provide) => {
    await provide(async () => {
      const context = await browser.newContext();
      contexts.push(context);
      return context.newPage();
    });
  },
});

export { expect };

/** A toast (role status/alert in the notification region) with the given text. */
export function toast(page: Page, text: string | RegExp) {
  return page
    .getByRole('region', { name: /notifications/i })
    .getByText(text)
    .first();
}

/** yyyy-MM-ddTHH:mm in UTC, the value of a datetime-local input in a UTC campaign. */
export function utcInput(ms: number): string {
  return new Date(ms).toISOString().slice(0, 16);
}

const escapeRe = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
/** A form control by its label, whether or not the field is marked "(optional)" / "*". */
export function field(scope: Page | Locator, label: string) {
  return scope.getByLabel(new RegExp(`^${escapeRe(label)}( \\(optional\\))?( \\*)?$`));
}

/** A valid create body (API arrangement), overridable per test. */
export function campaignBody(overrides: Record<string, unknown> = {}) {
  const now = Date.now();
  return {
    title: `Arranged ${state().runId} ${Math.floor(Math.random() * 1e6)}`,
    summary: 'An arranged campaign.',
    description: '',
    topics: [],
    visibility: 'Public',
    startsAt: new Date(now - 3600_000).toISOString(),
    endsAt: new Date(now + 20 * DAY).toISOString(),
    timeZone: 'UTC',
    postingInstructions: 'Post about it.',
    defaultDisclosureText: '#ad',
    maxSubmissionsPerParticipant: 10,
    minPostLiveHours: 0,
    requireScreenshot: true,
    eligibility: { minAccountAgeDays: null, minFollowers: 0, requireVerifiedAccount: false },
    platforms: ['Instagram', 'TikTok'],
    rewardRules: { currency: 'USD', rules: [{ type: 'BaseRate', amount: 5 }] },
    ...overrides,
  };
}
