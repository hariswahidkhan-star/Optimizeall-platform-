import { expect, test } from '@playwright/test';
import {
  API_URL,
  ApiSession,
  accounts,
  actor,
  api,
  landing,
  recall,
  refused,
  runId,
  state,
  withToken,
} from './support/content';

/**
 * Who may change the public site:
 *   UI — staff without site.manage see the 403 page on the CMS screens (and no nav entry); the writer sees the blog but
 *   not pages or settings; the designer sees landing pages but not the website CMS; a client user never reaches the
 *   agency portal. API — the same answers (403), anonymous callers get 401.
 *   Tenancy — a user whose forms.manage comes from a custom role without clients.view only sees the clients they belong
 *   to (none): other clients' pages and forms answer 404, and cannot be created.
 *   Impersonation — an admin viewing as the website editor may edit content, but not the site settings (which set the
 *   analytics/tag-manager scripts on every public page and the canonical site URL).
 */
const FORBIDDEN = 'You don’t have access to this page';

test('staff without the content permissions get 403 in the UI and the API', async ({ browser }) => {
  // Account manager: no site.manage, blog.*, forms.manage.
  const am = await actor(browser, accounts.am, landing.agency);
  const nav = am.getByRole('navigation', { name: 'Agency navigation' });
  await expect(nav.getByRole('link', { name: 'Site settings' })).toHaveCount(0);
  await expect(nav.getByRole('link', { name: 'Pages', exact: true })).toHaveCount(0);
  await expect(nav.getByRole('link', { name: 'Landing pages' })).toHaveCount(0);
  for (const path of [
    '/agency/website/pages',
    '/agency/website/settings',
    '/agency/website/blog',
    '/agency/website/services',
    '/agency/pages',
  ]) {
    await am.goto(path);
    await expect(am.getByRole('heading', { level: 1, name: FORBIDDEN }), path).toBeVisible();
  }
  const amApi = await api(accounts.am);
  for (const path of [
    '/agency/website/pages',
    '/agency/website/settings',
    '/agency/website/blog/posts',
    '/agency/website/testimonials',
    '/agency/pages/landing-pages',
    '/agency/pages/forms',
    '/admin/content/banners',
  ])
    expect((await refused(amApi.get(path))).status, path).toBe(403);
  // The catalog picker (read-only) stays available to CRM users.
  expect(Array.isArray(await amApi.get('/agency/website/catalog'))).toBe(true);

  // Writer: blog yes; pages, settings, services, landing pages no.
  const writer = await actor(browser, accounts.writer, landing.agency);
  await writer.goto('/agency/website/blog');
  await expect(writer.getByRole('heading', { level: 1, name: 'Blog' })).toBeVisible();
  for (const path of ['/agency/website/pages', '/agency/website/settings', '/agency/pages']) {
    await writer.goto(path);
    await expect(writer.getByRole('heading', { level: 1, name: FORBIDDEN }), path).toBeVisible();
  }
  const writerApi = await api(accounts.writer);
  expect(
    (
      await refused(
        writerApi.post('/agency/website/pages', {
          slug: `w-${runId()}`,
          title: 'x',
          kind: 'Standard',
          blocks: [],
          seo: {},
        }),
      )
    ).status,
  ).toBe(403);
  expect(
    (
      await refused(
        writerApi.put('/agency/website/copy', { changes: [{ key: 'home.hero.eyebrow', value: 'Hijacked' }] }),
      )
    ).status,
  ).toBe(403);

  // Designer: landing pages yes; website CMS no.
  const designer = await actor(browser, accounts.designer, landing.agency);
  await designer.goto('/agency/website/pages');
  await expect(designer.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
  await designer.goto('/agency/pages');
  await expect(designer.getByRole('heading', { level: 1, name: 'Landing pages' })).toBeVisible();

  // Client user: the agency portal is off limits, and so is every staff content API.
  const client = await actor(browser, accounts.nimbusApprover, landing.client);
  await client.goto('/agency/pages');
  await expect(client.getByRole('heading', { level: 1, name: FORBIDDEN })).toBeVisible();
  const clientApi = await api(accounts.nimbusApprover);
  const { id: pageId } = recall<{ id: string }>('bootcampPage');
  for (const path of [
    '/agency/pages/landing-pages',
    `/agency/pages/landing-pages/${pageId}`,
    '/agency/website/pages',
    '/agency/website/blog/posts',
  ])
    expect((await refused(clientApi.get(path))).status, path).toBe(403);

  // Anonymous: 401.
  for (const path of ['/agency/website/pages', '/agency/pages/landing-pages', '/agency/website/settings']) {
    const res = await fetch(`${API_URL}/api/v1${path}`, { headers: { 'X-Requested-With': 'fetch' } });
    expect(res.status, path).toBe(401);
  }
});

test('tenancy: forms.manage without clients.view only reaches the clients the user belongs to', async () => {
  const id = runId();
  const admin = await api(accounts.admin);
  const role = await admin.post<{ role: { id: string } }>('/admin/roles', {
    name: `Forms only ${id}`,
    description: 'Landing pages without agency-wide client access (e2e j-content).',
    permissions: ['forms.manage'],
  });
  const user = await admin.post<{ id: string; email: string; password: string }>('/admin/test-users', {
    roles: ['Participant'],
    displayName: `Freya Forms ${id}`,
  });
  await admin.put(`/admin/roles/${role.role.id}/users/${user.id}`);
  const scoped = await ApiSession.login(user.email, user.password);

  const { id: pageId } = recall<{ id: string }>('bootcampPage');
  const { id: formId, clientId } = recall<{ id: string; clientId: string }>('bootcampForm');
  expect((await scoped.get<{ items: unknown[]; total: number }>('/agency/pages/landing-pages')).total).toBe(
    0,
  );
  expect((await scoped.get<{ items: unknown[]; total: number }>('/agency/pages/forms')).total).toBe(0);
  expect(await scoped.get<unknown[]>('/agency/pages/client-options')).toEqual([]);
  expect((await refused(scoped.get(`/agency/pages/landing-pages/${pageId}`))).status).toBe(404);
  expect((await refused(scoped.get(`/agency/pages/forms/${formId}/submissions`))).status).toBe(404);
  expect((await refused(scoped.get(`/agency/pages/forms/${formId}/submissions/export.csv`))).status).toBe(
    404,
  );
  expect((await refused(scoped.post(`/agency/pages/landing-pages/${pageId}/publish`))).status).toBe(404);
  expect(
    (
      await refused(
        scoped.post('/agency/pages/landing-pages', { clientAccountId: clientId, name: 'Not mine' }),
      )
    ).status,
  ).toBe(404);
  await admin.delete(`/admin/roles/${role.role.id}?confirm=true`);
});

test('impersonation: content edits are allowed as the editor, site settings are not', async () => {
  const id = runId();
  const admin = await api(accounts.admin);
  const started = await admin.post<{ accessToken: string; user: { id: string } }>(
    `/admin/users/${state().editor.id}/impersonate`,
    {
      reason: `E2E content support check ${id}`,
      confirm: true,
    },
  );
  expect(started.user.id).toBe(state().editor.id);
  const token = started.accessToken;

  // Everyday content work is allowed (it is audited as "admin as editor").
  const testimonial = await withToken<{ id: string }>(token, 'POST', '/agency/website/testimonials', {
    quote: `Written while impersonating ${id}`,
    authorName: 'Ivy Impersonated',
    isPublished: false,
    isFeatured: false,
    sortOrder: 0,
  });
  await withToken(token, 'DELETE', `/agency/website/testimonials/${testimonial.id}`);

  // Site settings carry the tag-manager / pixel ids injected on every public page and the canonical site URL.
  const settings = await withToken<{ settings: unknown; concurrencyStamp: string }>(
    token,
    'GET',
    '/agency/website/settings',
  );
  const error = await refused(withToken(token, 'PUT', '/agency/website/settings', settings));
  expect(error.status).toBe(403);
  expect(error.code).toBe('auth.impersonation_forbidden_action');
  // The editor themselves (not impersonated) can still save them.
  const editor = await api(state().editor);
  const own = await editor.get<{ settings: unknown; concurrencyStamp: string }>('/agency/website/settings');
  await editor.put('/agency/website/settings', own);
});
