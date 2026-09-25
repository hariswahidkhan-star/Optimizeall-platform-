import { describe, expect, it } from 'vitest';
import { devProxy, proxiedBy } from './devProxy';

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
    ['/sitemaps/blog.xml', '^/sitemaps/'],
    ['/llms.txt', '^/llms(-full)?\\.txt$'],
    ['/llms-full.txt', '^/llms(-full)?\\.txt$'],
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
