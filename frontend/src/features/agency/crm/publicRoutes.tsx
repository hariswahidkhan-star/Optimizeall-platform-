import type { RouteObject } from 'react-router-dom';
import { PublicProposalPage } from './PublicProposalPage';

/**
 * Public (anonymous) CRM pages. Wire into `app/router.tsx` as children of the `PublicLayout` route:
 * `...crmPublicRoutes` → `/p/:token` (proposal view, typed-signature acceptance and decline).
 */
export const publicRoutes: RouteObject[] = [{ path: 'p/:token', element: <PublicProposalPage /> }];
