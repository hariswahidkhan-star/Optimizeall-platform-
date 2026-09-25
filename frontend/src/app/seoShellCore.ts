/**
 * Server-rendered public pages (docs/SEO_CRO.md § Rendering). The API answers `/_document{path}` with a complete HTML
 * document (SEO head + crawlable content) that contains two nginx SSI directives in place of the app shell. The shell is
 * the Vite build's own `index.html` minus its default SEO tags: at build time it is split into `dist/__shell/head.html`
 * and `dist/__shell/body.html` (nginx includes them), and `vite` / `vite preview` fill the directives the same way
 * (seoShell.ts). Pure string helpers only, so they are unit-tested and shared by the build, dev and preview servers.
 */

export const SHELL_HEAD_INCLUDE = '<!--# include virtual="/__shell/head.html" -->';
export const SHELL_BODY_INCLUDE = '<!--# include virtual="/__shell/body.html" -->';

/** Marks the default SEO tags in index.html that server-rendered documents replace with page-specific ones. */
export const SEO_DEFAULTS_START = '<!-- oa:seo-defaults -->';
export const SEO_DEFAULTS_END = '<!-- /oa:seo-defaults -->';

export interface Shell {
  head: string;
  body: string;
}

function inner(html: string, tag: 'head' | 'body'): string {
  const open = html.search(new RegExp(`<${tag}(\\s[^>]*)?>`, 'i'));
  const close = html.search(new RegExp(`</${tag}>`, 'i'));
  if (open < 0 || close < 0) throw new Error(`index.html has no <${tag}> element`);
  return html.slice(html.indexOf('>', open) + 1, close);
}

/**
 * Splits a built (or dev-transformed) index.html into the parts every server-rendered page needs: the head without
 * `<meta charset>`, `<title>` and the default SEO region, and the body without the empty `#root` and `<noscript>`.
 */
export function extractShell(indexHtml: string): Shell {
  let head = inner(indexHtml, 'head');
  const start = head.indexOf(SEO_DEFAULTS_START);
  const end = head.indexOf(SEO_DEFAULTS_END);
  if (start >= 0 && end > start) head = head.slice(0, start) + head.slice(end + SEO_DEFAULTS_END.length);
  head = head
    .replace(/<meta\s+charset=[^>]*>/i, '')
    .replace(/<title>[\s\S]*?<\/title>/i, '')
    .replace(/\n\s*\n+/g, '\n')
    .trim();
  // The "needs JavaScript" notice is dropped: server-rendered pages are readable without scripts.
  const body = inner(indexHtml, 'body')
    .replace(/<div id="root"><\/div>/, '')
    .replace(/<noscript>[\s\S]*?<\/noscript>/i, '')
    .replace(/\n\s*\n+/g, '\n')
    .trim();
  return { head, body };
}

/** Replaces the SSI directives of a server-rendered document with the shell (what nginx does in production). */
export function fillShell(document: string, shell: Shell): string {
  return document.replace(SHELL_HEAD_INCLUDE, () => shell.head).replace(SHELL_BODY_INCLUDE, () => shell.body);
}

/** Paths the web server never renders as documents: API, tracking, static assets, Vite internals. */
const NOT_DOCUMENTS =
  /^\/(api|t|e|assets|media|__shell|_document|_markdown|@[a-z-]+|src|node_modules|health)(\/|$)/;

/**
 * Whether a request is for a page document (rendered by the API) rather than an asset or API call: GET or HEAD, not
 * under a reserved prefix, and without a file extension in its last segment.
 */
export function isDocumentRequest(method: string | undefined, url: string | undefined): boolean {
  if (method !== 'GET' && method !== 'HEAD') return false;
  const path = (url ?? '/').split('?')[0];
  if (NOT_DOCUMENTS.test(path)) return false;
  const last = path.slice(path.lastIndexOf('/') + 1);
  return !last.includes('.');
}

/** Response headers of the API's document that the browser should see. */
export const DOCUMENT_HEADERS = [
  'content-type',
  'cache-control',
  'x-robots-tag',
  'content-language',
  'retry-after',
  'location',
];
