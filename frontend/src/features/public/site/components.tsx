import clsx from 'clsx';
import { ArrowRight, Check, ChevronLeft, ChevronRight, Pause, Play, Quote, Star } from 'lucide-react';
import { useEffect, useId, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ButtonLink, EmptyState, ErrorState, Skeleton, SkeletonText } from '@/components/ui';
import { isApiError } from '@/lib/api/errors';
import { formatDate } from '@/lib/format/dates';
import { formatMoney } from '@/lib/format/money';
import type {
  BillingPeriod,
  CaseStudyCard as CaseStudyCardData,
  Metric,
  PostCard as PostCardData,
  Price,
  PublicPackage,
  ServiceCard as ServiceCardData,
  Testimonial,
} from './api';
import { useSiteCopy } from './copy';
import { SiteIcon } from './icons';

export const PERIOD_SUFFIX: Record<BillingPeriod, string> = {
  OneTime: 'one-time',
  Monthly: '/ month',
  Quarterly: '/ quarter',
  Yearly: '/ year',
};

export function PriceText({ price, prefix = 'From' }: { price: Price; prefix?: string }) {
  return (
    <span className="site-price-text">
      {prefix} <strong className="tabular">{formatMoney(price.amount, price.currency, { currencyDisplay: 'narrowSymbol' })}</strong>{' '}
      {PERIOD_SUFFIX[price.billingPeriod]}
    </span>
  );
}

export function Section({
  id,
  eyebrow,
  title,
  intro,
  children,
  className,
  tone,
  actions,
}: {
  id?: string;
  eyebrow?: ReactNode;
  title: ReactNode;
  intro?: ReactNode;
  children: ReactNode;
  className?: string;
  tone?: 'muted' | 'brand';
  actions?: ReactNode;
}) {
  const headingId = useId();
  return (
    <section id={id} className={clsx('site-section', tone && `site-section--${tone}`, className)} aria-labelledby={headingId}>
      <div className="container">
        <div className="site-section__head">
          <div>
            {eyebrow && <p className="eyebrow">{eyebrow}</p>}
            <h2 id={headingId} className="site-section__title">
              {title}
            </h2>
            {intro && <p className="site-section__intro">{intro}</p>}
          </div>
          {actions && <div className="site-section__actions">{actions}</div>}
        </div>
        {children}
      </div>
    </section>
  );
}

export function PageHero({
  eyebrow,
  title,
  lead,
  actions,
  breadcrumbs,
  children,
}: {
  eyebrow?: ReactNode;
  title: ReactNode;
  lead?: ReactNode;
  actions?: ReactNode;
  breadcrumbs?: { label: string; to?: string }[];
  children?: ReactNode;
}) {
  return (
    <header className="site-hero">
      <div className="container site-hero__inner">
        <div className="site-hero__copy">
          {breadcrumbs && <Breadcrumbs items={breadcrumbs} />}
          {eyebrow && <p className="site-hero__eyebrow">{eyebrow}</p>}
          <h1 className="site-hero__title">{title}</h1>
          {lead && <p className="site-hero__lead">{lead}</p>}
          {actions && <div className="site-hero__actions">{actions}</div>}
        </div>
        {children && <div className="site-hero__aside">{children}</div>}
      </div>
    </header>
  );
}

export function Breadcrumbs({ items }: { items: { label: string; to?: string }[] }) {
  return (
    <nav aria-label="Breadcrumb" className="site-breadcrumbs">
      <ol>
        {items.map((item, i) => (
          <li key={`${item.label}-${i}`}>
            {item.to && i < items.length - 1 ? <Link to={item.to}>{item.label}</Link> : <span aria-current={i === items.length - 1 ? 'page' : undefined}>{item.label}</span>}
          </li>
        ))}
      </ol>
    </nav>
  );
}

export function ServiceCard({ service, headingLevel = 3 }: { service: ServiceCardData; headingLevel?: 3 | 4 }) {
  const H = `h${headingLevel}` as 'h3';
  return (
    <article className="site-card site-card--service">
      <span className="site-card__icon">
        <SiteIcon name={service.icon} />
      </span>
      <H className="site-card__title">
        <Link to={`/services/${service.slug}`} className="site-card__link">
          {service.name}
        </Link>
      </H>
      <p className="site-card__text">{service.tagline}</p>
      {service.startingPrice && (
        <p className="site-card__meta">
          <PriceText price={service.startingPrice} />
        </p>
      )}
    </article>
  );
}

export function MetricValue({ metric }: { metric: Metric }) {
  const estimated = metric.measurement === 'Estimated';
  return (
    <div className={clsx('site-metric', estimated && 'site-metric--estimated')}>
      <p className="site-metric__value tabular">
        {estimated && <span aria-hidden="true">≈</span>}
        {metric.value}
      </p>
      <p className="site-metric__label">{metric.label}</p>
      <p className="site-metric__tag">
        <span className={clsx('site-tag', estimated ? 'site-tag--estimated' : 'site-tag--measured')}>
          {estimated ? 'Estimated' : 'Measured'}
        </span>
        {metric.context && <span className="site-metric__context">{metric.context}</span>}
      </p>
    </div>
  );
}

export function CaseStudyCard({ study, headingLevel = 3 }: { study: CaseStudyCardData; headingLevel?: 2 | 3 | 4 }) {
  const H = `h${headingLevel}` as 'h2' | 'h3' | 'h4';
  return (
    <article className="site-card site-card--case">
      {study.coverImageUrl && (
        <img className="site-card__image" src={study.coverImageUrl} alt="" loading="lazy" decoding="async" width={640} height={360} />
      )}
      <p className="site-card__eyebrow">
        {study.clientName}
        {study.industryName && <> · {study.industryName}</>}
      </p>
      <H className="site-card__title">
        <Link to={`/case-studies/${study.slug}`} className="site-card__link">
          {study.title}
        </Link>
      </H>
      <p className="site-card__text">{study.summary}</p>
      {study.highlights.length > 0 && (
        <div className="site-card__metrics">
          {study.highlights.slice(0, 2).map((m) => (
            <MetricValue key={m.label} metric={m} />
          ))}
        </div>
      )}
    </article>
  );
}

export function PostCard({ post, headingLevel = 3 }: { post: PostCardData; headingLevel?: 2 | 3 }) {
  const H = `h${headingLevel}` as 'h3';
  return (
    <article className="site-card site-card--post">
      {post.coverImageUrl && (
        <img className="site-card__image" src={post.coverImageUrl} alt={post.coverImageAlt ?? ''} loading="lazy" decoding="async" width={640} height={360} />
      )}
      {post.categories[0] && <p className="site-card__eyebrow">{post.categories[0].name}</p>}
      <H className="site-card__title">
        <Link to={`/blog/${post.slug}`} className="site-card__link">
          {post.title}
        </Link>
      </H>
      <p className="site-card__text">{post.excerpt}</p>
      <p className="site-card__meta">
        {post.publishedAt && <time dateTime={post.publishedAt}>{formatDate(post.publishedAt)}</time>}
        {' · '}
        {post.readingMinutes} min read
        {post.authorName && <> · {post.authorName}</>}
      </p>
    </article>
  );
}

export function PackageCard({ pkg, serviceName, serviceSlug }: { pkg: PublicPackage; serviceName?: string; serviceSlug?: string }) {
  const headingId = useId();
  const quoteLink = `/get-a-quote?${new URLSearchParams({ ...(serviceSlug ? { service: serviceSlug } : {}), package: pkg.id }).toString()}`;
  return (
    <article className={clsx('site-package', pkg.isMostPopular && 'site-package--popular')} aria-labelledby={headingId}>
      {pkg.isMostPopular && <p className="site-package__badge">Most popular</p>}
      <h3 id={headingId} className="site-package__name">
        {serviceName && <span className="site-package__service">{serviceName}</span>}
        {pkg.name}
      </h3>
      {pkg.description && <p className="site-package__desc">{pkg.description}</p>}
      <p className="site-package__price">
        {pkg.isCustomQuote || pkg.price === null ? (
          <span className="site-package__amount">Custom quote</span>
        ) : (
          <>
            <span className="site-package__amount tabular">{formatMoney(pkg.price, pkg.currency, { currencyDisplay: 'narrowSymbol' })}</span>{' '}
            <span className="site-package__period">{PERIOD_SUFFIX[pkg.billingPeriod]}</span>
          </>
        )}
      </p>
      {pkg.setupFee !== null && pkg.setupFee > 0 && (
        <p className="site-package__setup">+ {formatMoney(pkg.setupFee, pkg.currency, { currencyDisplay: 'narrowSymbol' })} one-time setup</p>
      )}
      <ul className="site-package__features">
        {pkg.features.map((f) => (
          <li key={f}>
            <Check aria-hidden="true" />
            {f}
          </li>
        ))}
      </ul>
      <ButtonLink to={quoteLink} variant={pkg.isMostPopular ? 'highlight' : 'secondary'} fullWidth>
        {pkg.isCustomQuote ? 'Request a quote' : 'Get started'}
        <span className="visually-hidden"> with {serviceName ? `${serviceName} ` : ''}{pkg.name}</span>
      </ButtonLink>
    </article>
  );
}

export function Stars({ rating }: { rating: number | null }) {
  if (!rating) return null;
  return (
    <span className="site-stars" role="img" aria-label={`${rating} out of 5 stars`}>
      {Array.from({ length: 5 }, (_, i) => (
        <Star key={i} aria-hidden="true" className={i < rating ? 'is-on' : undefined} />
      ))}
    </span>
  );
}

/**
 * Accessible testimonial carousel: one quote at a time, previous/next buttons, a pause/play control (auto-advance pauses
 * on hover/focus and is off when the user prefers reduced motion), and a polite live region only while paused.
 */
export function TestimonialCarousel({ items, label = 'Client testimonials' }: { items: Testimonial[]; label?: string }) {
  const [index, setIndex] = useState(0);
  const reduced = typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const [playing, setPlaying] = useState(!reduced && items.length > 1);
  const [hovering, setHovering] = useState(false);
  const count = items.length;

  useEffect(() => {
    if (!playing || hovering || count < 2) return;
    const timer = window.setInterval(() => setIndex((i) => (i + 1) % count), 7000);
    return () => window.clearInterval(timer);
  }, [playing, hovering, count]);

  if (count === 0) return null;
  const current = items[Math.min(index, count - 1)];
  return (
    <div
      className="site-carousel"
      role="group"
      aria-roledescription="carousel"
      aria-label={label}
      onMouseEnter={() => setHovering(true)}
      onMouseLeave={() => setHovering(false)}
      onFocus={() => setHovering(true)}
      onBlur={() => setHovering(false)}
    >
      <div className="site-carousel__slide" role="group" aria-roledescription="slide" aria-label={`${index + 1} of ${count}`} aria-live={playing ? 'off' : 'polite'}>
        <Quote aria-hidden="true" className="site-carousel__mark" />
        <blockquote className="site-carousel__quote">
          <p>{current.quote}</p>
        </blockquote>
        <p className="site-carousel__author">
          {current.avatarUrl && <img src={current.avatarUrl} alt="" width={40} height={40} loading="lazy" />}
          <span>
            <strong>{current.authorName}</strong>
            {(current.authorRole || current.company) && (
              <span className="text-muted">
                {' '}
                — {[current.authorRole, current.company].filter(Boolean).join(', ')}
              </span>
            )}
          </span>
          <Stars rating={current.rating} />
        </p>
      </div>
      {count > 1 && (
        <div className="site-carousel__controls">
          <button type="button" className="site-carousel__btn" onClick={() => setPlaying((p) => !p)} aria-label={playing ? 'Pause testimonials' : 'Play testimonials'}>
            {playing ? <Pause aria-hidden="true" /> : <Play aria-hidden="true" />}
          </button>
          <button type="button" className="site-carousel__btn" onClick={() => setIndex((i) => (i - 1 + count) % count)} aria-label="Previous testimonial">
            <ChevronLeft aria-hidden="true" />
          </button>
          <span className="site-carousel__count tabular" aria-hidden="true">
            {index + 1} / {count}
          </span>
          <button type="button" className="site-carousel__btn" onClick={() => setIndex((i) => (i + 1) % count)} aria-label="Next testimonial">
            <ChevronRight aria-hidden="true" />
          </button>
        </div>
      )}
    </div>
  );
}

export function CtaBand({ title, text }: { title?: string; text?: string }) {
  const id = useId();
  const copy = useSiteCopy();
  return (
    <section className="site-cta" aria-labelledby={id}>
      <div className="container site-cta__inner">
        <div>
          <h2 id={id}>{title ?? copy.text('shared.cta.title')}</h2>
          <p>{text ?? copy.text('shared.cta.text')}</p>
        </div>
        <div className="site-cta__actions">
          <ButtonLink to="/free-audit" variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
            {copy.text('shared.cta.primary')}
          </ButtonLink>
          <ButtonLink to="/book-a-consultation" variant="secondary" size="lg">
            {copy.text('shared.cta.secondary')}
          </ButtonLink>
        </div>
      </div>
    </section>
  );
}

/** Loading / error / not-found handling for public detail pages. */
export function PublicQueryState({ error, isLoading, notFoundTitle, children }: { error: unknown; isLoading: boolean; notFoundTitle: string; children: ReactNode }) {
  const copy = useSiteCopy();
  if (isLoading)
    return (
      <div className="container site-loading" aria-busy="true">
        <Skeleton height={48} width="60%" />
        <SkeletonText lines={4} />
      </div>
    );
  if (error) {
    if (isApiError(error) && error.status === 404)
      return (
        <div className="container site-loading">
          <EmptyState
            title={notFoundTitle}
            headingLevel={2}
            description={copy.text('shared.notFound.description')}
            action={
              <ButtonLink to="/" variant="secondary">
                Go to the homepage
              </ButtonLink>
            }
          />
        </div>
      );
    return (
      <div className="container site-loading">
        <ErrorState error={error} />
      </div>
    );
  }
  return <>{children}</>;
}

export function formatPublished(date: string | null) {
  return date ? formatDate(date) : null;
}
