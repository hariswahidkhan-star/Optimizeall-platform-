import { ArrowRight, BadgeCheck, CheckCircle2, Wallet } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ButtonLink, Skeleton } from '@/components/ui';
import { useHome } from '../site/api';
import { useSiteCopy } from '../site/copy';
import { LogoCloud, StatsGrid } from '../site/Blocks';
import { CaseStudyCard, CtaBand, PackageCard, PostCard, Section, TestimonialCarousel } from '../site/components';
import { headFromSeo, useDocumentHead } from '../site/head';
import { SiteIcon } from '../site/icons';
import { NewsletterSignup } from '../site/NewsletterSignup';

/** Agency homepage: value proposition, proof, services, results, process, industries, pricing, blog and creators. */
export function HomePage() {
  const { data, isLoading } = useHome();
  const copy = useSiteCopy();
  useDocumentHead(
    data ? headFromSeo({ ...data.seo, title: '' }, data.jsonLd) : { title: null, description: copy.text('home.seo.description') },
  );

  return (
    <>
      <header className="site-hero site-home-hero">
        <div className="container site-hero__inner">
          <div className="site-hero__copy">
            <p className="site-hero__eyebrow">{copy.text('home.hero.eyebrow')}</p>
            <h1 className="site-hero__title">
              {copy.text('home.hero.title')} <span className="site-hero__highlight">{copy.text('home.hero.titleHighlight')}</span>
            </h1>
            <p className="site-hero__lead">{copy.text('home.hero.lead')}</p>
            <div className="site-hero__actions">
              <ButtonLink to="/free-audit" variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
                {copy.text('home.hero.primaryCta')}
              </ButtonLink>
              <ButtonLink to="/book-a-consultation" variant="secondary" size="lg">
                {copy.text('home.hero.secondaryCta')}
              </ButtonLink>
            </div>
            <ul className="site-hero__proof">
              {copy.list('home.hero.proof').map((item) => (
                <li key={item}>
                  <CheckCircle2 aria-hidden="true" /> {item}
                </li>
              ))}
            </ul>
          </div>
          <div className="site-hero__aside">
            <div className="site-hero__panel">
              <p className="eyebrow">{copy.text('home.audit.title')}</p>
              <ul className="site-checklist">
                {copy.list('home.audit.items').map((item) => (
                  <li key={item}>
                    <BadgeCheck aria-hidden="true" />
                    {item}
                  </li>
                ))}
              </ul>
              <ButtonLink to="/free-audit" variant="primary" fullWidth>
                {copy.text('home.audit.cta')}
              </ButtonLink>
            </div>
          </div>
        </div>
      </header>

      {data && data.trustLogos.length > 0 && (
        <div className="site-section site-section--tight">
          <div className="container">
            <LogoCloud logos={data.trustLogos} title={copy.text('home.logos.title')} />
          </div>
        </div>
      )}

      <Section
        id="services"
        eyebrow={copy.text('home.services.eyebrow')}
        title={copy.text('home.services.title')}
        intro={copy.text('home.services.intro')}
        actions={
          <ButtonLink to="/services" variant="secondary">
            {copy.text('home.services.cta')}
          </ButtonLink>
        }
      >
        {isLoading ? (
          <div className="site-grid site-grid--3">
            {Array.from({ length: 6 }, (_, i) => (
              <Skeleton key={i} height={180} />
            ))}
          </div>
        ) : (
          <ul className="site-grid site-grid--3">
            {(data?.serviceCategories ?? []).map((group) => (
              <li key={group.slug}>
                <article className="site-card">
                  <span className="site-card__icon">
                    <SiteIcon name={group.icon} />
                  </span>
                  <h3 className="site-card__title">{group.name}</h3>
                  {group.description && <p className="site-card__text">{group.description}</p>}
                  <ul className="site-chips">
                    {group.services.slice(0, 5).map((s) => (
                      <li key={s.slug}>
                        <Link to={`/services/${s.slug}`} className="site-chip">
                          {s.name}
                        </Link>
                      </li>
                    ))}
                  </ul>
                </article>
              </li>
            ))}
          </ul>
        )}
      </Section>

      {data && data.stats.length > 0 && (
        <Section eyebrow={copy.text('home.results.eyebrow')} title={copy.text('home.results.title')} tone="muted" intro={copy.text('home.results.intro')}>
          <StatsGrid items={data.stats} />
        </Section>
      )}

      {data && data.featuredCaseStudies.length > 0 && (
        <Section
          eyebrow={copy.text('home.caseStudies.eyebrow')}
          title={copy.text('home.caseStudies.title')}
          actions={
            <ButtonLink to="/case-studies" variant="secondary">
              {copy.text('home.caseStudies.cta')}
            </ButtonLink>
          }
        >
          <ul className="site-grid site-grid--3">
            {data.featuredCaseStudies.map((c) => (
              <li key={c.slug}>
                <CaseStudyCard study={c} />
              </li>
            ))}
          </ul>
        </Section>
      )}

      <Section
        id="how-we-work"
        eyebrow={copy.text('home.process.eyebrow')}
        title={copy.text('home.process.title')}
        tone="muted"
        actions={
          <ButtonLink to="/how-we-work" variant="secondary">
            {copy.text('home.process.cta')}
          </ButtonLink>
        }
      >
        <ol className="site-steps">
          {copy.pairs('home.process.steps').map((step) => (
            <li key={step.title}>
              <h3>{step.title}</h3>
              <p>{step.text}</p>
            </li>
          ))}
        </ol>
      </Section>

      {data && data.industries.length > 0 && (
        <Section
          eyebrow={copy.text('home.industries.eyebrow')}
          title={copy.text('home.industries.title')}
          actions={
            <ButtonLink to="/industries" variant="secondary">
              {copy.text('home.industries.cta')}
            </ButtonLink>
          }
        >
          <ul className="site-grid site-grid--3">
            {data.industries.slice(0, 6).map((industry) => (
              <li key={industry.slug}>
                <article className="site-card">
                  <span className="site-card__icon">
                    <SiteIcon name={industry.icon} />
                  </span>
                  <h3 className="site-card__title">
                    <Link to={`/industries/${industry.slug}`} className="site-card__link">
                      {industry.name}
                    </Link>
                  </h3>
                  <p className="site-card__text">{industry.summary}</p>
                </article>
              </li>
            ))}
          </ul>
        </Section>
      )}

      {data && data.testimonials.length > 0 && (
        <Section eyebrow={copy.text('home.testimonials.eyebrow')} title={copy.text('home.testimonials.title')} tone="muted">
          <TestimonialCarousel items={data.testimonials} />
        </Section>
      )}

      {data && data.pricingTeaser.length > 0 && (
        <Section
          eyebrow={copy.text('home.pricing.eyebrow')}
          title={copy.text('home.pricing.title')}
          intro={copy.text('home.pricing.intro')}
          actions={
            <ButtonLink to="/pricing" variant="secondary">
              {copy.text('home.pricing.cta')}
            </ButtonLink>
          }
        >
          <div className="site-packages">
            {data.pricingTeaser.map((t) => (
              <PackageCard key={t.package.id} pkg={t.package} serviceName={t.serviceName} serviceSlug={t.serviceSlug} />
            ))}
          </div>
        </Section>
      )}

      {data && data.latestPosts.length > 0 && (
        <Section
          eyebrow={copy.text('home.blog.eyebrow')}
          title={copy.text('home.blog.title')}
          actions={
            <ButtonLink to="/blog" variant="secondary">
              {copy.text('home.blog.cta')}
            </ButtonLink>
          }
        >
          <ul className="site-grid site-grid--3">
            {data.latestPosts.map((p) => (
              <li key={p.slug}>
                <PostCard post={p} />
              </li>
            ))}
          </ul>
        </Section>
      )}

      <Section eyebrow={copy.text('home.creators.eyebrow')} title={copy.text('home.creators.title')} tone="brand" intro={copy.text('home.creators.intro')}>
        <div className="site-hero__actions">
          <ButtonLink to="/register" variant="highlight" size="lg" leadingIcon={<Wallet />}>
            {copy.text('home.creators.primaryCta')}
          </ButtonLink>
          <ButtonLink to="/creators" variant="secondary" size="lg">
            {copy.text('home.creators.secondaryCta')}
          </ButtonLink>
        </div>
      </Section>

      <CtaBand />

      <Section title={copy.text('home.newsletter.title')} intro={copy.text('home.newsletter.intro')}>
        <div className="site-narrow">
          <NewsletterSignup source="home" />
        </div>
      </Section>
    </>
  );
}
