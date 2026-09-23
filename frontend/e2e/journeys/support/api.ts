/**
 * Minimal client for the real API, used only to *arrange* journey data (global setup, second submissions, schedule
 * tweaks) — every behaviour under test goes through the UI.
 */
export const API_URL = (
  process.env.E2E_API_URL ||
  process.env.E2E_BASE_URL ||
  'http://localhost:5099'
).replace(/\/$/, '');
const API_BASE = `${API_URL}/api/v1`;

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    readonly body: unknown,
    what: string,
  ) {
    super(`${what} → ${status} ${code ?? ''} ${JSON.stringify(body)}`);
  }
}

async function send<T>(
  method: string,
  path: string,
  { body, token, form }: { body?: unknown; token?: string; form?: FormData } = {},
): Promise<T> {
  const headers: Record<string, string> = { 'X-Requested-With': 'fetch' };
  if (token) headers.Authorization = `Bearer ${token}`;
  let payload: BodyInit | undefined;
  if (form) payload = form;
  else if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
    payload = JSON.stringify(body);
  }
  const res = await fetch(`${API_BASE}${path}`, { method, headers, body: payload });
  const text = await res.text();
  const json = text ? safeJson(text) : undefined;
  if (!res.ok) {
    const code = (json as { code?: string } | undefined)?.code;
    throw new ApiError(res.status, code, json ?? text, `${method} ${path}`);
  }
  return json as T;
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export interface SessionUser {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  permissions: string[];
}

/** A signed-in API session (bearer token from POST /auth/login). */
export class ApiSession {
  private constructor(
    readonly token: string,
    readonly user: SessionUser,
  ) {}

  static async login(email: string, password: string): Promise<ApiSession> {
    const res = await send<{ accessToken: string; user: SessionUser }>('POST', '/auth/login', {
      body: { email, password },
    });
    return new ApiSession(res.accessToken, res.user);
  }

  get<T>(path: string) {
    return send<T>('GET', path, { token: this.token });
  }
  post<T>(path: string, body?: unknown) {
    return send<T>('POST', path, { token: this.token, body: body ?? {} });
  }
  put<T>(path: string, body?: unknown) {
    return send<T>('PUT', path, { token: this.token, body: body ?? {} });
  }
  upload<T>(path: string, form: FormData) {
    return send<T>('POST', path, { token: this.token, form });
  }
}

export const publicApi = {
  post: <T>(path: string, body: unknown) => send<T>('POST', path, { body }),
};

interface MailboxMessage {
  subject: string;
  date: string;
  text: string;
  links: string[];
}

/**
 * Latest email to `to` from the dev mailbox (GET /dev/mailbox, file-mode email only). Polls briefly because the
 * mail is written by the request that triggered it, but the caller may race the file system.
 */
export async function latestMail(
  to: string,
  subjectPattern?: RegExp,
  timeoutMs = 15_000,
): Promise<MailboxMessage> {
  const deadline = Date.now() + timeoutMs;
  let last: unknown;
  while (Date.now() < deadline) {
    try {
      const mail = await send<MailboxMessage>('GET', `/dev/mailbox?to=${encodeURIComponent(to)}`);
      if (!subjectPattern || subjectPattern.test(mail.subject)) return mail;
      last = mail.subject;
    } catch (error) {
      last = error;
    }
    await new Promise((r) => setTimeout(r, 250));
  }
  throw new Error(`No mail to ${to} matching ${subjectPattern ?? 'anything'} (last: ${String(last)})`);
}

/** The first link in the latest matching email whose path starts with `pathPrefix` (e.g. "/verify-email"). */
export async function mailLink(to: string, pathPrefix: string, subjectPattern?: RegExp): Promise<URL> {
  const mail = await latestMail(to, subjectPattern);
  const link = mail.links.map((l) => new URL(l)).find((u) => u.pathname.startsWith(pathPrefix));
  if (!link) throw new Error(`No ${pathPrefix} link in "${mail.subject}" to ${to}: ${mail.links.join(', ')}`);
  return link;
}
