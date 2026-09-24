import { type IncomingMessage, type Server, type ServerResponse, createServer } from 'node:http';

/**
 * A local stand-in for the Meta Graph API (and X), served by the suite's global setup on E2E_STUB_PORT. The API under
 * test is started by scripts/e2e-journeys.sh with SocialMedia__GraphApiBaseUrl=http://127.0.0.1:$E2E_STUB_PORT/graph,
 * so publishing never reaches a real network. The page token decides the answer:
 *
 *   e2e-token-ok…        → 200 {"id": "<page>_<n>"}            (published)
 *   e2e-token-expired…   → 400 {"error": {"code": 190}}         (token expired: Authorization failure)
 *   e2e-token-flaky…     → 503 on the first call per page, then 200   (transient: retried with backoff)
 *   e2e-token-rejected…  → 400 {"error": {"code": 100}}         (rejected by the network)
 *
 * Every call is recorded; the specs read them with GET /__stub/requests and clear them with POST /__stub/reset.
 * POST /__stub/delay {"ms": n} delays every Graph answer (to make publishing runs overlap).
 */
export interface StubRequest {
  method: string;
  path: string;
  token: string;
  form: Record<string, string>;
  status: number;
  at: string;
}

export const STUB_PORT = Number(process.env.E2E_STUB_PORT || 7099);
export const STUB_URL = `http://127.0.0.1:${STUB_PORT}`;

export function startStub(): Promise<Server> {
  const requests: StubRequest[] = [];
  const flakySeen = new Set<string>();
  let counter = 1000;
  let delayMs = 0;

  const read = (req: IncomingMessage) =>
    new Promise<string>((resolve) => {
      let body = '';
      req.on('data', (chunk: Buffer) => (body += chunk.toString('utf8')));
      req.on('end', () => resolve(body));
    });

  const send = (res: ServerResponse, status: number, body: unknown) => {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(body));
  };

  const server = createServer(async (req, res) => {
    const url = new URL(req.url ?? '/', STUB_URL);
    const raw = await read(req);
    if (url.pathname === '/__stub/requests') return send(res, 200, requests);
    if (url.pathname === '/__stub/reset') {
      requests.length = 0;
      flakySeen.clear();
      delayMs = 0;
      return send(res, 200, { ok: true });
    }
    if (url.pathname === '/__stub/delay') {
      delayMs = Number((JSON.parse(raw || '{}') as { ms?: number }).ms ?? 0);
      return send(res, 200, { ok: true });
    }

    const token = (req.headers.authorization ?? '').replace(/^Bearer\s+/i, '');
    const isX = url.pathname.startsWith('/x/');
    const form: Record<string, string> = isX
      ? Object.fromEntries(
          Object.entries(JSON.parse(raw || '{}') as Record<string, unknown>).map(([k, v]) => [
            k,
            JSON.stringify(v),
          ]),
        )
      : Object.fromEntries(new URLSearchParams(raw));
    const path = url.pathname.replace(/^\/graph/, '');
    const record = (status: number) =>
      requests.push({ method: req.method ?? 'GET', path, token, form, status, at: new Date().toISOString() });
    if (delayMs > 0) await new Promise((r) => setTimeout(r, delayMs));
    if (isX) {
      // X API v2: POST /2/tweets → 201 {"data": {"id": …}}; 401 for an expired token.
      if (!token.startsWith('e2e-token-ok')) {
        record(401);
        return send(res, 401, { title: 'Unauthorized', status: 401, detail: 'Unauthorized (e2e stub)' });
      }
      record(201);
      return send(res, 201, { data: { id: String(++counter), text: form.text ?? '' } });
    }

    if (token.startsWith('e2e-token-expired')) {
      record(400);
      return send(res, 400, {
        error: {
          message: 'Error validating access token: Session has expired.',
          type: 'OAuthException',
          code: 190,
        },
      });
    }
    if (token.startsWith('e2e-token-rejected')) {
      record(400);
      return send(res, 400, {
        error: { message: 'Invalid parameter (e2e)', type: 'OAuthException', code: 100 },
      });
    }
    if (token.startsWith('e2e-token-flaky') && req.method === 'POST' && !flakySeen.has(path)) {
      flakySeen.add(path);
      record(503);
      return send(res, 503, { error: { message: 'Service temporarily unavailable (e2e)', code: 2 } });
    }
    if (!token.startsWith('e2e-token-')) {
      record(401);
      return send(res, 401, { error: { message: 'Unknown token (e2e stub)', code: 190 } });
    }
    record(200);
    if (req.method === 'GET')
      return send(res, 200, { id: path.slice(1), permalink_url: `https://www.facebook.com${path}` });
    const page = path.split('/')[1] ?? 'page';
    return send(res, 200, { id: `${page}_${++counter}` });
  });
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(STUB_PORT, '127.0.0.1', () => resolve(server));
  });
}

/** The Graph calls recorded since the last reset (the stub runs in the Playwright runner process). */
export async function stubRequests(): Promise<StubRequest[]> {
  return (await (await fetch(`${STUB_URL}/__stub/requests`)).json()) as StubRequest[];
}

export async function resetStub() {
  await fetch(`${STUB_URL}/__stub/reset`, { method: 'POST' });
}

export async function delayStub(ms: number) {
  await fetch(`${STUB_URL}/__stub/delay`, { method: 'POST', body: JSON.stringify({ ms }) });
}
