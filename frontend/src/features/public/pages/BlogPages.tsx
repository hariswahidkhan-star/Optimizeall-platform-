import { Rss } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { EmptyState, Input, Pagination } from '@/components/ui';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { initials } from '@/lib/format/text';
import { isExternalHref } from '@/lib/safeHref';
import { useBlog, usePost, useSite } from '../site/api';
import { useSiteCopy } from '../site/copy';
import { CtaBand, formatPublished, PageHero, PostCard, PublicQueryState, Section } from '../site/components';
import { absoluteUrl, headFromSeo, useDocumentHead } from '../site/head';
import { extractHeadings, Markdown } from '../site/Markdown';
import { NewsletterSignup } from '../site/NewsletterSignup';

/** /blog — categories, tags, search and pagination, all kept in the URL. */
export function BlogPage() {
  const [params, setParams] = useSearchParams();
  const page = Math.max(1, Number(params.get('page')) || 1);
  const category = params.get('category') ?? undefined;
  const tag = params.get('tag') ?? undefined;
  const [search, setSearch] = useState(params.get('q') ?? '');
  const debounced = useDebouncedValue(search, 300);
  const { data, isLoading, error } = useBlog({ page, category, tag, search: debounced || undefined });
  const copy = useSiteCopy();
  // Same rules as the server-rendered page (SeoPageResolver.BlogAsync): each archive page and each category is its own
  // canonical URL; tag filters and searches are noindex (links still followed) and point at /blog.
  const categoryInfo = category ? data?.categories.find((c) => c.slug === category) : undefined;
  const paged = page > 1 ? `page=${page}` : '';
  useDocumentHead(
    tag || debounced
      ? { title: copy.text('blog.seo.title'), description: copy.text('blog.seo.description'), canonical: '/blog', noIndex: true, follow: true }
      : categoryInfo
        ? {
            title: `${categoryInfo.name} articles`,
            description: categoryInfo.description || copy.text('blog.seo.description'),
            canonical: `/blog?category=${encodeURIComponent(categoryInfo.slug)}${paged ? `&${paged}` : ''}`,
          }
        : {
            title: page > 1 ? `${copy.text('blog.seo.title')} — page ${page}` : copy.text('blog.seo.title'),
            description: copy.text('blog.seo.description'),
            canonical: paged ? `/blog?${paged}` : '/blog',
          },
  );

  const update = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    if (!('page' in changes)) next.delete('page');
    setParams(next);
  };

  return (
    <>
      <PageHero
        eyebrow={copy.text('blog.hero.eyebrow')}
        title={copy.text('blog.hero.title')}
        lead={copy.text('blog.hero.lead')}
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Blog' }]}
        actions={
          <a className="site-chip" href="/api/v1/public/blog/rss.xml">
            <Rss aria-hidden="true" width={16} height={16} /> RSS feed
          </a>
        }
      />
      <div className="site-section site-section--tight">
        <div className="container" style={{ display: 'grid', gap: 'var(--space-4)' }}>
          <form role="search" aria-label="Search the blog" onSubmit={(e) => e.preventDefault()}>
            <label className="visually-hidden" htmlFor="blog-search">
              Search articles
            </label>
            <Input
              id="blog-search"
              type="search"
              placeholder="Search articles"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                update({ q: e.target.value || undefined });
              }}
            />
          </form>
          {data && data.categories.length > 0 && (
            <nav aria-label="Blog categories">
              <ul className="site-chips">
                <li>
                  <button type="button" className="site-chip" aria-pressed={!category} onClick={() => update({ category: undefined })}>
                    All topics
                  </button>
                </li>
                {data.categories.map((c) => (
                  <li key={c.slug}>
                    <button type="button" className="site-chip" aria-pressed={category === c.slug} onClick={() => update({ category: c.slug })}>
                      {c.name} ({c.postCount})
                    </button>
                  </li>
                ))}
              </ul>
            </nav>
          )}
          {tag && (
            <p className="text-small">
              Showing posts tagged <strong>{tag}</strong>.{' '}
              <button type="button" className="site-linkbutton" onClick={() => update({ tag: undefined })}>
                Clear tag
              </button>
            </p>
          )}
        </div>
      </div>
      <PublicQueryState error={error} isLoading={isLoading && !data} notFoundTitle="Blog unavailable">
        <div className="site-section">
          <div className="container">
            {data && data.items.length === 0 ? (
              <EmptyState title={copy.text('blog.empty.title')} headingLevel={2} description={copy.text('blog.empty.description')} />
            ) : (
              <ul className="site-grid site-grid--3">
                {(data?.items ?? []).map((p) => (
                  <li key={p.slug}>
                    <PostCard post={p} headingLevel={2} />
                  </li>
                ))}
              </ul>
            )}
            {data && data.total > data.pageSize && (
              <Pagination
                page={data.page}
                pageSize={data.pageSize}
                total={data.total}
                onPageChange={(p) => update({ page: String(p) })}
                label="Blog pages"
              />
            )}
            {data && data.tags.length > 0 && (
              <nav aria-label="Popular tags" className="site-section--tight">
                <h2 className="public-footer__heading">Popular tags</h2>
                <ul className="site-chips">
                  {data.tags.map((t) => (
                    <li key={t.tag}>
                      <button type="button" className="site-chip" aria-pressed={tag === t.tag} onClick={() => update({ tag: t.tag })}>
                        #{t.tag}
                      </button>
                    </li>
                  ))}
                </ul>
              </nav>
            )}
          </div>
        </div>
      </PublicQueryState>
      <Section title={copy.text('blog.newsletter.title')} tone="muted">
        <div className="site-narrow">
          <NewsletterSignup source="blog" />
        </div>
      </Section>
    </>
  );
}

/** /blog/:slug — article with table of contents, share links, author box and related posts. */
export function BlogPostPage() {
  const { slug = '' } = useParams();
  const { data: post, isLoading, error } = usePost(slug);
  const { data: site } = useSite();
  const copy = useSiteCopy();
  useDocumentHead(post ? headFromSeo(post.seo, post.jsonLd, 'article') : { title: 'Blog' });
  const headings = useMemo(() => (post ? extractHeadings(post.bodyMarkdown) : []), [post]);
  const url = post ? absoluteUrl(`/blog/${post.slug}`, site?.seo.siteUrl) ?? '' : '';
  const share = post
    ? [
        { label: 'LinkedIn', href: `https://www.linkedin.com/sharing/share-offsite/?url=${encodeURIComponent(url)}` },
        { label: 'X', href: `https://twitter.com/intent/tweet?url=${encodeURIComponent(url)}&text=${encodeURIComponent(post.title)}` },
        { label: 'Facebook', href: `https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(url)}` },
        { label: 'Email', href: `mailto:?subject=${encodeURIComponent(post.title)}&body=${encodeURIComponent(url)}` },
      ]
    : [];

  return (
    <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="We couldn't find that article">
      {post && (
        <>
          <PageHero
            eyebrow={post.categories.map((c) => c.name).join(' · ') || 'Blog'}
            title={post.title}
            lead={post.excerpt}
            breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Blog', to: '/blog' }, { label: post.title }]}
            actions={
              <p className="text-small text-muted">
                {post.author && <>By {post.author.name} · </>}
                {post.publishedAt && <time dateTime={post.publishedAt}>{formatPublished(post.publishedAt)}</time>} · {post.readingMinutes} min read
              </p>
            }
          >
            {post.coverImageUrl && (
              <img className="site-hero__image" src={post.coverImageUrl} alt={post.coverImageAlt ?? ''} width={640} height={480} decoding="async" />
            )}
          </PageHero>
          <div className="container site-article">
            <article aria-label={post.title}>
              <Markdown source={post.bodyMarkdown} />
              {post.tags.length > 0 && (
                <ul className="site-chips" aria-label="Tags" style={{ marginTop: 'var(--space-8)' }}>
                  {post.tags.map((t) => (
                    <li key={t}>
                      <Link className="site-chip" to={`/blog?tag=${encodeURIComponent(t)}`}>
                        #{t}
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
              {post.author && (
                <aside className="site-author" aria-label="About the author" style={{ marginTop: 'var(--space-8)' }}>
                  {post.author.photoUrl ? (
                    <img src={post.author.photoUrl} alt="" width={64} height={64} loading="lazy" />
                  ) : (
                    <span className="site-team__placeholder" aria-hidden="true" style={{ width: 64, fontSize: '1.25rem' }}>
                      {initials(post.author.name)}
                    </span>
                  )}
                  <div>
                    <p>
                      <strong>{post.author.name}</strong> — {post.author.role}
                    </p>
                    {post.author.bio && <p className="text-small text-muted">{post.author.bio}</p>}
                    {post.author.socialLinks.filter((l) => isExternalHref(l.url)).map((l) => (
                      <a key={l.url} href={l.url} className="text-small" target="_blank" rel="noopener noreferrer">
                        {l.label}
                        <span className="visually-hidden"> (opens in a new tab)</span>
                      </a>
                    ))}
                  </div>
                </aside>
              )}
            </article>
            <aside className="site-toc" aria-label="Article tools">
              {headings.length > 1 && (
                <nav aria-labelledby="toc-title">
                  <h2 id="toc-title" className="public-footer__heading">
                    On this page
                  </h2>
                  <ol>
                    {headings.map((h) => (
                      <li key={h.id} className={h.level > 2 ? 'is-sub' : undefined}>
                        <a href={`#${h.id}`}>{h.text}</a>
                      </li>
                    ))}
                  </ol>
                </nav>
              )}
              <div>
                <h2 className="public-footer__heading">Share</h2>
                <ul className="site-share">
                  {share.map((s) => (
                    <li key={s.label}>
                      <a className="site-chip" href={s.href} target={s.label === 'Email' ? undefined : '_blank'} rel="noopener noreferrer">
                        {s.label}
                        {s.label !== 'Email' && <span className="visually-hidden"> (opens in a new tab)</span>}
                      </a>
                    </li>
                  ))}
                </ul>
              </div>
            </aside>
          </div>
          {post.related.length > 0 && (
            <Section title={copy.text('blog.detail.relatedTitle')} tone="muted">
              <ul className="site-grid site-grid--3">
                {post.related.map((r) => (
                  <li key={r.slug}>
                    <PostCard post={r} />
                  </li>
                ))}
              </ul>
            </Section>
          )}
          <CtaBand />
        </>
      )}
    </PublicQueryState>
  );
}
