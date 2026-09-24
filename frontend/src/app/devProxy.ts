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
