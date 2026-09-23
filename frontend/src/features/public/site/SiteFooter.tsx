import { Mail, MapPin, MessageCircle, Phone } from 'lucide-react';
import { Link } from 'react-router-dom';
import { BRAND_TAGLINE, Logo } from '@/components/brand/Logo';
import { isExternalHref, isInternalHref } from '@/lib/safeHref';
import { type SiteLink, useSite } from './api';
import { NewsletterSignup } from './NewsletterSignup';

const FALLBACK_COLUMNS: { title: string; links: SiteLink[] }[] = [
  {
    title: 'Services',
    links: [
      { label: 'All services', url: '/services' },
      { label: 'Pricing', url: '/pricing' },
      { label: 'Case studies', url: '/case-studies' },
    ],
  },
  {
    title: 'Company',
    links: [
      { label: 'About', url: '/about' },
      { label: 'Careers', url: '/careers' },
      { label: 'Blog', url: '/blog' },
    ],
  },
  {
    title: 'Creators',
    links: [
      { label: 'Become a creator', url: '/creators' },
      { label: 'Creator FAQ', url: '/faq' },
      { label: 'Create an account', url: '/register' },
    ],
  },
];

const FALLBACK_LEGAL: SiteLink[] = [
  { label: 'Privacy policy', url: '/privacy-policy' },
  { label: 'Terms of service', url: '/terms-of-service' },
  { label: 'Cookie policy', url: '/cookie-policy' },
];

export function FooterLink({ link }: { link: SiteLink }) {
  if (isInternalHref(link.url)) return <Link to={link.url}>{link.label}</Link>;
  if (isExternalHref(link.url))
    return (
      <a href={link.url} target="_blank" rel="noopener noreferrer">
        {link.label}
        <span className="visually-hidden"> (opens in a new tab)</span>
      </a>
    );
  return <span>{link.label}</span>;
}

/** CMS-driven footer: blurb, link columns, contact details, social profiles, newsletter and legal links. */
export function SiteFooter({ onCookieSettings }: { onCookieSettings: () => void }) {
  const { data: site } = useSite();
  const columns = site?.footer.columns.length ? site.footer.columns : FALLBACK_COLUMNS;
  const legal = site?.footer.legalLinks.length ? site.footer.legalLinks : FALLBACK_LEGAL;
  const contact = site?.contact;
  const year = new Date().getFullYear();

  return (
    <footer className="public-footer site-footer">
      <div className="container site-footer__top">
        <div className="site-footer__brand">
          <Logo variant="stacked" size={72} tagline title={`Optimize All — ${BRAND_TAGLINE}`} />
          <p className="site-footer__blurb">
            {site?.footer.blurb ??
              'A full-service digital marketing agency: search, social, paid media, content, email, brand and web.'}
          </p>
          {contact && (
            <ul className="site-footer__contact">
              {contact.email && (
                <li>
                  <Mail aria-hidden="true" />
                  <a href={`mailto:${contact.email}`}>{contact.email}</a>
                </li>
              )}
              {contact.phone && (
                <li>
                  <Phone aria-hidden="true" />
                  <a href={`tel:${contact.phone.replace(/[^\d+]/g, '')}`}>{contact.phone}</a>
                </li>
              )}
              {contact.whatsApp && (
                <li>
                  <MessageCircle aria-hidden="true" />
                  <a href={`https://wa.me/${contact.whatsApp.replace(/\D/g, '')}`} target="_blank" rel="noopener noreferrer">
                    WhatsApp<span className="visually-hidden"> (opens in a new tab)</span>
                  </a>
                </li>
              )}
              {contact.address && (
                <li>
                  <MapPin aria-hidden="true" />
                  <span>{contact.address}</span>
                </li>
              )}
            </ul>
          )}
        </div>
        <nav aria-label="Footer" className="site-footer__nav">
          {columns.map((column) => (
            <div key={column.title}>
              <h2 className="public-footer__heading">{column.title}</h2>
              <ul>
                {column.links.map((link) => (
                  <li key={link.label + link.url}>
                    <FooterLink link={link} />
                  </li>
                ))}
              </ul>
            </div>
          ))}
          <div>
            <h2 className="public-footer__heading">Sign in</h2>
            <ul>
              <li>
                <Link to="/login">Client login</Link>
              </li>
              <li>
                <Link to="/login">Creator sign in</Link>
              </li>
              <li>
                <Link to="/register">Create a creator account</Link>
              </li>
            </ul>
          </div>
        </nav>
        <section className="site-footer__newsletter" aria-labelledby="footer-newsletter">
          <h2 id="footer-newsletter" className="public-footer__heading">
            Marketing insights, twice a month
          </h2>
          <p className="text-small text-muted">Practical playbooks from our strategists. No spam, unsubscribe any time.</p>
          <NewsletterSignup source="footer" compact />
        </section>
      </div>
      <div className="container public-footer__legal site-footer__legal">
        <p>© {year} Optimize All. Paid creator posts are always disclosed.</p>
        <ul>
          {legal.map((link) => (
            <li key={link.url}>
              <FooterLink link={link} />
            </li>
          ))}
          <li>
            <button type="button" className="site-linkbutton" onClick={onCookieSettings}>
              Cookie settings
            </button>
          </li>
          {site?.social.map((s) => (
            <li key={s.url}>
              <FooterLink link={{ label: s.platform, url: s.url }} />
            </li>
          ))}
        </ul>
      </div>
    </footer>
  );
}
