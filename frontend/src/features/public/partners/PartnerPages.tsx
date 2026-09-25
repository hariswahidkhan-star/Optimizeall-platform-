import { CheckCircle2, Info } from 'lucide-react';
import { useRef, type CSSProperties } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import { ButtonLink, Skeleton } from '@/components/ui';
import { isInternalHref } from '@/lib/safeHref';
import { PageHero, PublicQueryState, Section } from '../site/components';
import { headFromSeo, useDocumentHead } from '../site/head';
import { Markdown } from '../site/Markdown';
import { type PartnerCard, type PartnerOffering, type PartnerProfile, usePartner, usePartners } from './api';
import { PartnerLogo, PartnerOfferNote, SponsoredLink } from './PartnerSlot';
import { useImpression } from './tracking';
import './partners.css';

function joinNames(names: string[]): string {
  if (names.length <= 1) return names.join('');
  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
}

/** The disclosure shown on every partner page (visible text, not hidden in markup). */
function Disclosure({ name }: { name?: string }) {
  return (
    <div className="partner-disclosure" role="note">
      <Info aria-hidden="true" width={20} height={20} />
      <p>
        {name ? `${name} is an independent platform, separate from Optimize All. ` : 'Our partners are independent platforms. '}
        Optimize All is {name ? 'its' : 'their'} official marketing partner, so links to {name ? `${name}'s` : 'partner'} website are partner
        links and are marked as sponsored.
      </p>
    </div>
  );
}

function DirectoryCard({ partner }: { partner: PartnerCard }) {
  const ref = useRef<HTMLElement>(null);
  const { pathname } = useLocation();
  useImpression(ref, { partner: partner.slug, slot: 'partners.directory', path: pathname });
  return (
    <article
      ref={ref}
      className="partner-card"
      style={partner.brandColor ? ({ '--partner-accent': partner.brandColor } as CSSProperties) : undefined}
    >
      <div className="partner-card__head">
        <PartnerLogo partner={partner} size={64} />
        <h2 className="partner-card__title">
          <Link to={partner.profilePath}>{partner.name}</Link>
        </h2>
      </div>
      <p>{partner.tagline}</p>
      <p className="text-small text-muted">{partner.relationshipLabel}.</p>
      <PartnerOfferNote partner={partner} />
      <div className="partner-card__actions">
        <ButtonLink to={partner.profilePath} variant="secondary" size="sm">
          About {partner.name}
        </ButtonLink>
        <SponsoredLink partner={partner} slot="partners.directory" variant="ghost" size="sm">
          Visit {partner.websiteHost}
        </SponsoredLink>
      </div>
    </article>
  );
}

/** /partners — every active partner with the partnership statement. */
export function PartnersPage() {
  const { data, isLoading, error } = usePartners();
  const names = (data?.partners ?? []).map((p) => p.name);
  useDocumentHead(
    data ? headFromSeo(data.seo, data.jsonLd) : { title: 'Our partners', description: 'Organizations Optimize All is the official marketing partner of.' },
  );
  return (
    <>
      <PageHero
        eyebrow="Partners"
        title="Our partners"
        lead={
          names.length > 0
            ? `Optimize All is the official marketing partner of ${joinNames(names)}. Each is an independent platform; here is what they offer.`
            : 'Organizations Optimize All is the official marketing partner of.'
        }
        breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Partners' }]}
      />
      <div className="site-section">
        <div className="container">
          <PublicQueryState error={error} isLoading={false} notFoundTitle="Partners are not available right now">
            {isLoading ? (
              <div className="partner-grid">
                <Skeleton height={260} />
                <Skeleton height={260} />
              </div>
            ) : (
              <ul className="partner-grid">
                {(data?.partners ?? []).map((p) => (
                  <li key={p.slug}>
                    <DirectoryCard partner={p} />
                  </li>
                ))}
              </ul>
            )}
            <div style={{ marginTop: 'var(--space-8)' }}>
              <Disclosure />
            </div>
          </PublicQueryState>
        </div>
      </div>
    </>
  );
}

function OfferingCard({ item }: { item: PartnerOffering }) {
  return (
    <li className="partner-offering" id={item.anchor ?? undefined}>
      <h3>{item.link && isInternalHref(item.link) ? <Link to={item.link}>{item.title}</Link> : item.title}</h3>
      {item.summary && <p>{item.summary}</p>}
      {item.facts.length > 0 && (
        <ul className="partner-offering__facts" aria-label={`${item.title}: key facts`}>
          {item.facts.map((f) => (
            <li key={f}>{f}</li>
          ))}
        </ul>
      )}
    </li>
  );
}

function Offerings({ partner }: { partner: PartnerProfile }) {
  const cards = partner.offerings.filter((o) => o.summary || o.facts.length > 0);
  const chips = partner.offerings.filter((o) => !o.summary && o.facts.length === 0);
  if (partner.offerings.length === 0) return null;
  return (
    <Section title={`What ${partner.name} offers`} tone="muted">
      {cards.length > 0 && (
        <ul className="partner-offerings">
          {cards.map((o) => (
            <OfferingCard key={o.anchor ?? o.title} item={o} />
          ))}
        </ul>
      )}
      {chips.length > 0 && (
        <div className="partner-chips">
          <h3>{cards.length > 0 ? 'Also' : 'Covers'}</h3>
          <ul className="site-chips">
            {chips.map((o) => (
              <li key={o.anchor ?? o.title} id={o.anchor ?? undefined} className="site-chip">
                {o.link && isInternalHref(o.link) ? <Link to={o.link}>{o.title}</Link> : o.title}
              </li>
            ))}
          </ul>
        </div>
      )}
    </Section>
  );
}

/** /partners/:slug — an indexable, content-rich profile of one partner (JSON-LD Organization + WebPage from the API). */
export function PartnerProfilePage() {
  const { slug = '' } = useParams();
  const { data: p, isLoading, error } = usePartner(slug);
  useDocumentHead(p ? headFromSeo(p.seo, p.jsonLd) : { title: 'Partner' });
  const ref = useRef<HTMLDivElement>(null);
  const { pathname } = useLocation();
  useImpression(ref, p ? { partner: p.slug, slot: 'partners.profile', path: pathname } : null);

  return (
    <PublicQueryState error={error} isLoading={isLoading} notFoundTitle="We couldn't find that partner">
      {p && (
        <div ref={ref}>
          <PageHero
            eyebrow="Official marketing partner"
            title={p.name}
            lead={p.tagline}
            breadcrumbs={[{ label: 'Home', to: '/' }, { label: 'Partners', to: '/partners' }, { label: p.name }]}
            actions={
              <>
                <SponsoredLink partner={p} slot="partners.profile" variant="highlight" size="lg">
                  Visit {p.websiteHost}
                </SponsoredLink>
                <ButtonLink to="/partners" variant="secondary" size="lg">
                  All partners
                </ButtonLink>
              </>
            }
          >
            <div
              className="partner-hero-card"
              style={p.brandColor ? ({ '--partner-accent': p.brandColor } as CSSProperties) : undefined}
            >
              <PartnerLogo partner={p} size={160} />
              <p>{p.relationshipLabel}.</p>
            </div>
          </PageHero>

          <div className="site-section">
            <div className="container site-narrow site-prose-stack">
              <Disclosure name={p.name} />
              {p.highlights.length > 0 && (
                <ul className="site-checklist" aria-label={`${p.name} at a glance`}>
                  {p.highlights.map((h) => (
                    <li key={h}>
                      <CheckCircle2 aria-hidden="true" />
                      {h}
                    </li>
                  ))}
                </ul>
              )}
              {p.descriptionMarkdown && (
                <section aria-labelledby="partner-about">
                  <h2 id="partner-about" className="site-section__title">
                    About {p.name}
                  </h2>
                  <Markdown source={p.descriptionMarkdown} minLevel={3} />
                </section>
              )}
              {p.offer && (
                <div className="partner-offer-box">
                  <PartnerOfferNote partner={{ ...p, profilePath: '', slots: [] }} />
                  <SponsoredLink partner={p} slot="partners.profile" variant="secondary" size="sm">
                    Get the offer
                  </SponsoredLink>
                </div>
              )}
            </div>
          </div>

          <Offerings partner={p} />

          {p.related.length > 0 && (
            <Section title="Related partners">
              <ul className="partner-grid">
                {p.related.map((r) => (
                  <li key={r.slug}>
                    <article className="partner-card">
                      <div className="partner-card__head">
                        <PartnerLogo partner={r} size={48} />
                        <h3 className="partner-card__title">
                          <Link to={r.profilePath}>{r.name}</Link>
                        </h3>
                      </div>
                      <p>{r.tagline}</p>
                    </article>
                  </li>
                ))}
              </ul>
            </Section>
          )}

          {p.visitUrl && (
            <Section title={`Learn more at ${p.websiteHost}`} tone="brand">
              <div className="site-hero__actions">
                <SponsoredLink partner={p} slot="partners.profile" variant="highlight" size="lg">
                  Visit {p.websiteHost}
                </SponsoredLink>
              </div>
            </Section>
          )}
        </div>
      )}
    </PublicQueryState>
  );
}
