import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ApiSession } from '../../journeys/support/api';
import type { Credentials, StaffUser } from '../../journeys/support/fixtures';
import { makePng } from '../../journeys/support/png';
import type { Participant } from '../../j-campaigns/support/campaigns';

export {
  addProfiles,
  createStaff,
  expect,
  field,
  modal,
  refused,
  registerParticipant,
  test,
  toast,
  utcInput,
  type Participant,
} from '../../j-campaigns/support/campaigns';

/**
 * Person-level pricing journey (E2E_SUITE=j-rates) against Baseline + Demo: a manager builds a rate card and a rate
 * group, bulk-assigns people, negotiates a personal deal that expires, participants see their own rate, a post is
 * submitted and approved, finance sees the rate source on the ledger, the card changes (old earning unchanged) and the
 * deal expires. The suite's own users and campaign are created by global-setup.ts (names carry the run id).
 */
export interface State {
  runId: string;
  admin: Credentials;
  manager: StaffUser;
  reviewer: StaffUser;
  finance: StaffUser;
  participants: Participant[];
  campaign: { id: string; slug: string; title: string };
  /** When the personal deal created in spec 02 expires (ms since epoch). */
  dealExpiresAt: number;
}

export const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'state.json');
const NOTES_FILE = join(dirname(STATE_FILE), 'notes.json');

export function writeState(state: State) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
  writeFileSync(NOTES_FILE, '{}');
}

let cached: State | undefined;
export function state(): State {
  cached ??= JSON.parse(readFileSync(STATE_FILE, 'utf8')) as State;
  return cached;
}

/** Values later specs need from earlier ones (the suite runs serially, in file order). */
export function remember(key: string, value: unknown) {
  const all = JSON.parse(readFileSync(NOTES_FILE, 'utf8')) as Record<string, unknown>;
  all[key] = value;
  writeFileSync(NOTES_FILE, JSON.stringify(all, null, 2));
}
export function recall<T>(key: string): T {
  const all = JSON.parse(readFileSync(NOTES_FILE, 'utf8')) as Record<string, unknown>;
  if (!(key in all)) throw new Error(`"${key}" was not recorded by an earlier spec`);
  return all[key] as T;
}

const sessions = new Map<string, Promise<ApiSession>>();
export function api(user: Credentials): Promise<ApiSession> {
  let s = sessions.get(user.email);
  if (!s) {
    s = ApiSession.login(user.email, user.password);
    sessions.set(user.email, s);
  }
  return s;
}

let postSeq = 0;
/** Submits an Instagram post as `who` through the API (unique URL and screenshot). */
export async function submitPost(who: Participant, campaignId: string): Promise<string> {
  const session = await api(who);
  postSeq += 1;
  const unique = `${state().runId}r${postSeq}${Math.floor(Math.random() * 1e6)}`.replace(/[^A-Za-z0-9]/g, '');
  const form = new FormData();
  form.append('campaignId', campaignId);
  form.append('socialAccountId', who.instagramId);
  form.append('platform', 'Instagram');
  form.append('postUrl', `https://www.instagram.com/p/Rt${unique}/`);
  form.append('postedAt', new Date(Date.now() - 30_000).toISOString());
  form.append('captionText', 'Rates journey #ad');
  form.append(
    'screenshot',
    new Blob([makePng(postSeq * 13 + (Date.now() % 991))], { type: 'image/png' }),
    'post.png',
  );
  return (await session.upload<{ id: string }>('/me/submissions', form)).id;
}

/** Claims and approves a submission through the API as `reviewer`. */
export async function approve(reviewer: Credentials, submissionId: string) {
  const session = await api(reviewer);
  const claim = await session.post<{ concurrencyStamp: string }>(`/review/submissions/${submissionId}/claim`);
  return session.post<{ status: string; reward: { total: number } | null }>(
    `/review/submissions/${submissionId}/decision`,
    {
      decision: 'Approve',
      concurrencyStamp: claim.concurrencyStamp,
    },
  );
}

export interface LedgerRow {
  id: string;
  type: string;
  originalAmount: number;
  originalCurrency: string;
  submissionId: string | null;
  rateSource: string | null;
  rateSourceLabel: string | null;
  rateCardId: string | null;
  rateCardVersion: number | null;
}

/** The post-reward ledger line of a submission, as finance sees it. */
export async function postReward(submissionId: string): Promise<LedgerRow> {
  const finance = await api(state().finance);
  const page = await finance.get<{ items: LedgerRow[] }>(
    `/finance/ledger?search=${submissionId}&pageSize=50`,
  );
  const row = page.items.find((r) => r.type === 'PostReward');
  if (!row) throw new Error(`No post reward for submission ${submissionId}`);
  return row;
}
