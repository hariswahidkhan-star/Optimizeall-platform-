import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** Written by global-setup.ts, read by every spec. */
export const FIXTURES_FILE = join(dirname(fileURLToPath(import.meta.url)), '..', '.state', 'fixtures.json');

export interface Credentials {
  email: string;
  password: string;
  displayName: string;
}

export interface StaffUser extends Credentials {
  id: string;
}

export interface Fixtures {
  runId: string;
  admin: Credentials;
  reviewer1: StaffUser;
  reviewer2: StaffUser;
  manager: StaffUser;
  finance1: StaffUser;
  finance2: StaffUser;
  /** An existing participant whose referral code the journey participants register with. */
  referrer: Credentials & { id: string; referralCode: string };
  campaign: { id: string; slug: string; title: string; disclosure: string; hashtag: string };
  /** Journey participants, registered through the UI by 01-participant.spec (one per Playwright project). */
  participants: Record<string, Credentials & { instagram: string; tiktok: string; postCode: string }>;
}

export function writeFixtures(fixtures: Fixtures) {
  mkdirSync(dirname(FIXTURES_FILE), { recursive: true });
  writeFileSync(FIXTURES_FILE, JSON.stringify(fixtures, null, 2));
}

let cached: Fixtures | undefined;
export function fixtures(): Fixtures {
  cached ??= JSON.parse(readFileSync(FIXTURES_FILE, 'utf8')) as Fixtures;
  return cached;
}

/** The journey participant of a Playwright project ("desktop-chromium" / "mobile-chromium"). */
export function participantFor(projectName: string) {
  const p = fixtures().participants[projectName];
  if (!p) throw new Error(`No journey participant for project ${projectName}`);
  return p;
}
