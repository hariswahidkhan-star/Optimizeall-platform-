import type { ReactElement } from 'react';
import { json, makeUser, mockFetch, session, type MockRequest } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';

export const ADMIN_PERMISSIONS = [
  'users.view',
  'users.manage',
  'users.suspend',
  'roles.assign',
  'content.manage',
  'settings.manage',
  'support.manage',
  'audit.view',
  'jobs.view',
  'analytics.view',
  'campaigns.manage',
];

export const adminUser = makeUser({
  id: 'admin-1',
  displayName: 'Platform Admin',
  email: 'admin@optimizeall.local',
  roles: ['Admin'],
  permissions: ADMIN_PERMISSIONS,
  timeZone: 'UTC',
});

type Handler = (req: MockRequest) => Response | Promise<Response>;

/** Mocks the API with a signed-in admin (session bootstrap via /auth/refresh) plus the given routes. */
export function mockAdminApi(routes: Record<string, Handler>, permissions = ADMIN_PERMISSIONS) {
  const user = { ...adminUser, permissions };
  return mockFetch({
    'POST /auth/refresh': () => json(200, session(user)),
    ...routes,
  });
}

export function renderAdmin(ui: ReactElement, route = '/admin/x', path = '/admin/*') {
  return renderWithApp(ui, { route, path });
}

export { json };
