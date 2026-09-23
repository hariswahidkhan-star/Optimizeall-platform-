import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import * as ads from './ads/routes';
import * as billing from './billing/routes';
import * as crm from './crm/routes';
import * as delivery from './delivery/routes';
import * as email from './email/routes';
import * as integrations from './integrations/routes';
import * as pages from './pages/routes';
import * as seo from './seo/routes';
import * as social from './social/routes';
import * as website from './website/routes';

/**
 * Agency portal (/agency): the marketing agency's operating system. Each area lives in its own folder and exports
 * `nav`, `routes` and `opensWith`; the delivery area owns the portal home (index route).
 */
const areas = [delivery, crm, billing, email, social, ads, seo, pages, website, integrations];

export const nav: PortalNavItem[] = areas.flatMap((a) => a.nav);

export const routes: RouteObject[] = areas.flatMap((a) => a.routes);

/** Agency staff permissions; each area may add more through `opensWith`. */
const agencyStaff: readonly string[] = [
  Permissions.ClientsView,
  Permissions.CrmView,
  Permissions.ProjectsView,
  Permissions.BillingView,
  Permissions.EmailManage,
  Permissions.SocialManage,
  Permissions.AdsManage,
  Permissions.SeoManage,
  Permissions.FormsManage,
  Permissions.SiteManage,
  Permissions.BlogWrite,
  Permissions.CareersManage,
  Permissions.IntegrationsManage,
];

export const portalRequires: PermissionRequirement = {
  anyOf: [...new Set([...agencyStaff, ...areas.flatMap((a) => a.opensWith)])],
};
