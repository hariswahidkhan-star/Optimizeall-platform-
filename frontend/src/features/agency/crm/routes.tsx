import { lazyPage } from '@/app/lazyPage';
import { FileSignature, Handshake, KanbanSquare, ListChecks, Repeat } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';

const CompaniesPage = lazyPage(() => import('./pages/CompaniesPages'), 'CompaniesPage');
const CompanyDetailPage = lazyPage(() => import('./pages/CompaniesPages'), 'CompanyDetailPage');
const ContactDetailPage = lazyPage(() => import('./pages/ContactsPages'), 'ContactDetailPage');
const ContactsPage = lazyPage(() => import('./pages/ContactsPages'), 'ContactsPage');
const ContractDetailPage = lazyPage(() => import('./pages/ContractPages'), 'ContractDetailPage');
const ContractEditorPage = lazyPage(() => import('./pages/ContractPages'), 'ContractEditorPage');
const ContractsPage = lazyPage(() => import('./pages/ContractPages'), 'ContractsPage');
const CrmDashboardPage = lazyPage(() => import('./pages/CrmDashboardPage'), 'CrmDashboardPage');
const CrmSettingsPage = lazyPage(() => import('./pages/CrmSettingsPage'), 'CrmSettingsPage');
const DealDetailPage = lazyPage(() => import('./pages/DealDetailPage'), 'DealDetailPage');
const DealsPage = lazyPage(() => import('./pages/DealsPage'), 'DealsPage');
const ProposalBuilderPage = lazyPage(() => import('./pages/ProposalPages'), 'ProposalBuilderPage');
const ProposalDetailPage = lazyPage(() => import('./pages/ProposalPages'), 'ProposalDetailPage');
const ProposalsPage = lazyPage(() => import('./pages/ProposalPages'), 'ProposalsPage');
const TasksPage = lazyPage(() => import('./pages/TasksPage'), 'TasksPage');

/**
 * Agency portal area: CRM & sales (leads, pipeline, proposals, contracts). Paths are relative to /agency. Each section
 * declares the permission of the API it reads; write actions inside a page are hidden without the write permission
 * (the API enforces them).
 */
const requires = {
  crm: { anyOf: [Permissions.CrmView] },
  proposals: { anyOf: [Permissions.ProposalsManage] },
  contracts: { anyOf: [Permissions.ContractsManage] },
} satisfies Record<string, PermissionRequirement>;

export const nav: PortalNavItem[] = [
  {
    to: 'crm',
    label: 'Sales CRM',
    icon: Handshake,
    description: 'Pipeline value, forecast, win rate and today’s follow-ups.',
    requires: requires.crm,
  },
  {
    to: 'crm/deals',
    label: 'Pipeline',
    icon: KanbanSquare,
    description: 'Deals by stage — drag or use the keyboard to move them.',
    requires: requires.crm,
  },
  {
    to: 'crm/tasks',
    label: 'My tasks',
    icon: ListChecks,
    description: 'Your calls, meetings and follow-ups.',
    requires: requires.crm,
  },
  {
    to: 'proposals',
    label: 'Proposals',
    icon: FileSignature,
    description: 'Build, send and track proposals.',
    requires: requires.proposals,
  },
  {
    to: 'contracts',
    label: 'Contracts',
    icon: Repeat,
    description: 'Retainers billed automatically every period.',
    requires: requires.contracts,
  },
];

export const routes: RouteObject[] = [
  { path: 'crm', element: <CrmDashboardPage />, handle: { requires: requires.crm } },
  { path: 'crm/deals', element: <DealsPage />, handle: { requires: requires.crm } },
  { path: 'crm/deals/:dealId', element: <DealDetailPage />, handle: { requires: requires.crm } },
  { path: 'crm/contacts', element: <ContactsPage />, handle: { requires: requires.crm } },
  { path: 'crm/contacts/:contactId', element: <ContactDetailPage />, handle: { requires: requires.crm } },
  { path: 'crm/companies', element: <CompaniesPage />, handle: { requires: requires.crm } },
  { path: 'crm/companies/:companyId', element: <CompanyDetailPage />, handle: { requires: requires.crm } },
  { path: 'crm/tasks', element: <TasksPage />, handle: { requires: requires.crm } },
  { path: 'crm/settings', element: <CrmSettingsPage />, handle: { requires: requires.crm } },
  { path: 'proposals', element: <ProposalsPage />, handle: { requires: requires.proposals } },
  { path: 'proposals/new', element: <ProposalBuilderPage />, handle: { requires: requires.proposals } },
  {
    path: 'proposals/:proposalId',
    element: <ProposalDetailPage />,
    handle: { requires: requires.proposals },
  },
  {
    path: 'proposals/:proposalId/edit',
    element: <ProposalBuilderPage />,
    handle: { requires: requires.proposals },
  },
  { path: 'contracts', element: <ContractsPage />, handle: { requires: requires.contracts } },
  { path: 'contracts/new', element: <ContractEditorPage />, handle: { requires: requires.contracts } },
  {
    path: 'contracts/:contractId',
    element: <ContractDetailPage />,
    handle: { requires: requires.contracts },
  },
  {
    path: 'contracts/:contractId/edit',
    element: <ContractEditorPage />,
    handle: { requires: requires.contracts },
  },
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [
  Permissions.CrmView,
  Permissions.ProposalsManage,
  Permissions.ContractsManage,
];
