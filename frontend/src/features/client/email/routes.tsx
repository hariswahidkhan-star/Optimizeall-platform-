import { Mail } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { ClientCampaignPage, ClientEmailPage } from './ClientEmailPages';

/** Client portal area (email). Paths are relative to /client. */
const requires: PermissionRequirement = { anyOf: [Permissions.ClientPortal] };

export const nav: PortalNavItem[] = [
  {
    to: 'email',
    label: 'Email marketing',
    shortLabel: 'Email',
    icon: Mail,
    description: 'Campaign results and approvals.',
    requires,
  },
];

export const routes: RouteObject[] = [
  {
    path: 'email',
    handle: { requires },
    children: [
      { index: true, element: <ClientEmailPage /> },
      { path: 'campaigns/:id', element: <ClientCampaignPage /> },
    ],
  },
];
