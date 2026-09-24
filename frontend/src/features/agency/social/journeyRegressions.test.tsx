import { act, screen, waitFor, within } from '@testing-library/react';
import { focusManager } from '@tanstack/react-query';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { renderWithApp } from '@/test/render';
import { CalendarPage } from './CalendarPage';
import { ComposerPage } from './ComposerPage';
import { LibraryPage } from './LibraryPage';
import { clients, profile, staffSession, summary } from './test/fixtures';

/** Regressions found by the social + ads E2E journey (frontend/e2e/j-social). */
describe('social journey regressions', () => {
  it('two quick Alt+→ presses move a post two days, the second from the first move’s time and stamp', async () => {
    const user = userEvent.setup();
    const now = new Date();
    const at = new Date(now.getFullYear(), now.getMonth(), 10, 14, 0);
    const post = summary({ scheduledAt: at.toISOString() });
    let version = 1;
    const { calls } = mockFetch({
      'POST /auth/refresh': staffSession(),
      'GET /agency/social/clients': () => json(200, clients),
      'GET /agency/social/calendar': () => json(200, { posts: [post], awarenessDays: [], bestTimes: [] }),
      'POST /agency/social/posts/post1/reschedule': (req) => {
        const body = req.body as { scheduledAt: string; concurrencyStamp: string };
        // The server refuses a stale stamp, like the real API.
        if (body.concurrencyStamp !== `stamp-${version}`)
          return json(409, { title: 'This post was changed by someone else.', code: 'concurrency.conflict' });
        version++;
        return json(200, {
          ...post,
          scheduledAt: body.scheduledAt,
          concurrencyStamp: `stamp-${version}`,
          variants: [],
          comments: [],
        });
      },
    });
    renderWithApp(<CalendarPage />, { route: '/agency/social', path: '/agency/social' });

    const chip = await screen.findByRole('button', { name: /^Launch day, Scheduled,/ }, { timeout: 5000 });
    chip.focus();
    await user.keyboard('{Alt>}{ArrowRight}{/Alt}');
    await user.keyboard('{Alt>}{ArrowRight}{/Alt}');

    await waitFor(
      () => expect(calls.filter((c) => c.path === '/agency/social/posts/post1/reschedule')).toHaveLength(2),
      { timeout: 5000 },
    );
    const [first, second] = calls
      .filter((c) => c.path === '/agency/social/posts/post1/reschedule')
      .map((c) => c.body as { scheduledAt: string; concurrencyStamp: string });
    expect(first!.concurrencyStamp).toBe('stamp-1');
    expect(second!.concurrencyStamp).toBe('stamp-2');
    expect(new Date(second!.scheduledAt).getDate()).toBe(at.getDate() + 2);
    expect(new Date(second!.scheduledAt).getHours()).toBe(14);
    await waitFor(() => expect(version).toBe(3));
    expect(screen.queryByText('Could not move the post')).not.toBeInTheDocument();
  });

  it('after a keyboard move the chip keeps the focus in its new day, so the next Alt+→ moves it again', async () => {
    const user = userEvent.setup();
    const now = new Date();
    const at = new Date(now.getFullYear(), now.getMonth(), 10, 14, 0);
    let current = summary({ scheduledAt: at.toISOString() });
    let version = 1;
    const { calls } = mockFetch({
      'POST /auth/refresh': staffSession(),
      'GET /agency/social/clients': () => json(200, clients),
      'GET /agency/social/calendar': () => json(200, { posts: [current], awarenessDays: [], bestTimes: [] }),
      'POST /agency/social/posts/post1/reschedule': (req) => {
        const body = req.body as { scheduledAt: string; concurrencyStamp: string };
        if (body.concurrencyStamp !== `stamp-${version}`)
          return json(409, { title: 'Changed by someone else.', code: 'concurrency.conflict' });
        version++;
        current = { ...current, scheduledAt: body.scheduledAt, concurrencyStamp: `stamp-${version}` };
        return json(200, { ...current, variants: [], comments: [] });
      },
    });
    renderWithApp(<CalendarPage />, { route: '/agency/social', path: '/agency/social' });
    const dayCell = (d: number) =>
      screen.getByRole('gridcell', {
        name: new RegExp(
          `^${new Date(at.getFullYear(), at.getMonth(), d).toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' })}`,
        ),
      });

    (await screen.findByRole('button', { name: /^Launch day, Scheduled,/ }, { timeout: 5000 })).focus();
    await user.keyboard('{Alt>}{ArrowRight}{/Alt}');
    // The calendar reloads and shows the post on the next day, and that chip has the focus.
    await waitFor(
      () =>
        expect(within(dayCell(11)).getByRole('button', { name: /^Launch day, Scheduled,/ })).toHaveFocus(),
      { timeout: 5000 },
    );

    await user.keyboard('{Alt>}{ArrowRight}{/Alt}');
    await waitFor(
      () =>
        expect(within(dayCell(12)).getByRole('button', { name: /^Launch day, Scheduled,/ })).toHaveFocus(),
      { timeout: 5000 },
    );
    expect(calls.filter((c) => c.path === '/agency/social/posts/post1/reschedule')).toHaveLength(2);
  });

  it('switching back to the tab does not replace unsaved edits with someone else’s version; the save is a 409', async () => {
    const user = userEvent.setup();
    const variant = {
      id: 'v1',
      profileId: 'p-x',
      profileHandle: 'nimbusfit',
      profileName: 'Nimbus Fitness',
      network: 'X',
      text: 'Original',
      title: null,
      mediaIds: [],
      altTexts: [],
      link: null,
      effectiveLink: null,
      firstComment: null,
      hashtags: [],
      mentions: [],
      publishStatus: 'Pending',
      attempts: 0,
      nextAttemptAt: null,
      failureKind: 'None',
      failureReason: null,
      externalPostId: null,
      publishedUrl: null,
      publishedAt: null,
      publishedManually: false,
      validation: { isValid: true, issues: [] },
    };
    const post = {
      ...summary({ status: 'Draft', scheduledAt: null }),
      campaignId: null,
      autoAppendUtm: false,
      evergreenIntervalDays: 30,
      evergreenMaxRepeats: 3,
      evergreenRepeatCount: 0,
      recycledFromPostId: null,
      recycleNumber: null,
      requiresClientApproval: false,
      isValid: true,
      allowedActions: ['submit', 'edit', 'delete'],
      variants: [variant],
      comments: [],
      createdByName: 'Sofia Social',
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-01T00:00:00Z',
    };
    let server = post;
    const { calls } = mockFetch({
      'POST /auth/refresh': staffSession(),
      'GET /agency/social/clients': () => json(200, clients),
      'GET /agency/social/presets': () => json(200, []),
      'GET /agency/social/clients/c1/profiles': () => json(200, [profile()]),
      'GET /agency/social/clients/c1/media': () => json(200, { items: [], total: 0, page: 1, pageSize: 100 }),
      'POST /agency/social/validate': () =>
        json(200, { isValid: true, variants: [{ isValid: true, issues: [] }] }),
      'GET /agency/social/posts/post1': () => json(200, server),
      'PUT /agency/social/posts/post1': () =>
        json(409, {
          title: 'This post was changed by someone else. Reload and try again.',
          code: 'concurrency.conflict',
        }),
    });
    renderWithApp(<ComposerPage />, {
      route: '/agency/social/posts/post1',
      path: '/agency/social/posts/:id',
    });
    const text = await screen.findByRole('textbox', { name: /^X text/ }, { timeout: 5000 });
    await user.clear(text);
    await user.type(text, 'My unsaved edit');

    // Someone else saves; the user switches tabs and comes back.
    server = {
      ...post,
      concurrencyStamp: 'stamp-2',
      variants: [{ ...variant, text: 'Someone else’s version' }],
    };
    act(() => {
      focusManager.setFocused(false);
      focusManager.setFocused(true);
    });
    await new Promise((r) => setTimeout(r, 200));
    expect(screen.getByRole('textbox', { name: /^X text/ })).toHaveValue('My unsaved edit');

    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText(/changed by someone else/, {}, { timeout: 5000 })).toBeInTheDocument();
    const put = calls.find((c) => c.method === 'PUT')!.body as {
      concurrencyStamp: string;
      variants: { text: string }[];
    };
    expect(put.concurrencyStamp).toBe('stamp-1');
    expect(put.variants[0]!.text).toBe('My unsaved edit');
  });

  it('deleting a post does not fetch the deleted post again (no 404 after "Post deleted")', async () => {
    const user = userEvent.setup();
    let deleted = false;
    const post = {
      ...summary({ status: 'Draft', scheduledAt: null }),
      campaignId: null,
      autoAppendUtm: false,
      evergreenIntervalDays: 30,
      evergreenMaxRepeats: 3,
      evergreenRepeatCount: 0,
      recycledFromPostId: null,
      recycleNumber: null,
      requiresClientApproval: false,
      isValid: true,
      allowedActions: ['submit', 'edit', 'delete'],
      variants: [],
      comments: [],
      createdByName: 'Sofia Social',
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-01T00:00:00Z',
    };
    const { calls } = mockFetch({
      'POST /auth/refresh': staffSession(),
      'GET /agency/social/clients': () => json(200, clients),
      'GET /agency/social/presets': () => json(200, []),
      'GET /agency/social/clients/c1/profiles': () => json(200, []),
      'GET /agency/social/clients/c1/media': () => json(200, { items: [], total: 0, page: 1, pageSize: 100 }),
      'GET /agency/social/posts/post1': () =>
        deleted ? json(404, { title: 'Post not found.', code: 'not_found' }) : json(200, post),
      'DELETE /agency/social/posts/post1': () => {
        deleted = true;
        return new Response(null, { status: 204 });
      },
      'GET /agency/social/calendar': () => json(200, { posts: [], awarenessDays: [], bestTimes: [] }),
    });
    renderWithApp(<ComposerPage />, {
      route: '/agency/social/posts/post1',
      path: '/agency/social/posts/:id',
      routes: [{ path: '/agency/social', element: <h1>Calendar</h1> }],
    });
    await user.click(await screen.findByRole('button', { name: 'Delete' }, { timeout: 5000 }));
    const confirm = await screen.findByRole('alertdialog', { name: 'Delete?' });
    await user.click(within(confirm).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleted).toBe(true));
    const removedAt = calls.findIndex((c) => c.method === 'DELETE');
    await new Promise((r) => setTimeout(r, 300));
    expect(
      calls.slice(removedAt + 1).filter((c) => c.method === 'GET' && c.path === '/agency/social/posts/post1'),
    ).toEqual([]);
  });

  it('the media upload dialog accepts images up to the 10 MB it promises (and the server allows)', async () => {
    const user = userEvent.setup();
    mockFetch({
      'POST /auth/refresh': staffSession(),
      'GET /agency/social/clients': () => json(200, clients),
      'GET /agency/social/clients/c1/media': () => json(200, { items: [], total: 0, page: 1, pageSize: 50 }),
    });
    renderWithApp(<LibraryPage />, {
      route: '/agency/social/library?client=c1',
      path: '/agency/social/library',
    });
    await user.click(await screen.findByRole('button', { name: 'Upload image' }, { timeout: 5000 }));
    const dialog = await screen.findByRole('dialog', { name: 'Upload image' });
    expect(
      within(dialog).getByText('PNG, JPEG or WebP up to 10 MB; metadata (GPS, EXIF) is removed.'),
    ).toBeInTheDocument();
    const input = dialog.querySelector('input[type="file"]') as HTMLInputElement;

    await user.upload(input, new File([new Uint8Array(9 * 1024 * 1024)], 'nine.png', { type: 'image/png' }));
    expect(within(dialog).queryByText(/The limit is/)).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Upload' })).toBeEnabled();

    await user.upload(
      input,
      new File([new Uint8Array(11 * 1024 * 1024)], 'eleven.png', { type: 'image/png' }),
    );
    expect(await within(dialog).findByText(/The limit is 10 MB/)).toBeInTheDocument();
  });
});
