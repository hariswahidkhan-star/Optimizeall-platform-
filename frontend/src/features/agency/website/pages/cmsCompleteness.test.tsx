import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { SitePage, SitePageRevision, SitePageRevisionSummary, Testimonial } from '../api';
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
