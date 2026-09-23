import { BadgeCheck, Briefcase, Building2, Landmark, Megaphone, ShieldHalf, Sparkles } from 'lucide-react';
import * as agencyPortal from '@/features/agency/routes';
import * as clientPortal from '@/features/client/routes';
import * as adminPortal from '@/features/admin/routes';
import * as managerPortal from '@/features/campaigns/routes';
import * as financePortal from '@/features/finance/routes';
import * as participantPortal from '@/features/participant/routes';
import * as reviewerPortal from '@/features/reviewer/routes';
import { matchRoutes } from 'react-router-dom';
import { meetsRequirement, type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import type { PortalDefinition, PortalId, PortalRouteHandle } from './portalTypes';

/**
 * Portal registry, in landing priority order (admin, finance, manage, review, participant). Each portal's nav and
 * routes live in its feature folder.
 */
export const portals: PortalDefinition[] = [
  {
    id: 'admin',
    label: 'Admin',
    description: 'People, platform settings, content, support and audit.',
    basePath: '/admin',
    icon: ShieldHalf,
    // Every permission that opens one of its sections (users, settings, content, categories, support, audit, jobs,
    // analytics), so e.g. support staff reach Support tickets.
    requires: adminPortal.portalRequires,
    landingRequires: { anyOf: [Permissions.SettingsManage, Permissions.ContentManage] },
    nav: adminPortal.nav,
    routes: adminPortal.routes,
  },
  {
    id: 'agency',
    label: 'Agency',
    description: 'Clients, sales, delivery and every marketing service the agency runs.',
    basePath: '/agency',
    icon: Briefcase,
    requires: agencyPortal.portalRequires,
    landingRequires: agencyPortal.portalRequires,
    nav: agencyPortal.nav,
    routes: agencyPortal.routes,
  },
  {
    id: 'finance',
    label: 'Finance',
    description: 'Ledger, approvals and biweekly payouts.',
    basePath: '/finance',
    icon: Landmark,
    // Includes rewards.approve_bonus (Pending approvals) and payouts.hold (Holds); landing stays finance staff only.
    requires: financePortal.portalRequires,
    landingRequires: { anyOf: [Permissions.PayoutsView, Permissions.LedgerView] },
    nav: financePortal.nav,
    routes: financePortal.routes,
  },
  {
    id: 'manager',
    label: 'Campaign manager',
    description: 'Campaigns, content, invitations and growth.',
    basePath: '/manage',
    icon: Megaphone,
    requires: managerPortal.portalRequires,
    landingRequires: { anyOf: [Permissions.CampaignsManage] },
    nav: managerPortal.nav,
    routes: managerPortal.routes,
  },
  {
    id: 'reviewer',
    label: 'Reviewer',
    description: 'Review submissions, appeals and social accounts.',
    basePath: '/review',
    icon: BadgeCheck,
    requires: reviewerPortal.portalRequires,
    landingRequires: { anyOf: [Permissions.SubmissionsReview] },
    nav: reviewerPortal.nav,
    routes: reviewerPortal.routes,
  },
  {
    id: 'participant',
    label: 'Participant',
    description: 'Share campaigns, submit proof and get paid.',
    basePath: '/app',
    icon: Sparkles,
    requires: participantPortal.portalRequires,
    landingRequires: { anyOf: [Permissions.ParticipantPortal] },
    bottomNav: true,
    nav: participantPortal.nav,
    routes: participantPortal.routes,
  },
  {
    id: 'client',
    label: 'Client portal',
    description: 'Your projects, approvals, reports and invoices.',
    basePath: '/client',
    icon: Building2,
    requires: clientPortal.portalRequires,
    landingRequires: clientPortal.portalRequires,
    nav: clientPortal.nav,
    routes: clientPortal.routes,
  },
];

export function getPortal(id: PortalId): PortalDefinition {
  const portal = portals.find((p) => p.id === id);
  if (!portal) throw new Error(`Unknown portal ${id}`);
  return portal;
}

export function accessiblePortals(permissions: readonly string[]): PortalDefinition[] {
  return portals.filter((p) => meetsRequirement(permissions, p.requires));
}

/** The portal that owns a path (longest base path match), if any. */
export function portalForPath(pathname: string): PortalDefinition | undefined {
  return portals.find((p) => pathname === p.basePath || pathname.startsWith(`${p.basePath}/`));
}

/** The `handle.requires` of every route matching `path` inside a portal (parents first). */
export function routeRequirements(portal: PortalDefinition, pathname: string): PermissionRequirement[] {
  const rest = pathname.slice(portal.basePath.length) || '/';
  return (matchRoutes(portal.routes, rest) ?? [])
    .map((m) => (m.route.handle as PortalRouteHandle | undefined)?.requires)
    .filter((r): r is PermissionRequirement => !!r);
}

/** True when the user may open `path` (public paths are always allowed). */
export function canOpenPath(permissions: readonly string[], path: string): boolean {
  const pathname = path.split(/[?#]/)[0] ?? path;
  const portal = portalForPath(pathname);
  if (!portal) return true;
  if (!meetsRequirement(permissions, portal.requires)) return false;
  return routeRequirements(portal, pathname).every((r) => meetsRequirement(permissions, r));
}

/**
 * Where to go after sign-in: the `next` path when the user may open it, otherwise the first portal (in priority
 * order) whose landing requirement they meet, then any accessible portal, then the public home page.
 */
export function defaultLandingPath(permissions: readonly string[], next?: string | null): string {
  const isAuthPage = !!next && /^\/(login|register|check-email|forgot-password)(\/|\?|$)/.test(next);
  if (next && next !== '/' && !isAuthPage && canOpenPath(permissions, next)) return next;
  const landing =
    portals.find((p) => meetsRequirement(permissions, p.landingRequires)) ??
    accessiblePortals(permissions)[0];
  return landing?.basePath ?? '/';
}
