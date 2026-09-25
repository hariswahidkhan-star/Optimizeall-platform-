import clsx from 'clsx';
import { ArrowRight, ChevronDown, GraduationCap, Menu } from 'lucide-react';
import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react';
import { Link, NavLink, useLocation } from 'react-router-dom';
import { defaultLandingPath } from '@/app/portals';
import { Logo } from '@/components/brand/Logo';
import { ThemeToggle } from '@/components/ThemeToggle';
import { ButtonLink, Drawer, IconButton } from '@/components/ui';
import { useAuth } from '@/lib/auth/useAuth';
import { useLearningSummary } from '@/features/learning/api';
import { isInternalHref } from '@/lib/safeHref';
import { type MenuCategory, type MenuItem, useSite } from './api';
import { SiteIcon } from './icons';

/** Navigation shown until (or if) the site settings can't be loaded. Mirrors SiteSettingsService.Defaults. */
export const FALLBACK_MENU: MenuItem[] = [
  {
    label: 'Academy',
    url: '/academy',
    description: 'Free courses with certificates.',
    children: [
      { label: 'All courses', url: '/learn', description: 'Free, self-paced courses with certificates.', children: null },
      { label: 'AI courses', url: '/learn?category=Ai', description: 'ChatGPT, Claude, prompting, agents and more.', children: null },
      { label: 'Learning paths', url: '/learn/paths', description: 'Beginner to advanced, one course at a time.', children: null },
      { label: 'Certificates', url: '/academy#certificates', description: 'Verifiable, and ready for LinkedIn.', children: null },
    ],
  },
  { label: 'Services', url: '/services', description: null, children: [] },
  { label: 'Industries', url: '/industries', description: null, children: null },
  { label: 'Case studies', url: '/case-studies', description: null, children: null },
  { label: 'Pricing', url: '/pricing', description: null, children: null },
  {
    label: 'About',
    url: '/about',
    description: null,
    children: [
      { label: 'About us', url: '/about', description: 'Our mission: the academy and the agency.', children: null },
      { label: 'Team', url: '/team', description: 'The people behind your results.', children: null },
      { label: 'Careers', url: '/careers', description: 'Join the team.', children: null },
      { label: 'Blog', url: '/blog', description: 'Playbooks, research and news.', children: null },
      { label: 'Creators', url: '/creators', description: 'Get paid to share brands you believe in.', children: null },
    ],
  },
];

/** The academy menu: its hub link is /academy (marketing overview) or /learn (the catalog), with sub-items. */
const isAcademyItem = (item: MenuItem) => (item.url === '/academy' || item.url === '/learn') && (item.children?.length ?? 0) > 0;

function focusables(panel: HTMLElement | null): HTMLElement[] {
  return panel ? Array.from(panel.querySelectorAll<HTMLElement>('a[href]')) : [];
}

/**
 * A disclosure-style dropdown (WAI-ARIA "disclosure navigation"): the trigger is a button with aria-expanded; Enter,
 * Space or ArrowDown opens and focuses the first link; arrow keys, Home and End move between links; Escape closes and
 * returns focus to the trigger; tabbing out closes it.
 */
function Dropdown({
  label,
  wide,
  academy,
  children,
  open,
  onOpenChange,
}: {
  label: string;
  wide?: boolean;
  academy?: boolean;
  children: ReactNode;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const panelId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const focusFirstOnOpen = useRef(false);

  useEffect(() => {
    if (open && focusFirstOnOpen.current) {
      focusFirstOnOpen.current = false;
      focusables(panelRef.current)[0]?.focus();
    }
  }, [open]);

  const close = (restoreFocus: boolean) => {
    onOpenChange(false);
    if (restoreFocus) triggerRef.current?.focus();
  };

  const onTriggerKeyDown = (e: ReactKeyboardEvent<HTMLButtonElement>) => {
    if (e.key === 'ArrowDown' || ((e.key === 'Enter' || e.key === ' ') && !open)) {
      e.preventDefault();
      focusFirstOnOpen.current = true;
      if (open) focusables(panelRef.current)[0]?.focus();
      else onOpenChange(true);
    } else if (e.key === 'Escape' && open) {
      e.preventDefault();
      close(true);
    }
  };

  // Arrow keys, Home/End and Escape inside the open panel (a native listener: the panel is not itself interactive).
  const closeRef = useRef(close);
  closeRef.current = close;
  useEffect(() => {
    const panel = panelRef.current;
    if (!panel) return;
    const onKeyDown = (e: KeyboardEvent) => {
      const links = focusables(panel);
      const index = links.indexOf(document.activeElement as HTMLElement);
      const move = (to: number) => {
        e.preventDefault();
        links[(to + links.length) % links.length]?.focus();
      };
      if (e.key === 'Escape') {
        e.preventDefault();
        closeRef.current(true);
      } else if (e.key === 'ArrowDown' || e.key === 'ArrowRight') move(index + 1);
      else if (e.key === 'ArrowUp' || e.key === 'ArrowLeft') move(index - 1);
      else if (e.key === 'Home') move(0);
      else if (e.key === 'End') move(links.length - 1);
    };
    panel.addEventListener('keydown', onKeyDown);
    return () => panel.removeEventListener('keydown', onKeyDown);
  }, []);

  return (
    <div
      className="site-nav__item"
      onBlur={(e) => {
        if (open && !e.currentTarget.contains(e.relatedTarget as Node | null)) onOpenChange(false);
      }}
    >
      <button
        ref={triggerRef}
        type="button"
        className={clsx('public-header__link site-nav__trigger', open && 'is-open')}
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => onOpenChange(!open)}
        onKeyDown={onTriggerKeyDown}
      >
        {label}
        <ChevronDown aria-hidden="true" className="site-nav__chevron" />
      </button>
      <div
        ref={panelRef}
        id={panelId}
        className={clsx('site-nav__panel', wide && 'site-nav__panel--mega', academy && 'site-nav__panel--academy')}
        hidden={!open}
      >
        {children}
      </div>
    </div>
  );
}

function ServicesMega({ categories, onNavigate }: { categories: MenuCategory[]; onNavigate: () => void }) {
  if (categories.length === 0)
    return (
      <Link to="/services" className="site-mega__all" onClick={onNavigate}>
        Explore all services
      </Link>
    );
  return (
    <>
      <div className="site-mega__grid">
        {categories.map((category) => (
          <div key={category.slug} className="site-mega__col">
            <p className="site-mega__heading">
              <SiteIcon name={category.icon} className="site-mega__icon" />
              {category.name}
            </p>
            <ul>
              {category.services.map((service) => (
                <li key={service.slug}>
                  <Link to={`/services/${service.slug}`} onClick={onNavigate} className="site-mega__link">
                    {service.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
      <div className="site-mega__footer">
        <Link to="/services" onClick={onNavigate} className="site-mega__all">
          All services
        </Link>
        <Link to="/pricing" onClick={onNavigate} className="site-mega__all">
          Pricing
        </Link>
        <Link to="/free-audit" onClick={onNavigate} className="site-mega__all">
          Get a free marketing audit
        </Link>
      </div>
    </>
  );
}

/**
 * The academy dropdown: the menu's own links (with descriptions) beside the live list of subjects (course counts from
 * the public learning API, fetched once the panel is first opened) and a "start learning" call to action.
 */
function AcademyMega({ item, onNavigate }: { item: MenuItem; onNavigate: () => void }) {
  const summary = useLearningSummary();
  const subjects = (summary.data?.categories ?? []).filter((c) => c.courseCount > 0);
  const children = item.children ?? [];
  return (
    <div className="site-academy-mega">
      <div>
        <p className="site-mega__heading">
          <GraduationCap aria-hidden="true" className="site-mega__icon" />
          {item.label}
        </p>
        <ul className="site-nav__list">
          {item.url && item.url !== children[0]?.url && (
            <li>
              <MenuLink item={{ ...item, label: `${item.label} overview` }} className="site-nav__sublink" onClick={onNavigate} />
              <span className="site-nav__desc">How it works, learning paths and certificates.</span>
            </li>
          )}
          {children.map((child) => (
            <li key={child.label}>
              <MenuLink item={child} className="site-nav__sublink" onClick={onNavigate} />
              {child.description && <span className="site-nav__desc">{child.description}</span>}
            </li>
          ))}
        </ul>
      </div>
      {subjects.length > 0 && (
        <div>
          <p className="site-mega__heading">Subjects</p>
          <ul className="site-nav__list">
            {subjects.map((c) => (
              <li key={c.category}>
                <Link to={`/learn?category=${encodeURIComponent(c.category)}`} onClick={onNavigate} className="site-mega__link site-academy-mega__subject">
                  {c.label}
                  <span className="site-academy-mega__count">{c.courseCount}</span>
                </Link>
              </li>
            ))}
          </ul>
        </div>
      )}
      <Link to="/learn" onClick={onNavigate} className="site-academy-mega__cta">
        <span className="site-academy-mega__kicker">Free for everyone</span>
        <span className="site-academy-mega__title">Start learning free</span>
        <span className="site-academy-mega__text">Self-paced courses and a verifiable certificate for LinkedIn.</span>
        <ArrowRight aria-hidden="true" className="site-academy-mega__arrow" />
      </Link>
    </div>
  );
}

function MenuLink({ item, className, onClick }: { item: MenuItem; className?: string; onClick?: () => void }) {
  if (!item.url) return <span className={className}>{item.label}</span>;
  if (isInternalHref(item.url))
    return (
      <NavLink to={item.url} className={className} onClick={onClick} end={item.url === '/'}>
        {item.label}
      </NavLink>
    );
  return (
    <a href={item.url} className={className} target="_blank" rel="noopener noreferrer" onClick={onClick}>
      {item.label}
    </a>
  );
}

export function SiteHeader() {
  const { data: site } = useSite();
  const { status, user } = useAuth();
  const location = useLocation();
  const [openMenu, setOpenMenu] = useState<string | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const menu = site?.header.menu?.length ? site.header.menu : FALLBACK_MENU;
  const categories = site?.serviceMenu ?? [];
  const cta = site?.header.cta ?? { label: 'Start learning free', url: '/learn' };
  // Both pillars stay one click away: the configured call to action, plus the other pillar's as a quieter button.
  const secondaryCta =
    cta.url === '/free-audit' ? { label: 'Start learning free', url: '/learn' } : { label: 'Get a free audit', url: '/free-audit' };
  const signedIn = status === 'authenticated' && user;
  const dashboard = signedIn ? defaultLandingPath(user.permissions) : '/login';
  const closeAll = useCallback(() => setOpenMenu(null), []);

  useEffect(() => {
    setOpenMenu(null);
    setDrawerOpen(false);
  }, [location.pathname]);

  const isMega = (item: MenuItem) => item.url === '/services';
  const hasMenu = (item: MenuItem) => isMega(item) || (item.children?.length ?? 0) > 0;

  return (
    <header className="public-header site-header">
      <div className="container public-header__inner">
        <Link to="/" className="public-header__brand" aria-label="Optimize All home">
          <Logo size={30} title="" />
        </Link>
        <nav aria-label="Main" className="public-header__nav site-nav">
          <ul>
            {menu.map((item) => {
              return (
                <li key={item.label}>
                  {hasMenu(item) ? (
                    <Dropdown
                      label={item.label}
                      wide={isMega(item)}
                      academy={isAcademyItem(item)}
                      open={openMenu === item.label}
                      onOpenChange={(open) => setOpenMenu(open ? item.label : null)}
                    >
                      {isMega(item) ? (
                        <ServicesMega categories={categories} onNavigate={closeAll} />
                      ) : isAcademyItem(item) ? (
                        openMenu === item.label && <AcademyMega item={item} onNavigate={closeAll} />
                      ) : (
                        <ul className="site-nav__list">
                          {item.url && (
                            <li>
                              <MenuLink item={{ ...item, label: `${item.label} overview` }} className="site-nav__sublink" onClick={closeAll} />
                            </li>
                          )}
                          {item.children!.map((child) => (
                            <li key={child.label}>
                              <MenuLink item={child} className="site-nav__sublink" onClick={closeAll} />
                              {child.description && <span className="site-nav__desc">{child.description}</span>}
                            </li>
                          ))}
                        </ul>
                      )}
                    </Dropdown>
                  ) : (
                    <MenuLink item={item} className="public-header__link" />
                  )}
                </li>
              );
            })}
          </ul>
        </nav>
        <div className="public-header__actions">
          <ThemeToggle />
          <div className="public-header__auth">
            {signedIn ? (
              <ButtonLink to={dashboard} variant="ghost" size="sm">
                Go to dashboard
              </ButtonLink>
            ) : (
              <ButtonLink to="/login" variant="ghost" size="sm">
                Sign in
              </ButtonLink>
            )}
          </div>
          <ButtonLink to={secondaryCta.url} variant="secondary" size="sm" className="site-header__cta site-header__cta--secondary">
            {secondaryCta.label}
          </ButtonLink>
          {isInternalHref(cta.url) && (
            <ButtonLink to={cta.url} variant="highlight" size="sm" className="site-header__cta">
              {cta.label}
            </ButtonLink>
          )}
          <IconButton
            className="public-header__menu"
            label="Open menu"
            icon={<Menu />}
            aria-expanded={drawerOpen}
            onClick={() => setDrawerOpen(true)}
          />
        </div>
      </div>

      <Drawer open={drawerOpen} onClose={() => setDrawerOpen(false)} title="Menu" side="right" headerContent={<Logo size={26} title="" />}>
        <nav aria-label="Mobile" className="public-drawer site-drawer">
          <ul>
            {menu.map((item) =>
              hasMenu(item) ? (
                <li key={item.label}>
                  <details className="site-drawer__group" open={isAcademyItem(item) || undefined}>
                    <summary className="public-drawer__link">{item.label}</summary>
                    <ul>
                      {isMega(item) ? (
                        <>
                          <li>
                            <Link to="/services" className="site-drawer__sublink">
                              All services
                            </Link>
                          </li>
                          {categories.map((c) => (
                            <li key={c.slug}>
                              <span className="site-drawer__label">{c.name}</span>
                              <ul>
                                {c.services.map((s) => (
                                  <li key={s.slug}>
                                    <Link to={`/services/${s.slug}`} className="site-drawer__sublink">
                                      {s.name}
                                    </Link>
                                  </li>
                                ))}
                              </ul>
                            </li>
                          ))}
                        </>
                      ) : (
                        <>
                          {isAcademyItem(item) && item.url !== item.children![0]?.url && (
                            <li>
                              <MenuLink item={{ ...item, label: `${item.label} overview` }} className="site-drawer__sublink" />
                            </li>
                          )}
                          {item.children!.map((child) => (
                            <li key={child.label}>
                              <MenuLink item={child} className="site-drawer__sublink" />
                            </li>
                          ))}
                        </>
                      )}
                    </ul>
                  </details>
                </li>
              ) : (
                <li key={item.label}>
                  <MenuLink item={item} className="public-drawer__link" />
                </li>
              ),
            )}
          </ul>
          <div className="public-drawer__actions">
            <ButtonLink to="/learn" variant="highlight" fullWidth>
              Start learning free
            </ButtonLink>
            <ButtonLink to="/free-audit" variant="secondary" fullWidth>
              Get a free audit
            </ButtonLink>
            <ButtonLink to="/book-a-consultation" variant="ghost" fullWidth>
              Book a call
            </ButtonLink>
            {signedIn ? (
              <ButtonLink to={dashboard} variant="ghost" fullWidth>
                Go to dashboard
              </ButtonLink>
            ) : (
              <>
                <ButtonLink to="/login" variant="ghost" fullWidth>
                  Sign in
                </ButtonLink>
                <ButtonLink to="/login" variant="ghost" fullWidth>
                  Client login
                </ButtonLink>
              </>
            )}
          </div>
        </nav>
      </Drawer>
    </header>
  );
}
