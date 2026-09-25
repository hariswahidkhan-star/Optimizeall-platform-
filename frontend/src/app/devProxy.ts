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
    // The API generates the SEO files: robots.txt, the sitemap index and sitemaps, llms.txt / llms-full.txt,
    // the llms/ section files, security.txt, humans.txt, the IndexNow key file and the Markdown version of every page (/{path}.md).
    // Page documents themselves are rendered by the API through the seoShell plugin (seoShell.ts).
    '^/robots\\.txt$': { target: apiTarget, changeOrigin: false },
    '^/sitemap\\.xml$': { target: apiTarget, changeOrigin: false },
    '^/sitemaps/': { target: apiTarget, changeOrigin: false },
    '^/llms(-full)?\\.txt$': { target: apiTarget, changeOrigin: false },
    '^/llms/[a-z0-9-]+\\.txt$': { target: apiTarget, changeOrigin: false },
    // Generated social cards (Open Graph images): /og{path}.png.
    '^/og/': { target: apiTarget, changeOrigin: false },
    '^/humans\\.txt$': { target: apiTarget, changeOrigin: false },
    '^/\\.well-known/security\\.txt$': { target: apiTarget, changeOrigin: false },
    '^/[A-Za-z0-9-]{8,128}\\.txt$': { target: apiTarget, changeOrigin: false },
    '^/(?!api/|assets/|src/|node_modules/)[^?]+\\.md(\\?.*)?$': {
      target: apiTarget,
      changeOrigin: false,
      rewrite: (path) => `/_markdown${path.split('?')[0].replace(/\.md$/, '')}`,
    },
  };
}

/** The rule Vite would apply to `path` (same matching as Vite's proxy middleware), if any. */
export function proxiedBy(rules: Record<string, DevProxyRule>, path: string): string | undefined {
  return Object.keys(rules).find((key) =>
    key.startsWith('^') ? new RegExp(key).test(path) : path.startsWith(key),
  );
}
