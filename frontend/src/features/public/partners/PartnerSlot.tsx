import { ArrowUpRight } from 'lucide-react';
import { useRef, type CSSProperties, type ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { buttonClasses, type ButtonSize, type ButtonVariant } from '@/components/ui/buttonStyles';
import { type PartnerCard, usePartnerPlacement, usePartners } from './api';
import { SPONSORED_REL, visitHref } from './partnerLinks';
import { slotKind, type PartnerListSlot, type PartnerSlotName, type PartnerUnitSlot } from './slots';
import { useImpression } from './tracking';
import './partners.css';

/**
 * An outbound link to a partner. Every such link is a partnership link, so it always carries `rel="sponsored noopener"`
 * and opens in a new tab (Google's link-spam policy); it goes through the click counter, which adds the UTM tags.
 * Renders nothing while the partner has no website.
 */
export function SponsoredLink({
  partner,
  slot,
  children,
  variant,
  size = 'md',
  className,
}: {
  partner: Pick<PartnerCard, 'visitUrl' | 'name'>;
  slot: PartnerSlotName;
  children: ReactNode;
  variant?: ButtonVariant;
  size?: ButtonSize;
  className?: string;
}) {
  const { pathname } = useLocation();
  const href = visitHref(partner.visitUrl, slot, pathname);
  if (!href) return null;
  return (
    <a href={href} rel={SPONSORED_REL} target="_blank" className={variant ? buttonClasses(variant, size, { className }) : className}>
      {children}
      <ArrowUpRight aria-hidden="true" width={16} height={16} />
      <span className="visually-hidden"> (opens in a new tab)</span>
    </a>
  );
}

function accent(partner: PartnerCard): CSSProperties | undefined {
  return partner.brandColor ? ({ '--partner-accent': partner.brandColor } as CSSProperties) : undefined;
}

export function PartnerLogo({ partner, size = 56 }: { partner: Pick<PartnerCard, 'logoUrl' | 'name'>; size?: number }) {
  return (
    <span className="partner-logo" style={{ width: size, height: size }}>
      <img src={partner.logoUrl} alt={`${partner.name} logo`} width={size} height={size} loading="lazy" decoding="async" />
    </span>
  );
}

export function PartnerOfferNote({ partner }: { partner: PartnerCard }) {
  if (!partner.offer) return null;
  return (
    <p className="partner-offer">
      <span>{partner.offer.text}</span>
      {partner.offer.code && (
        <>
          {' '}
          — code <code className="partner-offer__code">{partner.offer.code}</code>
        </>
      )}
      {partner.offer.expiresAt && (
        <span className="partner-offer__until"> (until {new Date(partner.offer.expiresAt).toLocaleDateString()})</span>
      )}
    </p>
  );
}

/** One ad unit, labelled "Sponsored" (required disclosure). */
export function PartnerAd({ partner, slot }: { partner: PartnerCard; slot: PartnerUnitSlot }) {
  const ref = useRef<HTMLElement>(null);
  const { pathname } = useLocation();
  useImpression(ref, { partner: partner.slug, slot, path: pathname });
  return (
    <aside ref={ref} className="partner-unit" aria-label={`Sponsored: ${partner.name}`} data-partner-slot={slot} style={accent(partner)}>
      <p className="partner-unit__disclosure">
        <span className="partner-badge">Sponsored</span>
        <span className="partner-unit__kind">Partner</span>
      </p>
      <div className="partner-unit__body">
        <PartnerLogo partner={partner} />
        <div className="partner-unit__text">
          <p className="partner-unit__name">{partner.name}</p>
          <p className="partner-unit__tagline">{partner.tagline}</p>
          <p className="partner-unit__relationship">{partner.relationshipLabel}.</p>
          <PartnerOfferNote partner={partner} />
        </div>
      </div>
      <div className="partner-unit__actions">
        <SponsoredLink partner={partner} slot={slot} variant="primary" size="sm">
          Visit {partner.websiteHost}
        </SponsoredLink>
        <Link to={partner.profilePath} className={buttonClasses('ghost', 'sm')}>
          About {partner.name}
        </Link>
      </div>
    </aside>
  );
}

function StripLogo({ partner, slot }: { partner: PartnerCard; slot: PartnerListSlot }) {
  const ref = useRef<HTMLLIElement>(null);
  const { pathname } = useLocation();
  useImpression(ref, { partner: partner.slug, slot, path: pathname });
  return (
    <li ref={ref} style={accent(partner)}>
      <Link to={partner.profilePath} className="partner-strip__item">
        <PartnerLogo partner={partner} size={48} />
        <span className="partner-strip__name">{partner.name}</span>
      </Link>
    </li>
  );
}

function names(partners: PartnerCard[]): ReactNode[] {
  return partners.flatMap((p, i) => [
    i === 0 ? null : i === partners.length - 1 ? ' and ' : ', ',
    <Link key={p.slug} to={p.profilePath}>
      {p.name}
    </Link>,
  ]);
}

/** Home page: "Official marketing partner of" logo strip plus the partnership statement. */
export function PartnerStrip({ partners, slot = 'home.partners' }: { partners: PartnerCard[]; slot?: PartnerListSlot }) {
  if (partners.length === 0) return null;
  return (
    <section className="partner-strip" aria-labelledby="partner-strip-title" data-partner-slot={slot}>
      <div className="container">
        <div className="partner-strip__inner">
        <div className="partner-strip__intro">
          <p className="eyebrow">Partners</p>
          <h2 id="partner-strip-title" className="partner-strip__title">
            Official marketing partner of
          </h2>
        </div>
        <ul className="partner-strip__logos">
          {partners.map((p) => (
            <StripLogo key={p.slug} partner={p} slot={slot} />
          ))}
        </ul>
        <p className="partner-strip__statement">
          Optimize All is the official marketing partner of {names(partners)}.{' '}
            <Link to="/partners" className="partner-strip__more">
              About our partners
            </Link>
          </p>
        </div>
      </div>
    </section>
  );
}

function FooterPartner({ partner, children }: { partner: PartnerCard; children: ReactNode }) {
  const ref = useRef<HTMLSpanElement>(null);
  const { pathname } = useLocation();
  useImpression(ref, { partner: partner.slug, slot: 'footer.partners', path: pathname });
  return <span ref={ref}>{children}</span>;
}

/** Footer: "Optimize All is the official marketing partner of PCI AI and Certuvo." (internal profile links). */
export function PartnerFooterLine({ partners }: { partners: PartnerCard[] }) {
  if (partners.length === 0) return null;
  return (
    <p className="site-footer__partners" data-partner-slot="footer.partners">
      Optimize All is the official marketing partner of{' '}
      {partners.map((p, i) => (
        <FooterPartner key={p.slug} partner={p}>
          {i === 0 ? null : i === partners.length - 1 ? ' and ' : ', '}
          <Link to={p.profilePath}>{p.name}</Link>
        </FooterPartner>
      ))}
      .
    </p>
  );
}

function UnitSlot({ slot, keywords, categories }: { slot: PartnerUnitSlot; keywords: string[]; categories: string[] }) {
  const { pathname } = useLocation();
  const { data } = usePartnerPlacement(slot, keywords, categories, pathname);
  if (!data?.partner) return null;
  return <PartnerAd partner={data.partner} slot={slot} />;
}

function ListSlot({ slot }: { slot: PartnerListSlot }) {
  const { data } = usePartners();
  const partners = (data?.partners ?? []).filter((p) => p.slots.includes(slot));
  return slot === 'footer.partners' ? <PartnerFooterLine partners={partners} /> : <PartnerStrip partners={partners} slot={slot} />;
}

export interface PartnerSlotProps {
  /** A slot name from slots.ts (the `learn.*` slots are reserved for the academy). */
  slot: PartnerListSlot | PartnerUnitSlot;
  /** Page keywords: blog tags, the service name, course topics… */
  keywords?: string[];
  /** Page categories: blog category slugs, the service slug, the course category… */
  categories?: string[];
}

/**
 * A partner placement. List slots render every partner enabled for them; unit slots render at most one ad unit chosen by
 * the API from the page's keywords and categories (with rotation when nothing matches), labelled "Sponsored". Renders
 * nothing when no partner is enabled, and every outbound link is `rel="sponsored noopener"`.
 *
 * @example <PartnerSlot slot="learn.course" keywords={course.tags} categories={[course.category]} />
 */
export function PartnerSlot({ slot, keywords = [], categories = [] }: PartnerSlotProps) {
  const kind = slotKind(slot);
  if (kind === 'List') return <ListSlot slot={slot as PartnerListSlot} />;
  if (kind === 'Unit') return <UnitSlot slot={slot as PartnerUnitSlot} keywords={keywords} categories={categories} />;
  return null;
}
