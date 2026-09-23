import { createBrowserRouter, Outlet, ScrollRestoration, type RouteObject } from 'react-router-dom';
import { CheckEmailPage } from '@/features/auth/CheckEmailPage';
import { ForgotPasswordPage } from '@/features/auth/ForgotPasswordPage';
import { LoginPage } from '@/features/auth/LoginPage';
import { RegisterPage } from '@/features/auth/RegisterPage';
import { ResetPasswordPage } from '@/features/auth/ResetPasswordPage';
import { VerifyEmailPage } from '@/features/auth/VerifyEmailPage';
import { FaqPage } from '@/features/public/FaqPage';
import { LandingPage } from '@/features/public/LandingPage';
import { NotFound } from '@/features/public/NotFound';
import { RouteErrorPage } from '@/features/public/RouteErrorPage';
import { AuthProvider } from '@/lib/auth/AuthProvider';
import { RedirectIfAuthenticated, RequireAuth, RequirePermission } from './guards';
import { AuthLayout } from './layouts/AuthLayout';
import { PortalLayout } from './layouts/PortalLayout';
import { PublicLayout } from './layouts/PublicLayout';
import { portals } from './portals';
import type { PortalDefinition } from './portalTypes';

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

/** Wraps routes whose nav item declares a permission, so deep links answer 403 like the hidden nav implies. */
function guardPortalRoutes(portal: PortalDefinition): RouteObject[] {
  return portal.routes.map((route) => {
    const item = route.path ? portal.nav.find((n) => n.to === route.path) : undefined;
    if (!item?.requires || route.index) return route;
    return {
      ...route,
      element: <RequirePermission {...item.requires}>{route.element ?? <Outlet />}</RequirePermission>,
    };
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
          { index: true, element: <LandingPage /> },
          { path: 'faq', element: <FaqPage /> },
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
        children: [...guardPortalRoutes(portal), { path: '*', element: <NotFound /> }],
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
