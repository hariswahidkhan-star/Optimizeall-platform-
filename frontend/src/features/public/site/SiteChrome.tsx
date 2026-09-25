import { X } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { isInternalHref } from '@/lib/safeHref';
import { safeStorage } from '@/lib/hooks/storage';
import { useSite } from './api';
import { captureAttribution } from './attribution';
import { PartnerLinksProvider } from '../partners/PartnerLinksContext';
import { CookieConsent } from './CookieConsent';
import { SiteFooter } from './SiteFooter';
import { SiteHeader } from './SiteHeader';
import './site.css';

function AnnouncementBar() {
  const { data: site } = useSite();
  const bar = site?.announcement;
  const key = bar?.text ? `oa.announcement.${bar.text.length}.${bar.text.slice(0, 24)}` : '';
  const [dismissed, setDismissed] = useState(() => (key ? safeStorage.get(key) === '1' : false));
  useEffect(() => setDismissed(key ? safeStorage.get(key) === '1' : false), [key]);
  if (!bar?.enabled || !bar.text || dismissed) return null;
  return (
    <div className="site-announcement" role="region" aria-label="Announcement">
      <p className="container site-announcement__inner">
        <span>{bar.text}</span>
        {bar.linkUrl && bar.linkLabel && isInternalHref(bar.linkUrl) && (
          <Link to={bar.linkUrl} className="site-announcement__link">
            {bar.linkLabel}
          </Link>
        )}
        <button
          type="button"
          className="site-announcement__close"
          aria-label="Dismiss announcement"
          onClick={() => {
            safeStorage.set(key, '1');
            setDismissed(true);
          }}
        >
          <X aria-hidden="true" />
        </button>
      </p>
    </div>
  );
}

/**
 * The public website frame used by PublicLayout: skip link, announcement bar, header with the services mega-menu,
 * content, CMS footer and the cookie-consent banner. Also records campaign attribution on the first page view.
 */
export function SiteChrome({ children }: { children: ReactNode }) {
  const { data: site } = useSite();
  const location = useLocation();
  const [consentOpen, setConsentOpen] = useState(false);
  const mainRef = useRef<HTMLElement>(null);
  const focusedPath = useRef(location.pathname);

  useEffect(() => {
    captureAttribution({ pathname: location.pathname, search: location.search });
  }, [location.pathname, location.search]);

  // After a client-side navigation, move focus to the new page's content so screen readers announce it and keyboard
  // users continue from the top of the page (a fresh page load keeps focus where the browser puts it).
  useEffect(() => {
    if (focusedPath.current === location.pathname) return;
    focusedPath.current = location.pathname;
    if (location.hash) return; // in-page anchors (#section) handle their own scrolling and focus
    mainRef.current?.focus({ preventScroll: true });
  }, [location.pathname, location.hash]);

  return (
    <PartnerLinksProvider>
      <div className="public-layout site-layout">
        <a className="skip-link" href="#main">
          Skip to content
        </a>
        <AnnouncementBar />
        <SiteHeader />
        <main id="main" ref={mainRef} tabIndex={-1} className="public-main">
          {children}
        </main>
        <SiteFooter onCookieSettings={() => setConsentOpen(true)} />
        <CookieConsent ids={site?.analytics} open={consentOpen} onClose={() => setConsentOpen(false)} />
      </div>
    </PartnerLinksProvider>
  );
}
