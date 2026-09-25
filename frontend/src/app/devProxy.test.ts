import { describe, expect, it } from 'vitest';
import { devProxy, proxiedBy } from './devProxy';
import { isDocumentRequest } from './seoShellCore';

const rules = devProxy('http://127.0.0.1:5080');

describe('dev/preview proxy (vite.config.ts)', () => {
  it.each([
    '/team',
    '/terms-of-service',
    '/testimonials',
    '/services',
    '/blog/ga4-consent-mode-guide',
    '/events',
    '/ogilvy-case-study',
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
    ['/sitemaps/blog.xml', '^/sitemaps/'],
    ['/llms.txt', '^/llms(-full)?\\.txt$'],
    ['/llms-full.txt', '^/llms(-full)?\\.txt$'],
    ['/llms/academy.txt', '^/llms/[a-z0-9-]+\\.txt$'],
    ['/og/index.png', '^/og/'],
    ['/og/learn/seo-basics.png', '^/og/'],
    ['/humans.txt', '^/humans\\.txt$'],
    ['/.well-known/security.txt', '^/\\.well-known/security\\.txt$'],
    ['/0123456789abcdef.txt', '^/[A-Za-z0-9-]{8,128}\\.txt$'],
    ['/services/seo.md', '^/(?!api/|assets/|src/|node_modules/)[^?]+\\.md(\\?.*)?$'],
  ])('forwards %s to the API like nginx does', (path, rule) => {
    expect(proxiedBy(rules, path)).toBe(rule);
  });

  it('serves the sitemap index from the API as is', () => {
    expect(rules['^/sitemap\\.xml$']!.rewrite).toBeUndefined();
  });

  it('maps Markdown page versions to the API renderer', () => {
    const md = rules['^/(?!api/|assets/|src/|node_modules/)[^?]+\\.md(\\?.*)?$']!;
    expect(md.rewrite!('/services/seo.md')).toBe('/_markdown/services/seo');
    expect(md.rewrite!('/index.md')).toBe('/_markdown/index');
  });
});

describe('page documents vs the proxy (vite.config.ts, mirrors nginx `location /` → @document)', () => {
  // One fallback chain, as in nginx: proxied paths go to the API as is; every other page request is rendered by
  // /_document (seoShell.ts), which also answers moved public addresses (Website → Redirects) with their real 301.
  it.each([
    ['GET', '/about-us', true],
    ['GET', '/blog/old-post?utm_source=x', true],
    ['HEAD', '/services?category=old', true],
    ['GET', '/', true],
    ['POST', '/about-us', false],
    ['GET', '/assets/index-abc.js', false],
    ['GET', '/@vite/client', false],
    ['GET', '/src/main.tsx', false],
    ['GET', '/favicon.ico', false],
  ])('%s %s is a page document: %s', (method, url, expected) => {
    expect(isDocumentRequest(method, url)).toBe(expected);
  });

  it.each([
    '/api/v1/public/site',
    '/t/abc123',
    '/e/o/token.gif',
    '/robots.txt',
    '/sitemap.xml',
    '/sitemaps/blog.xml',
    '/llms.txt',
    '/llms/academy.txt',
    '/og/services/seo.png',
    '/humans.txt',
    '/.well-known/security.txt',
    '/0123456789abcdef.txt',
    '/services/seo.md',
  ])('the proxied path %s is never rendered as a page', (path) => {
    expect(proxiedBy(rules, path)).toBeDefined();
    expect(isDocumentRequest('GET', path)).toBe(false);
  });
});
