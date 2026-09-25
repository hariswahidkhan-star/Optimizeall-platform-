import { lazyPage } from '@/app/lazyPage';
import {
  BookOpenText,
  CalendarClock,
  ClipboardCheck,
  Coins,
  Layers,
  LayoutDashboard,
  PauseCircle,
  TicketPercent,
  Wallet,
} from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { BatchReviewPage } from './batch/BatchReviewPage';
import './finance.css';

const LedgerPage = lazyPage(() => import('./ledger/LedgerPage'), 'LedgerPage');
const UserBalancePage = lazyPage(() => import('./ledger/UserBalancePage'), 'UserBalancePage');
const ApprovalsPage = lazyPage(() => import('./pages/ApprovalsPage'), 'ApprovalsPage');
const BatchesPage = lazyPage(() => import('./pages/BatchesPage'), 'BatchesPage');
const ExchangeRatesPage = lazyPage(() => import('./pages/ExchangeRatesPage'), 'ExchangeRatesPage');
const HoldsPage = lazyPage(() => import('./pages/HoldsPage'), 'HoldsPage');
const OverviewPage = lazyPage(() => import('./pages/OverviewPage'), 'OverviewPage');
const PaymentsPage = lazyPage(() => import('./payments/PaymentsPage'), 'PaymentsPage');
const SchedulePage = lazyPage(() => import('./pages/SchedulePage'), 'SchedulePage');
const FinanceCodeSalesPage = lazyPage(() => import('../codes/staff/CodeSalesQueuePage'), 'FinanceCodeSalesPage');
const FinanceCodeSaleDetailPage = lazyPage(
  () => import('../codes/staff/CodeSalesQueuePage'),
  'FinanceCodeSaleDetailPage',
);

/**
 * Finance portal (/finance). Paths are relative to the portal base. Every section declares the permission of the API
 * it reads (route `handle.requires` + nav item). Campaign managers (rewards.approve_bonus) can open the portal for
 * Pending approvals only.
 */
const requires = {
  batches: { anyOf: [Permissions.PayoutsView] },
  // Payments hub: outgoing payouts (payouts.view) and/or incoming client payments. billing.view alone (account managers,
  // sales reps) is served by Agency → Billing, so the finance portal opens the hub for billing managers only.
  payments: { anyOf: [Permissions.PayoutsView, Permissions.BillingManage] },
  ledger: { anyOf: [Permissions.LedgerView] },
  approvals: { anyOf: [Permissions.RewardsApproveBonus] },
  holds: { anyOf: [Permissions.PayoutsHold] },
  exchangeRates: { anyOf: [Permissions.PayoutsView] },
  schedule: { anyOf: [Permissions.PayoutsView] },
  // Discount-code sales: refunds reverse commissions (sales.reverse).
  codeSales: { anyOf: [Permissions.SalesReverse] },
} satisfies Record<string, PermissionRequirement>;

/** Portal entry: any permission that opens one of its sections. */
export const portalRequires: PermissionRequirement = {
  anyOf: [...new Set(Object.values(requires).flatMap((r) => r.anyOf))],
};

export const nav: PortalNavItem[] = [
  { to: '', label: 'Overview', icon: LayoutDashboard },
  {
    to: 'payments',
    label: 'Payments',
    icon: Wallet,
    description: 'Every incoming and outgoing payment: record, correct, reverse, mark paid, send reminders.',
    requires: requires.payments,
  },
  {
    to: 'batches',
    label: 'Payout batches',
    icon: Layers,
    description: 'Prepare, finalize and record biweekly payout batches.',
    requires: requires.batches,
  },
  {
    to: 'ledger',
    label: 'Ledger',
    icon: BookOpenText,
    description: 'Every earning, adjustment and reversal.',
    requires: requires.ledger,
  },
  {
    to: 'approvals',
    label: 'Pending approvals',
    icon: ClipboardCheck,
    description: 'Bonuses and adjustments waiting for approval.',
    requires: requires.approvals,
  },
  {
    to: 'code-sales',
    label: 'Code sales',
    icon: TicketPercent,
    description: 'Discount-code commissions; mark refunded orders to reverse them.',
    requires: requires.codeSales,
  },
  {
    to: 'holds',
    label: 'Holds',
    icon: PauseCircle,
    description: 'Participants whose payouts are on hold, and why.',
    requires: requires.holds,
  },
  {
    to: 'exchange-rates',
    label: 'Exchange rates',
    icon: Coins,
    description: 'Rates used to convert earnings to the settlement currency.',
    requires: requires.exchangeRates,
  },
  {
    to: 'schedule',
    label: 'Payout schedule',
    icon: CalendarClock,
    description: 'Payout periods, cut-offs and minimum thresholds.',
    requires: requires.schedule,
  },
];

export const routes: RouteObject[] = [
  { index: true, element: <OverviewPage /> },
  { path: 'payments', handle: { requires: requires.payments }, element: <PaymentsPage /> },
  {
    path: 'batches',
    handle: { requires: requires.batches },
    children: [
      { index: true, element: <BatchesPage /> },
      { path: ':batchId', element: <BatchReviewPage /> },
      { path: ':batchId/reconciliation', element: <BatchReviewPage tab="reconciliation" /> },
    ],
  },
  {
    path: 'ledger',
    handle: { requires: requires.ledger },
    children: [
      { index: true, element: <LedgerPage /> },
      { path: 'users/:userId', element: <UserBalancePage /> },
    ],
  },
  { path: 'approvals', handle: { requires: requires.approvals }, element: <ApprovalsPage /> },
  { path: 'holds', handle: { requires: requires.holds }, element: <HoldsPage /> },
  {
    path: 'code-sales',
    handle: { requires: requires.codeSales },
    children: [
      { index: true, element: <FinanceCodeSalesPage /> },
      { path: ':saleId', element: <FinanceCodeSaleDetailPage /> },
    ],
  },
  { path: 'exchange-rates', handle: { requires: requires.exchangeRates }, element: <ExchangeRatesPage /> },
  { path: 'schedule', handle: { requires: requires.schedule }, element: <SchedulePage /> },
];
