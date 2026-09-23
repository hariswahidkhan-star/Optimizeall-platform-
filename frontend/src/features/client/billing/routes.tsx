import { lazyPage } from '@/app/lazyPage';
import { Receipt } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { Permissions } from '@/lib/auth/permissions';

const ClientBillingPage = lazyPage(() => import('./ClientBillingPages'), 'ClientBillingPage');
const ClientInvoicePage = lazyPage(() => import('./ClientBillingPages'), 'ClientInvoicePage');
const ClientProposalPage = lazyPage(() => import('./ClientBillingPages'), 'ClientProposalPage');

/**
 * Client portal area (billing). Paths are relative to /client. Every client user holds `client.portal`; the API further
 * limits billing to members with the Billing or Owner duty (others see a friendly explanation).
 */
const requires = { anyOf: [Permissions.ClientPortal] };

export const nav: PortalNavItem[] = [
  {
    to: 'billing',
    label: 'Billing',
    icon: Receipt,
    description: 'Invoices, proposals to review, contracts and your statement.',
    requires,
  },
];

export const routes: RouteObject[] = [
  { path: 'billing', element: <ClientBillingPage />, handle: { requires } },
  { path: 'billing/invoices/:invoiceId', element: <ClientInvoicePage />, handle: { requires } },
  { path: 'billing/proposals/:proposalId', element: <ClientProposalPage />, handle: { requires } },
];
