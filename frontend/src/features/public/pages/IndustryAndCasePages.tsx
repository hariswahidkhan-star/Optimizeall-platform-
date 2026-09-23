import { Quote } from 'lucide-react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { EmptyState, FormField, Select } from '@/components/ui';
import { useCaseStudies, useCaseStudy, useIndustries, useIndustry, useServices } from '../site/api';
import { CaseStudyCard, CtaBand, formatPublished, MetricValue, PageHero, PublicQueryState, Section, ServiceCard } from '../site/components';
import { headFromSeo, useDocumentHead } from '../site/head';
import { SiteIcon } from '../site/icons';
import { Markdown } from '../site/Markdown';

/** /industries */
export function IndustriesPage() {
  const { data, isLoading, error } = useIndustries();
  useDocumentHead({
    title: 'Industries',
    description: 'Digital marketing expertise for e-commerce, SaaS, real estate, healthcare, education, hospitality, finance and local businesses.',
  });
  return (
    <>
      <PageHero
        eyebrow="Industries"
        title="Marketing that speaks your industry's language"
        lead="Every market has its own buyers, rules and seasons. Our teams bring sector experience — and the playbooks that go with it."
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Industries' }]}
      />
      <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Industries unavailable">
        <div className="site-section">
          <div className="container">
            <ul className="site-grid site-grid--3">
              {(data ?? []).map((i) => (
                <li key={i.slug}>
                  <article className="site-card">
                    <span className="site-card__icon">
                      <SiteIcon name={i.icon} />
                    </span>
                    <h2 className="site-card__title">
                      <Link to={`/industries/${i.slug}`} className="site-card__link">
                        {i.name}
                      </Link>
                    </h2>
                    <p className="site-card__text">{i.summary}</p>
                  </article>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </PublicQueryState>
      <CtaBand />
    </>
  );
}

/** /industries/:slug */
export function IndustryDetailPage() {
  const { slug = '' } = useParams();
  const { data: i, isLoading, error } = useIndustry(slug);
  useDocumentHead(i ? headFromSeo(i.seo, i.jsonLd) : { title: 'Industry' });
  return (
    <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="We couldn't find that industry">
      {i && (
        <>
          <PageHero
            eyebrow="Industries"
            title={`Digital marketing for ${i.name}`}
            lead={i.summary}
            breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Industries', to: '/industries' }, { label: i.name }]}
          >
            {i.heroImageUrl && <img className="site-hero__image" src={i.heroImageUrl} alt="" width={640} height={480} decoding="async" />}
          </PageHero>
          {i.bodyMarkdown && (
            <div className="site-section">
              <div className="container site-narrow">
                <Markdown source={i.bodyMarkdown} />
              </div>
            </div>
          )}
          {i.challenges.length > 0 && (
            <Section title="Challenges we help with" tone="muted">
              <ul className="site-grid site-grid--2">
                {i.challenges.map((c) => (
                  <li key={c} className="site-card">
                    <p>{c}</p>
                  </li>
                ))}
              </ul>
            </Section>
          )}
          {i.services.length > 0 && (
            <Section title="Recommended services">
              <ul className="site-grid site-grid--3">
                {i.services.map((s) => (
                  <li key={s.slug}>
                    <ServiceCard service={s} />
                  </li>
                ))}
              </ul>
            </Section>
          )}
          {i.caseStudies.length > 0 && (
            <Section title={`${i.name} case studies`} tone="muted">
              <ul className="site-grid site-grid--3">
                {i.caseStudies.map((c) => (
                  <li key={c.slug}>
                    <CaseStudyCard study={c} />
                  </li>
                ))}
              </ul>
            </Section>
          )}
          <CtaBand title={`Growing a ${i.name.toLowerCase()} business?`} />
        </>
      )}
    </PublicQueryState>
  );
}

/** /case-studies — filter by service and industry (kept in the URL). */
export function CaseStudiesPage() {
  const [params, setParams] = useSearchParams();
  const service = params.get('service') ?? '';
  const industry = params.get('industry') ?? '';
  const { data, isLoading, error } = useCaseStudies({ service: service || undefined, industry: industry || undefined });
  const services = useServices();
  const industries = useIndustries();
  useDocumentHead({ title: 'Case studies', description: 'How we have helped brands grow: strategy, execution and results, clearly labelled measured or estimated.' });

  const update = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  };

  return (
    <>
      <PageHero
        eyebrow="Case studies"
        title="Proof, not promises"
        lead="The challenge, what we did and what changed. Every figure says whether it was measured or estimated."
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Case studies' }]}
      />
      <div className="site-section site-section--tight">
        <div className="container site-form__row" role="search" aria-label="Filter case studies">
          <FormField label="Service">
            <Select
              value={service}
              onChange={(e) => update('service', e.target.value)}
              placeholder="All services"
              options={(services.data ?? []).map((g) => ({ label: g.name, options: g.services.map((s) => ({ value: s.slug, label: s.name })) }))}
            />
          </FormField>
          <FormField label="Industry">
            <Select
              value={industry}
              onChange={(e) => update('industry', e.target.value)}
              placeholder="All industries"
              options={(industries.data ?? []).map((i) => ({ value: i.slug, label: i.name }))}
            />
          </FormField>
        </div>
      </div>
      <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Case studies unavailable">
        <div className="site-section">
          <div className="container">
            {data && data.length === 0 ? (
              <EmptyState title="No case studies match these filters yet" headingLevel={2} description="Try another service or industry." />
            ) : (
              <ul className="site-grid site-grid--3" aria-label="Case studies">
                {(data ?? []).map((c) => (
                  <li key={c.slug}>
                    <CaseStudyCard study={c} headingLevel={3} />
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      </PublicQueryState>
      <CtaBand />
    </>
  );
}

/** /case-studies/:slug — challenge, strategy, execution and results metrics (each labelled measured or estimated). */
export function CaseStudyDetailPage() {
  const { slug = '' } = useParams();
  const { data: c, isLoading, error } = useCaseStudy(slug);
  useDocumentHead(c ? headFromSeo(c.seo, c.jsonLd, 'article') : { title: 'Case study' });
  const hasEstimate = c?.metrics.some((m) => m.measurement === 'Estimated');
  return (
    <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="We couldn't find that case study">
      {c && (
        <>
          <PageHero
            eyebrow={[c.clientName, c.industryName].filter(Boolean).join(' · ')}
            title={c.title}
            lead={c.summary}
            breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Case studies', to: '/case-studies' }, { label: c.title }]}
          >
            {c.coverImageUrl && <img className="site-hero__image" src={c.coverImageUrl} alt="" width={640} height={480} decoding="async" />}
          </PageHero>

          {c.metrics.length > 0 && (
            <Section title="Results" tone="muted" intro={hasEstimate ? 'Figures marked “Estimated” are projections or modelled values, not direct measurements.' : undefined}>
              <div className="site-stats">
                {c.metrics.map((m) => (
                  <MetricValue key={m.label} metric={m} />
                ))}
              </div>
            </Section>
          )}

          <div className="site-section">
            <div className="container site-narrow site-prose-stack">
              {c.challengeMarkdown && (
                <section aria-labelledby="cs-challenge">
                  <h2 id="cs-challenge" className="site-section__title">
                    The challenge
                  </h2>
                  <Markdown source={c.challengeMarkdown} minLevel={3} />
                </section>
              )}
              {c.strategyMarkdown && (
                <section aria-labelledby="cs-strategy">
                  <h2 id="cs-strategy" className="site-section__title">
                    Our strategy
                  </h2>
                  <Markdown source={c.strategyMarkdown} minLevel={3} />
                </section>
              )}
              {c.executionMarkdown && (
                <section aria-labelledby="cs-execution">
                  <h2 id="cs-execution" className="site-section__title">
                    Execution
                  </h2>
                  <Markdown source={c.executionMarkdown} minLevel={3} />
                </section>
              )}
              {c.testimonialQuote && (
                <figure className="site-quote">
                  <Quote aria-hidden="true" />
                  <blockquote>
                    <p>{c.testimonialQuote}</p>
                  </blockquote>
                  {c.testimonialAuthor && (
                    <figcaption>
                      <strong>{c.testimonialAuthor}</strong>
                      {c.testimonialRole && <>, {c.testimonialRole}</>}
                    </figcaption>
                  )}
                </figure>
              )}
              {c.publishedAt && <p className="text-small text-muted">Published {formatPublished(c.publishedAt)}</p>}
            </div>
          </div>

          {c.galleryImageUrls.length > 0 && (
            <Section title="Gallery" tone="muted">
              <div className="site-gallery">
                {c.galleryImageUrls.map((url, index) => (
                  <img key={url} src={url} alt={`${c.title} — gallery ${index + 1} of ${c.galleryImageUrls.length}`} loading="lazy" decoding="async" width={440} height={330} />
                ))}
              </div>
            </Section>
          )}

          {c.services.length > 0 && (
            <Section title="Services used">
              <ul className="site-grid site-grid--3">
                {c.services.map((s) => (
                  <li key={s.slug}>
                    <ServiceCard service={s} />
                  </li>
                ))}
              </ul>
            </Section>
          )}

          {c.related.length > 0 && (
            <Section title="More case studies" tone="muted">
              <ul className="site-grid site-grid--3">
                {c.related.map((r) => (
                  <li key={r.slug}>
                    <CaseStudyCard study={r} />
                  </li>
                ))}
              </ul>
            </Section>
          )}
          <CtaBand title="Want results like these?" />
        </>
      )}
    </PublicQueryState>
  );
}
