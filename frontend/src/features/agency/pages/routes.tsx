import { lazyPage } from '@/app/lazyPage';
import { FileText, LayoutTemplate } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';

const FormBuilderPage = lazyPage(() => import('./FormBuilderPage'), 'FormBuilderPage');
const FormsListPage = lazyPage(() => import('./FormsListPage'), 'FormsListPage');
const LandingPagesListPage = lazyPage(() => import('./LandingPagesListPage'), 'LandingPagesListPage');
const PageBuilderPage = lazyPage(() => import('./PageBuilderPage'), 'PageBuilderPage');
const SubmissionsPage = lazyPage(() => import('./SubmissionsPage'), 'SubmissionsPage');
const TemplatesPage = lazyPage(() => import('./TemplatesPage'), 'TemplatesPage');

/** Every page in this area calls forms.manage APIs. */
const forms: PermissionRequirement = { anyOf: [Permissions.FormsManage] };

/** Agency portal area: Landing pages & forms. Paths are relative to /agency. */
export const nav: PortalNavItem[] = [
  {
    to: 'pages',
    label: 'Landing pages',
    icon: LayoutTemplate,
    description: 'Block-based landing pages with A/B testing.',
    requires: forms,
  },
  {
    to: 'pages/forms',
    label: 'Forms',
    icon: FileText,
    description: 'Form builder, submissions and embed codes.',
    requires: forms,
  },
];

export const routes: RouteObject[] = [
  { path: 'pages', element: <LandingPagesListPage />, handle: { requires: forms } },
  { path: 'pages/templates', element: <TemplatesPage />, handle: { requires: forms } },
  { path: 'pages/forms', element: <FormsListPage />, handle: { requires: forms } },
  { path: 'pages/forms/:formId', element: <FormBuilderPage />, handle: { requires: forms } },
  { path: 'pages/forms/:formId/submissions', element: <SubmissionsPage />, handle: { requires: forms } },
  { path: 'pages/:pageId', element: <PageBuilderPage />, handle: { requires: forms } },
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [Permissions.FormsManage];
