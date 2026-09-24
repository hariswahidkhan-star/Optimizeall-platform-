import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { BlogPost, SitePage, SitePageRevision, SitePageRevisionSummary, Testimonial } from '../api';
import { PostEditorPage } from './BlogAdmin';
import { TestimonialsAdminPage } from './ContentPages';
import { PageEditorPage } from './PagesAdmin';

const staff = makeUser({ id: 'staff-1', roles: ['Admin'], permissions: ['site.manage'], displayName: 'Web Editor' });

const page: SitePage = {
  id: 'p1',
  slug: 'privacy-policy',
  title: 'Privacy policy',
  summary: null,
  kind: 'Legal',
  blocks: [{ id: 'b1', type: 'richText', data: { markdown: 'We keep data for 90 days.' } }],
  seo: { title: null, description: null, ogImageUrl: null, canonicalUrl: null, noIndex: false },
  isPublished: true,
  sortOrder: 0,
  updatedAt: '2026-09-20T10:00:00Z',
  concurrencyStamp: 'stamp-2',
  publishAt: null,
  version: 2,
};

const history: SitePageRevisionSummary[] = [
  { version: 2, action: 'updated', note: 'Retention is now 90 days', title: 'Privacy policy', isPublished: true, publishAt: null, authorUserId: 'staff-1', authorName: 'Web Editor', createdAt: '2026-09-20T10:00:00Z', isCurrent: true },
  { version: 1, action: 'created', note: null, title: 'Privacy policy', isPublished: true, publishAt: null, authorUserId: 'staff-1', authorName: 'Web Editor', createdAt: '2026-09-01T10:00:00Z', isCurrent: false },
];

const v1: SitePageRevision = {
  ...history[1],
  slug: 'privacy-policy',
  summary: null,
  kind: 'Legal',
  blocks: [{ id: 'b1', type: 'richText', data: { markdown: 'We keep data for 30 days.' } }],
  seo: page.seo,
};

describe('Page editor history', () => {
  it('previews an old version and restores it with the current stamp', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/pages/p1': () => json(200, page),
      'GET /agency/website/pages/p1/revisions': () => json(200, history),
      'GET /agency/website/pages/p1/revisions/1': () => json(200, v1),
      'POST /agency/website/pages/p1/revisions/1/restore': () => json(200, { ...page, version: 3, concurrencyStamp: 'stamp-3' }),
    });
    const { container } = renderWithApp(<PageEditorPage />, { route: '/agency/website/pages/p1', path: '/agency/website/pages/:pageId' });

    const section = await screen.findByRole('region', { name: 'Version history' });
    expect(await within(section).findByText(/Retention is now 90 days/)).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    await user.click(within(section).getByRole('button', { name: 'Preview version 1' }));
    const preview = await screen.findByRole('complementary', { name: 'Preview of version 1' });
    expect(await within(preview).findByText('We keep data for 30 days.')).toBeInTheDocument();

    await user.click(within(section).getByRole('button', { name: 'Restore version 1' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Restore version 1?' });
    await user.click(within(dialog).getByRole('button', { name: 'Restore version' }));
    expect(calls.find((c) => c.method === 'POST' && c.path.endsWith('/restore'))?.body).toEqual({ concurrencyStamp: 'stamp-2' });
  });

  it('explains a restore conflict', async () => {
    const user = userEvent.setup();
    mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/pages/p1': () => json(200, page),
      'GET /agency/website/pages/p1/revisions': () => json(200, history),
      'POST /agency/website/pages/p1/revisions/1/restore': () => problem(409, 'concurrency.conflict', 'Changed elsewhere.'),
    });
    renderWithApp(<PageEditorPage />, { route: '/agency/website/pages/p1', path: '/agency/website/pages/:pageId' });
    const section = await screen.findByRole('region', { name: 'Version history' });
    await user.click(await within(section).findByRole('button', { name: 'Restore version 1' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Restore version 1?' });
    await user.click(within(dialog).getByRole('button', { name: 'Restore version' }));
    expect(await within(dialog).findByText(/Someone else saved this page/)).toBeInTheDocument();
  });
});

const testimonial = (id: string, author: string): Testimonial =>
  ({
    id,
    quote: 'Great work',
    authorName: author,
    authorRole: null,
    company: null,
    rating: 5,
    avatarUrl: null,
    serviceId: null,
    isPublished: true,
    isFeatured: false,
    sortOrder: 10,
    updatedAt: '2026-09-01T00:00:00Z',
    concurrencyStamp: 's',
  }) as Testimonial;

describe('CMS reorder', () => {
  it('reorders testimonials with the keyboard and saves the new order', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/testimonials': () =>
        json(200, { items: [testimonial('t1', 'Ada'), testimonial('t2', 'Grace')], total: 2, page: 1, pageSize: 200, totalPages: 1 }),
      'POST /agency/website/testimonials/reorder': () => json(200, { updated: 2 }),
    });
    const { container } = renderWithApp(<TestimonialsAdminPage />, { route: '/agency/website/testimonials', path: '/agency/website/testimonials' });
    expect((await screen.findAllByText('Grace')).length).toBeGreaterThan(0);
    await user.click(screen.getByRole('button', { name: 'Reorder' }));
    const handle = await screen.findByRole('button', { name: 'Reorder Grace, position 2 of 2' });
    handle.focus();
    await user.keyboard(' ');
    await user.keyboard('{ArrowUp}');
    await user.keyboard(' ');
    expect(await axeViolations(container)).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Save order' }));
    expect(calls.find((c) => c.path === '/agency/website/testimonials/reorder')?.body).toEqual({ ids: ['t2', 't1'] });
  });
});

describe('creating a page or a post', () => {
  // The editors live at /agency/website/pages/new and /agency/website/blog/new; after the first save they must move to
  // the new record's own address, not /pages/pages/:id (no such route: the editor vanished into a 404).
  it('opens the new page at /agency/website/pages/:id after the first save', async () => {
    const user = userEvent.setup();
    const created: SitePage = { ...page, id: 'p9', slug: 'about-us', title: 'About us', version: 1 };
    mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'POST /agency/website/pages': () => json(201, created),
      'GET /agency/website/pages/p9': () => json(200, created),
      'GET /agency/website/pages/p9/revisions': () => json(200, []),
      'GET /agency/website/pages': () => json(200, []),
    });
    const { router } = renderWithApp(<PageEditorPage />, { route: '/agency/website/pages/new', path: '/agency/website/pages/:pageId' });
    const form = await screen.findByRole('form', { name: 'Page editor' });
    await user.type(within(form).getAllByLabelText('Title')[0]!, 'About us');
    await user.type(within(form).getByLabelText('Slug'), 'about-us');
    await user.click(within(form).getByRole('button', { name: 'Save page' }));
    await vi.waitFor(() => expect(router.state.location.pathname).toBe('/agency/website/pages/p9'));
  });

  it('opens the new post at /agency/website/blog/:id after the first save', async () => {
    const user = userEvent.setup();
    const post: BlogPost = {
      id: 'b9', slug: 'hello', title: 'Hello', excerpt: 'Hi', bodyMarkdown: 'Body', coverImageUrl: null, coverImageAlt: null, authorId: null,
      categoryIds: [], tags: [], readingMinutes: 1, status: 'Draft', publishAt: null, publishedAt: null, relatedPostIds: [], seo: page.seo,
      createdAt: '2026-09-20T10:00:00Z', updatedAt: '2026-09-20T10:00:00Z', concurrencyStamp: 's1',
      can: { edit: true, submit: true, publish: false, schedule: false, unpublish: false, returnToDraft: false, delete: true },
    };
    mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/website/blog/categories': () => json(200, []),
      'GET /agency/website/blog/authors': () => json(200, []),
      'POST /agency/website/blog/posts': () => json(201, post),
      'GET /agency/website/blog/posts/b9': () => json(200, post),
      'GET /agency/website/blog/posts': () => json(200, { items: [], total: 0, page: 1, pageSize: 100, totalPages: 0 }),
    });
    const { router } = renderWithApp(<PostEditorPage />, { route: '/agency/website/blog/new', path: '/agency/website/blog/:postId' });
    const form = await screen.findByRole('form', { name: 'Post editor' });
    await user.type(within(form).getByLabelText('Title'), 'Hello');
    await user.type(within(form).getByLabelText('Slug'), 'hello');
    await user.click(within(form).getByRole('button', { name: 'Create draft' }));
    await vi.waitFor(() => expect(router.state.location.pathname).toBe('/agency/website/blog/b9'));
  });
});
