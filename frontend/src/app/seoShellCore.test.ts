import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import {
  extractShell,
  fillShell,
  isDocumentRequest,
  SHELL_BODY_INCLUDE,
  SHELL_HEAD_INCLUDE,
} from './seoShellCore';

const built = `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
    <!-- oa:seo-defaults -->
    <meta name="description" content="Default" />
    <meta property="og:title" content="Default" />
    <title>Optimize All</title>
    <!-- /oa:seo-defaults -->
    <script type="module" crossorigin src="/assets/index-abc123.js"></script>
    <link rel="stylesheet" crossorigin href="/assets/index-def456.css">
  </head>
  <body>
    <div id="root"></div>
    <noscript>Optimize All needs JavaScript to run.</noscript>
  </body>
</html>`;

describe('seo shell (server-rendered pages)', () => {
  it('keeps the app assets and drops the default SEO tags, title, charset, root and noscript', () => {
    const shell = extractShell(built);
    expect(shell.head).toContain('<script type="module" crossorigin src="/assets/index-abc123.js"></script>');
    expect(shell.head).toContain('/assets/index-def456.css');
    expect(shell.head).toContain('name="viewport"');
    expect(shell.head).not.toMatch(/description|og:title|<title>|charset/);
    expect(shell.body).toBe('');
  });

  it('fills the SSI directives exactly like nginx', () => {
    const doc = `<head>${SHELL_HEAD_INCLUDE}</head><body><div id="root"><div id="oa-ssr">x</div></div>${SHELL_BODY_INCLUDE}</body>`;
    const filled = fillShell(doc, { head: '<script src="/a.js"></script>', body: '<!-- b -->' });
    expect(filled).toBe(
      '<head><script src="/a.js"></script></head><body><div id="root"><div id="oa-ssr">x</div></div><!-- b --></body>',
    );
    // `$` sequences in the shell are inserted literally (no replacement patterns).
    expect(fillShell(SHELL_HEAD_INCLUDE, { head: "$&$'", body: '' })).toBe("$&$'");
  });

  it('the real index.html marks its default SEO region', () => {
    const html = readFileSync('index.html', 'utf8');
    const shell = extractShell(html);
    expect(shell.head).toContain('/site.webmanifest');
    expect(shell.head).not.toContain('og:image');
    expect(shell.body).toContain('/src/main.tsx');
  });

  it.each([
    ['GET', '/', true],
    ['GET', '/services/seo', true],
    ['HEAD', '/blog?page=2', true],
    ['GET', '/careers/seo-strategist?utm_source=x', true],
    ['POST', '/contact', false],
    ['GET', '/api/v1/public/site', false],
    ['GET', '/assets/index-abc.js', false],
    ['GET', '/favicon.ico', false],
    ['GET', '/robots.txt', false],
    ['GET', '/services/seo.md', false],
    ['GET', '/t/abc', false],
    ['GET', '/e/o/x.gif', false],
    ['GET', '/@vite/client', false],
    ['GET', '/src/main.tsx', false],
    ['GET', '/media/videos/intro.mp4', false],
    ['GET', '/team', true],
    ['GET', '/email/unsubscribe/token', true],
  ])('%s %s is a page document: %s', (method, url, expected) => {
    expect(isDocumentRequest(method, url)).toBe(expected);
  });
});
