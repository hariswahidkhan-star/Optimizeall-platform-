import { BadgeCheck, CalendarClock, ShieldCheck } from 'lucide-react';
import { Link, Outlet } from 'react-router-dom';
import { BRAND_TAGLINE, Logo } from '@/components/brand/Logo';
import { ThemeToggle } from '@/components/ThemeToggle';
import { ImpersonationBanner } from './ImpersonationBanner';
import './AuthLayout.css';

const POINTS = [
  {
    icon: BadgeCheck,
    title: 'Brands you choose',
    text: 'Share company-approved content that fits your audience.',
  },
  {
    icon: ShieldCheck,
    title: 'Human review',
    text: 'Every post is checked by a person — clear feedback, fair decisions.',
  },
  {
    icon: CalendarClock,
    title: 'Biweekly payouts',
    text: 'Approved earnings are paid on a transparent schedule.',
  },
];

/** Split screen for sign-in flows: navy brand panel (large screens) and the form. */
export function AuthLayout() {
  return (
    <div className="auth-layout">
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <aside className="auth-brand" aria-label="About Optimize All">
        <div className="auth-brand__glow" aria-hidden="true" />
        <Link to="/" className="auth-brand__home" aria-label="Optimize All home">
          <Logo size={34} tone="onDark" title="" />
        </Link>
        <div className="auth-brand__copy">
          <p className="auth-brand__eyebrow">{BRAND_TAGLINE}</p>
          <p className="auth-brand__headline">Get paid to share brands you believe in.</p>
          <ul className="auth-brand__points">
            {POINTS.map(({ icon: Icon, title, text }) => (
              <li key={title}>
                <span className="auth-brand__icon" aria-hidden="true">
                  <Icon />
                </span>
                <span>
                  <strong>{title}</strong>
                  <span>{text}</span>
                </span>
              </li>
            ))}
          </ul>
        </div>
      </aside>
      <div className="auth-panel">
        <ImpersonationBanner />
        <header className="auth-panel__header">
          <Link to="/" className="auth-panel__logo" aria-label="Optimize All home">
            <Logo size={28} title="" />
          </Link>
          <ThemeToggle />
        </header>
        <main id="main" tabIndex={-1} className="auth-panel__main">
          <div className="auth-panel__content">
            <Outlet />
          </div>
        </main>
        <footer className="auth-panel__footer">
          <p>
            <span className="auth-panel__tagline">{BRAND_TAGLINE}</span> · © {new Date().getFullYear()}{' '}
            Optimize All
          </p>
        </footer>
      </div>
    </div>
  );
}
