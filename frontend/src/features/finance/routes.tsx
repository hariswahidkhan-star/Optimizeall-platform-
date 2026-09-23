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
import { Permissions } from '@/lib/auth/permissions';
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

/** Finance portal (/finance). Paths are relative to the portal base. */
export const nav: PortalNavItem[] = [
  { to: '', label: 'Overview', icon: LayoutDashboard },
  {
    to: 'batches',
    label: 'Payout batches',
    icon: Layers,
    description: 'Prepare, finalize and record biweekly payout batches.',
    requires: { anyOf: [Permissions.PayoutsView] },
  },
  {
    to: 'ledger',
    label: 'Ledger',
    icon: BookOpenText,
    description: 'Every earning, adjustment and reversal.',
    requires: { anyOf: [Permissions.LedgerView] },
  },
  {
    to: 'approvals',
    label: 'Pending approvals',
    icon: ClipboardCheck,
    description: 'Bonuses and adjustments waiting for approval.',
    requires: { anyOf: [Permissions.RewardsApproveBonus, Permissions.LedgerAdjust] },
  },
  {
    to: 'holds',
    label: 'Holds',
    icon: PauseCircle,
    description: 'Participants whose payouts are on hold, and why.',
    requires: { anyOf: [Permissions.PayoutsHold, Permissions.PayoutsView] },
  },
  {
    to: 'exchange-rates',
    label: 'Exchange rates',
    icon: Coins,
    description: 'Rates used to convert earnings to the settlement currency.',
    requires: { anyOf: [Permissions.PayoutsView, Permissions.PayoutSettingsEdit] },
  },
  {
    to: 'schedule',
    label: 'Payout schedule',
    icon: CalendarClock,
    description: 'Payout periods, cut-offs and minimum thresholds.',
    requires: { anyOf: [Permissions.PayoutsView, Permissions.PayoutSettingsEdit] },
  },
];

export const routes: RouteObject[] = [
  { index: true, element: <OverviewPage /> },
  {
    path: 'batches',
    children: [
      { index: true, element: <BatchesPage /> },
      { path: ':batchId', element: <BatchReviewPage /> },
      { path: ':batchId/reconciliation', element: <BatchReviewPage tab="reconciliation" /> },
    ],
  },
  {
    path: 'ledger',
    children: [
      { index: true, element: <LedgerPage /> },
      { path: 'users/:userId', element: <UserBalancePage /> },
    ],
  },
  { path: 'approvals', element: <ApprovalsPage /> },
  { path: 'holds', element: <HoldsPage /> },
  { path: 'exchange-rates', element: <ExchangeRatesPage /> },
  { path: 'schedule', element: <SchedulePage /> },
];
