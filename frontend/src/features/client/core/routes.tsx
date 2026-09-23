import { BarChart3, FileCheck2, FolderKanban, Home, MessagesSquare, NotebookPen, Palette, Smile, Users } from 'lucide-react';
import type { RouteObject } from 'react-router-dom';
import type { PortalNavItem } from '@/app/portalTypes';
import { ApprovalDetailPage, ApprovalsPage } from './ApprovalsPages';
import { ClientHomePage } from './ClientHomePage';
import {
  ClientBrandKitPage,
  ClientBriefsPage,
  ClientMessagesPage,
  ClientProjectPage,
  ClientProjectsPage,
  ClientReportPage,
  ClientReportsPage,
  ClientTeamPage,
} from './ClientPages';
import { FeedbackPage } from './FeedbackPage';

/**
 * Client portal area (core). Paths are relative to /client. Every page needs only `client.portal` (the portal entry);
 * what a user may do inside an organization depends on their duty there, which the API enforces per request.
 */
export const nav: PortalNavItem[] = [
  { to: '', label: 'Home', icon: Home },
  { to: 'approvals', label: 'Approvals', icon: FileCheck2, description: 'Review and approve work.' },
  { to: 'projects', label: 'Projects', icon: FolderKanban, description: 'Progress and milestones.' },
  { to: 'reports', label: 'Reports', icon: BarChart3, description: 'Monthly performance reports.' },
  { to: 'briefs', label: 'Briefs', icon: NotebookPen, description: 'Request new work.' },
  { to: 'messages', label: 'Messages', icon: MessagesSquare, description: 'Talk to your account team.' },
  { to: 'brand', label: 'Brand kit', icon: Palette, description: 'Colours, fonts, voice and assets.' },
  { to: 'team', label: 'Team', icon: Users, description: 'Your agency team and your colleagues.' },
  { to: 'feedback', label: 'Feedback', icon: Smile, description: 'Tell us how we are doing.' },
];

export const routes: RouteObject[] = [
  { index: true, element: <ClientHomePage /> },
  {
    path: 'approvals',
    children: [
      { index: true, element: <ApprovalsPage /> },
      { path: ':deliverableId', element: <ApprovalDetailPage /> },
    ],
  },
  {
    path: 'projects',
    children: [
      { index: true, element: <ClientProjectsPage /> },
      { path: ':projectId', element: <ClientProjectPage /> },
    ],
  },
  {
    path: 'reports',
    children: [
      { index: true, element: <ClientReportsPage /> },
      { path: ':reportId', element: <ClientReportPage /> },
    ],
  },
  { path: 'briefs', element: <ClientBriefsPage /> },
  { path: 'messages', element: <ClientMessagesPage /> },
  { path: 'brand', element: <ClientBrandKitPage /> },
  { path: 'team', element: <ClientTeamPage /> },
  { path: 'feedback', element: <FeedbackPage /> },
];
