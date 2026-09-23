import { lazyPage } from '@/app/lazyPage';
import { Search } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';

const AuditResultsPage = lazyPage(() => import('./AuditResultsPage'), 'AuditResultsPage');
const OnPageAnalyzerPage = lazyPage(() => import('./OnPageAnalyzerPage'), 'OnPageAnalyzerPage');
const SeoSitePage = lazyPage(() => import('./SeoSitePage'), 'SeoSitePage');
const SeoSitesPage = lazyPage(() => import('./SeoSitesPage'), 'SeoSitesPage');

/** Every SEO page calls seo.manage APIs. */
const seo: PermissionRequirement = { anyOf: [Permissions.SeoManage] };

/** Agency portal area: SEO toolkit. Paths are relative to /agency. */
export const nav: PortalNavItem[] = [
  {
    to: 'seo',
    label: 'SEO',
    icon: Search,
    description: 'Site audits, rankings, backlinks, local SEO and content briefs.',
    requires: seo,
  },
];

export const routes: RouteObject[] = [
  { path: 'seo', element: <SeoSitesPage />, handle: { requires: seo } },
  { path: 'seo/analyzer', element: <OnPageAnalyzerPage />, handle: { requires: seo } },
  { path: 'seo/sites/:siteId', element: <SeoSitePage />, handle: { requires: seo } },
  { path: 'seo/audits/:auditId', element: <AuditResultsPage />, handle: { requires: seo } },
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [Permissions.SeoManage];
