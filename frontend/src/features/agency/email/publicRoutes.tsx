import type { RouteObject } from 'react-router-dom';
import { ConfirmSubscriptionPage, PreferencesPage, SignupPage, UnsubscribePage } from './public/PublicEmailPages';

/**
 * Anonymous pages linked from marketing emails (backend EmailMarketingUrls). Spread into the PublicLayout children in
 * app/router.tsx: `children: [..., ...emailPublicRoutes]`. Paths are absolute from the site root.
 */
export const publicRoutes: RouteObject[] = [
  { path: 'email/unsubscribe/:token', element: <UnsubscribePage /> },
  { path: 'email/preferences/:token', element: <PreferencesPage /> },
  { path: 'email/confirm/:token', element: <ConfirmSubscriptionPage /> },
  { path: 'email/subscribe/:formKey', element: <SignupPage /> },
];
