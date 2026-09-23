import { lazyPage } from '@/app/lazyPage';
import type { RouteObject } from 'react-router-dom';

const ConfirmSubscriptionPage = lazyPage(
  () => import('./public/PublicEmailPages'),
  'ConfirmSubscriptionPage',
);
const PreferencesPage = lazyPage(() => import('./public/PublicEmailPages'), 'PreferencesPage');
const SignupPage = lazyPage(() => import('./public/PublicEmailPages'), 'SignupPage');
const UnsubscribePage = lazyPage(() => import('./public/PublicEmailPages'), 'UnsubscribePage');

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
