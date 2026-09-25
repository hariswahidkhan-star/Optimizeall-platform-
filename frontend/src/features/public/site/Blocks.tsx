import { useId } from 'react';
import { ButtonLink } from '@/components/ui';
import { isInternalHref } from '@/lib/safeHref';
import type { CaseStudyCard as CaseStudyCardData, FaqEntry, HomeStat, PageBlock, ServiceCategoryGroup, SiteLink, Testimonial, TrustLogo } from './api';
import { CaseStudyCard, MetricValue, Section, ServiceCard, TestimonialCarousel } from './components';
import { SiteIcon } from './icons';
import { Markdown } from './Markdown';
import { SiteVideo, type SiteVideoData } from './SiteVideo';

export interface BlockContext {
  testimonials?: Testimonial[];
  caseStudies?: CaseStudyCardData[];
  serviceCategories?: ServiceCategoryGroup[];
  trustLogos?: TrustLogo[];
  /** First block renders the page's h1 when it is a hero. */
  pageTitle?: string;
}

const str = (v: unknown): string | null => (typeof v === 'string' && v.trim() ? v : null);
const link = (v: unknown): SiteLink | null => {
  const l = v as SiteLink | null | undefined;
  return l && str(l.label) && str(l.url) ? l : null;
};
const list = <T,>(v: unknown): T[] => (Array.isArray(v) ? (v as T[]) : []);

function CtaButton({ value, variant }: { value: SiteLink | null; variant: 'highlight' | 'secondary' }) {
  if (!value || !isInternalHref(value.url)) {
    if (value && /^https:\/\//.test(value.url))
      return (
        <a className={`ui-button ui-button--${variant} ui-button--lg`} href={value.url} target="_blank" rel="noopener noreferrer">
          {value.label}
        </a>
      );
    return null;
  }
  return (
    <ButtonLink to={value.url} variant={variant} size="lg">
      {value.label}
    </ButtonLink>
  );
}

export function FaqList({ items, title }: { items: FaqEntry[]; title?: string | null }) {
  const id = useId();
  return (
    <div className="site-faq">
      {title && (
        <h2 id={id} className="site-section__title">
          {title}
        </h2>
      )}
      <div className="site-faq__list">
        {items.map((item) => (
          <details key={item.question} className="site-faq__item">
            <summary>{item.question}</summary>
            <Markdown source={item.answer} className="site-faq__answer" />
          </details>
        ))}
      </div>
    </div>
  );
}

export function StatsGrid({ items }: { items: HomeStat[] }) {
  return (
    <div className="site-stats">
      {items.map((s) => (
        <MetricValue key={s.label} metric={s} />
      ))}
    </div>
  );
}

export function LogoCloud({ logos, title }: { logos: TrustLogo[]; title?: string | null }) {
  if (logos.length === 0) return null;
  return (
    <div className="site-logos">
      {title && <p className="site-logos__title">{title}</p>}
      <ul>
        {logos.map((logo) => (
          <li key={logo.name}>
            <img src={logo.imageUrl} alt={logo.name} loading="lazy" decoding="async" height={40} width={140} />
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Renders CMS page blocks. Unknown block types are skipped. Used by public pages and the editor's live preview. */
export function Blocks({ blocks, context = {} }: { blocks: PageBlock[]; context?: BlockContext }) {
  return (
    <>
      {blocks.map((block, index) => {
        const d = block.data ?? {};
        switch (block.type) {
          case 'hero': {
            const Heading = index === 0 ? 'h1' : 'h2';
            return (
              <header key={block.id} className="site-hero site-hero--block">
                <div className="container site-hero__inner">
                  <div className="site-hero__copy">
                    {str(d.eyebrow) && <p className="site-hero__eyebrow">{str(d.eyebrow)}</p>}
                    <Heading className="site-hero__title">{str(d.title) ?? context.pageTitle}</Heading>
                    {str(d.subtitle) && <p className="site-hero__lead">{str(d.subtitle)}</p>}
                    {(link(d.primaryCta) || link(d.secondaryCta)) && (
                      <div className="site-hero__actions">
                        <CtaButton value={link(d.primaryCta)} variant="highlight" />
                        <CtaButton value={link(d.secondaryCta)} variant="secondary" />
                      </div>
                    )}
                  </div>
                  {str(d.imageUrl) && (
                    <div className="site-hero__aside">
                      <img className="site-hero__image" src={str(d.imageUrl)!} alt="" width={640} height={480} decoding="async" />
                    </div>
                  )}
                </div>
              </header>
            );
          }
          case 'richText':
            return (
              <div key={block.id} className="site-section site-section--prose">
                <div className="container site-narrow">
                  <Markdown source={str(d.markdown)} />
                </div>
              </div>
            );
          case 'featuresGrid':
            return (
              <Section key={block.id} title={str(d.title) ?? 'Highlights'} intro={str(d.intro)}>
                <ul className="site-grid site-grid--3 site-features">
                  {list<{ title: string; text: string; icon: string | null }>(d.items).map((item) => (
                    <li key={item.title} className="site-feature">
                      <span className="site-card__icon">
                        <SiteIcon name={item.icon} />
                      </span>
                      <h3>{item.title}</h3>
                      <p>{item.text}</p>
                    </li>
                  ))}
                </ul>
              </Section>
            );
          case 'stats':
            return (
              <Section key={block.id} title={str(d.title) ?? 'Results'} tone="muted">
                <StatsGrid items={list<HomeStat>(d.items)} />
              </Section>
            );
          case 'cta':
            return (
              <section key={block.id} className="site-cta" aria-label={str(d.title) ?? 'Call to action'}>
                <div className="container">
                  <div className="site-cta__inner">
                    <div>
                      <h2>{str(d.title)}</h2>
                      {str(d.text) && <p>{str(d.text)}</p>}
                    </div>
                    <div className="site-cta__actions">
                      <CtaButton value={link(d.primary)} variant="highlight" />
                      <CtaButton value={link(d.secondary)} variant="secondary" />
                    </div>
                  </div>
                </div>
              </section>
            );
          case 'faq':
            return (
              <div key={block.id} className="site-section">
                <div className="container site-narrow">
                  <FaqList title={str(d.title) ?? 'Frequently asked questions'} items={list<FaqEntry>(d.items)} />
                </div>
              </div>
            );
          case 'testimonials': {
            const ids = list<string>(d.testimonialIds);
            const all = context.testimonials ?? [];
            const items = ids.length ? all.filter((t) => ids.includes(t.id)) : all.slice(0, 8);
            if (items.length === 0) return null;
            return (
              <Section key={block.id} title={str(d.title) ?? 'What clients say'} tone="muted">
                <TestimonialCarousel items={items} />
              </Section>
            );
          }
          case 'logoCloud': {
            const logos = list<TrustLogo>(d.logos);
            const items = logos.length ? logos : (context.trustLogos ?? []);
            if (items.length === 0) return null;
            return (
              <div key={block.id} className="site-section site-section--tight">
                <div className="container">
                  <LogoCloud logos={items} title={str(d.title)} />
                </div>
              </div>
            );
          }
          case 'servicesGrid': {
            const slug = str(d.categorySlug);
            const groups = (context.serviceCategories ?? []).filter((g) => !slug || g.slug === slug);
            if (groups.length === 0) return null;
            return (
              <Section key={block.id} title={str(d.title) ?? 'Our services'} intro={str(d.intro)}>
                <ul className="site-grid site-grid--3">
                  {groups.flatMap((g) => g.services).map((s) => (
                    <li key={s.slug}>
                      <ServiceCard service={s} />
                    </li>
                  ))}
                </ul>
              </Section>
            );
          }
          case 'caseStudyHighlight': {
            const study = (context.caseStudies ?? []).find((c) => c.slug === str(d.caseStudySlug));
            if (!study) return null;
            return (
              <Section key={block.id} title={str(d.title) ?? 'Featured case study'}>
                <div className="site-grid site-grid--2">
                  <CaseStudyCard study={study} />
                </div>
              </Section>
            );
          }
          case 'video': {
            const video = d as unknown as SiteVideoData;
            if (!str(video.title)) return null;
            return (
              <div key={block.id} className="site-section">
                <div className="container site-narrow">
                  <SiteVideo video={video} />
                </div>
              </div>
            );
          }
          default:
            return null;
        }
      })}
    </>
  );
}
