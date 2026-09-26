import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { useSite, type JsonLd, type PublicSeo } from './api';

export interface DocumentHead {
  /** Page title without the site suffix; the site's title template ("%s | Optimize All") is applied. */
  title?: string | null;
  description?: string | null;
  /** Absolute canonical URL, or an app path resolved against the site URL. */
  canonical?: string | null;
  image?: string | null;
  noIndex?: boolean;
  /** With noIndex: let crawlers follow the page's links (search results, utility pages). Default: nofollow. */
  follow?: boolean;
  type?: 'website' | 'article';
  /** schema.org objects written into `<script type="application/ld+json">` via textContent (never parsed as HTML). */
  jsonLd?: JsonLd[] | null;
}

const MARK = 'data-oa-head';
/** Robots directives of an indexable page (the server renders the same; large image previews and full snippets). */
export const INDEXABLE_ROBOTS = 'index, follow, max-image-preview:large, max-snippet:-1, max-video-preview:-1';
const DEFAULT_TEMPLATE = '%s | Optimize All';
/** Built-in social image (1200×630) used when neither the page nor the site settings choose one. */
export const DEFAULT_OG_IMAGE = '/og-default.png';

/**
 * Server-rendered documents (docs/SEO_CRO.md § Rendering) mark their head tags `data-oa-ssr`. The head manager updates
 * the shared ones (title, description, canonical, Open Graph, Twitter) in place, so no tag is ever duplicated. The
 * server's JSON-LD — which can be richer than a page's own, e.g. ItemList on listing pages — is kept while the visitor
 * is still on the page the server rendered, and replaced by the page's own JSON-LD after the first client navigation.
 */
const SSR = 'data-oa-ssr';
const initialPath = typeof window === 'undefined' ? '' : window.location.pathname;

function removeServerOnlyTags() {
  document.head
    .querySelectorAll(
      `script[${SSR}], link[rel="prev"][${SSR}], link[rel="next"][${SSR}], meta[property^="og:image:"][${SSR}], ` +
        `meta[property^="article:"][${SSR}], meta[name="twitter:image:alt"][${SSR}]`,
    )
    .forEach((el) => el.remove());
}

/** Longest title search results show in full. */
const TITLE_MAX = 60;

/**
 * The page title with the site's title template — unless the title already names the site, or the suffix would push a
 * title that fits on its own past 60 characters (same rule as the server's SeoText.ApplyTemplate).
 */
export function applyTitleTemplate(title: string, template: string, siteName: string): string {
  if (title.toLowerCase().includes(siteName.toLowerCase())) return title;
  const full = template.replace('%s', title);
  return full.length > TITLE_MAX && title.length <= TITLE_MAX ? title : full;
}

function setMeta(attr: 'name' | 'property', key: string, content: string | null | undefined) {
  const selector = `meta[${attr}="${key}"]`;
  let el = document.head.querySelector<HTMLMetaElement>(selector);
  if (!content) {
    if (el?.hasAttribute(MARK)) el.remove();
    return;
  }
  if (!el) {
    el = document.createElement('meta');
    el.setAttribute(attr, key);
    el.setAttribute(MARK, '');
    document.head.appendChild(el);
  }
  el.setAttribute('content', content);
}

function setCanonical(href: string | null) {
  let el = document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]');
  if (!href) {
    el?.remove();
    return;
  }
  if (!el) {
    el = document.createElement('link');
    el.rel = 'canonical';
    el.setAttribute(MARK, '');
    document.head.appendChild(el);
  }
  el.href = href;
}

/** Resolves an app path or upload URL against the site origin (falls back to the current origin). */
export function absoluteUrl(url: string | null | undefined, siteUrl?: string | null): string | null {
  if (!url) return null;
  if (/^https?:\/\//i.test(url)) return url;
  const base = (siteUrl || window.location.origin).replace(/\/+$/, '');
  return url.startsWith('/') ? base + url : `${base}/${url}`;
}

/**
 * Small head manager for the SPA: title, meta description, canonical, Open Graph / Twitter tags, robots and JSON-LD.
 * JSON-LD is written with `textContent`, so API data can never inject markup. Tags it created are removed on unmount.
 */
export function useDocumentHead(head: DocumentHead) {
  const { data: site } = useSite();
  const { pathname } = useLocation();
  const template = site?.seo.titleTemplate || DEFAULT_TEMPLATE;
  const siteName = site?.siteName || 'Optimize All';
  const title = head.title ? applyTitleTemplate(head.title, template, siteName) : site?.seo.defaultTitle || siteName;
  const description = head.description ?? site?.seo.defaultDescription ?? null;
  const canonical = absoluteUrl(head.canonical ?? pathname, site?.seo.siteUrl);
  const pageImage = head.image ?? null;
  const image = absoluteUrl(pageImage ?? site?.seo.defaultOgImageUrl ?? DEFAULT_OG_IMAGE, site?.seo.siteUrl);
  const jsonLdText = JSON.stringify(head.jsonLd ?? []);
  const twitter = site?.seo.twitterHandle ?? null;

  useEffect(() => {
    const onServerRenderedPage = pathname === initialPath && document.head.querySelector(`script[${SSR}]`) !== null;
    if (!onServerRenderedPage) removeServerOnlyTags();
    document.title = title;
    setMeta('name', 'description', description);
    setMeta('name', 'robots', head.noIndex ? (head.follow ? 'noindex, follow' : 'noindex, nofollow') : INDEXABLE_ROBOTS);
    setMeta('property', 'og:title', title);
    setMeta('property', 'og:description', description);
    setMeta('property', 'og:type', head.type ?? 'website');
    setMeta('property', 'og:url', canonical);
    // The server may render a page-specific social card; keep it on that page unless the page chooses its own image.
    const keepServerImage = onServerRenderedPage && !pageImage && document.head.querySelector(`meta[property="og:image"][${SSR}]`) !== null;
    if (!keepServerImage) setMeta('property', 'og:image', image);
    setMeta('property', 'og:site_name', siteName);
    setMeta('name', 'twitter:card', 'summary_large_image');
    setMeta('name', 'twitter:title', title);
    setMeta('name', 'twitter:description', description);
    if (!keepServerImage) setMeta('name', 'twitter:image', image);
    setMeta('name', 'twitter:site', twitter);
    setCanonical(canonical);

    // The server's JSON-LD already describes this page: keep it rather than add a second copy.
    const items = onServerRenderedPage ? [] : (JSON.parse(jsonLdText) as JsonLd[]);
    const scripts = items.map((item) => {
      const script = document.createElement('script');
      script.type = 'application/ld+json';
      script.setAttribute(MARK, '');
      script.textContent = JSON.stringify(item);
      document.head.appendChild(script);
      return script;
    });
    return () => {
      for (const script of scripts) script.remove();
    };
  }, [title, description, canonical, image, siteName, twitter, pageImage, head.noIndex, head.follow, head.type, jsonLdText, pathname]);

  useEffect(
    () => () => {
      setCanonical(null);
      setMeta('name', 'robots', null);
    },
    [],
  );
}

/** Maps the API's resolved SEO block onto the head manager. */
export function headFromSeo(seo: PublicSeo | undefined, jsonLd?: JsonLd[], type?: 'website' | 'article'): DocumentHead {
  if (!seo) return {};
  return {
    title: seo.title,
    description: seo.description,
    canonical: seo.canonicalUrl,
    image: seo.ogImageUrl,
    noIndex: seo.noIndex,
    jsonLd,
    type,
  };
}
