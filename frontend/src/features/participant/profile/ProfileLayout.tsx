import clsx from 'clsx';
import { NavLink, Outlet } from 'react-router-dom';
import { PageHeader } from '@/components/ui/PageHeader';
import './ProfileLayout.css';

const SECTIONS = [
  { to: '', label: 'Profile', end: true },
  { to: 'payout-details', label: 'Payout details' },
  { to: 'notification-preferences', label: 'Notifications' },
  { to: 'security', label: 'Security' },
];

/** Profile area with section navigation (links, so each section has its own URL). */
export function ProfileLayout() {
  return (
    <>
      <PageHeader
        title="Profile"
        description="Your details, how you get paid, what we notify you about and your sign-in security."
      />
      <nav aria-label="Profile sections" className="profile-nav">
        <ul>
          {SECTIONS.map((section) => (
            <li key={section.to}>
              <NavLink
                to={section.to}
                end={section.end}
                className={({ isActive }) => clsx('profile-nav__link', isActive && 'is-active')}
              >
                {section.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
      <div className="profile-content">
        <Outlet />
      </div>
    </>
  );
}
