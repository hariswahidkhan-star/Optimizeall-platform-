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
import { PendingSection } from '@/components/PendingSection';
import { PortalOverview } from '@/components/PortalOverview';
import { Permissions } from '@/lib/auth/permissions';

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
    description: 'Payout items on hold and why.',
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
  { index: true, element: <PortalOverview /> },
  {
    path: 'batches',
    element: <PendingSection title="Payout batches" description="Batches by payout period." />,
  },
  {
    path: 'ledger',
    element: <PendingSection title="Ledger" description="Earning entries across all participants." />,
  },
  {
    path: 'approvals',
    element: <PendingSection title="Pending approvals" description="Items that need a finance decision." />,
  },
  {
    path: 'holds',
    element: <PendingSection title="Holds" description="Payout items currently on hold." />,
  },
  {
    path: 'exchange-rates',
    element: <PendingSection title="Exchange rates" description="Current and historical exchange rates." />,
  },
  {
    path: 'schedule',
    element: <PendingSection title="Payout schedule" description="How and when payouts happen." />,
  },
];
