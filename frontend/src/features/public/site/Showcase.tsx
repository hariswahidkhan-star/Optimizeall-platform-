import clsx from 'clsx';
import {
  Accessibility,
  ArrowRight,
  Award,
  BadgeCheck,
  BookOpen,
  Clock,
  Database,
  Eye,
  GraduationCap,
  Layers,
  Lock,
  Scale,
  ShieldCheck,
  Sparkles,
} from 'lucide-react';
import type { CSSProperties, ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ButtonLink, Skeleton } from '@/components/ui';
import { usePaths } from '@/features/learning/api';
import { CourseCard } from '@/features/learning/components/CourseCard';
import { PathGrid } from '@/features/learning/components/PathViews';
import '@/features/learning/learning.css';
import { categoryPath, coursePath, type AcademyOverview } from './academy';
import { CategoryArt, CertificateVisual } from './art';
import { useSiteCopy } from './copy';
import { CountUp, Marquee, trackGlow, useInViewClass } from './motion';
import './marketing.css';

/**
 * Marketing sections shared by the home page, the /academy overview and About: live academy figures, subject tiles,
 * featured courses, suggested learning paths, the "how it works" timeline, the certificate strip, the trust grid and
 * the closing dual call-to-action. Every figure and course comes from the public learning API; every word from the
 * editable page copy.
 */

/** Live academy figures (counting up). Renders skeletons while loading and nothing if the academy is unavailable. */
export function AcademyStats({ data, isLoading }: { data: AcademyOverview | null; isLoading: boolean }) {
  const copy = useSiteCopy();
  if (!data)
    return isLoading ? (
      <div className="oa-stats" aria-hidden="true">
        {Array.from({ length: 4 }, (_, i) => (
          <Skeleton key={i} height={92} />
        ))}
      </div>
    ) : null;
  const items: { value: ReactNode; label: string; icon: ReactNode }[] = [
    { value: <CountUp value={data.courseCount} />, label: copy.text('home.academy.statCourses'), icon: <BookOpen /> },
    {
      value: <CountUp value={data.lessonCount} suffix={data.lessonCountComplete ? '' : '+'} />,
      label: copy.text('home.academy.statLessons'),
      icon: <Layers />,
    },
    { value: <CountUp value={data.categories.length} />, label: copy.text('home.academy.statSubjects'), icon: <GraduationCap /> },
    { value: <span className="oa-count">$0</span>, label: copy.text('home.academy.statFree'), icon: <Award /> },
  ];
  return (
    <dl className="oa-stats" data-reveal="stagger">
      {items.map((item) => (
        <div key={item.label} className="oa-stat">
          <span className="oa-stat__icon" aria-hidden="true">
            {item.icon}
          </span>
          <dt className="oa-stat__label">{item.label}</dt>
          <dd className="oa-stat__value">{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}

/** Subject tiles: illustration, name, live course count and a one-line description; each opens the filtered catalog. */
export function SubjectTiles({ data, headingLevel = 3 }: { data: AcademyOverview; headingLevel?: 3 | 4 }) {
  const copy = useSiteCopy();
  const descriptions = new Map(copy.pairs('home.academy.subjects').map((p) => [p.title.toLowerCase(), p.text]));
  const Heading = `h${headingLevel}` as const;
  return (
    <ul className="oa-subjects" data-reveal="stagger" onPointerMove={trackGlow}>
      {data.categories.map((c) => (
        <li key={c.category} className={clsx('oa-subject', `lx-cat--${c.category.toLowerCase()}`)} data-glow="">
          <CategoryArt category={c.category} className="oa-subject__art" />
          <Heading className="oa-subject__title">
            <Link to={categoryPath(c.category)} className="oa-subject__link">
              {c.label}
            </Link>
          </Heading>
          <p className="oa-subject__count">
            {c.courseCount} {c.courseCount === 1 ? 'course' : 'courses'}
          </p>
          {descriptions.get(c.category.toLowerCase()) && <p className="oa-subject__text">{descriptions.get(c.category.toLowerCase())}</p>}
          <ArrowRight className="oa-subject__arrow" aria-hidden="true" />
        </li>
      ))}
    </ul>
  );
}

/** Featured courses (then the newest others) as course cards; the first featured card gets an animated border. */
export function FeaturedCourseGrid({ data, limit = 6 }: { data: AcademyOverview; limit?: number }) {
  const picked = [...data.featured, ...data.courses.filter((c) => !c.isFeatured)].slice(0, limit);
  if (picked.length === 0) return null;
  return (
    <ul className="lx-grid oa-courses" data-reveal="stagger">
      {picked.map((c, i) => (
        <li key={c.id} className={clsx(i === 0 && c.isFeatured && 'oa-glow-border')}>
          <CourseCard course={c} to={coursePath(c.slug)} />
        </li>
      ))}
    </ul>
  );
}

/**
 * The academy's learning paths (GET /public/learning/paths): up to four path cards with their badges and totals, and a
 * link to every path. Renders nothing until the paths load (or when none has a published course yet).
 */
export function SuggestedPaths({ limit = 4 }: { limit?: number }) {
  const paths = usePaths();
  const list = paths.data?.paths ?? [];
  if (list.length === 0) return null;
  return (
    <div className="oa-real-paths">
      <PathGrid paths={list.slice(0, limit)} linkFor={(slug) => `/learn/paths/${encodeURIComponent(slug)}`} />
      <p className="oa-real-paths__more">
        <Link to="/learn/paths">
          See all {list.length} learning paths <ArrowRight aria-hidden="true" className="lx-inline-icon" />
        </Link>
      </p>
    </div>
  );
}

/** A numbered timeline whose progress line and step markers loop gently while it is on screen. */
export function Timeline({ steps, className }: { steps: { title: string; text: string }[]; className?: string }) {
  const ref = useInViewClass<HTMLOListElement>();
  return (
    <ol ref={ref} className={clsx('oa-timeline', className)} style={{ '--n': steps.length } as CSSProperties} data-reveal="stagger">
      {steps.map((step, i) => (
        <li key={step.title} className="oa-timeline__step" style={{ '--i': i } as CSSProperties}>
          <span className="oa-timeline__dot" aria-hidden="true">
            {i + 1}
          </span>
          <h3 className="oa-timeline__title">{step.title}</h3>
          <p className="oa-timeline__text">{step.text}</p>
        </li>
      ))}
    </ol>
  );
}

/** "Free certificates" strip: copy, proof points and a certificate illustration. */
export function CertificateStrip({ id = 'certificates' }: { id?: string }) {
  const copy = useSiteCopy();
  return (
    <section id={id} className="site-section site-section--muted oa-cert" aria-labelledby={`${id}-title`}>
      <div className="container oa-cert__inner">
        <div className="oa-cert__copy" data-reveal="">
          <p className="eyebrow">{copy.text('home.cert.eyebrow')}</p>
          <h2 id={`${id}-title`} className="site-section__title">
            {copy.text('home.cert.title')}
          </h2>
          <p className="site-section__intro">{copy.text('home.cert.text')}</p>
          <ul className="oa-checks">
            {copy.list('home.cert.points').map((p) => (
              <li key={p}>
                <BadgeCheck aria-hidden="true" /> {p}
              </li>
            ))}
          </ul>
          <div className="site-hero__actions">
            <ButtonLink to="/learn" variant="primary" size="lg" trailingIcon={<ArrowRight />}>
              {copy.text('home.cert.cta')}
            </ButtonLink>
          </div>
        </div>
        <CertificateVisual />
      </div>
    </section>
  );
}

/** Skills taught across the catalog, scrolling slowly (static and wrapped for reduced motion). */
export function SkillsMarquee({ skills }: { skills: string[] }) {
  const copy = useSiteCopy();
  if (skills.length < 6) return null;
  return (
    <Marquee
      className="oa-skills"
      label={copy.text('home.academy.skillsLabel')}
      items={skills.map((s) => (
        <span className="oa-skill">
          <Sparkles aria-hidden="true" />
          {s}
        </span>
      ))}
    />
  );
}

const TRUST_ICONS = [Lock, Accessibility, ShieldCheck, BadgeCheck, Scale, Database, Eye, Clock];

/** Trust and compliance: true statements only, from the page copy, with the policy pages one click away. */
export function TrustGrid() {
  const copy = useSiteCopy();
  return (
    <section className="site-section site-section--muted oa-trust" aria-labelledby="trust-title">
      <div className="container">
        <div className="site-section__head">
          <div>
            <p className="eyebrow">{copy.text('home.trust.eyebrow')}</p>
            <h2 id="trust-title" className="site-section__title">
              {copy.text('home.trust.title')}
            </h2>
            <p className="site-section__intro">{copy.text('home.trust.intro')}</p>
          </div>
        </div>
        <ul className="oa-trust__grid" data-reveal="stagger">
          {copy.pairs('home.trust.items').map((item, i) => {
            const Icon = TRUST_ICONS[i % TRUST_ICONS.length];
            return (
              <li key={item.title} className="oa-trust__item">
                <span className="oa-tile" aria-hidden="true">
                  <Icon />
                </span>
                <h3>{item.title}</h3>
                <p>{item.text}</p>
              </li>
            );
          })}
        </ul>
        <p className="oa-trust__links">
          <Link to="/privacy-policy">Privacy policy</Link>
          <Link to="/accessibility">Accessibility statement</Link>
          <Link to="/cookie-policy">Cookie policy</Link>
          <Link to="/terms-of-service">Terms of service</Link>
        </p>
      </div>
    </section>
  );
}

/** Closing band with both pillars side by side: start learning, or get a free audit. */
export function DualCta() {
  const copy = useSiteCopy();
  return (
    <section className="site-cta oa-dual" aria-labelledby="dual-cta-title">
      <div className="container">
        <div className="site-cta__inner oa-dual__inner">
          <div className="oa-dual__copy">
            <h2 id="dual-cta-title">{copy.text('home.final.title')}</h2>
            <p>{copy.text('home.final.text')}</p>
          </div>
          <div className="oa-dual__cards">
            <Link to="/learn" className="oa-dual__card">
              <GraduationCap aria-hidden="true" />
              <span className="oa-dual__kicker">{copy.text('home.academy.eyebrow')}</span>
              <span className="oa-dual__label">{copy.text('home.final.learnCta')}</span>
              <ArrowRight aria-hidden="true" className="oa-dual__arrow" />
            </Link>
            <Link to="/free-audit" className="oa-dual__card oa-dual__card--agency">
              <Sparkles aria-hidden="true" />
              <span className="oa-dual__kicker">{copy.text('home.agency.eyebrow')}</span>
              <span className="oa-dual__label">{copy.text('home.final.agencyCta')}</span>
              <ArrowRight aria-hidden="true" className="oa-dual__arrow" />
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}
