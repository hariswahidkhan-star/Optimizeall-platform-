import { afterEach, describe, expect, it, vi } from 'vitest';
import { seoShell } from './seoShell';

type Handler = (
  req: { method?: string; url?: string; headers: Record<string, string> },
  res: FakeResponse,
  next: (err?: unknown) => void,
) => void;

interface FakeResponse {
  statusCode: number;
  headers: Record<string, string | number>;
  body: string | undefined;
  ended: boolean;
  setHeader(name: string, value: string | number): void;
  end(body?: string): void;
}

/** Installs the plugin's dev-server middleware and sends one request through it. */
async function serve(url: string, method = 'GET') {
  const plugin = seoShell('http://127.0.0.1:5080');
  let handler: Handler | undefined;
  const server = {
    middlewares: { use: (fn: Handler) => void (handler = fn) },
    transformIndexHtml: async (_url: string, html: string) => html,
  };
  (plugin.configResolved as (c: unknown) => void)({ root: __dirname, build: { outDir: 'dist' } });
  (plugin.configureServer as (s: unknown) => void)(server);
  const res: FakeResponse = {
    statusCode: 200,
    headers: {},
    body: undefined,
    ended: false,
    setHeader(n, v) {
      this.headers[n.toLowerCase()] = v;
    },
    end(body) {
      this.body = body;
      this.ended = true;
    },
  };
  return new Promise<{ res: FakeResponse; next: boolean }>((resolve) => {
    handler!({ method, url, headers: { accept: 'text/html' } }, res, () => resolve({ res, next: true }));
    const poll = setInterval(() => {
      if (res.ended) {
        clearInterval(poll);
        resolve({ res, next: false });
      }
    }, 1);
  });
}

describe('seoShell plugin (vite / vite preview, mirrors nginx @document)', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('passes a moved address’s 301 from /_document through with its Location (Website → Redirects)', async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(null, {
          status: 301,
          headers: { Location: '/who-we-are?utm_source=x', 'Cache-Control': 'public, max-age=3600' },
        }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const moved = await serve('/about-us?utm_source=x');
    expect(moved.next).toBe(false);
    expect(moved.res.statusCode).toBe(301);
    expect(moved.res.headers.location).toBe('/who-we-are?utm_source=x');
    expect(moved.res.headers['cache-control']).toBe('public, max-age=3600');
    expect((fetchMock.mock.calls[0] as unknown[])[0]).toBe(
      'http://127.0.0.1:5080/_document/about-us?utm_source=x',
    );
  });

  it('serves the rendered page with the shell and its real status (404 here)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(
            '<!doctype html><html><head><title>Not found</title><!--# include virtual="/__shell/head.html" --></head><body><h1>Not found</h1><!--# include virtual="/__shell/body.html" --></body></html>',
            { status: 404, headers: { 'Content-Type': 'text/html; charset=utf-8' } },
          ),
      ),
    );
    const page = await serve('/no-such-page');
    expect(page.res.statusCode).toBe(404);
    expect(page.res.body).toContain('<h1>Not found</h1>');
    expect(page.res.body).not.toContain('<!--# include');
  });

  it('never asks the API for assets or API calls, and falls back to the SPA when the API is unreachable', async () => {
    const fetchMock = vi.fn(async () => Promise.reject(new TypeError('fetch failed')));
    vi.stubGlobal('fetch', fetchMock);
    expect((await serve('/api/v1/public/site')).next).toBe(true);
    expect((await serve('/assets/index-abc.js')).next).toBe(true);
    expect(fetchMock).not.toHaveBeenCalled();
    expect((await serve('/about-us')).next).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('preloads the Latin Inter file the build emits for the imported axis set (opsz), not Latin-extended or italic', () => {
    const plugin = seoShell('http://127.0.0.1:5080');
    const hook = plugin.transformIndexHtml as {
      handler: (html: string, ctx: { bundle?: Record<string, unknown> }) => string;
    };
    const bundle = {
      'assets/inter-latin-ext-opsz-normal-AAA.woff2': {},
      'assets/inter-latin-opsz-italic-BBB.woff2': {},
      'assets/inter-latin-opsz-normal-CCC.woff2': {},
    };
    const html = hook.handler('<html><head></head><body></body></html>', { bundle });
    expect(html).toContain(
      '<link rel="preload" as="font" type="font/woff2" href="/assets/inter-latin-opsz-normal-CCC.woff2" crossorigin />',
    );
    expect(html.match(/rel="preload"/g)).toHaveLength(1);
  });
});
