import type { RouteObject } from 'react-router-dom';
import { PublicInvoicePage } from './PublicInvoicePage';

/**
 * Public (anonymous) billing pages. Wire into `app/router.tsx` as children of the `PublicLayout` route:
 * `...billingPublicRoutes` → `/i/:token` (tokenized invoice view linked from invoice emails).
 */
export const publicRoutes: RouteObject[] = [{ path: 'i/:token', element: <PublicInvoicePage /> }];
