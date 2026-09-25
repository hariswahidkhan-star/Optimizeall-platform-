import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ApiSession } from '../../journeys/support/api';
import type { Credentials, StaffUser } from '../../journeys/support/fixtures';

export {
  createStaff,
  expect,
  field,
  modal,
  refused,
  registerParticipant,
  test,
  toast,
} from '../../j-campaigns/support/campaigns';

/**
 * Discount-code (affiliate) sales journey (E2E_SUITE=j-codes) against Baseline + Demo: a manager creates a brand
 * program, imports the brand's codes from CSV, assigns a personal code and a shared (rate-group) code; participants see
 * their codes and report sales; a reviewer approves one and finance sees the commission with its payout source; the
 * brand's sales report matches / flags sales and reports a refund that reverses the commission. The suite's users are
 * created by global-setup.ts (names carry the run id).
 */
export interface State {
  runId: string;
  admin: Credentials;
  manager: StaffUser;
  reviewer: StaffUser;
  finance: StaffUser;
  /** Personal-code holder. */
  ivy: StaffUser;
  /** Member of the shared-code group. */
  milo: StaffUser;
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

/** The suite's codes: two personal codes and one shared code, named after the run. */
export function codes() {
  const r = state().runId.toUpperCase();
  return { ivy: `IVY-${r}`, spare: `SPARE-${r}`, squad: `SQUAD-${r}` };
}

/** A CSV file for a Playwright file input. */
export function csvFile(name: string, content: string) {
  return { name, mimeType: 'text/csv', buffer: Buffer.from(content, 'utf8') };
}

export interface LedgerRow {
  id: string;
  type: string;
  status: string;
  originalAmount: number;
  originalCurrency: string;
  rateSource: string | null;
  rateSourceLabel: string | null;
  codeSaleId: string | null;
  reversesEntryId: string | null;
}

/** Ledger rows of a user, as finance sees them. */
export async function ledgerOf(userId: string): Promise<LedgerRow[]> {
  const finance = await api(state().finance);
  return (await finance.get<{ items: LedgerRow[] }>(`/finance/ledger?userId=${userId}&pageSize=100`)).items;
}
