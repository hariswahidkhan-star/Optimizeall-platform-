import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { SitePage, SitePageSummary, SiteRedirect } from '../api';
import { PageEditorPage, PagesAdminPage } from './PagesAdmin';
import { RedirectsAdminPage } from './RedirectsAdmin';

const staff = makeUser({
  id: 'staff-1',
  roles: ['Admin'],
  permissions: ['site.manage'],
  displayName: 'Web Editor',
});

const page: SitePage = {
  id: 'p1',
  slug: 'our-promise',
  title: 'Our promise',
  summary: null,
  kind: 'Standard',
  blocks: [{ id: 'b1', type: 'richText', data: { markdown: 'We answer within a day.' } }],
  seo: { title: null, description: null, ogImageUrl: null, canonicalUrl: null, noIndex: false },
  isPublished: true,
  sortOrder: 0,
  updatedAt: '2026-09-20T10:00:00Z',
  concurrencyStamp: 'stamp-1',
  publishAt: null,
  version: 1,
};

const summary: SitePageSummary = {
  id: 'p1',
  slug: 'our-promise',
  title: 'Our promise',
  kind: 'Standard',
  isPublished: true,
  blockCount: 1,
  updatedAt: '2026-09-20T10:00:00Z',
  publishAt: null,
  version: 1,
};

const redirect: SiteRedirect = {
  id: 'r1',
  fromPath: '/about-us',
  toPath: '/who-we-are',
  source: 'Automatic',
  contentType: 'page',
  contentId: 'p1',
  createdAt: '2026-09-21T10:00:00Z',
  updatedAt: '2026-09-21T10:00:00Z',
};

describe('Deleting CMS pages', () => {
  it('deletes a page from the editor after confirming and returns to the list', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/pages/p1': () => json(200, page),
      'GET /agency/website/pages/p1/revisions': () => json(200, []),
      'DELETE /agency/website/pages/p1': () => new Response(null, { status: 204 }),
    });
    const { router } = renderWithApp(<PageEditorPage />, {
      route: '/agency/website/pages/p1',
      path: '/agency/website/pages/:pageId',
      routes: [{ path: '/agency/website/pages', element: <p>Pages list</p> }],
    });

    await user.click(await screen.findByRole('button', { name: 'Delete page' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Delete “Our promise”?' });
    expect(dialog).toHaveTextContent('/our-promise stops working for visitors');
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false);
    await user.click(within(dialog).getByRole('button', { name: 'Delete page' }));

    expect(await screen.findByText('Pages list')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/agency/website/pages');
    expect(calls.filter((c) => c.method === 'DELETE').map((c) => c.path)).toEqual([
      '/agency/website/pages/p1',
    ]);
    // The deleted page is not fetched again (that would be a 404 while leaving the editor).
    expect(calls.filter((c) => c.method === 'GET' && c.path === '/agency/website/pages/p1')).toHaveLength(1);
  });

  it('deletes a page from the list row menu', async () => {
    const user = userEvent.setup();
    let pages = [summary];
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/pages': () => json(200, pages),
      'DELETE /agency/website/pages/p1': () => {
        pages = [];
        return new Response(null, { status: 204 });
      },
    });
    const { container } = renderWithApp(<PagesAdminPage />, { route: '/agency/website/pages' });
    await user.click(await screen.findByRole('button', { name: 'Actions for Our promise' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Delete page…' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Delete “Our promise”?' });
    await user.click(within(dialog).getByRole('button', { name: 'Delete page' }));
    await screen.findByText('No pages yet.');
    expect(calls.some((c) => c.method === 'DELETE' && c.path === '/agency/website/pages/p1')).toBe(true);
    expect(await axeViolations(container)).toEqual([]);
  });

  it('hides the delete action from users without site.manage', async () => {
    const reader = makeUser({ id: 'u2', roles: ['Designer'], permissions: ['forms.manage'] });
    mockFetch({
      'POST /auth/refresh': () => json(200, session(reader)),
      'GET /agency/website/pages/p1': () => json(200, page),
      'GET /agency/website/pages/p1/revisions': () => json(200, []),
    });
    renderWithApp(<PageEditorPage />, {
      route: '/agency/website/pages/p1',
      path: '/agency/website/pages/:pageId',
    });
    expect(await screen.findByRole('heading', { level: 1, name: 'Edit “Our promise”' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete page' })).not.toBeInTheDocument();
  });
});

describe('Website → Redirects', () => {
  it('lists redirects, adds a manual one and deletes one', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/redirects': () =>
        json(200, { items: [redirect], total: 1, page: 1, pageSize: 50 }),
      'POST /agency/website/redirects': () =>
        json(201, {
          ...redirect,
          id: 'r2',
          fromPath: '/spring-sale',
          toPath: '/offers',
          source: 'Manual',
          contentType: null,
          contentId: null,
        }),
      'DELETE /agency/website/redirects/r1': () => new Response(null, { status: 204 }),
    });
    const { container } = renderWithApp(<RedirectsAdminPage />, { route: '/agency/website/redirects' });

    const table = await screen.findByRole('table', { name: 'Redirects' });
    expect(await within(table).findByText('/about-us')).toBeInTheDocument();
    expect(within(table).getByText('/who-we-are')).toBeInTheDocument();
    expect(within(table).getByText('Page renamed')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Add redirect' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add redirect' });
    await user.type(within(dialog).getByLabelText(/Old address/), '/spring-sale');
    await user.type(within(dialog).getByLabelText(/New address/), '/offers');
    await user.click(within(dialog).getByRole('button', { name: 'Add redirect' }));
    await screen.findByText('/spring-sale → /offers');
    expect(calls.find((c) => c.method === 'POST' && c.path === '/agency/website/redirects')?.body).toEqual({
      fromPath: '/spring-sale',
      toPath: '/offers',
    });

    await user.click(screen.getByRole('button', { name: 'Actions for /about-us' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Delete redirect…' }));
    const confirm = await screen.findByRole('alertdialog', { name: 'Delete the redirect from /about-us?' });
    await user.click(within(confirm).getByRole('button', { name: 'Delete redirect' }));
    await screen.findByText('Redirect deleted');
    expect(calls.some((c) => c.method === 'DELETE' && c.path === '/agency/website/redirects/r1')).toBe(true);
  });

  it('shows the API’s field errors (e.g. a loop) next to the fields', async () => {
    const user = userEvent.setup();
    mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/redirects': () => json(200, { items: [], total: 0, page: 1, pageSize: 50 }),
      'POST /agency/website/redirects': () =>
        problem(409, 'website.redirect_loop', 'Loop.', {
          errors: { toPath: ['This would send visitors in a circle back to the address they came from.'] },
        }),
    });
    renderWithApp(<RedirectsAdminPage />, { route: '/agency/website/redirects' });
    await user.click(await screen.findByRole('button', { name: 'Add redirect' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add redirect' });
    await user.type(within(dialog).getByLabelText(/Old address/), '/b');
    await user.type(within(dialog).getByLabelText(/New address/), '/a');
    await user.click(within(dialog).getByRole('button', { name: 'Add redirect' }));
    expect(
      await within(dialog).findByText(
        'This would send visitors in a circle back to the address they came from.',
      ),
    ).toBeInTheDocument();
  });
});
