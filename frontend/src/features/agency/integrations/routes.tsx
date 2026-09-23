import { PlugZap } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { IntegrationsPage } from './IntegrationsPage';

const integrations: PermissionRequirement = { anyOf: [Permissions.IntegrationsManage] };

/** Agency portal area: Integrations (third-party credentials). Paths are relative to /agency. */
export const nav: PortalNavItem[] = [
  {
    to: 'integrations',
    label: 'Integrations',
    icon: PlugZap,
    description: 'Connect social, ads, messaging, email and SEO providers.',
    requires: integrations,
  },
];

export const routes: RouteObject[] = [{ path: 'integrations', element: <IntegrationsPage />, handle: { requires: integrations } }];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [Permissions.IntegrationsManage];
