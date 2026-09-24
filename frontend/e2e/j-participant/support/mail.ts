import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Every email written to the file-mode pickup directory (E2E_MAIL_DIR, set by scripts/e2e-journeys.sh) for one
 * recipient, oldest first. The dev mailbox endpoint only returns the latest message; asserting that a message was
 * *not* sent (a muted notification kind) needs the whole list.
 */
export interface Mail {
  file: string;
  subject: string;
  body: string;
}

const MAIL_DIR = process.env.E2E_MAIL_DIR;

function decodeWords(value: string): string {
  return value.replace(
    /=\?([^?]+)\?([BbQq])\?([^?]*)\?=/g,
    (_m, _charset: string, enc: string, text: string) =>
      enc.toUpperCase() === 'B'
        ? Buffer.from(text, 'base64').toString('utf8')
        : Buffer.from(
            text
              .replace(/_/g, ' ')
              .replace(/=([0-9A-Fa-f]{2})/g, (_x, h: string) => String.fromCharCode(parseInt(h, 16))),
            'latin1',
          ).toString('utf8'),
  );
}

function parse(file: string): { to: string; subject: string; body: string } {
  const raw = readFileSync(file, 'utf8');
  const split = raw.search(/\r?\n\r?\n/);
  const head = raw.slice(0, split).replace(/\r?\n[ \t]+/g, ' ');
  const header = (name: string) =>
    decodeWords(head.match(new RegExp(`^${name}:\\s*(.*)$`, 'im'))?.[1]?.trim() ?? '').replace(
      /\?=\s+=\?/g,
      '?==?',
    );
  return { to: header('To').toLowerCase(), subject: header('Subject'), body: raw.slice(split) };
}

export function mailsTo(address: string): Mail[] {
  if (!MAIL_DIR) throw new Error('E2E_MAIL_DIR is not set (run the suite through scripts/e2e-journeys.sh)');
  const wanted = address.toLowerCase();
  return readdirSync(MAIL_DIR)
    .filter((f) => f.endsWith('.eml'))
    .sort()
    .map((f) => ({ file: f, ...parse(join(MAIL_DIR, f)) }))
    .filter((m) => m.to.includes(wanted))
    .map(({ file, subject, body }) => ({ file, subject, body }));
}

export function subjectsTo(address: string): string[] {
  return mailsTo(address).map((m) => m.subject);
}

/** The first link starting with `pathPrefix` in the latest email to `address` whose subject matches. */
export function linkIn(address: string, subject: RegExp, pathPrefix: string): URL {
  const mail = mailsTo(address)
    .filter((m) => subject.test(m.subject))
    .pop();
  if (!mail) throw new Error(`No mail to ${address} matching ${subject}: ${subjectsTo(address).join(' | ')}`);
  const link = (mail.body.match(/https?:\/\/\S+/g) ?? [])
    .map((l) => new URL(l))
    .find((u) => u.pathname.startsWith(pathPrefix));
  if (!link) throw new Error(`No ${pathPrefix} link in "${mail.subject}" to ${address}`);
  return link;
}
