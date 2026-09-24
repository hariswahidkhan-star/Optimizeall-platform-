import clsx from 'clsx';
import {
  Bell,
  ChevronsUpDown,
  LogOut,
  Menu,
  MoreHorizontal,
  PanelLeftClose,
  PanelLeftOpen,
  Search,
  UserRound,
} from 'lucide-react';
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom';
import { Logo } from '@/components/brand/Logo';
import { ThemeToggle } from '@/components/ThemeToggle';
import { Avatar } from '@/components/ui/Avatar';
import { Drawer } from '@/components/ui/Drawer';
import { DropdownMenu, type MenuEntry } from '@/components/ui/DropdownMenu';
import { IconButton } from '@/components/ui/IconButton';
import { meetsRequirement, Permissions } from '@/lib/auth/permissions';
import { safeStorage } from '@/lib/hooks/storage';
import { useAuth } from '@/lib/auth/useAuth';
import { groupNav } from '../navGroups';
import { PortalContext } from '../portalContext';
import { accessiblePortals, getPortal } from '../portals';
import type { PortalDefinition, PortalNavItem } from '../portalTypes';
import { EmailVerificationBanner } from './EmailVerificationBanner';
import { CommandPalette, useCommandPaletteShortcut } from './CommandPalette';
import { ImpersonationBanner } from './ImpersonationBanner';
import { NotificationBell } from './NotificationBell';
import { NotificationSettingsDialog } from './NotificationSettingsDialog';
import './PortalLayout.css';

/** Portals whose staff get the global search / command palette (Ctrl/Cmd+K). */
const PALETTE_PORTALS = new Set(['agency', 'admin']);

function itemPath(portal: PortalDefinition, item: PortalNavItem): string {
  return item.to ? `${portal.basePath}/${item.to}` : portal.basePath;
}

const SIDEBAR_KEY = 'oa.sidebar';

function PortalNav({
  portal,
  items,
  onNavigate,
  collapsed = false,
}: {
  portal: PortalDefinition;
  items: PortalNavItem[];
  onNavigate?: () => void;
  /** Icon rail: labels are visually hidden (still the links' names) and shown as a tooltip. */
  collapsed?: boolean;
}) {
  const idPrefix = useId();
  const sections = groupNav(portal.id, items);
  return (
    <nav aria-label={`${portal.label} navigation`} className="portal-nav">
      {sections.map((section, index) => {
        const headingId = `${idPrefix}-section-${index}`;
        return (
          <div key={`${section.label ?? ''}-${index}`} className="portal-nav__section">
            {section.label && (
              <p id={headingId} className="portal-nav__heading">
                {section.label}
              </p>
            )}
            <ul aria-labelledby={section.label ? headingId : undefined}>
              {section.items.map((item) => {
                const Icon = item.icon;
                return (
                  <li key={item.to}>
                    <NavLink
                      to={itemPath(portal, item)}
                      end={item.to === ''}
                      className={({ isActive }) => clsx('portal-nav__link', isActive && 'is-active')}
                      title={collapsed ? item.label : undefined}
                      onClick={onNavigate}
                    >
                      <Icon aria-hidden="true" />
                      <span className="portal-nav__label">{item.label}</span>
                    </NavLink>
                  </li>
                );
              })}
            </ul>
          </div>
        );
      })}
    </nav>
  );
}

/**
 * Shell for every role portal: sidebar (collapses into a drawer below lg), top bar with portal switcher, theme and
 * account menu, the unverified-email banner, and a bottom tab bar on phones for the participant portal.
 */
export function PortalLayout({ portal }: { portal: PortalDefinition }) {
  const { user, permissions, logout } = useAuth();
  const location = useLocation();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [notificationSettingsOpen, setNotificationSettingsOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);
  // Desktop only: the sidebar folds into an icon rail. Remembered per browser.
  const [collapsed, setCollapsed] = useState(() => safeStorage.get(SIDEBAR_KEY) === 'collapsed');
  const toggleSidebar = () =>
    setCollapsed((was) => {
      safeStorage.set(SIDEBAR_KEY, was ? 'expanded' : 'collapsed');
      return !was;
    });
  const hasPalette = PALETTE_PORTALS.has(portal.id);
  const openPalette = useCallback(() => setPaletteOpen(true), []);
  useCommandPaletteShortcut(hasPalette, openPalette);
  const mainRef = useRef<HTMLElement>(null);
  // The path focus was last moved for. Comparing paths (not a "first render" flag) keeps a fresh page load — and React
  // StrictMode's effect replay in development — from stealing focus from the skip link.
  const focusedPath = useRef(location.pathname);

  const items = portal.nav.filter((item) => !item.requires || meetsRequirement(permissions, item.requires));
  const available = accessiblePortals(permissions);
  const mobileItems = portal.bottomNav ? items.filter((item) => item.mobilePrimary) : [];

  useEffect(() => {
    document.title = `${portal.label} · Optimize All`;
  }, [portal.label]);

  // Move focus to the page content after client-side navigation so screen readers announce the new page.
  useEffect(() => {
    setDrawerOpen(false);
    if (focusedPath.current === location.pathname) return;
    focusedPath.current = location.pathname;
    mainRef.current?.focus({ preventScroll: true });
    window.scrollTo({ top: 0 });
  }, [location.pathname]);

  const participant = getPortal('participant');
  const accountItems: MenuEntry[] = [
    {
      type: 'label',
      id: 'who',
      label: user?.email ?? '',
    },
    ...(meetsRequirement(permissions, { anyOf: [Permissions.ParticipantPortal] })
      ? [
          {
            id: 'profile',
            label: 'Profile & security',
            icon: <UserRound />,
            to: `${participant.basePath}/profile`,
          },
          {
            id: 'notification-settings',
            label: 'Notification settings',
            icon: <Bell />,
            to: `${participant.basePath}/profile/notification-preferences`,
          },
        ]
      : [
          {
            id: 'notification-settings',
            label: 'Notification settings',
            icon: <Bell />,
            onSelect: () => setNotificationSettingsOpen(true),
          },
        ]),
    { type: 'separator', id: 'sep' },
    { id: 'logout', label: 'Sign out', icon: <LogOut />, onSelect: () => void logout() },
  ];

  const portalItems: MenuEntry[] = [
    { type: 'label', id: 'portals-label', label: 'Switch portal' },
    ...available.map((p) => {
      const Icon = p.icon;
      return {
        id: p.id,
        label: p.label,
        description: p.description,
        icon: <Icon />,
        to: p.basePath,
        current: p.id === portal.id,
      };
    }),
  ];

  const PortalIcon = portal.icon;

  return (
    <PortalContext.Provider value={portal}>
      <div
        className={clsx(
          'portal-layout',
          mobileItems.length > 0 && 'portal-layout--bottom-nav',
          collapsed && 'portal-layout--collapsed',
        )}
      >
        <a className="skip-link" href="#main">
          Skip to content
        </a>

        <aside className="portal-sidebar" id="portal-sidebar">
          <div className="portal-sidebar__head">
            <Link to={portal.basePath} className="portal-sidebar__brand" aria-label={`${portal.label} home`}>
              <Logo size={26} title="" className="portal-sidebar__logo" />
              <Logo variant="mark" size={26} title="" className="portal-sidebar__mark" />
            </Link>
          </div>
          <p className="portal-sidebar__portal">
            <span className="portal-sidebar__portal-icon" aria-hidden="true">
              <PortalIcon />
            </span>
            <span className="portal-sidebar__portal-label">{portal.label}</span>
          </p>
          <PortalNav portal={portal} items={items} collapsed={collapsed} />
          <div className="portal-sidebar__foot">
            <button
              type="button"
              className="portal-sidebar__collapse"
              aria-controls="portal-sidebar"
              aria-expanded={!collapsed}
              title={collapsed ? 'Expand sidebar' : undefined}
              onClick={toggleSidebar}
            >
              {collapsed ? <PanelLeftOpen aria-hidden="true" /> : <PanelLeftClose aria-hidden="true" />}
              <span className="portal-nav__label">{collapsed ? 'Expand sidebar' : 'Collapse sidebar'}</span>
            </button>
          </div>
        </aside>

        <Drawer
          open={drawerOpen}
          onClose={() => setDrawerOpen(false)}
          title={`${portal.label} menu`}
          headerContent={<Logo size={26} title="" />}
        >
          <p className="portal-sidebar__portal">
            <span className="portal-sidebar__portal-icon" aria-hidden="true">
              <PortalIcon />
            </span>
            <span className="portal-sidebar__portal-label">{portal.label}</span>
          </p>
          <PortalNav portal={portal} items={items} onNavigate={() => setDrawerOpen(false)} />
        </Drawer>

        <div className="portal-main-col">
          <ImpersonationBanner />
          <header className="portal-topbar">
            <IconButton
              className="portal-topbar__menu"
              label="Open navigation"
              icon={<Menu />}
              aria-expanded={drawerOpen}
              onClick={() => setDrawerOpen(true)}
            />
            <Link to={portal.basePath} className="portal-topbar__logo" aria-label={`${portal.label} home`}>
              <Logo variant="mark" size={28} title="" />
            </Link>
            {hasPalette && (
              <button
                type="button"
                className="portal-search"
                aria-keyshortcuts="Control+K Meta+K"
                aria-haspopup="dialog"
                onClick={openPalette}
              >
                <Search aria-hidden="true" />
                <span className="portal-search__label">Search</span>
                <kbd aria-hidden="true">Ctrl K</kbd>
              </button>
            )}
            <div className="portal-topbar__spacer" />
            {available.length > 1 && (
              <DropdownMenu
                align="end"
                items={portalItems}
                trigger={
                  <button
                    type="button"
                    className="portal-switcher"
                    aria-label={`Portal: ${portal.label}. Switch portal`}
                  >
                    <PortalIcon aria-hidden="true" />
                    <span className="portal-switcher__label">{portal.label}</span>
                    <ChevronsUpDown aria-hidden="true" className="portal-switcher__chevron" />
                  </button>
                }
              />
            )}
            <ThemeToggle />
            {/* Participants have their own Notifications page; every other portal gets the inbox in the top bar. */}
            {user && portal.id !== 'participant' && <NotificationBell />}
            {user && (
              <DropdownMenu
                align="end"
                items={accountItems}
                trigger={
                  <button
                    type="button"
                    className="portal-account"
                    aria-label={`Account menu for ${user.displayName}`}
                  >
                    <Avatar name={user.displayName} size={28} decorative />
                    <span className="portal-account__name">{user.displayName}</span>
                  </button>
                }
              />
            )}
          </header>

          {user && !user.emailVerified && <EmailVerificationBanner email={user.email} />}

          <main id="main" ref={mainRef} tabIndex={-1} className="portal-main">
            <Outlet />
          </main>
        </div>

        {hasPalette && <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />}

        <NotificationSettingsDialog
          open={notificationSettingsOpen}
          onClose={() => setNotificationSettingsOpen(false)}
        />

        {mobileItems.length > 0 && (
          <nav aria-label="Quick navigation" className="portal-bottom-nav">
            <ul>
              {mobileItems.map((item) => {
                const Icon = item.icon;
                return (
                  <li key={item.to}>
                    <NavLink
                      to={itemPath(portal, item)}
                      end={item.to === ''}
                      className={({ isActive }) => clsx('portal-bottom-nav__link', isActive && 'is-active')}
                    >
                      <Icon aria-hidden="true" />
                      <span>{item.shortLabel ?? item.label}</span>
                    </NavLink>
                  </li>
                );
              })}
              <li>
                <button
                  type="button"
                  className="portal-bottom-nav__link"
                  aria-expanded={drawerOpen}
                  onClick={() => setDrawerOpen(true)}
                >
                  <MoreHorizontal aria-hidden="true" />
                  <span>More</span>
                </button>
              </li>
            </ul>
          </nav>
        )}
      </div>
    </PortalContext.Provider>
  );
}
