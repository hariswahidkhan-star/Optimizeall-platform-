import { Compass } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { useSiteCopy } from './site/copy';
import { MovedOrNotFound } from './site/redirects';
import { StatusPage } from './StatusPage';

/** Where visitors of a missing website page most likely wanted to go (the server-rendered 404 lists the same). */
const HELPFUL_LINKS = [
  { to: '/services', label: 'Services' },
  { to: '/case-studies', label: 'Case studies' },
  { to: '/pricing', label: 'Pricing' },
  { to: '/blog', label: 'Blog' },
  { to: '/contact', label: 'Contact us' },
  { to: '/search', label: 'Search the site' },
];

/**
 * The 404 page. A public address that has moved (Website → Redirects) navigates to its new address instead.
 * `siteLinks` adds the website's most useful destinations (public website only, not the portals).
 */
export function NotFound({ siteLinks = false }: { siteLinks?: boolean }) {
  return (
    <MovedOrNotFound>
      <NotFoundPage siteLinks={siteLinks} />
    </MovedOrNotFound>
  );
}

function NotFoundPage({ siteLinks }: { siteLinks: boolean }) {
  const copy = useSiteCopy();
  return (
    <StatusPage
      code="404"
      icon={<Compass />}
      title={copy.text('shared.page404.title')}
      description={copy.text('shared.page404.description')}
      actions={
        <>
          <ButtonLink to="/">Back to the home page</ButtonLink>
          <ButtonLink to="/faq" variant="secondary">
            Read the FAQ
          </ButtonLink>
          {siteLinks && (
            <nav aria-label="Helpful links" className="status-page__links">
              <ul>
                {HELPFUL_LINKS.map((l) => (
                  <li key={l.to}>
                    <Link to={l.to}>{l.label}</Link>
                  </li>
                ))}
              </ul>
            </nav>
          )}
        </>
      }
    />
  );
}
