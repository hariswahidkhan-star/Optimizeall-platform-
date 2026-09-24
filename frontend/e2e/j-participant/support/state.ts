import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Credentials, StaffUser } from '../../journeys/support/fixtures';

/**
 * State of the participant-lifecycle suite (E2E_SUITE=j-participant). global-setup.ts writes the fixtures (staff,
 * campaigns, the credentials the journey participants will register with); specs add what later files need (ids of
 * submissions, the payout batch…) with `remember`. The specs run serially on one worker, so a JSON file is enough.
 */
const STATE_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'j-participant.json');

export interface Participant extends Credentials {
  /** Instagram handle of the profile that qualifies (declared exactly 90 days old). */
  instagram: string;
  /** TikTok handle of the profile one day short of qualifying (89 days old). */
  tiktok: string;
  /** Unique per run: prefix of every post code the participant submits. */
  postPrefix: string;
}

export interface CampaignRef {
  id: string;
  slug: string;
  title: string;
  hashtag: string;
  disclosure: string;
}

export interface Fixtures {
  runId: string;
  admin: Credentials;
  reviewer1: StaffUser;
  reviewer2: StaffUser;
  manager: StaffUser;
  finance1: StaffUser;
  finance2: StaffUser;
  /** Instagram + TikTok, lifestyle topic, 5 USD base + 1 USD first-post bonus, up to 5 posts. */
  main: CampaignRef;
  /** X only, "gadgets" topic — used to check the filters. */
  other: CampaignRef;
  /** The lifecycle participant (registered through the UI by 01-onboarding). */
  pat: Participant;
  /** Pat's friend, who signs up through Pat's referral link (05-referral). */
  friend: Participant;
  /** Registered and used by responsive.spec.ts (mobile project) only. */
  mobile: Participant;
  /** A second, unrelated participant (registered by global setup) whose ids Pat must never reach. */
  stranger: Credentials & { submissionId: string; ticketId: string };
  /** Filled in by the specs. */
  memo: Record<string, string>;
}

export function writeState(state: Fixtures) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

export function state(): Fixtures {
  return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as Fixtures;
}

/** Stores a value for later spec files (e.g. a submission id). */
export function remember(key: string, value: string) {
  const current = state();
  current.memo[key] = value;
  writeState(current);
}

export function recall(key: string): string {
  const value = state().memo[key];
  if (!value) throw new Error(`Nothing remembered under "${key}" — did an earlier spec file fail?`);
  return value;
}
