import { expect, type APIRequestContext, type Browser, type Page } from '@playwright/test';

/**
 * Helpers for the technical-SEO suite (E2E_SUITE=j-seo): what search engines, AI crawlers and link unfurlers get from
 * every public URL without running JavaScript. The web server under test is `vite preview` with the seoShell plugin
 * (the default of scripts/e2e-journeys.sh) or the production nginx configuration (E2E_WEB_SERVER=nginx).
 */
export const BASE = (
  process.env.E2E_BASE_URL ||
  process.env.PLAYWRIGHT_BASE_URL ||
  'http://localhost:4173'
).replace(/\/$/, '');

/** Built-in public pages that must be indexable (Baseline + Demo seed). */
export const PUBLIC_PAGES = [
  '/',
  '/services',
  '/services/seo',
  '/services/google-ads-ppc',
  '/pricing',
  '/industries',
  '/industries/ecommerce',
  '/case-studies',
  '/blog',
  '/team',
  '/careers',
  '/contact',
  '/free-audit',
  '/get-a-quote',
  '/book-a-consultation',
  '/creators',
  '/faq',
  '/about',
  '/academy',
  '/how-we-work',
  '/privacy-policy',
  '/terms-of-service',
  '/cookie-policy',
  '/accessibility',
  '/refund-policy',
];

/** Signed-in areas and sign-in pages: never indexed. */
export const PRIVATE_PAGES = [
  '/login',
  '/register',
  '/forgot-password',
  '/agency',
  '/admin/users',
  '/app',
  '/client',
  '/manage/campaigns',
];

/** User agents of search engines, AI crawlers and link unfurlers: all must get the same HTML as everyone else. */
export const BOT_AGENTS = {
  googlebot: 'Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)',
  bingbot: 'Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)',
  gptbot:
    'Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko); compatible; GPTBot/1.2; +https://openai.com/gptbot',
  claudebot:
    'Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; ClaudeBot/1.0; +claudebot@anthropic.com)',
  facebook: 'facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)',
  linkedin: 'LinkedInBot/1.0 (compatible; Mozilla/5.0; Apache-HttpClient +http://www.linkedin.com)',
};

/** A browser page with JavaScript disabled: it sees exactly the server's HTML, like a crawler that does not render. */
export async function noJsPage(browser: Browser): Promise<Page> {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL: BASE });
  return context.newPage();
}

export interface Head {
  title: string;
  description: string | null;
  robots: string | null;
  canonical: string | null;
  og: Record<string, string>;
  twitter: Record<string, string>;
  jsonLd: Record<string, unknown>[];
}

/** Reads the SEO head of the current document (works with JavaScript disabled: locators, no page scripts). */
export async function readHead(page: Page): Promise<Head> {
  const attr = async (selector: string, name: string) => {
    const el = page.locator(selector).first();
    return (await el.count()) ? el.getAttribute(name) : null;
  };
  const og: Record<string, string> = {};
  for (const el of await page.locator('meta[property^="og:"]').all())
    og[(await el.getAttribute('property'))!] = (await el.getAttribute('content')) ?? '';
  const twitter: Record<string, string> = {};
  for (const el of await page.locator('meta[name^="twitter:"]').all())
    twitter[(await el.getAttribute('name'))!] = (await el.getAttribute('content')) ?? '';
  const jsonLd: Record<string, unknown>[] = [];
  for (const el of await page.locator('script[type="application/ld+json"]').all())
    jsonLd.push(JSON.parse((await el.textContent()) ?? 'null') as Record<string, unknown>);
  return {
    title: await page.title(),
    description: await attr('meta[name="description"]', 'content'),
    robots: await attr('meta[name="robots"]', 'content'),
    canonical: await attr('link[rel="canonical"]', 'href'),
    og,
    twitter,
    jsonLd,
  };
}

/** The same head fields parsed from raw HTML (what `curl` or a non-rendering crawler gets). */
export function parseHead(html: string) {
  const meta = (key: string) =>
    new RegExp(`<meta (?:name|property)="${key.replace(/[.:]/g, '\\$&')}" content="([^"]*)"`).exec(
      html,
    )?.[1] ?? null;
  return {
    title: /<title>([^<]*)<\/title>/.exec(html)?.[1] ?? null,
    description: meta('description'),
    robots: meta('robots'),
    canonical: /<link rel="canonical" href="([^"]*)"/.exec(html)?.[1] ?? null,
    ogTitle: meta('og:title'),
    ogImage: meta('og:image'),
    jsonLdCount: (html.match(/<script type="application\/ld\+json"/g) ?? []).length,
    h1: /<h1>([^<]*)<\/h1>/.exec(html)?.[1] ?? null,
  };
}

/** Decodes the HTML entities the server writes in text and attributes. */
export function decode(text: string): string {
  return text
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'");
}

/** Every `<loc>` of an XML document. */
export function locs(xml: string): string[] {
  return [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => decode(m[1]));
}

/** The URLs of every page sitemap (index → urlsets, images and videos excluded). */
export async function sitemapUrls(request: APIRequestContext): Promise<string[]> {
  const index = await request.get(`${BASE}/sitemap.xml`);
  expect(index.status()).toBe(200);
  const files = locs(await index.text()).filter((f) => !/\/sitemaps\/(images|videos)(-\d+)?\.xml$/.test(f));
  const urls: string[] = [];
  for (const file of files) {
    const res = await request.get(BASE + new URL(file).pathname);
    expect(res.status(), file).toBe(200);
    urls.push(...locs(await res.text()));
  }
  return urls;
}

/** Validates the schema.org shape of a JSON-LD node (the rules Google's rich results rely on). */
export function expectValidJsonLd(node: Record<string, unknown>, where: string) {
  expect(node['@context'], `${where}: @context`).toBe('https://schema.org');
  // "@type" is one type or a list (e.g. ["LearningResource", "Article"]); a node must satisfy the rules of each type.
  const types = jsonLdTypes(node);
  expect(types.length, `${where}: @type`).toBeGreaterThan(0);
  for (const type of types) {
    expect(typeof type, `${where}: @type`).toBe('string');
    expectValidJsonLdType(node, type, where);
  }
}

/** The types of a JSON-LD node ("@type" as a string or a list of strings). */
export function jsonLdTypes(node: Record<string, unknown>): string[] {
  const t = node['@type'];
  return Array.isArray(t) ? (t as string[]) : t === undefined ? [] : [t as string];
}

function expectValidJsonLdType(node: Record<string, unknown>, type: string, where: string) {
  const has = (key: string) => node[key] !== undefined && node[key] !== null && node[key] !== '';
  const need = (...keys: string[]) => {
    for (const key of keys) expect(has(key), `${where}: ${type} needs ${key}`).toBe(true);
  };
  switch (type) {
    case 'Organization':
      need('name', 'url', 'logo');
      break;
    case 'WebSite':
      need('name', 'url', 'potentialAction');
      break;
    case 'BreadcrumbList': {
      const items = node.itemListElement as { position: number; item: string; name: string }[];
      expect(items.length, `${where}: breadcrumb length`).toBeGreaterThanOrEqual(2);
      items.forEach((item, i) => {
        expect(item.position).toBe(i + 1);
        expect(item.item).toMatch(/^https?:\/\//);
        expect(item.name).toBeTruthy();
      });
      break;
    }
    case 'FAQPage':
      for (const q of node.mainEntity as { name: string; acceptedAnswer: { text: string } }[]) {
        expect(q.name).toBeTruthy();
        expect(q.acceptedAnswer.text).toBeTruthy();
      }
      break;
    case 'Service':
      need('name', 'provider', 'url');
      break;
    case 'Article':
    case 'BlogPosting':
      need('headline', 'datePublished', 'dateModified', 'author', 'publisher');
      break;
    case 'JobPosting':
      need('title', 'description', 'datePosted', 'hiringOrganization');
      break;
    case 'VideoObject':
      need('name', 'description', 'thumbnailUrl', 'uploadDate');
      expect(has('contentUrl') || has('embedUrl'), `${where}: VideoObject needs contentUrl or embedUrl`).toBe(
        true,
      );
      break;
    case 'ItemList':
    case 'OfferCatalog':
      expect((node.itemListElement as unknown[]).length).toBeGreaterThan(0);
      break;
    default:
      need('name');
  }
}
