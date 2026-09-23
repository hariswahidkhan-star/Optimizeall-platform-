import { useParams } from 'react-router-dom';
import { NotFound } from '@/features/public/NotFound';
import { isApiError } from '@/lib/api/errors';
import { formatDate } from '@/lib/format/dates';
import { usePage } from '../site/api';
import { Blocks } from '../site/Blocks';
import { PageHero, PublicQueryState } from '../site/components';
import { headFromSeo, useDocumentHead } from '../site/head';

/**
 * Generic CMS page at /:slug (About, How we work, legal pages, and any page staff create). A page without a hero block
 * gets a standard title header; legal pages show their last-updated date.
 */
export function CmsPage({ slug: fixedSlug }: { slug?: string }) {
  const params = useParams();
  const slug = fixedSlug ?? params.slug ?? '';
  const { data: page, isLoading, error } = usePage(slug);
  useDocumentHead(page ? headFromSeo(page.seo, page.jsonLd) : { title: null });

  if (isApiError(error) && error.status === 404) return <NotFound />;

  const startsWithHero = page?.blocks[0]?.type === 'hero';
  return (
    <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Page not found">
      {page && (
        <article className={page.kind === 'Legal' ? 'site-legal' : undefined}>
          {!startsWithHero && (
            <PageHero
              eyebrow={page.kind === 'Legal' ? 'Legal' : undefined}
              title={page.title}
              lead={page.summary}
              breadcrumbs={[{ label: 'Home', to: '/' }, { label: page.title }]}
            />
          )}
          <Blocks
            blocks={page.blocks}
            context={{
              testimonials: page.testimonials,
              caseStudies: page.caseStudies,
              serviceCategories: page.serviceCategories,
              trustLogos: page.trustLogos,
              pageTitle: page.title,
            }}
          />
          {page.kind === 'Legal' && (
            <p className="container site-narrow text-small text-muted site-legal__updated">Last updated {formatDate(page.updatedAt)}</p>
          )}
        </article>
      )}
    </PublicQueryState>
  );
}
