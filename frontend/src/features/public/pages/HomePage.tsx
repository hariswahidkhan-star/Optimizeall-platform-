import { ArrowRight, BadgeCheck, CheckCircle2, Wallet } from 'lucide-react';
import { useRef, type CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import { ButtonLink, Skeleton } from '@/components/ui';
import { useHome } from '../site/api';
import { useAcademyOverview } from '../site/academy';
import { HeroVisual } from '../site/art';
import { useSiteCopy } from '../site/copy';
import { LogoCloud, StatsGrid } from '../site/Blocks';
import { CaseStudyCard, PackageCard, PostCard, Section, TestimonialCarousel } from '../site/components';
import { headFromSeo, useDocumentHead } from '../site/head';
import { SiteIcon } from '../site/icons';
import { trackGlow, useReveal } from '../site/motion';
import { NewsletterSignup } from '../site/NewsletterSignup';
import {
  AcademyStats,
  CertificateStrip,
  DualCta,
  FeaturedCourseGrid,
  SkillsMarquee,
  SubjectTiles,
  SuggestedPaths,
  Timeline,
  TrustGrid,
} from '../site/Showcase';
import { PartnerSlot } from '../partners/PartnerSlot';

/**
 * Home: two pillars, academy first. Hero (learn free with certificates; the agency as the second path) with a CSS/SVG
 * product visual → partners → the academy (live figures, subjects, featured courses, suggested paths, how it works,
 * certificates) → the agency (services bento, free audit, results, process, industries, testimonials, pricing) →
 * trust → blog → creators → closing dual call-to-action → newsletter. Figures come from the APIs, words from page copy.
 */
export function HomePage() {
  const { data, isLoading } = useHome();
  const academy = useAcademyOverview();
  const copy = useSiteCopy();
  const root = useRef<HTMLDivElement>(null);
  useReveal(root);
  useDocumentHead(
    data ? headFromSeo({ ...data.seo, title: '' }, data.jsonLd) : { title: null, description: copy.text('home.seo.description') },
  );
  // The hero's course card shows a real flagship course: a featured AI course when there is one.
  const heroCourse =
    academy.data?.featured.find((c) => c.category === 'Ai') ??
    academy.data?.featured.find((c) => c.category !== 'Platform') ??
    academy.data?.courses[0] ??
    null;
  const categories = data?.serviceCategories ?? [];

  return (
    <div ref={root} className="oa-home">
      <header className="site-hero site-home-hero oa-hero">
        <div className="container site-hero__inner oa-hero__inner">
          <div className="site-hero__copy oa-hero__copy">
            <p className="site-hero__eyebrow">{copy.text('home.hero.eyebrow')}</p>
            <h1 className="site-hero__title">
              {copy.text('home.hero.title')} <span className="site-hero__highlight">{copy.text('home.hero.titleHighlight')}</span>
            </h1>
            <p className="site-hero__lead">{copy.text('home.hero.lead')}</p>
            <div className="site-hero__actions">
              <ButtonLink to="/learn" variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
                {copy.text('home.hero.learnCta')}
              </ButtonLink>
              <ButtonLink to="/free-audit" variant="secondary" size="lg">
                {copy.text('home.hero.primaryCta')}
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
          <div className="site-hero__aside oa-hero__aside">
            <HeroVisual course={heroCourse} />
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

      <PartnerSlot slot="home.partners" />

      {/* ------------------------------------------------------------------ Pillar 1: the academy */}
      <section id="academy" className="site-section oa-academy" aria-labelledby="academy-title">
        <div className="container">
          <div className="site-section__head">
            <div>
              <p className="eyebrow">{copy.text('home.academy.eyebrow')}</p>
              <h2 id="academy-title" className="site-section__title">
                {copy.text('home.academy.title')}
              </h2>
              <p className="site-section__intro">{copy.text('home.academy.intro')}</p>
            </div>
            <div className="site-section__actions">
              <ButtonLink to="/learn" variant="primary" trailingIcon={<ArrowRight />}>
                {copy.text('home.academy.cta')}
              </ButtonLink>
            </div>
          </div>
          <AcademyStats data={academy.data} isLoading={academy.isLoading} />
          {academy.data && (
            <>
              <h3 className="oa-subhead" data-reveal="">
                {copy.text('home.academy.subjectsTitle')}
              </h3>
              <SubjectTiles data={academy.data} headingLevel={4} />
              <h3 className="oa-subhead" data-reveal="">
                {copy.text('home.academy.featuredTitle')}
              </h3>
              <FeaturedCourseGrid data={academy.data} />
            </>
          )}
        </div>
        {academy.data && <SkillsMarquee skills={academy.data.skills} />}
      </section>

      {academy.data && (
        <Section
          id="paths"
          tone="muted"
          eyebrow={copy.text('home.paths.eyebrow')}
          title={copy.text('home.paths.title')}
          intro={copy.text('home.paths.intro')}
        >
          <SuggestedPaths data={academy.data} />
        </Section>
      )}

      <Section eyebrow={copy.text('home.learnSteps.eyebrow')} title={copy.text('home.learnSteps.title')}>
        <Timeline steps={copy.pairs('home.learnSteps.steps')} />
      </Section>

      <CertificateStrip />

      {/* ------------------------------------------------------------------ Pillar 2: the agency */}
      <section id="agency" className="site-section oa-agency" aria-labelledby="agency-title">
        <div className="container oa-agency__inner">
          <div className="oa-agency__copy" data-reveal="">
            <p className="eyebrow">{copy.text('home.agency.eyebrow')}</p>
            <h2 id="agency-title" className="site-section__title">
              {copy.text('home.agency.title')}
            </h2>
            <p className="site-section__intro">{copy.text('home.agency.intro')}</p>
            <ul className="site-hero__proof">
              {copy.list('home.agency.proof').map((item) => (
                <li key={item}>
                  <CheckCircle2 aria-hidden="true" /> {item}
                </li>
              ))}
            </ul>
            <div className="site-hero__actions">
              <ButtonLink to="/free-audit" variant="primary" size="lg" trailingIcon={<ArrowRight />}>
                {copy.text('home.hero.primaryCta')}
              </ButtonLink>
              <ButtonLink to="/book-a-consultation" variant="secondary" size="lg">
                {copy.text('home.hero.secondaryCta')}
              </ButtonLink>
            </div>
          </div>
          <div className="site-hero__panel oa-agency__panel" data-reveal="">
            <p className="eyebrow">{copy.text('home.audit.title')}</p>
            <ul className="site-checklist">
              {copy.list('home.audit.items').map((item) => (
                <li key={item}>
                  <BadgeCheck aria-hidden="true" />
                  {item}
                </li>
              ))}
            </ul>
            <ButtonLink to="/free-audit" variant="secondary" fullWidth>
              {copy.text('home.audit.cta')}
            </ButtonLink>
          </div>
        </div>
      </section>

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
          <div className="oa-bento">
            {Array.from({ length: 6 }, (_, i) => (
              <Skeleton key={i} height={180} />
            ))}
          </div>
        ) : (
          <ul className="oa-bento" data-reveal="stagger" onPointerMove={trackGlow}>
            {categories.map((group, i) => (
              <li key={group.slug} className="oa-bento__item" data-glow="" style={{ '--i': i } as CSSProperties}>
                <article className="oa-bento__card">
                  <span className="oa-tile" aria-hidden="true">
                    <SiteIcon name={group.icon} />
                  </span>
                  <h3 className="oa-bento__title">
                    <Link to={`/services?category=${encodeURIComponent(group.slug)}`} className="site-card__link">
                      {group.name}
                    </Link>
                  </h3>
                  {group.description && <p className="oa-bento__text">{group.description}</p>}
                  <ul className="site-chips">
                    {group.services.slice(0, i < 2 ? 6 : 4).map((s) => (
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
          <ul className="site-grid site-grid--3" data-reveal="stagger">
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
        <Timeline steps={copy.pairs('home.process.steps')} className="oa-timeline--agency" />
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
          <ul className="site-grid site-grid--3" data-reveal="stagger">
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
          <div className="site-packages" data-reveal="stagger">
            {data.pricingTeaser.map((t) => (
              <PackageCard key={t.package.id} pkg={t.package} serviceName={t.serviceName} serviceSlug={t.serviceSlug} />
            ))}
          </div>
        </Section>
      )}

      <TrustGrid />

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
          <ul className="site-grid site-grid--3" data-reveal="stagger">
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

      <DualCta />

      <Section title={copy.text('home.newsletter.title')} intro={copy.text('home.newsletter.intro')}>
        <div className="site-narrow">
          <NewsletterSignup source="home" />
        </div>
      </Section>
    </div>
  );
}
