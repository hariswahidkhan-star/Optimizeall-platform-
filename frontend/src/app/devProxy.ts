/**
 * What `vite` and `vite preview` forward to the API (vite.config.ts), mirroring nginx/default.conf.template. Keys follow
 * Vite's proxy rules: a key starting with `^` is a regular expression, any other key matches paths that start with it —
 * so tracking links must be `^/t/`, not `/t`, or /team and /terms-of-service would be sent to the API.
 */
export interface DevProxyRule {
  target: string;
  changeOrigin: boolean;
  rewrite?: (path: string) => string;
}

export function devProxy(apiTarget: string): Record<string, DevProxyRule> {
  return {
    '/api': { target: apiTarget, changeOrigin: false },
    // Campaign tracking links (/t/{code}) and email open pixel, click redirect and one-click unsubscribe (/e/…).
    '^/t/': { target: apiTarget, changeOrigin: false },
    '^/e/': { target: apiTarget, changeOrigin: false },
    // The API generates robots.txt and the sitemap.
    '^/robots\\.txt$': { target: apiTarget, changeOrigin: false },
    '^/sitemap\\.xml$': {
      target: apiTarget,
      changeOrigin: false,
      rewrite: () => '/api/v1/public/sitemap.xml',
    },
  };
}

/** The rule Vite would apply to `path` (same matching as Vite's proxy middleware), if any. */
export function proxiedBy(rules: Record<string, DevProxyRule>, path: string): string | undefined {
  return Object.keys(rules).find((key) =>
    key.startsWith('^') ? new RegExp(key).test(path) : path.startsWith(key),
  );
}

/**
 * Whether the web server asks the API's redirect gate before serving the app shell for this request (nginx's
 * `location /` fallback, mirrored for `vite` and `vite preview` by {@link redirectGate}): page navigations only — not
 * API/proxied paths, Vite's own module requests or static files.
 */
export function asksRedirectGate(
  rules: Record<string, DevProxyRule>,
  method: string,
  url: string,
  accept = '',
): boolean {
  if (method !== 'GET' && method !== 'HEAD') return false;
  const path = url.split(/[?#]/)[0] ?? '/';
  if (proxiedBy(rules, path)) return false;
  if (/^\/(@|node_modules\/|src\/|assets\/)/.test(path)) return false;
  if ((path.split('/').pop() ?? '').includes('.')) return false;
  return accept === '' || accept.includes('text/html') || accept.includes('*/*');
}

/** The subset of Vite's dev/preview server the gate needs (keeps Node's http types out of the app build). */
interface GateServer {
  middlewares: {
    use(
      fn: (
        req: { method?: string; url?: string; headers: Record<string, string | string[] | undefined> },
        res: { statusCode: number; setHeader(name: string, value: string): void; end(): void },
        next: () => void,
      ) => void,
    ): void;
  };
}

/**
 * Vite plugin: answers a moved public address with the API's 301 (Website → Redirects) before the app shell is served,
 * exactly like nginx does in production (frontend/nginx/default.conf.template). Anything else — or an unreachable
 * API — falls through to the SPA.
 */
export function redirectGate(apiTarget: string, rules: Record<string, DevProxyRule>) {
  // Returns nothing on purpose: a function returned from configureServer would be run by Vite as a "post" hook.
  const install = (server: GateServer): void => {
    server.middlewares.use((req, res, next) => {
      const url = req.url ?? '/';
      const accept = String(req.headers.accept ?? '');
      if (!asksRedirectGate(rules, req.method ?? 'GET', url, accept)) return next();
      fetch(`${apiTarget}/api/v1/public/redirects/gate`, {
        headers: { 'X-Original-URI': url },
        redirect: 'manual',
        signal: AbortSignal.timeout(5_000),
      })
        .then((response) => {
          const location = response.headers.get('location');
          if (response.status !== 301 || !location) return next();
          res.statusCode = 301;
          res.setHeader('Location', location);
          res.setHeader('Cache-Control', response.headers.get('cache-control') ?? 'no-cache');
          res.end();
        })
        .catch(() => next());
    });
  };
  return {
    name: 'optimizeall-redirect-gate',
    configureServer: install,
    configurePreviewServer: install,
  };
}
