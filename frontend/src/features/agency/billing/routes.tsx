import { lazyPage } from '@/app/lazyPage';
import { FileText, Wallet } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';

const BillingOverviewPage = lazyPage(() => import('./pages/BillingOverviewPage'), 'BillingOverviewPage');
const BillingSettingsPage = lazyPage(() => import('./pages/BillingSettingsPage'), 'BillingSettingsPage');
const InvoiceDetailPage = lazyPage(() => import('./pages/InvoiceDetailPage'), 'InvoiceDetailPage');
const InvoiceEditorPage = lazyPage(() => import('./pages/InvoiceEditorPage'), 'InvoiceEditorPage');
const InvoicesPage = lazyPage(() => import('./pages/InvoicesPage'), 'InvoicesPage');
const PaymentsPage = lazyPage(() => import('./pages/PaymentsPage'), 'PaymentsPage');
const ReportsPage = lazyPage(() => import('./pages/ReportsPage'), 'ReportsPage');

/**
 * Agency portal area: client billing (invoices, payments, credit notes, reports, settings). Paths are relative to
 * /agency. Every page reads `billing.view` APIs; actions inside need `billing.manage` / `billing.settings` (buttons are
 * hidden without them and the API enforces them).
 */
const requires: PermissionRequirement = { anyOf: [Permissions.BillingView] };

export const nav: PortalNavItem[] = [
  {
    to: 'billing',
    label: 'Billing',
    icon: Wallet,
    description: 'Receivables, overdue invoices, MRR and collections.',
    requires,
  },
  {
    to: 'billing/invoices',
    label: 'Invoices',
    icon: FileText,
    description: 'Draft, issue, send and collect client invoices.',
    requires,
  },
];

export const routes: RouteObject[] = [
  { path: 'billing', element: <BillingOverviewPage />, handle: { requires } },
  { path: 'billing/invoices', element: <InvoicesPage />, handle: { requires } },
  { path: 'billing/invoices/new', element: <InvoiceEditorPage />, handle: { requires } },
  { path: 'billing/invoices/:invoiceId', element: <InvoiceDetailPage />, handle: { requires } },
  { path: 'billing/invoices/:invoiceId/edit', element: <InvoiceEditorPage />, handle: { requires } },
  { path: 'billing/payments', element: <PaymentsPage />, handle: { requires } },
  { path: 'billing/reports', element: <ReportsPage />, handle: { requires } },
  { path: 'billing/settings', element: <BillingSettingsPage />, handle: { requires } },
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [Permissions.BillingView];
