import { describe, expect, it, vi } from 'vitest';
import { asksRedirectGate, devProxy, proxiedBy, redirectGate } from './devProxy';

const rules = devProxy('http://127.0.0.1:5080');

describe('dev/preview proxy (vite.config.ts)', () => {
  it.each([
    '/team',
    '/terms-of-service',
    '/testimonials',
    '/services',
    '/blog/ga4-consent-mode-guide',
    '/events',
    '/',
  ])('serves the SPA for the public page %s (never the API)', (path) => {
    expect(proxiedBy(rules, path)).toBeUndefined();
  });

  it.each([
    ['/api/v1/public/site', '/api'],
    ['/t/abc123', '^/t/'],
    ['/e/o/token.gif', '^/e/'],
    ['/e/c/token', '^/e/'],
    ['/robots.txt', '^/robots\\.txt$'],
    ['/sitemap.xml', '^/sitemap\\.xml$'],
  ])('forwards %s to the API like nginx does', (path, rule) => {
    expect(proxiedBy(rules, path)).toBe(rule);
  });

  it('serves the sitemap from the API sitemap endpoint', () => {
    expect(rules['^/sitemap\\.xml$']!.rewrite!('/sitemap.xml')).toBe('/api/v1/public/sitemap.xml');
  });
});

describe('redirect gate (vite.config.ts, mirrors nginx @redirect_gate)', () => {
  it.each([
    ['GET', '/about-us', 'text/html,application/xhtml+xml', true],
    ['GET', '/blog/old-post?utm_source=x', '', true],
    ['HEAD', '/services?category=old', '*/*', true],
    ['POST', '/about-us', 'text/html', false],
    ['GET', '/api/v1/public/site', 'application/json', false],
    ['GET', '/t/abc123', '*/*', false],
    ['GET', '/sitemap.xml', '*/*', false],
    ['GET', '/assets/index-abc.js', '*/*', false],
    ['GET', '/@vite/client', '*/*', false],
    ['GET', '/src/main.tsx', '*/*', false],
    ['GET', '/favicon.ico', 'image/avif,image/webp', false],
    ['GET', '/about-us', 'application/json', false],
  ])('%s %s (Accept: %s) asks the gate: %s', (method, url, accept, expected) => {
    expect(asksRedirectGate(rules, method, url, accept)).toBe(expected);
  });

  function serve(plugin: ReturnType<typeof redirectGate>, url: string) {
    let handler:
      Parameters<Parameters<typeof plugin.configureServer>[0]['middlewares']['use']>[0] | undefined;
    plugin.configureServer({ middlewares: { use: (fn) => void (handler = fn) } });
    const res = {
      statusCode: 200,
      headers: {} as Record<string, string>,
      ended: false,
      setHeader(n: string, v: string) {
        this.headers[n] = v;
      },
      end() {
        this.ended = true;
      },
    };
    return new Promise<{ res: typeof res; next: boolean }>((resolve) => {
      handler!({ method: 'GET', url, headers: { accept: 'text/html' } }, res, () =>
        resolve({ res, next: true }),
      );
      const poll = setInterval(() => {
        if (res.ended) {
          clearInterval(poll);
          resolve({ res, next: false });
        }
      }, 1);
    });
  }

  it('answers a moved address with the API’s 301 and falls through otherwise', async () => {
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      const target = new Headers(init?.headers).get('X-Original-URI');
      return target === '/about-us?utm_source=x'
        ? new Response(null, {
            status: 301,
            headers: { Location: '/who-we-are?utm_source=x', 'Cache-Control': 'public, max-age=3600' },
          })
        : new Response(null, { status: 404 });
    });
    vi.stubGlobal('fetch', fetchMock);
    const plugin = redirectGate('http://127.0.0.1:5080', rules);

    const moved = await serve(plugin, '/about-us?utm_source=x');
    expect(moved.next).toBe(false);
    expect(moved.res.statusCode).toBe(301);
    expect(moved.res.headers.Location).toBe('/who-we-are?utm_source=x');
    expect(fetchMock.mock.calls[0]![0]).toBe('http://127.0.0.1:5080/api/v1/public/redirects/gate');

    expect((await serve(plugin, '/still-here')).next).toBe(true);
    // Portal and asset requests never reach the API.
    const calls = fetchMock.mock.calls.length;
    expect((await serve(plugin, '/api/v1/public/site')).next).toBe(true);
    expect(fetchMock.mock.calls.length).toBe(calls);
  });

  it('serves the app when the API is unreachable', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Promise.reject(new TypeError('fetch failed'))),
    );
    expect((await serve(redirectGate('http://127.0.0.1:5080', rules), '/about-us')).next).toBe(true);
  });
});
