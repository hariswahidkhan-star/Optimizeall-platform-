import { ArrowRight, CheckCircle2 } from 'lucide-react';
import { useParams, useSearchParams } from 'react-router-dom';
import { ButtonLink, Skeleton } from '@/components/ui';
import { isExternalHref, isInternalHref } from '@/lib/safeHref';
import { useService, useServices } from '../site/api';
import { FaqList } from '../site/Blocks';
import { useSiteCopy } from '../site/copy';
import { CaseStudyCard, CtaBand, PackageCard, PageHero, PublicQueryState, Section, ServiceCard, TestimonialCarousel } from '../site/components';
import { headFromSeo, useDocumentHead } from '../site/head';
import { RedirectIfMoved } from '../site/redirects';
import { SiteIcon } from '../site/icons';
import { Markdown } from '../site/Markdown';

/** /services — every service line, filterable by category. */
export function ServicesPage() {
  const { data, isLoading, error } = useServices();
  const [params, setParams] = useSearchParams();
  const active = params.get('category');
  const copy = useSiteCopy();
  useDocumentHead({ title: copy.text('services.seo.title'), description: copy.text('services.seo.description') });
  const groups = (data ?? []).filter((g) => !active || g.slug === active);
  // A service line that was renamed: follow its redirect to the new filter.
  const unknownCategory = !!active && !!data && !data.some((g) => g.slug === active);

  return (
    <>
      <RedirectIfMoved when={unknownCategory} />
      <PageHero
        eyebrow={copy.text('services.hero.eyebrow')}
        title={copy.text('services.hero.title')}
        lead={copy.text('services.hero.lead')}
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Services' }]}
      />
      <div className="site-section site-section--tight">
        <div className="container">
          <div role="group" aria-label="Filter by category" className="site-chips">
            <button type="button" className="site-chip" aria-pressed={!active} onClick={() => setParams({})}>
              All
            </button>
            {(data ?? []).map((g) => (
              <button key={g.slug} type="button" className="site-chip" aria-pressed={active === g.slug} onClick={() => setParams({ category: g.slug })}>
                {g.name}
              </button>
            ))}
          </div>
        </div>
      </div>
      <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Services unavailable">
        {groups.map((group) => (
          <Section key={group.slug} title={group.name} intro={group.description}>
            <ul className="site-grid site-grid--3">
              {group.services.map((s) => (
                <li key={s.slug}>
                  <ServiceCard service={s} />
                </li>
              ))}
            </ul>
          </Section>
        ))}
      </PublicQueryState>
      <CtaBand title={copy.text('services.cta.title')} text={copy.text('services.cta.text')} />
    </>
  );
}

function ServiceCta({ label, url }: { label: string | null; url: string | null }) {
  if (!label || !url) return null;
  if (isInternalHref(url))
    return (
      <ButtonLink to={url} variant="secondary" size="lg">
        {label}
      </ButtonLink>
    );
  if (isExternalHref(url))
    return (
      <a className="ui-button ui-button--secondary ui-button--lg" href={url} target="_blank" rel="noopener noreferrer">
        {label}
      </a>
    );
  return null;
}

/** /services/:slug — hero, problems, deliverables, process, packages, results, FAQ (FAQPage JSON-LD) and CTA. */
export function ServiceDetailPage() {
  const { slug = '' } = useParams();
  const { data: s, isLoading, error } = useService(slug);
  const copy = useSiteCopy();
  useDocumentHead(s ? headFromSeo(s.seo, s.jsonLd) : { title: 'Service' });

  return (
    <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="We couldn't find that service">
      {s && (
        <>
          <PageHero
            eyebrow={
              <>
                <SiteIcon name={s.icon} className="site-mega__icon" /> {s.categoryName}
              </>
            }
            title={s.name}
            lead={s.heroBody ?? s.tagline}
            breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Services', to: '/services' }, { label: s.name }]}
            actions={
              <>
                <ButtonLink to={`/get-a-quote?service=${encodeURIComponent(s.slug)}`} variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
                  {copy.text('services.detail.quoteCta')}
                </ButtonLink>
                <ServiceCta label={s.ctaLabel} url={s.ctaUrl} />
                {!s.ctaLabel && (
                  <ButtonLink to="/book-a-consultation" variant="secondary" size="lg">
                    {copy.text('services.detail.callCta')}
                  </ButtonLink>
                )}
              </>
            }
          >
            {s.heroImageUrl ? (
              <img className="site-hero__image" src={s.heroImageUrl} alt="" width={640} height={480} decoding="async" />
            ) : (
              s.kpis.length > 0 && (
                <div className="site-hero__panel">
                  <p className="eyebrow">{copy.text('services.detail.kpisTitle')}</p>
                  <ul className="site-checklist">
                    {s.kpis.map((k) => (
                      <li key={k}>
                        <CheckCircle2 aria-hidden="true" />
                        {k}
                      </li>
                    ))}
                  </ul>
                </div>
              )
            )}
          </PageHero>

          {s.overviewMarkdown && (
            <div className="site-section">
              <div className="container site-narrow">
                <Markdown source={s.overviewMarkdown} />
              </div>
            </div>
          )}

          {s.problemsSolved.length > 0 && (
            <Section title={copy.text('services.detail.problemsTitle')} tone="muted">
              <ul className="site-grid site-grid--2">
                {s.problemsSolved.map((p) => (
                  <li key={p} className="site-card">
                    <p>{p}</p>
                  </li>
                ))}
              </ul>
            </Section>
          )}

          {s.deliverables.length > 0 && (
            <Section title={copy.text('services.detail.includedTitle')}>
              <ul className="site-checklist site-grid site-grid--2">
                {s.deliverables.map((d) => (
                  <li key={d}>
                    <CheckCircle2 aria-hidden="true" />
                    {d}
                  </li>
                ))}
              </ul>
              {s.tools.length > 0 && (
                <>
                  <h3 className="site-subheading">{copy.text('services.detail.toolsTitle')}</h3>
                  <ul className="site-chips">
                    {s.tools.map((t) => (
                      <li key={t} className="site-chip">
                        {t}
                      </li>
                    ))}
                  </ul>
                </>
              )}
            </Section>
          )}

          {s.processSteps.length > 0 && (
            <Section title={copy.text('services.detail.processTitle')} tone="muted">
              <ol className="site-steps">
                {s.processSteps.map((step) => (
                  <li key={step.title}>
                    <h3>{step.title}</h3>
                    <p>{step.description}</p>
                  </li>
                ))}
              </ol>
            </Section>
          )}

          {s.packages.length > 0 && (
            <Section id="pricing" title={copy.text('services.detail.pricingTitle')} intro={copy.text('services.detail.pricingIntro')}>
              <div className="site-packages">
                {s.packages.map((p) => (
                  <PackageCard key={p.id} pkg={p} serviceSlug={s.slug} />
                ))}
              </div>
            </Section>
          )}

          {s.caseStudies.length > 0 && (
            <Section title={copy.text('services.detail.caseStudiesTitle')} tone="muted">
              <ul className="site-grid site-grid--3">
                {s.caseStudies.map((c) => (
                  <li key={c.slug}>
                    <CaseStudyCard study={c} />
                  </li>
                ))}
              </ul>
            </Section>
          )}

          {s.testimonials.length > 0 && (
            <Section title={copy.text('services.detail.testimonialsTitle')}>
              <TestimonialCarousel items={s.testimonials} />
            </Section>
          )}

          {s.faqs.length > 0 && (
            <div className="site-section">
              <div className="container site-narrow">
                <FaqList title={copy.text('services.detail.faqTitle')} items={s.faqs} />
              </div>
            </div>
          )}

          {s.relatedServices.length > 0 && (
            <Section title={copy.text('services.detail.relatedTitle')} tone="muted">
              <ul className="site-grid site-grid--3">
                {s.relatedServices.map((r) => (
                  <li key={r.slug}>
                    <ServiceCard service={r} />
                  </li>
                ))}
              </ul>
            </Section>
          )}

          <CtaBand title={copy.text('services.detail.ctaTitle', { name: s.name })} />
        </>
      )}
    </PublicQueryState>
  );
}

export function ServiceGridSkeleton() {
  return (
    <div className="site-grid site-grid--3">
      {Array.from({ length: 6 }, (_, i) => (
        <Skeleton key={i} height={160} />
      ))}
    </div>
  );
}
