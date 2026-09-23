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
  type?: 'website' | 'article';
  /** schema.org objects written into `<script type="application/ld+json">` via textContent (never parsed as HTML). */
  jsonLd?: JsonLd[] | null;
}

const MARK = 'data-oa-head';
const DEFAULT_TEMPLATE = '%s | Optimize All';

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
  const title = head.title ? template.replace('%s', head.title) : site?.seo.defaultTitle || siteName;
  const description = head.description ?? site?.seo.defaultDescription ?? null;
  const canonical = absoluteUrl(head.canonical ?? pathname, site?.seo.siteUrl);
  const image = absoluteUrl(head.image ?? site?.seo.defaultOgImageUrl ?? '/og-image.png', site?.seo.siteUrl);
  const jsonLdText = JSON.stringify(head.jsonLd ?? []);
  const twitter = site?.seo.twitterHandle ?? null;

  useEffect(() => {
    document.title = title;
    setMeta('name', 'description', description);
    setMeta('name', 'robots', head.noIndex ? 'noindex, nofollow' : null);
    setMeta('property', 'og:title', title);
    setMeta('property', 'og:description', description);
    setMeta('property', 'og:type', head.type ?? 'website');
    setMeta('property', 'og:url', canonical);
    setMeta('property', 'og:image', image);
    setMeta('property', 'og:site_name', siteName);
    setMeta('name', 'twitter:card', 'summary_large_image');
    setMeta('name', 'twitter:title', title);
    setMeta('name', 'twitter:description', description);
    setMeta('name', 'twitter:image', image);
    setMeta('name', 'twitter:site', twitter);
    setCanonical(canonical);

    const scripts = (JSON.parse(jsonLdText) as JsonLd[]).map((item) => {
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
  }, [title, description, canonical, image, siteName, twitter, head.noIndex, head.type, jsonLdText]);

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
