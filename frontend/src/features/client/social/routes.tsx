import { lazyPage } from '@/app/lazyPage';
import { BarChart3, CalendarDays, ClipboardCheck } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';

const ClientApprovalsPage = lazyPage(() => import('./ClientSocialPages'), 'ClientApprovalsPage');
const ClientPerformancePage = lazyPage(() => import('./ClientSocialPages'), 'ClientPerformancePage');
const ClientSocialCalendarPage = lazyPage(() => import('./ClientSocialPages'), 'ClientSocialCalendarPage');

/** Client portal area (social): calendar preview, post approvals and a social + ads performance summary. Paths are relative to /client. */
export const nav: PortalNavItem[] = [
  {
    to: 'social',
    label: 'Social calendar',
    icon: CalendarDays,
    description: 'What goes live when on your social profiles.',
  },
  {
    to: 'social/approvals',
    label: 'Post approvals',
    icon: ClipboardCheck,
    description: 'Approve posts or request changes.',
  },
  {
    to: 'social/performance',
    label: 'Performance',
    icon: BarChart3,
    description: 'Social and paid ads results.',
  },
];

export const routes: RouteObject[] = [
  { path: 'social', element: <ClientSocialCalendarPage /> },
  { path: 'social/approvals', element: <ClientApprovalsPage /> },
  { path: 'social/performance', element: <ClientPerformancePage /> },
];
