import { ArrowRight } from 'lucide-react';
import { useRef, type ReactNode } from 'react';
import { ButtonLink } from '@/components/ui';
import { NotFound } from '@/features/public/NotFound';
import { isApiError } from '@/lib/api/errors';
import { isInternalHref } from '@/lib/safeHref';
import { type PageBlock, type PublicPage, type SiteLink, usePage } from '../site/api';
import { useAcademyOverview } from '../site/academy';
import { HeroVisual, PillarsVisual } from '../site/art';
import { Blocks } from '../site/Blocks';
import { PublicQueryState, Section } from '../site/components';
import { useSiteCopy } from '../site/copy';
import { headFromSeo, useDocumentHead } from '../site/head';
import { useReveal } from '../site/motion';
import {
  AcademyStats,
  CertificateStrip,
  FeaturedCourseGrid,
  SkillsMarquee,
  SubjectTiles,
  SuggestedPaths,
  Timeline,
  TrustGrid,
} from '../site/Showcase';

/**
 * The two pillar pages built on CMS pages: /academy (the academy's marketing overview; the catalog itself is /learn)
 * and /about (the dual mission). Their words — hero, features, FAQ, call to action — are the CMS page's blocks, which
 * the server also renders for search engines; the live academy figures, subjects, courses and paths come from the
 * public learning API and sit between the hero and the remaining blocks.
 */

const text = (v: unknown): string | null => (typeof v === 'string' && v.trim() ? v : null);
const cta = (v: unknown): SiteLink | null => {
  const l = v as SiteLink | null | undefined;
  return l && text(l.label) && text(l.url) && isInternalHref(l.url) ? l : null;
};

/** The CMS hero block with an illustration in place of an image. */
function PillarHero({ block, fallbackTitle, aside }: { block: PageBlock | undefined; fallbackTitle: string; aside: ReactNode }) {
  const d = block?.data ?? {};
  const primary = cta(d.primaryCta);
  const secondary = cta(d.secondaryCta);
  return (
    <header className="site-hero site-hero--block site-home-hero oa-hero">
      <div className="container site-hero__inner oa-hero__inner">
        <div className="site-hero__copy">
          {text(d.eyebrow) && <p className="site-hero__eyebrow">{text(d.eyebrow)}</p>}
          <h1 className="site-hero__title">{text(d.title) ?? fallbackTitle}</h1>
          {text(d.subtitle) && <p className="site-hero__lead">{text(d.subtitle)}</p>}
          {(primary || secondary) && (
            <div className="site-hero__actions">
              {primary && (
                <ButtonLink to={primary.url} variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
                  {primary.label}
                </ButtonLink>
              )}
              {secondary && (
                <ButtonLink to={secondary.url} variant="secondary" size="lg">
                  {secondary.label}
                </ButtonLink>
              )}
            </div>
          )}
        </div>
        <div className="site-hero__aside oa-hero__aside">{aside}</div>
      </div>
    </header>
  );
}

/** The page's blocks after its hero; `beforeCta` renders just ahead of a closing call-to-action block. */
function RestBlocks({ page, beforeCta }: { page: PublicPage; beforeCta?: ReactNode }) {
  const blocks = page.blocks[0]?.type === 'hero' ? page.blocks.slice(1) : page.blocks;
  const closing = blocks.length > 0 && blocks[blocks.length - 1].type === 'cta' ? blocks.slice(-1) : [];
  const context = {
    testimonials: page.testimonials,
    caseStudies: page.caseStudies,
    serviceCategories: page.serviceCategories,
    trustLogos: page.trustLogos,
    pageTitle: page.title,
  };
  return (
    <>
      <Blocks blocks={closing.length ? blocks.slice(0, -1) : blocks} context={context} />
      {beforeCta}
      <Blocks blocks={closing} context={context} />
    </>
  );
}

/** /academy: why learn with Optimize All, live figures and subjects, featured courses, paths and certificates. */
export function AcademyOverviewPage() {
  const { data: page, isLoading, error } = usePage('academy');
  const academy = useAcademyOverview();
  const copy = useSiteCopy();
  const root = useRef<HTMLDivElement>(null);
  useReveal(root);
  const missing = isApiError(error) && error.status === 404;
  useDocumentHead(
    page
      ? headFromSeo(page.seo, page.jsonLd)
      : { title: missing ? 'Academy: Free AI and Marketing Courses | Optimize All' : null, description: copy.text('home.academy.intro') },
  );
  const heroCourse = academy.data?.featured.find((c) => c.category === 'Ai') ?? academy.data?.featured[0] ?? null;

  const live = (
    <>
      <section className="site-section oa-academy-live" aria-label="The academy in numbers">
        <div className="container">
          <AcademyStats data={academy.data} isLoading={academy.isLoading} />
        </div>
      </section>
      {academy.data && (
        <>
          <Section eyebrow={copy.text('home.academy.eyebrow')} title={copy.text('home.academy.subjectsTitle')} intro={copy.text('home.academy.intro')}>
            <SubjectTiles data={academy.data} />
          </Section>
          <section className="site-section site-section--muted" aria-labelledby="academy-featured">
            <div className="container">
              <div className="site-section__head">
                <div>
                  <h2 id="academy-featured" className="site-section__title">
                    {copy.text('home.academy.featuredTitle')}
                  </h2>
                </div>
                <div className="site-section__actions">
                  <ButtonLink to="/learn" variant="primary" trailingIcon={<ArrowRight />}>
                    {copy.text('home.academy.cta')}
                  </ButtonLink>
                </div>
              </div>
              <FeaturedCourseGrid data={academy.data} />
            </div>
            <SkillsMarquee skills={academy.data.skills} />
          </section>
          <Section id="paths" eyebrow={copy.text('home.paths.eyebrow')} title={copy.text('home.paths.title')} intro={copy.text('home.paths.intro')}>
            <SuggestedPaths data={academy.data} />
          </Section>
        </>
      )}
      <Section eyebrow={copy.text('home.learnSteps.eyebrow')} title={copy.text('home.learnSteps.title')} tone="muted">
        <Timeline steps={copy.pairs('home.learnSteps.steps')} />
      </Section>
      <CertificateStrip />
    </>
  );

  if (missing)
    return (
      <div ref={root} className="oa-page">
        <PillarHero
          block={{ id: 'hero', type: 'hero', data: { eyebrow: copy.text('home.academy.eyebrow'), title: copy.text('home.academy.title'), subtitle: copy.text('home.academy.intro'), primaryCta: { label: copy.text('home.academy.cta'), url: '/learn' } } }}
          fallbackTitle="Optimize All Academy"
          aside={<HeroVisual course={heroCourse} />}
        />
        {live}
      </div>
    );

  return (
    <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Page not found">
      {page && (
        <div ref={root} className="oa-page">
          <PillarHero
            block={page.blocks[0]?.type === 'hero' ? page.blocks[0] : undefined}
            fallbackTitle={page.title}
            aside={<HeroVisual course={heroCourse} />}
          />
          {live}
          <RestBlocks page={page} />
        </div>
      )}
    </PublicQueryState>
  );
}

/** /about: the dual mission — the CMS page with the two-pillar illustration and the academy's live figures. */
export function AboutPage() {
  const { data: page, isLoading, error } = usePage('about');
  const academy = useAcademyOverview();
  const root = useRef<HTMLDivElement>(null);
  useReveal(root);
  useDocumentHead(page ? headFromSeo(page.seo, page.jsonLd) : { title: null });

  if (isApiError(error) && error.status === 404) return <NotFound siteLinks />;
  return (
    <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="Page not found">
      {page && (
        <div ref={root} className="oa-page">
          <PillarHero block={page.blocks[0]?.type === 'hero' ? page.blocks[0] : undefined} fallbackTitle={page.title} aside={<PillarsVisual />} />
          <section className="site-section oa-academy-live" aria-label="The academy in numbers">
            <div className="container">
              <AcademyStats data={academy.data} isLoading={academy.isLoading} />
            </div>
          </section>
          <RestBlocks page={page} beforeCta={<TrustGrid />} />
        </div>
      )}
    </PublicQueryState>
  );
}
