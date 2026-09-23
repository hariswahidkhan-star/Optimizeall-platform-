import { TrendingUp } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { ClientSeoPage } from './ClientSeoPage';

const portal: PermissionRequirement = { anyOf: [Permissions.ClientPortal] };

/** Client portal area (seo). Paths are relative to /client. */
export const nav: PortalNavItem[] = [
  {
    to: 'seo',
    label: 'SEO & leads',
    icon: TrendingUp,
    description: 'Search visibility, rankings and landing-page leads.',
    requires: portal,
  },
];

export const routes: RouteObject[] = [{ path: 'seo', element: <ClientSeoPage />, handle: { requires: portal } }];
