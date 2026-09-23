import { QueryClient } from '@tanstack/react-query';
import { act, render, type RenderResult } from '@testing-library/react';
import axe from 'axe-core';
import type { ReactElement } from 'react';
import { createMemoryRouter, Outlet, RouterProvider, type RouteObject } from 'react-router-dom';
import { AppProviders } from '@/app/providers';
import { routerFuture } from '@/app/router';
import { AuthProvider } from '@/lib/auth/AuthProvider';

export function testQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
  });
}

interface Options {
  /** Initial URL. */
  route?: string;
  /** Path pattern the element is mounted at (default "*"). */
  path?: string;
  /** Extra routes (e.g. redirect targets) rendered alongside. */
  routes?: RouteObject[];
  /** Wrap in AuthProvider (bootstraps via POST /auth/refresh). */
  withAuth?: boolean;
}

/** Renders UI inside the app providers and a memory router. */
export function renderWithApp(
  ui: ReactElement,
  { route = '/', path = '*', routes = [], withAuth = true }: Options = {},
): RenderResult & { router: ReturnType<typeof createMemoryRouter> } {
  const root = withAuth ? (
    <AuthProvider>
      <Outlet />
    </AuthProvider>
  ) : (
    <Outlet />
  );
  const router = createMemoryRouter([{ element: root, children: [...routes, { path, element: ui }] }], {
    initialEntries: [route],
    future: routerFuture,
  });
  const result = render(
    <AppProviders queryClient={testQueryClient()}>
      <RouterProvider router={router} future={{ v7_startTransition: true }} />
    </AppProviders>,
  );
  return { ...result, router };
}

/** Runs axe against a container; color-contrast is excluded because jsdom has no layout/computed colors. */
export async function axeViolations(container: Element) {
  // Let pending effects (e.g. the session bootstrap) settle so axe audits the final DOM.
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  const results = await axe.run(container, {
    rules: { 'color-contrast': { enabled: false }, region: { enabled: false } },
  });
  return results.violations.map((v) => ({ id: v.id, help: v.help, nodes: v.nodes.map((n) => n.html) }));
}
