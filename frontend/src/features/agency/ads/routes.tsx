import {
  BadgeDollarSign,
  BellRing,
  ClipboardList,
  FileSpreadsheet,
  FlaskConical,
  Gauge,
  LayoutDashboard,
  Link2,
  Megaphone,
} from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { ImportWizardPage } from './ImportWizardPage';
import { AlertsPage, PacingPage } from './PacingPages';
import { AdExperimentsPage, CreativesPage, MediaPlansPage, UtmPage } from './PlanningPages';
import { AdAccountDetailPage, AdAccountsPage, AdsOverviewPage } from './ReportingPages';

const ads: PermissionRequirement = { anyOf: [Permissions.AdsManage] };

/** Agency portal area: Paid advertising. Paths are relative to /agency. */
export const nav: PortalNavItem[] = [
  { to: 'ads', label: 'Ads overview', icon: LayoutDashboard, description: 'Spend, ROAS and CPA across clients.', requires: ads },
  { to: 'ads/accounts', label: 'Ad accounts', icon: BadgeDollarSign, description: 'Accounts, campaigns and KPIs.', requires: ads },
  { to: 'ads/pacing', label: 'Budget pacing', icon: Gauge, description: 'Actual vs expected spend and projections.', requires: ads },
  { to: 'ads/alerts', label: 'Ads alerts', icon: BellRing, description: 'Pacing, CPA, ROAS and zero-conversion alerts.', requires: ads },
  { to: 'ads/media-plans', label: 'Media plans', icon: ClipboardList, description: 'Plans vs actuals per client and month.', requires: ads },
  { to: 'ads/creatives', label: 'Creative library', icon: Megaphone, description: 'Ad copy with per-platform limits and approvals.', requires: ads },
  { to: 'ads/import', label: 'Import ad data', icon: FileSpreadsheet, description: 'CSV import of Google Ads / Meta exports.', requires: ads },
  { to: 'ads/utm', label: 'UTM & naming', icon: Link2, description: 'Tagged URLs and campaign naming conventions.', requires: ads },
  { to: 'ads/experiments', label: 'Ad experiments', icon: FlaskConical, description: 'A/B tests and their significance.', requires: ads },
];

export const routes: RouteObject[] = [
  { path: 'ads', element: <AdsOverviewPage />, handle: { requires: ads } },
  { path: 'ads/accounts', element: <AdAccountsPage />, handle: { requires: ads } },
  { path: 'ads/accounts/:id', element: <AdAccountDetailPage />, handle: { requires: ads } },
  { path: 'ads/pacing', element: <PacingPage />, handle: { requires: ads } },
  { path: 'ads/alerts', element: <AlertsPage />, handle: { requires: ads } },
  { path: 'ads/media-plans', element: <MediaPlansPage />, handle: { requires: ads } },
  { path: 'ads/creatives', element: <CreativesPage />, handle: { requires: ads } },
  { path: 'ads/import', element: <ImportWizardPage />, handle: { requires: ads } },
  { path: 'ads/utm', element: <UtmPage />, handle: { requires: ads } },
  { path: 'ads/experiments', element: <AdExperimentsPage />, handle: { requires: ads } },
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [Permissions.AdsManage];
