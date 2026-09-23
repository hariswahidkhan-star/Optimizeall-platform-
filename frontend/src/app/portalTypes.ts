import type { LucideIcon } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PermissionRequirement } from '@/lib/auth/permissions';

export type PortalId = 'participant' | 'reviewer' | 'manager' | 'finance' | 'admin';

/** One sidebar / bottom-bar destination inside a portal. */
export interface PortalNavItem {
  /** Path relative to the portal base ('' = the portal home). */
  to: string;
  label: string;
  /** Compact label for the mobile bottom tab bar. */
  shortLabel?: string;
  icon: LucideIcon;
  /** One line shown on the portal overview cards. */
  description?: string;
  /** Hide the item (and guard its route) unless the user meets this requirement. */
  requires?: PermissionRequirement;
  /** Shown in the mobile bottom tab bar (participant portal). */
  mobilePrimary?: boolean;
}

/** Exported by each `features/<portal>/routes.tsx` so portal owners control their own nav and routes. */
export interface PortalModule {
  nav: PortalNavItem[];
  /** Child routes, relative to the portal base path. */
  routes: RouteObject[];
}

export interface PortalDefinition extends PortalModule {
  id: PortalId;
  label: string;
  /** Short description for the portal switcher. */
  description: string;
  basePath: string;
  icon: LucideIcon;
  /** Access to the portal at all. */
  requires: PermissionRequirement;
  /**
   * Stricter requirement used to pick the post-login landing portal, so e.g. a reviewer who can view users is not
   * sent to the admin portal by default.
   */
  landingRequires: PermissionRequirement;
  /** Use the mobile bottom tab bar (participant). */
  bottomNav?: boolean;
}
