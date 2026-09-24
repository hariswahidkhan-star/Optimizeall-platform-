import { createBrowserRouter, Outlet, ScrollRestoration, type RouteObject } from 'react-router-dom';
import { CheckEmailPage } from '@/features/auth/CheckEmailPage';
import { ForgotPasswordPage } from '@/features/auth/ForgotPasswordPage';
import { GoogleCallbackPage } from '@/features/auth/google/GoogleCallbackPage';
import { LoginPage } from '@/features/auth/LoginPage';
import { RegisterPage } from '@/features/auth/RegisterPage';
import { ResetPasswordPage } from '@/features/auth/ResetPasswordPage';
import { VerifyEmailPage } from '@/features/auth/VerifyEmailPage';
import { CampaignLandingPage } from '@/features/public/landing/CampaignLandingPage';
import { JoinPage } from '@/features/public/landing/JoinPage';
import { NotFound } from '@/features/public/NotFound';
import { RouteErrorPage } from '@/features/public/RouteErrorPage';
import { publicRoutes } from '@/features/public/routes';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { RedirectIfAuthenticated, RequireAuth, RequirePermission } from './guards';
import { AuthLayout } from './layouts/AuthLayout';
import { PortalLayout } from './layouts/PortalLayout';
import { publicRoutes as billingPublicRoutes } from '@/features/agency/billing/publicRoutes';
import { publicRoutes as crmPublicRoutes } from '@/features/agency/crm/publicRoutes';
import { publicRoutes as emailPublicRoutes } from '@/features/agency/email/publicRoutes';
import { publicRoutes as pagesPublicRoutes } from '@/features/agency/pages/publicRoutes';
import { PublicLayout } from './layouts/PublicLayout';
import { portals } from './portals';
import type { PortalRouteHandle } from './portalTypes';

/** The design-system showcase ships in development and in builds with VITE_SHOW_DESIGN_SYSTEM=true (staging). */
export const showDesignSystem = import.meta.env.DEV || import.meta.env.VITE_SHOW_DESIGN_SYSTEM === 'true';

function RootRoute() {
  return (
    <AuthProvider>
      <ScrollRestoration />
      <Outlet />
    </AuthProvider>
  );
}

/**
 * Wraps every portal route that declares `handle.requires` (at any depth) in RequirePermission, so deep links answer
 * 403 like the hidden nav implies. A parent's requirement covers its children (list + detail pages).
 */
export function guardPortalRoutes(routes: RouteObject[]): RouteObject[] {
  return routes.map((route): RouteObject => {
    const requires = (route.handle as PortalRouteHandle | undefined)?.requires;
    const element = requires ? (
      <RequirePermission {...requires}>{route.element ?? <Outlet />}</RequirePermission>
    ) : (
      route.element
    );
    if (route.index) return { ...route, element };
    return { ...route, element, children: route.children ? guardPortalRoutes(route.children) : undefined };
  });
}

export const routes: RouteObject[] = [
  {
    element: <RootRoute />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        element: <PublicLayout />,
        children: [
          // Public agency website (home, services, blog, careers, forms, CMS pages…): features/public/routes.tsx.
          ...publicRoutes,
          // Invitation links (backend MarketingUrls.InvitationLink) and shareable public campaign pages.
          { path: 'join/:code', element: <JoinPage /> },
          { path: 'c/:slug', element: <CampaignLandingPage /> },
          // Tokenized proposal (/p/:token) and invoice (/i/:token) links sent to clients by email.
          ...crmPublicRoutes,
          ...billingPublicRoutes,
          // Unsubscribe, preference center, double opt-in and hosted sign-up links from marketing emails.
          ...emailPublicRoutes,
          ...(showDesignSystem
            ? [
                {
                  path: 'design-system',
                  lazy: async () => {
                    const { DesignSystemPage } = await import('@/features/design-system/DesignSystemPage');
                    return { Component: DesignSystemPage };
                  },
                },
              ]
            : []),
        ],
      },
      // Client landing pages (/lp/:client/:slug) and embeddable forms (/f/:formId) render without the site chrome.
      ...pagesPublicRoutes,
      {
        element: <AuthLayout />,
        children: [
          {
            path: 'login',
            element: (
              <RedirectIfAuthenticated>
                <LoginPage />
              </RedirectIfAuthenticated>
            ),
          },
          {
            path: 'register',
            element: (
              <RedirectIfAuthenticated>
                <RegisterPage />
              </RedirectIfAuthenticated>
            ),
          },
          { path: 'check-email', element: <CheckEmailPage /> },
          { path: 'verify-email', element: <VerifyEmailPage /> },
          { path: 'forgot-password', element: <ForgotPasswordPage /> },
          { path: 'reset-password', element: <ResetPasswordPage /> },
          // Google OAuth redirect URI (AppLinks.GoogleCallback); also finishes linking from the profile.
          { path: 'auth/google/callback', element: <GoogleCallbackPage /> },
        ],
      },
      ...portals.map<RouteObject>((portal) => ({
        path: portal.basePath,
        element: (
          <RequireAuth>
            <RequirePermission {...portal.requires}>
              <PortalLayout portal={portal} />
            </RequirePermission>
          </RequireAuth>
        ),
        children: [...guardPortalRoutes(portal.routes), { path: '*', element: <NotFound /> }],
      })),
      {
        element: <PublicLayout />,
        children: [{ path: '*', element: <NotFound /> }],
      },
    ],
  },
];

/** Opt into React Router v7 behaviours now so the eventual upgrade is a no-op. */
export const routerFuture = {
  v7_fetcherPersist: true,
  v7_normalizeFormMethod: true,
  v7_partialHydration: true,
  v7_relativeSplatPath: true,
  v7_skipActionErrorRevalidation: true,
} as const;

export function createAppRouter() {
  return createBrowserRouter(routes, { future: routerFuture });
}
