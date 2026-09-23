import { lazyPage } from '@/app/lazyPage';
import type { RouteObject } from 'react-router-dom';

const PublicProposalPage = lazyPage(() => import('./PublicProposalPage'), 'PublicProposalPage');

/**
 * Public (anonymous) CRM pages. Wire into `app/router.tsx` as children of the `PublicLayout` route:
 * `...crmPublicRoutes` → `/p/:token` (proposal view, typed-signature acceptance and decline).
 */
export const publicRoutes: RouteObject[] = [{ path: 'p/:token', element: <PublicProposalPage /> }];
