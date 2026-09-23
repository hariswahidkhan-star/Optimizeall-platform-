import { ArrowRight, CheckCircle2 } from 'lucide-react';
import { useParams, useSearchParams } from 'react-router-dom';
import { ButtonLink, Skeleton } from '@/components/ui';
import { isExternalHref, isInternalHref } from '@/lib/safeHref';
import { useService, useServices } from '../site/api';
import { FaqList } from '../site/Blocks';
import { CaseStudyCard, CtaBand, PackageCard, PageHero, PublicQueryState, Section, ServiceCard, TestimonialCarousel } from '../site/components';
import { headFromSeo, useDocumentHead } from '../site/head';
import { SiteIcon } from '../site/icons';
import { Markdown } from '../site/Markdown';

/** /services — every service line, filterable by category. */
export function ServicesPage() {
  const { data, isLoading, error } = useServices();
  const [params, setParams] = useSearchParams();
  const active = params.get('category');
  useDocumentHead({
    title: 'Services',
    description: 'SEO, social media, paid media, content, email, branding, web, growth, reputation and analytics services.',
  });
  const groups = (data ?? []).filter((g) => !active || g.slug === active);

  return (
    <>
      <PageHero
        eyebrow="Services"
        title="Everything you need to grow, under one roof"
        lead="Nine service lines delivered by specialists who work as one team. Start with one service or build an integrated programme."
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
      <CtaBand title="Not sure where to start?" text="Tell us your goals and we'll recommend the right mix of services — free." />
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
                  Get a quote
                </ButtonLink>
                <ServiceCta label={s.ctaLabel} url={s.ctaUrl} />
                {!s.ctaLabel && (
                  <ButtonLink to="/book-a-consultation" variant="secondary" size="lg">
                    Book a call
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
                  <p className="eyebrow">Results we move</p>
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
            <Section title="Problems we solve" tone="muted">
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
            <Section title="What's included">
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
                  <h3 className="site-subheading">Tools and platforms</h3>
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
            <Section title="How it works" tone="muted">
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
            <Section id="pricing" title="Packages and pricing" intro="Prices exclude taxes and third-party costs such as ad spend, which are billed at cost.">
              <div className="site-packages">
                {s.packages.map((p) => (
                  <PackageCard key={p.id} pkg={p} serviceSlug={s.slug} />
                ))}
              </div>
            </Section>
          )}

          {s.caseStudies.length > 0 && (
            <Section title="Related case studies" tone="muted">
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
            <Section title="What clients say">
              <TestimonialCarousel items={s.testimonials} />
            </Section>
          )}

          {s.faqs.length > 0 && (
            <div className="site-section">
              <div className="container site-narrow">
                <FaqList title="Frequently asked questions" items={s.faqs} />
              </div>
            </div>
          )}

          {s.relatedServices.length > 0 && (
            <Section title="Related services" tone="muted">
              <ul className="site-grid site-grid--3">
                {s.relatedServices.map((r) => (
                  <li key={r.slug}>
                    <ServiceCard service={r} />
                  </li>
                ))}
              </ul>
            </Section>
          )}

          <CtaBand title={`Let's talk about ${s.name}`} />
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
