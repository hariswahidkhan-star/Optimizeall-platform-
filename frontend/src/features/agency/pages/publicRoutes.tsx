import { useQuery } from '@tanstack/react-query';
import { useEffect, useLayoutEffect, useRef } from 'react';
import { useParams, useSearchParams, type RouteObject } from 'react-router-dom';
import { ErrorState, Skeleton } from '@/components/ui';
import { getVisitorId } from '@/features/public/landing/landingApi';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type { PublicForm, PublicLandingPage } from './api';
import { BlockRenderer } from './BlockRenderer';
import { FormRenderer } from './FormRenderer';
import './pages.css';

/** Sets a `<meta name=…>` (or `<meta property=…>` for Open Graph) tag while the page is mounted. */
function useMeta(name: string, content: string | null | undefined, attr: 'name' | 'property' = 'name') {
  useEffect(() => {
    if (!content) return;
    let tag = document.head.querySelector<HTMLMetaElement>(`meta[${attr}="${name}"]`);
    const created = !tag;
    const previous = tag?.content;
    if (!tag) {
      tag = document.createElement('meta');
      tag.setAttribute(attr, name);
      document.head.appendChild(tag);
    }
    tag.content = content;
    return () => {
      if (created) tag.remove();
      else if (previous !== undefined) tag.content = previous;
    };
  }, [name, content, attr]);
}

/** An upload path (/api/v1/files/…) as an absolute URL: Open Graph images must be absolute for social previews. */
function absolute(url: string | null | undefined): string | null {
  if (!url) return null;
  return /^https?:\/\//i.test(url) ? url : `${window.location.origin}${url.startsWith('/') ? '' : '/'}${url}`;
}

function Unavailable({ what }: { what: string }) {
  return (
    <main className="lp-shell" id="main">
      <div className="lp-block">
        <div className="lp-block__inner lp-text">
          <h1>This {what} isn’t available</h1>
          <p className="lp-muted">The link may be out of date, or the {what} was taken offline.</p>
        </div>
      </div>
    </main>
  );
}

/** A published client landing page (`/lp/:client/:slug`). No app chrome; A/B variant is sticky per visitor. */
export function PublicLandingPageView() {
  const { client = '', slug = '' } = useParams();
  const [search] = useSearchParams();
  const utmSource = search.get('utm_source') ?? undefined;
  const referrer = typeof document !== 'undefined' && document.referrer ? document.referrer : undefined;
  const query = useQuery({
    queryKey: ['public', 'lp', client, slug],
    queryFn: () =>
      api.get<PublicLandingPage>(`/public/lp/${encodeURIComponent(client)}/${encodeURIComponent(slug)}`, {
        query: { utm_source: utmSource, referrer },
        headers: { 'X-Visitor-Id': getVisitorId() },
      }),
    staleTime: Infinity,
    retry: (count, err) => !(isApiError(err) && err.status === 404) && count < 2,
  });
  const data = query.data;
  useEffect(() => {
    if (data) document.title = data.title;
  }, [data]);
  useMeta('description', data?.metaDescription);
  useMeta('robots', data?.noIndex ? 'noindex, nofollow' : null);
  // The builder's SEO settings (title, description, Open Graph image) also drive the social preview tags.
  useMeta('og:title', data?.title, 'property');
  useMeta('og:description', data?.metaDescription, 'property');
  useMeta('og:image', absolute(data?.ogImageUrl), 'property');
  useMeta('og:type', data ? 'website' : null, 'property');
  useMeta('twitter:card', data?.ogImageUrl ? 'summary_large_image' : null);

  if (query.isError) {
    if (isApiError(query.error) && query.error.status === 404) return <Unavailable what="page" />;
    return (
      <main className="lp-shell" id="main">
        <div className="lp-block">
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </div>
      </main>
    );
  }
  if (!data)
    return (
      <main className="lp-shell" id="main" aria-busy="true">
        <div className="lp-block">
          <Skeleton height="24rem" />
        </div>
      </main>
    );

  const forms: Record<string, PublicForm> = Object.fromEntries(data.forms.map((f) => [f.id, f]));
  return (
    <div className="lp-shell">
      <main id="main">
        <BlockRenderer blocks={data.blocks} forms={forms} landingPageId={data.pageId} variantKey={data.variantKey} />
      </main>
      <footer className="lp-shell__footer">© {new Date().getFullYear()} {data.clientName}</footer>
    </div>
  );
}

/** The embedding site's origin: ancestorOrigins (Chromium/WebKit) or the referrer. Null when not framed. */
export function embeddingOrigin(): string | null {
  if (typeof window === 'undefined' || window.parent === window) return null;
  const ancestors = (window.location as Location & { ancestorOrigins?: DOMStringList }).ancestorOrigins;
  if (ancestors && ancestors.length > 0) return ancestors[0];
  try {
    return document.referrer ? new URL(document.referrer).origin : null;
  } catch {
    return null;
  }
}

/** Embeddable form (`/f/:formId`), loaded in an iframe on the client's own site. Reports its height to the parent. */
export function EmbeddedFormPage() {
  const { formId = '' } = useParams();
  const origin = useRef(embeddingOrigin()).current;
  const rootRef = useRef<HTMLDivElement>(null);
  const query = useQuery({
    queryKey: ['public', 'form', formId],
    queryFn: () => api.get<PublicForm>(`/public/forms/${encodeURIComponent(formId)}`, { headers: origin ? { 'X-Embed-Origin': origin } : {} }),
    staleTime: Infinity,
    retry: false,
  });
  useEffect(() => {
    if (query.data) document.title = query.data.name;
  }, [query.data]);

  // Post height changes to the embedding page only (never '*'), so the iframe can be resized without scrollbars.
  useLayoutEffect(() => {
    const el = rootRef.current;
    if (!el || !origin || typeof ResizeObserver === 'undefined') return;
    const post = () => window.parent.postMessage({ type: 'oa-form:height', formId, height: Math.ceil(el.getBoundingClientRect().height) }, origin);
    const observer = new ResizeObserver(post);
    observer.observe(el);
    post();
    return () => observer.disconnect();
  }, [formId, origin, query.data]);

  return (
    <main className="lp-embed" id="main">
      <div ref={rootRef}>
        {query.isError ? (
          isApiError(query.error) && (query.error.status === 404 || query.error.status === 403) ? (
            <p className="lp-muted">This form isn’t available here.</p>
          ) : (
            <ErrorState error={query.error} onRetry={() => void query.refetch()} />
          )
        ) : query.data ? (
          <>
            <h1 className="visually-hidden">{query.data.name}</h1>
            <FormRenderer form={query.data} embedOrigin={origin ?? undefined} />
          </>
        ) : (
          <Skeleton height="16rem" />
        )}
      </div>
    </main>
  );
}

/**
 * Anonymous routes for the root router (mount as siblings of the public/auth layouts, outside any app chrome):
 * `/lp/:client/:slug` landing pages and `/f/:formId` embeddable forms.
 */
export const publicRoutes: RouteObject[] = [
  { path: 'lp/:client/:slug', element: <PublicLandingPageView /> },
  { path: 'f/:formId', element: <EmbeddedFormPage /> },
];
