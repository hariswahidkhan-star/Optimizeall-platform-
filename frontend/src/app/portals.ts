import { BadgeCheck, Landmark, Megaphone, ShieldHalf, Sparkles } from 'lucide-react';
import * as adminPortal from '@/features/admin/routes';
import * as managerPortal from '@/features/campaigns/routes';
import * as financePortal from '@/features/finance/routes';
import * as participantPortal from '@/features/participant/routes';
import * as reviewerPortal from '@/features/reviewer/routes';
import { meetsRequirement, Permissions } from '@/lib/auth/permissions';
import type { PortalDefinition, PortalId } from './portalTypes';

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
    requires: {
      anyOf: [
        Permissions.UsersView,
        Permissions.SettingsManage,
        Permissions.ContentManage,
        Permissions.AuditView,
      ],
    },
    landingRequires: { anyOf: [Permissions.SettingsManage, Permissions.ContentManage] },
    ...adminPortal,
  },
  {
    id: 'finance',
    label: 'Finance',
    description: 'Ledger, approvals and biweekly payouts.',
    basePath: '/finance',
    icon: Landmark,
    requires: { anyOf: [Permissions.PayoutsView, Permissions.LedgerView] },
    landingRequires: { anyOf: [Permissions.PayoutsView, Permissions.LedgerView] },
    ...financePortal,
  },
  {
    id: 'manager',
    label: 'Campaign manager',
    description: 'Campaigns, content, invitations and growth.',
    basePath: '/manage',
    icon: Megaphone,
    requires: { anyOf: [Permissions.CampaignsManage] },
    landingRequires: { anyOf: [Permissions.CampaignsManage] },
    ...managerPortal,
  },
  {
    id: 'reviewer',
    label: 'Reviewer',
    description: 'Review submissions, appeals and social accounts.',
    basePath: '/review',
    icon: BadgeCheck,
    requires: { anyOf: [Permissions.SubmissionsReview] },
    landingRequires: { anyOf: [Permissions.SubmissionsReview] },
    ...reviewerPortal,
  },
  {
    id: 'participant',
    label: 'Participant',
    description: 'Share campaigns, submit proof and get paid.',
    basePath: '/app',
    icon: Sparkles,
    requires: { anyOf: [Permissions.ParticipantPortal] },
    landingRequires: { anyOf: [Permissions.ParticipantPortal] },
    bottomNav: true,
    ...participantPortal,
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

/** True when the user may open `path` (public paths are always allowed). */
export function canOpenPath(permissions: readonly string[], path: string): boolean {
  const portal = portalForPath(path.split(/[?#]/)[0] ?? path);
  if (!portal) return true;
  if (!meetsRequirement(permissions, portal.requires)) return false;
  const rest = path.slice(portal.basePath.length).replace(/^\//, '').split(/[/?#]/)[0] ?? '';
  const item = portal.nav.find((n) => n.to === rest);
  return !item?.requires || meetsRequirement(permissions, item.requires);
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
