import {
  BookOpenText,
  CalendarClock,
  ClipboardCheck,
  Coins,
  Layers,
  LayoutDashboard,
  PauseCircle,
} from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { BatchReviewPage } from './batch/BatchReviewPage';
import { LedgerPage } from './ledger/LedgerPage';
import { UserBalancePage } from './ledger/UserBalancePage';
import { ApprovalsPage } from './pages/ApprovalsPage';
import { BatchesPage } from './pages/BatchesPage';
import { ExchangeRatesPage } from './pages/ExchangeRatesPage';
import { HoldsPage } from './pages/HoldsPage';
import { OverviewPage } from './pages/OverviewPage';
import { SchedulePage } from './pages/SchedulePage';
import './finance.css';

/**
 * Finance portal (/finance). Paths are relative to the portal base. Every section declares the permission of the API
 * it reads (route `handle.requires` + nav item). Campaign managers (rewards.approve_bonus) can open the portal for
 * Pending approvals only.
 */
const requires = {
  batches: { anyOf: [Permissions.PayoutsView] },
  ledger: { anyOf: [Permissions.LedgerView] },
  approvals: { anyOf: [Permissions.RewardsApproveBonus] },
  holds: { anyOf: [Permissions.PayoutsHold] },
  exchangeRates: { anyOf: [Permissions.PayoutsView] },
  schedule: { anyOf: [Permissions.PayoutsView] },
} satisfies Record<string, PermissionRequirement>;

/** Portal entry: any permission that opens one of its sections. */
export const portalRequires: PermissionRequirement = {
  anyOf: [...new Set(Object.values(requires).flatMap((r) => r.anyOf))],
};

export const nav: PortalNavItem[] = [
  { to: '', label: 'Overview', icon: LayoutDashboard },
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
  { path: 'exchange-rates', handle: { requires: requires.exchangeRates }, element: <ExchangeRatesPage /> },
  { path: 'schedule', handle: { requires: requires.schedule }, element: <SchedulePage /> },
];
