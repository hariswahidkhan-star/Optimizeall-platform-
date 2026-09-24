import { lazyPage } from '@/app/lazyPage';
import { Mail, MessageSquareText } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { type PermissionRequirement, Permissions } from '@/lib/auth/permissions';
import { CampaignEditorPage } from './campaigns/CampaignEditorPage';
import { CampaignReportPage } from './campaigns/CampaignReportPage';
import { CampaignsPage } from './campaigns/CampaignsPage';

const ListDetailPage = lazyPage(() => import('./audience/ListDetailPage'), 'ListDetailPage');
const ListsPage = lazyPage(() => import('./audience/ListsPage'), 'ListsPage');
const SubscriberPage = lazyPage(() => import('./audience/SubscriberPage'), 'SubscriberPage');
const TagsFieldsPage = lazyPage(() => import('./audience/TagsFieldsPage'), 'TagsFieldsPage');
const AutomationEditorPage = lazyPage(
  () => import('./automations/AutomationEditorPage'),
  'AutomationEditorPage',
);
const AutomationsPage = lazyPage(() => import('./automations/AutomationsPage'), 'AutomationsPage');
const EmailLayout = lazyPage(() => import('./EmailLayout'), 'EmailLayout');
const EmailOverviewPage = lazyPage(() => import('./overview/EmailOverviewPage'), 'EmailOverviewPage');
const SegmentEditorPage = lazyPage(() => import('./segments/SegmentPages'), 'SegmentEditorPage');
const SegmentsPage = lazyPage(() => import('./segments/SegmentPages'), 'SegmentsPage');
const EmailSettingsPage = lazyPage(() => import('./settings/EmailSettingsPage'), 'EmailSettingsPage');
const TemplateEditorPage = lazyPage(() => import('./templates/TemplateEditorPage'), 'TemplateEditorPage');
const TemplatesPage = lazyPage(() => import('./templates/TemplatesPage'), 'TemplatesPage');

/**
 * Agency portal area: Email & SMS marketing. Paths are relative to /agency. Everything lives under two top-level
 * routes (email, sms) whose requirement guards every nested page; sending additionally needs email.send, which the
 * API enforces and the pages reflect.
 */
const requires = {
  email: { anyOf: [Permissions.EmailManage] },
  sms: { allOf: [Permissions.SmsManage, Permissions.EmailManage] },
} satisfies Record<string, PermissionRequirement>;

export const nav: PortalNavItem[] = [
  {
    to: 'email',
    label: 'Email marketing',
    shortLabel: 'Email',
    icon: Mail,
    description: 'Lists, segments, templates, campaigns and journeys.',
    requires: requires.email,
  },
  {
    to: 'sms',
    label: 'SMS & WhatsApp',
    shortLabel: 'SMS',
    icon: MessageSquareText,
    description: 'Consent-based text and WhatsApp template campaigns.',
    requires: requires.sms,
  },
];

export const routes: RouteObject[] = [
  {
    path: 'email',
    element: <EmailLayout />,
    handle: { requires: requires.email },
    children: [
      { index: true, element: <EmailOverviewPage /> },
      { path: 'campaigns', element: <CampaignsPage channel="email" /> },
      { path: 'campaigns/new', element: <CampaignEditorPage channel="email" /> },
      { path: 'campaigns/:id', element: <CampaignEditorPage channel="email" /> },
      { path: 'campaigns/:id/report', element: <CampaignReportPage channel="email" /> },
      { path: 'lists', element: <ListsPage /> },
      { path: 'lists/:id', element: <ListDetailPage /> },
      { path: 'contacts/:id', element: <SubscriberPage /> },
      { path: 'tags', element: <TagsFieldsPage /> },
      { path: 'segments', element: <SegmentsPage /> },
      { path: 'segments/new', element: <SegmentEditorPage /> },
      { path: 'segments/:id', element: <SegmentEditorPage /> },
      { path: 'templates', element: <TemplatesPage /> },
      { path: 'templates/new', element: <TemplateEditorPage /> },
      { path: 'templates/:id', element: <TemplateEditorPage /> },
      { path: 'automations', element: <AutomationsPage /> },
      { path: 'automations/new', element: <AutomationEditorPage /> },
      { path: 'automations/:id', element: <AutomationEditorPage /> },
      { path: 'settings', element: <EmailSettingsPage /> },
    ],
  },
  {
    path: 'sms',
    element: <EmailLayout />,
    handle: { requires: requires.sms },
    children: [
      { index: true, element: <CampaignsPage channel="sms" /> },
      { path: 'new', element: <CampaignEditorPage channel="sms" /> },
      { path: ':id', element: <CampaignEditorPage channel="sms" /> },
      { path: ':id/report', element: <CampaignReportPage channel="sms" /> },
    ],
  },
];

/** Permissions that open at least one page of this area (added to the agency portal's entry requirement). */
export const opensWith: readonly string[] = [Permissions.EmailManage, Permissions.SmsManage];
