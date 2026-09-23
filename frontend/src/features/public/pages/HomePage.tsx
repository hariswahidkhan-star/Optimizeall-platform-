import { ArrowRight, BadgeCheck, CheckCircle2, Wallet } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ButtonLink, Skeleton } from '@/components/ui';
import { useHome } from '../site/api';
import { LogoCloud, StatsGrid } from '../site/Blocks';
import { CaseStudyCard, CtaBand, PackageCard, PostCard, Section, TestimonialCarousel } from '../site/components';
import { headFromSeo, useDocumentHead } from '../site/head';
import { SiteIcon } from '../site/icons';
import { NewsletterSignup } from '../site/NewsletterSignup';

const PROCESS = [
  { title: 'Audit', text: 'An honest look at your marketing, tracking and competitors — shared with you whether or not you hire us.' },
  { title: 'Strategy', text: 'A prioritised 90-day plan with targets, budget and owners, before any work begins.' },
  { title: 'Execution', text: 'Specialists deliver the work; you approve drafts and creatives in your client portal.' },
  { title: 'Reporting', text: 'Live dashboards and a monthly review of results, learnings and next steps.' },
];

/** Agency homepage: value proposition, proof, services, results, process, industries, pricing, blog and creators. */
export function HomePage() {
  const { data, isLoading } = useHome();
  useDocumentHead(
    data
      ? headFromSeo({ ...data.seo, title: '' }, data.jsonLd)
      : { title: null, description: 'Full-service digital marketing agency: SEO, social, paid media, content, email and web.' },
  );

  return (
    <>
      <header className="site-hero site-home-hero">
        <div className="container site-hero__inner">
          <div className="site-hero__copy">
            <p className="site-hero__eyebrow">Full-service digital marketing agency</p>
            <h1 className="site-hero__title">
              Marketing that grows revenue — <span className="site-hero__highlight">and proves it</span>
            </h1>
            <p className="site-hero__lead">
              Search, social, paid media, content, email, brand and web from one accountable team. Every result reported in
              plain numbers, clearly labelled measured or estimated.
            </p>
            <div className="site-hero__actions">
              <ButtonLink to="/free-audit" variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
                Get a free audit
              </ButtonLink>
              <ButtonLink to="/book-a-consultation" variant="secondary" size="lg">
                Book a call
              </ButtonLink>
            </div>
            <ul className="site-hero__proof">
              <li>
                <CheckCircle2 aria-hidden="true" /> No long lock-ins
              </li>
              <li>
                <CheckCircle2 aria-hidden="true" /> Your accounts, your data
              </li>
              <li>
                <CheckCircle2 aria-hidden="true" /> Senior strategists on every account
              </li>
            </ul>
          </div>
          <div className="site-hero__aside">
            <div className="site-hero__panel">
              <p className="eyebrow">What you get in your free audit</p>
              <ul className="site-checklist">
                {['Tracking and analytics health check', 'Search visibility vs. your top competitors', 'Paid media waste and quick wins', 'A prioritised 90-day action plan'].map((item) => (
                  <li key={item}>
                    <BadgeCheck aria-hidden="true" />
                    {item}
                  </li>
                ))}
              </ul>
              <ButtonLink to="/free-audit" variant="primary" fullWidth>
                Request my audit
              </ButtonLink>
            </div>
          </div>
        </div>
      </header>

      {data && data.trustLogos.length > 0 && (
        <div className="site-section site-section--tight">
          <div className="container">
            <LogoCloud logos={data.trustLogos} title="Trusted by growing brands" />
          </div>
        </div>
      )}

      <Section id="services" eyebrow="What we do" title="Every channel, one accountable team" intro="Pick a single service or combine them into an integrated growth programme." actions={<ButtonLink to="/services" variant="secondary">All services</ButtonLink>}>
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
        <Section eyebrow="Results" title="Numbers we're proud of" tone="muted" intro="Each figure is labelled: measured from tracked data, or estimated.">
          <StatsGrid items={data.stats} />
        </Section>
      )}

      {data && data.featuredCaseStudies.length > 0 && (
        <Section eyebrow="Case studies" title="Real work, real results" actions={<ButtonLink to="/case-studies" variant="secondary">All case studies</ButtonLink>}>
          <ul className="site-grid site-grid--3">
            {data.featuredCaseStudies.map((c) => (
              <li key={c.slug}>
                <CaseStudyCard study={c} />
              </li>
            ))}
          </ul>
        </Section>
      )}

      <Section id="how-we-work" eyebrow="How we work" title="A process built for accountability" tone="muted" actions={<ButtonLink to="/how-we-work" variant="secondary">Our process</ButtonLink>}>
        <ol className="site-steps">
          {PROCESS.map((step) => (
            <li key={step.title}>
              <h3>{step.title}</h3>
              <p>{step.text}</p>
            </li>
          ))}
        </ol>
      </Section>

      {data && data.industries.length > 0 && (
        <Section eyebrow="Industries" title="Specialists in your market" actions={<ButtonLink to="/industries" variant="secondary">All industries</ButtonLink>}>
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
        <Section eyebrow="Testimonials" title="What clients say" tone="muted">
          <TestimonialCarousel items={data.testimonials} />
        </Section>
      )}

      {data && data.pricingTeaser.length > 0 && (
        <Section eyebrow="Pricing" title="Transparent pricing, no surprises" intro="Clear starting packages for every service. Ad spend is always billed at cost." actions={<ButtonLink to="/pricing" variant="secondary">See all pricing</ButtonLink>}>
          <div className="site-packages">
            {data.pricingTeaser.map((t) => (
              <PackageCard key={t.package.id} pkg={t.package} serviceName={t.serviceName} serviceSlug={t.serviceSlug} />
            ))}
          </div>
        </Section>
      )}

      {data && data.latestPosts.length > 0 && (
        <Section eyebrow="From the blog" title="Playbooks and insights" actions={<ButtonLink to="/blog" variant="secondary">Read the blog</ButtonLink>}>
          <ul className="site-grid site-grid--3">
            {data.latestPosts.map((p) => (
              <li key={p.slug}>
                <PostCard post={p} />
              </li>
            ))}
          </ul>
        </Section>
      )}

      <Section eyebrow="For creators" title="Become an Optimize All creator" tone="brand" intro="Have an established social account? Share campaigns from brands you believe in and get paid for every approved, clearly disclosed post.">
        <div className="site-hero__actions">
          <ButtonLink to="/register" variant="highlight" size="lg" leadingIcon={<Wallet />}>
            Join as a creator
          </ButtonLink>
          <ButtonLink to="/creators" variant="secondary" size="lg">
            How the creator program works
          </ButtonLink>
        </div>
      </Section>

      <CtaBand />

      <Section title="Get marketing insights in your inbox" intro="Two practical emails a month from our strategists. Unsubscribe any time.">
        <div className="site-narrow">
          <NewsletterSignup source="home" />
        </div>
      </Section>
    </>
  );
}
