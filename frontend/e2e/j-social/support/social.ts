import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ApiError, ApiSession, mailLink, publicApi } from '../../journeys/support/api';
import type { Credentials } from '../../journeys/support/fixtures';
import { DEMO_PASSWORD, accounts as agencyAccounts } from '../../agency/support/agency';

export { API_URL, ApiError, ApiSession, latestMail, mailLink } from '../../journeys/support/api';
export { makePng } from '../../journeys/support/png';
export { isoDate, landing, modal, toast, watchErrors } from '../../agency/support/agency';
export {
  errorOf,
  expect,
  fakePng,
  png,
  raw,
  sizedPng,
  statusOf,
  test,
} from '../../j-delivery/support/delivery';

/**
 * Social media + paid ads journey (E2E_SUITE=j-social, Baseline + Demo seed). The journey's own client (with client
 * approval required, an Approver and a Viewer client user) is created by global-setup.ts through the real API; the specs
 * hand on what they create through the run state file (.state/j-social.json). Every name carries the run id, so reruns
 * against a kept database never collide. Publishing goes to the local Graph API stub (support/stub.ts).
 */
const demo = (email: string, displayName: string): Credentials => ({
  email,
  password: DEMO_PASSWORD,
  displayName,
});

export const accounts = {
  ...agencyAccounts,
  /** Ads specialist (Marcus Chen): ads.manage + reports.manage, no social permissions. */
  ads: demo('ads@demo.optimizeall.app', 'Marcus Chen'),
  /** Content creator (Priya Nair): social.manage without social.publish, no ads. */
  content: demo('content@demo.optimizeall.app', 'Priya Nair'),
  /** Strategist (Daniel Okafor): ads.manage, no social permissions. */
  strategist: demo('strategist@demo.optimizeall.app', 'Daniel Okafor'),
  /** Designer (Lucas Moreau): neither social nor ads permissions. */
  designerStaff: demo('designer@demo.optimizeall.app', 'Lucas Moreau'),
  /** Social media manager (Sofia Alvarez): social.manage + social.publish, no ads. */
  socialManager: demo('social@demo.optimizeall.app', 'Sofia Alvarez'),
  /** Nimbus Fitness Owner: another tenant than the journey's client. */
  nimbusOwner: demo('owner@nimbus.demo.optimizeall.app', 'Jordan Blake'),
} as const;

export const CLIENT_PASSWORD = 'Client#Social-2026!';

// ------------------------------------------------------------------ run state

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'j-social.json');

export interface ClientUser extends Credentials {
  id: string;
}

export interface SocialState {
  runId: string;
  client: { id: string; name: string; currency: string };
  approver: ClientUser;
  viewer: ClientUser;
  /** Brand profiles created by 01-connections (by key: facebook, linkedin, x). */
  profiles?: Record<string, { id: string; handle: string }>;
  /** Posts created by the specs, by key. */
  posts?: Record<string, { id: string; title: string }>;
  adAccount?: { id: string; name: string };
  /** When the posts scheduled by 04-publishing become due (ISO). */
  due?: string;
}

export function writeState(state: SocialState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

export function state(): SocialState {
  return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as SocialState;
}

export function saveState(patch: Partial<SocialState>) {
  const current = existsSync(STATE_FILE) ? state() : ({} as SocialState);
  writeState({ ...current, ...patch });
}

export function need<K extends keyof SocialState>(key: K): NonNullable<SocialState[K]> {
  const value = state()[key];
  if (value === undefined || value === null)
    throw new Error(`The run state has no "${key}": run the j-social specs in order (01-connections first).`);
  return value as NonNullable<SocialState[K]>;
}

export function profile(key: 'facebook' | 'linkedin' | 'x' | 'flaky' | 'expired') {
  const p = need('profiles')[key];
  if (!p) throw new Error(`No ${key} profile in the run state: 01-connections.spec.ts must run first.`);
  return p;
}

export function rememberPost(key: string, post: { id: string; title: string }) {
  saveState({ posts: { ...(state().posts ?? {}), [key]: post } });
}

export function post(key: string) {
  const p = state().posts?.[key];
  if (!p) throw new Error(`No post "${key}" in the run state: an earlier j-social spec must create it.`);
  return p;
}

export const runId = () => state().runId;

// ------------------------------------------------------------------ API helpers

const sessions = new Map<string, Promise<ApiSession>>();
/** A cached API session per user. */
export function api(user: Credentials): Promise<ApiSession> {
  let s = sessions.get(user.email);
  if (!s) {
    s = ApiSession.login(user.email, user.password);
    sessions.set(user.email, s);
  }
  return s;
}

export interface VariantBody {
  profileId: string;
  text: string;
  title?: string | null;
  mediaIds?: string[];
  altTexts?: string[];
  link?: string | null;
  firstComment?: string | null;
  hashtags?: string[];
  mentions?: string[];
}

export interface PostDto {
  id: string;
  title: string;
  status: string;
  scheduledAt: string | null;
  concurrencyStamp: string;
  allowedActions: string[];
  failureReason: string | null;
  publishedAt: string | null;
  isValid: boolean;
  variants: {
    id: string;
    profileId: string;
    network: string;
    publishStatus: string;
    attempts: number;
    failureKind: string;
    failureReason: string | null;
    externalPostId: string | null;
    publishedUrl: string | null;
    publishedManually: boolean;
    nextAttemptAt: string | null;
  }[];
  comments: { authorName: string; isClient: boolean; isInternal: boolean; kind: string; body: string }[];
}

export function postBody(title: string, variants: VariantBody[], extra: Record<string, unknown> = {}) {
  return {
    clientAccountId: state().client.id,
    title,
    variants: variants.map((v) => ({
      mediaIds: [],
      altTexts: [],
      hashtags: [],
      mentions: [],
      title: null,
      link: null,
      firstComment: null,
      ...v,
    })),
    ...extra,
  };
}

/** Creates a draft through the API (arrangement only). */
export async function createPost(
  session: ApiSession,
  title: string,
  variants: VariantBody[],
  extra: Record<string, unknown> = {},
) {
  return session.post<PostDto>('/agency/social/posts', postBody(title, variants, extra));
}

/**
 * Takes a draft to Approved the way the product requires for the journey's client (client approval on): submit, internal
 * approval by the social media manager, client approval by the client's Approver.
 */
export async function approveThroughClient(postId: string) {
  const author = await api(accounts.socialManager);
  await author.post(`/agency/social/posts/${postId}/submit`, {});
  await author.post(`/agency/social/posts/${postId}/approve`, {});
  const approver = await api(state().approver);
  await approver.post(`/client/social/posts/${postId}/approve`, {});
  return author.get<PostDto>(`/agency/social/posts/${postId}`);
}

/** Runs the publishing job now (admin, jobs.view + settings.manage); waits out a concurrent run's lease. */
export async function runPublishingJob(): Promise<{ summary: string | null; status: string }> {
  const admin = await api(accounts.admin);
  for (let attempt = 0; attempt < 20; attempt++) {
    try {
      return await admin.post<{ summary: string | null; status: string }>(
        '/admin/jobs/SocialPublishingJob/run',
      );
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        await new Promise((r) => setTimeout(r, 1000));
        continue;
      }
      throw error;
    }
  }
  throw new Error('The publishing job stayed busy');
}

/** Resolves once `at` (ISO) has passed on the server clock (with a small margin). */
export async function waitUntil(at: string, marginMs = 2_000) {
  const wait = new Date(at).getTime() - Date.now() + marginMs;
  if (wait > 0) await new Promise((r) => setTimeout(r, wait));
}

/** Seconds from now as an ISO timestamp (UTC). */
export const inSeconds = (seconds: number) => new Date(Date.now() + seconds * 1000).toISOString();

/** yyyy-MM-ddTHH:mm for a datetime-local input (the browser's local time zone), `minutes` from now. */
export function localInput(minutes: number) {
  const d = new Date(Date.now() + minutes * 60_000);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

// ------------------------------------------------------------------ client users

/** Invites a client user (as the account manager) and sets their password through the emailed link (API only). */
export async function inviteClientUser(
  clientId: string,
  email: string,
  displayName: string,
  role: string,
): Promise<ClientUser> {
  const am = await api(accounts.am);
  await am.post(`/agency/clients/${clientId}/members`, { email, displayName, role });
  const link = await mailLink(email, '/reset-password');
  const token = link.searchParams.get('token');
  if (!token) throw new Error(`No token in the invitation link to ${email}: ${link.href}`);
  await publicApi.post('/auth/reset-password', { token, newPassword: CLIENT_PASSWORD });
  const session = await ApiSession.login(email, CLIENT_PASSWORD);
  return { id: session.user.id, email, displayName, password: CLIENT_PASSWORD };
}
