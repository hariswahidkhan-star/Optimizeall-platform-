import clsx from 'clsx';
import { Check, Star } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { isSafeHref } from '@/lib/safeHref';
import type { Block, CountdownProps, PublicForm } from './api';
import { FormRenderer } from './FormRenderer';

export interface BlockRendererProps {
  blocks: Block[];
  /** Forms referenced by form blocks (public page) — keyed by id. */
  forms: Record<string, PublicForm | undefined>;
  /** In the builder: headings start at h2 (the staff page owns the h1), videos show a placeholder, forms never submit. */
  preview?: boolean;
  landingPageId?: string;
  variantKey?: string;
  /** Highlights the selected block in the builder preview. */
  selectedId?: string | null;
}

/** A link from page content: only http(s), site paths, #anchors, mailto: and tel: (validated server-side too). */
function PageLink({ href, className, children }: { href: string | null | undefined; className?: string; children: ReactNode }) {
  if (!href) return null;
  const ok = href.startsWith('#') || href.startsWith('mailto:') || href.startsWith('tel:') || isSafeHref(href);
  if (!ok) return <span className={className}>{children}</span>;
  const external = /^https?:\/\//.test(href);
  return (
    <a href={href} className={className} {...(external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}>
      {children}
    </a>
  );
}

/** Renders landing-page blocks as semantic, text-only HTML (no raw HTML is ever injected). */
export function BlockRenderer({ blocks, forms, preview, landingPageId, variantKey, selectedId }: BlockRendererProps) {
  const H1 = preview ? 'h2' : 'h1';
  const H2 = preview ? 'h3' : 'h2';
  const H3 = preview ? 'h4' : 'h3';
  let heroSeen = false;
  return (
    <div className="lp-page">
      {blocks.map((block) => {
        const selected = selectedId === block.id;
        const wrap = (content: ReactNode, extra?: string) => (
          <section key={block.id} id={block.id} className={clsx('lp-block', `lp-block--${block.type}`, extra, selected && 'is-selected')}>
            <div className="lp-block__inner">{content}</div>
          </section>
        );
        switch (block.type) {
          case 'hero': {
            const p = block.props;
            const Heading = heroSeen ? H2 : H1;
            heroSeen = true;
            return wrap(
              <div className={clsx('lp-hero', `lp-hero--${p.align}`)}>
                <div className="lp-hero__copy">
                  <Heading className="lp-hero__title">{p.headline}</Heading>
                  {p.subheadline && <p className="lp-hero__sub">{p.subheadline}</p>}
                  {p.ctaLabel && p.ctaHref && (
                    <PageLink href={p.ctaHref} className="lp-button lp-button--highlight">
                      {p.ctaLabel}
                    </PageLink>
                  )}
                </div>
                {p.imageUrl && <img className="lp-hero__image" src={p.imageUrl} alt={p.imageAlt ?? ''} />}
              </div>,
              `lp-theme--${p.theme}`,
            );
          }
          case 'text':
            return wrap(
              <div className="lp-text">
                {block.props.heading && <H2>{block.props.heading}</H2>}
                {block.props.body.split(/\n{1,}/).map((para, i) => (
                  <p key={i}>{para}</p>
                ))}
              </div>,
            );
          case 'image': {
            const p = block.props;
            const img = <img src={p.url} alt={p.decorative ? '' : (p.alt ?? '')} loading="lazy" />;
            return wrap(
              <figure className="lp-figure">
                {p.linkHref ? <PageLink href={p.linkHref}>{img}</PageLink> : img}
                {p.caption && <figcaption>{p.caption}</figcaption>}
              </figure>,
            );
          }
          case 'video': {
            const p = block.props;
            const src =
              p.provider === 'vimeo' && p.videoId
                ? `https://player.vimeo.com/video/${p.videoId}?dnt=1`
                : p.provider === 'youtube' && p.videoId
                  ? `https://www.youtube-nocookie.com/embed/${p.videoId}`
                  : null;
            return wrap(
              <div className="lp-video">
                {preview || !src ? (
                  <div className="lp-video__placeholder">
                    {p.title} ({p.provider ?? 'video'}{p.videoId ? ` · ${p.videoId}` : ''})
                  </div>
                ) : (
                  <iframe
                    src={src}
                    title={p.title}
                    loading="lazy"
                    allow="accelerometer; encrypted-media; gyroscope; picture-in-picture; fullscreen"
                    referrerPolicy="strict-origin-when-cross-origin"
                    sandbox="allow-scripts allow-same-origin allow-presentation"
                  />
                )}
              </div>,
            );
          }
          case 'features':
            return wrap(
              <div className="lp-features">
                {block.props.heading && <H2>{block.props.heading}</H2>}
                {block.props.intro && <p className="lp-lead">{block.props.intro}</p>}
                <ul className="lp-grid">
                  {block.props.items.map((item, i) => (
                    <li key={i} className="lp-card">
                      <H3>{item.title}</H3>
                      {item.body && <p>{item.body}</p>}
                    </li>
                  ))}
                </ul>
              </div>,
            );
          case 'testimonials':
            return wrap(
              <div className="lp-testimonials">
                {block.props.heading && <H2>{block.props.heading}</H2>}
                <ul className="lp-grid">
                  {block.props.items.map((t, i) => (
                    <li key={i} className="lp-card">
                      <figure>
                        {t.rating ? (
                          <span className="lp-stars" role="img" aria-label={`${t.rating} out of 5 stars`}>
                            {Array.from({ length: 5 }, (_, s) => (
                              <Star key={s} aria-hidden="true" className={s < t.rating! ? 'is-on' : undefined} />
                            ))}
                          </span>
                        ) : null}
                        <blockquote>{t.quote}</blockquote>
                        <figcaption>
                          <strong>{t.author}</strong>
                          {t.role && <span>, {t.role}</span>}
                        </figcaption>
                      </figure>
                    </li>
                  ))}
                </ul>
              </div>,
            );
          case 'pricing':
            return wrap(
              <div className="lp-pricing">
                {block.props.heading && <H2>{block.props.heading}</H2>}
                <ul className="lp-grid">
                  {block.props.plans.map((plan, i) => (
                    <li key={i} className={clsx('lp-card lp-plan', plan.highlighted && 'is-highlighted')}>
                      <H3>{plan.name}</H3>
                      <p className="lp-plan__price">
                        <span className="tabular">{plan.price}</span>
                        {plan.period && <span className="lp-plan__period"> {plan.period}</span>}
                      </p>
                      {plan.description && <p>{plan.description}</p>}
                      <ul className="lp-plan__features">
                        {plan.features.map((f, j) => (
                          <li key={j}>
                            <Check aria-hidden="true" /> {f}
                          </li>
                        ))}
                      </ul>
                      {plan.ctaLabel && plan.ctaHref && (
                        <PageLink href={plan.ctaHref} className={clsx('lp-button', plan.highlighted && 'lp-button--highlight')}>
                          {plan.ctaLabel}
                        </PageLink>
                      )}
                    </li>
                  ))}
                </ul>
                {block.props.footnote && <p className="lp-footnote">{block.props.footnote}</p>}
              </div>,
            );
          case 'faq':
            return wrap(
              <div className="lp-faq">
                {block.props.heading && <H2>{block.props.heading}</H2>}
                {block.props.items.map((item, i) => (
                  <details key={i}>
                    <summary>{item.question}</summary>
                    <p>{item.answer}</p>
                  </details>
                ))}
              </div>,
            );
          case 'countdown':
            return wrap(<Countdown props={block.props} Heading={H2} />);
          case 'form': {
            const form = forms[block.props.formId];
            return wrap(
              <div className="lp-form-block">
                {block.props.heading && <H2>{block.props.heading}</H2>}
                {block.props.description && <p className="lp-lead">{block.props.description}</p>}
                {form ? (
                  <FormRenderer form={form} preview={preview} landingPageId={landingPageId} variantKey={variantKey} />
                ) : (
                  <p className="lp-muted">This form is currently unavailable.</p>
                )}
              </div>,
            );
          }
          case 'cta':
            return wrap(
              <div className="lp-cta">
                <H2>{block.props.heading}</H2>
                {block.props.body && <p>{block.props.body}</p>}
                <PageLink href={block.props.buttonHref} className={clsx('lp-button', `lp-button--${block.props.style}`)}>
                  {block.props.buttonLabel}
                </PageLink>
              </div>,
            );
          case 'logos':
            return wrap(
              <div className="lp-logos">
                {block.props.heading && <H2>{block.props.heading}</H2>}
                <ul>
                  {block.props.items.map((logo, i) => (
                    <li key={i}>
                      {logo.href ? (
                        <PageLink href={logo.href}>
                          <img src={logo.imageUrl} alt={logo.name} loading="lazy" />
                        </PageLink>
                      ) : (
                        <img src={logo.imageUrl} alt={logo.name} loading="lazy" />
                      )}
                    </li>
                  ))}
                </ul>
              </div>,
            );
          case 'spacer':
            return <div key={block.id} className={clsx('lp-spacer', `lp-spacer--${block.props.size}`, selected && 'is-selected')} aria-hidden="true" />;
          default:
            return null;
        }
      })}
    </div>
  );
}

function Countdown({ props, Heading }: { props: CountdownProps; Heading: 'h2' | 'h3' }) {
  const end = new Date(props.endsAt).getTime();
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);
  const left = Math.max(0, end - now);
  const parts = [
    ['days', Math.floor(left / 86_400_000)],
    ['hours', Math.floor(left / 3_600_000) % 24],
    ['minutes', Math.floor(left / 60_000) % 60],
    ['seconds', Math.floor(left / 1000) % 60],
  ] as const;
  const endLabel = Number.isNaN(end) ? '' : new Date(end).toLocaleString();
  return (
    <div className="lp-countdown">
      {props.heading && <Heading>{props.heading}</Heading>}
      {left === 0 ? (
        <p>{props.expiredText ?? 'This offer has ended.'}</p>
      ) : (
        <>
          <p className="visually-hidden">Ends {endLabel}</p>
          <ol className="lp-countdown__units" aria-hidden="true">
            {parts.map(([label, value]) => (
              <li key={label}>
                <span className="tabular">{String(value).padStart(2, '0')}</span>
                <span>{label}</span>
              </li>
            ))}
          </ol>
        </>
      )}
    </div>
  );
}
