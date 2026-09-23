import { Menu } from 'lucide-react';
import { useState } from 'react';
import { Link, NavLink, Outlet } from 'react-router-dom';
import { BRAND_TAGLINE, Logo } from '@/components/brand/Logo';
import { ThemeToggle } from '@/components/ThemeToggle';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Drawer } from '@/components/ui/Drawer';
import { IconButton } from '@/components/ui/IconButton';
import { useAuth } from '@/lib/auth/useAuth';
import { defaultLandingPath } from '../portals';
import './PublicLayout.css';

const LINKS = [
  { to: '/#how-it-works', label: 'How it works' },
  { to: '/#rules', label: 'Rules & trust' },
  { to: '/faq', label: 'FAQ' },
];

/** Marketing pages: sticky header, content, footer with the brand tagline. */
export function PublicLayout() {
  const { status, user } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);
  const signedIn = status === 'authenticated' && user;
  const dashboard = signedIn ? defaultLandingPath(user.permissions) : '/app';

  const authActions = signedIn ? (
    <ButtonLink to={dashboard} size="sm">
      Go to dashboard
    </ButtonLink>
  ) : (
    <>
      <ButtonLink to="/login" variant="ghost" size="sm">
        Sign in
      </ButtonLink>
      <ButtonLink to="/register" variant="highlight" size="sm">
        Get started
      </ButtonLink>
    </>
  );

  return (
    <div className="public-layout">
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="public-header">
        <div className="container public-header__inner">
          <Link to="/" className="public-header__brand" aria-label="Optimize All home">
            <Logo size={30} title="" />
          </Link>
          <nav aria-label="Main" className="public-header__nav">
            <ul>
              {LINKS.map((link) => (
                <li key={link.to}>
                  <NavLink to={link.to} className="public-header__link">
                    {link.label}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
          <div className="public-header__actions">
            <ThemeToggle />
            <div className="public-header__auth">{authActions}</div>
            <IconButton
              className="public-header__menu"
              label="Open menu"
              icon={<Menu />}
              aria-expanded={menuOpen}
              onClick={() => setMenuOpen(true)}
            />
          </div>
        </div>
      </header>

      <Drawer
        open={menuOpen}
        onClose={() => setMenuOpen(false)}
        title="Menu"
        side="right"
        headerContent={<Logo size={26} title="" />}
      >
        <nav aria-label="Main" className="public-drawer">
          <ul>
            {LINKS.map((link) => (
              <li key={link.to}>
                <Link to={link.to} className="public-drawer__link" onClick={() => setMenuOpen(false)}>
                  {link.label}
                </Link>
              </li>
            ))}
          </ul>
          <div className="public-drawer__actions">
            {signedIn ? (
              <ButtonLink to={dashboard} fullWidth onClick={() => setMenuOpen(false)}>
                Go to dashboard
              </ButtonLink>
            ) : (
              <>
                <ButtonLink to="/register" variant="highlight" fullWidth onClick={() => setMenuOpen(false)}>
                  Get started
                </ButtonLink>
                <ButtonLink to="/login" variant="secondary" fullWidth onClick={() => setMenuOpen(false)}>
                  Sign in
                </ButtonLink>
              </>
            )}
          </div>
        </nav>
      </Drawer>

      <main id="main" tabIndex={-1} className="public-main">
        <Outlet />
      </main>

      <footer className="public-footer">
        <div className="container public-footer__inner">
          <div className="public-footer__brand">
            <Logo variant="stacked" size={72} tagline title={`Optimize All — ${BRAND_TAGLINE}`} />
          </div>
          <nav aria-label="Footer" className="public-footer__nav">
            <div>
              <h2 className="public-footer__heading">Participants</h2>
              <ul>
                <li>
                  <Link to="/register">Create an account</Link>
                </li>
                <li>
                  <Link to="/login">Sign in</Link>
                </li>
                <li>
                  <Link to="/#how-it-works">How it works</Link>
                </li>
              </ul>
            </div>
            <div>
              <h2 className="public-footer__heading">Help</h2>
              <ul>
                <li>
                  <Link to="/faq">FAQ</Link>
                </li>
                <li>
                  <Link to="/#rules">Rules & disclosure</Link>
                </li>
                <li>
                  <Link to="/forgot-password">Reset your password</Link>
                </li>
              </ul>
            </div>
          </nav>
        </div>
        <div className="container public-footer__legal">
          <p>© {new Date().getFullYear()} Optimize All. Paid posts are always disclosed.</p>
        </div>
      </footer>
    </div>
  );
}
