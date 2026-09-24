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
  ])('forwards %s to the API like nginx does', (path, rule) => {
    expect(proxiedBy(rules, path)).toBe(rule);
  });

  it('serves the sitemap from the API sitemap endpoint', () => {
    expect(rules['^/sitemap\\.xml$']!.rewrite!('/sitemap.xml')).toBe('/api/v1/public/sitemap.xml');
  });
});
