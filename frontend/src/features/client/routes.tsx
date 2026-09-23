import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import * as billing from './billing/routes';
import * as core from './core/routes';
import * as email from './email/routes';
import * as seo from './seo/routes';
import * as social from './social/routes';

/** Client portal (/client): what a client organization sees. The core area owns the portal home (index route). */
const areas = [core, social, email, seo, billing];

export const nav: PortalNavItem[] = areas.flatMap((a) => a.nav);

export const routes: RouteObject[] = areas.flatMap((a) => a.routes);

export const portalRequires: PermissionRequirement = { anyOf: [Permissions.ClientPortal] };
