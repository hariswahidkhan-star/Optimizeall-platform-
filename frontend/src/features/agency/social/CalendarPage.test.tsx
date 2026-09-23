import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { CalendarPage } from './CalendarPage';
import { clients, staffSession, summary } from './test/fixtures';

/** A post at 14:00 local time, a few days into the current month so ±1 day stays on the grid. */
function scheduled(): { post: ReturnType<typeof summary>; at: Date } {
  const now = new Date();
  const at = new Date(now.getFullYear(), now.getMonth(), 10, 14, 0);
  return { post: summary({ scheduledAt: at.toISOString() }), at };
}

function setup() {
  const { post, at } = scheduled();
  const mock = mockFetch({
    'POST /auth/refresh': staffSession(),
    'GET /agency/social/clients': () => json(200, clients),
    'GET /agency/social/calendar': () =>
      json(200, {
        posts: [post, summary({ id: 'post2', title: 'Already out', status: 'Published', scheduledAt: at.toISOString(), publishedAt: at.toISOString() })],
        awarenessDays: [],
        bestTimes: [],
      }),
    'POST /agency/social/posts/post1/reschedule': (req) => {
      const body = req.body as { scheduledAt: string };
      return json(200, { ...post, scheduledAt: body.scheduledAt, variants: [], comments: [] });
    },
  });
  return { ...mock, post, at };
}

function dayLabel(d: Date) {
  return d.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' });
}

describe('CalendarPage', () => {
  it('moves a post one day later with Alt+ArrowRight (keyboard alternative to drag)', async () => {
    const user = userEvent.setup();
    const { calls, at } = setup();
    const { container } = renderWithApp(<CalendarPage />, { route: '/agency/social', path: '/agency/social' });

    const chip = await screen.findByRole('button', { name: /^Launch day, Scheduled,.*Press Alt and an arrow key to move it\.$/ }, { timeout: 5000 });
    chip.focus();
    await user.keyboard('{Alt>}{ArrowRight}{/Alt}');

    await waitFor(() => expect(calls.some((c) => c.path === '/agency/social/posts/post1/reschedule')).toBe(true), { timeout: 5000 });
    const body = calls.find((c) => c.path === '/agency/social/posts/post1/reschedule')!.body as { scheduledAt: string; concurrencyStamp: string };
    const moved = new Date(body.scheduledAt);
    expect(moved.getDate()).toBe(at.getDate() + 1);
    expect(moved.getHours()).toBe(14);
    expect(body.concurrencyStamp).toBe('stamp-1');
    expect(await screen.findByText(/Moved “Launch day”/, {}, { timeout: 5000 })).toBeInTheDocument();

    // Published posts cannot be moved: no keyboard hint and no move button.
    expect(screen.getByRole('button', { name: /^Already out, Published,[^.]*$/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Move Already out to another date' })).not.toBeInTheDocument();

    expect(await axeViolations(container)).toEqual([]);
  });

  it('reschedules by dragging a post onto another day, keeping its time', async () => {
    const { calls, at } = setup();
    renderWithApp(<CalendarPage />, { route: '/agency/social', path: '/agency/social' });

    const chip = await screen.findByRole('button', { name: /^Launch day, Scheduled/ }, { timeout: 5000 });
    const target = new Date(at.getFullYear(), at.getMonth(), at.getDate() + 3);
    const cell = screen.getByRole('gridcell', { name: new RegExp(`^${dayLabel(target)}`) });

    fireEvent.dragStart(chip);
    fireEvent.dragOver(cell);
    fireEvent.drop(cell);

    await waitFor(() => expect(calls.some((c) => c.path === '/agency/social/posts/post1/reschedule')).toBe(true), { timeout: 5000 });
    const body = calls.find((c) => c.path === '/agency/social/posts/post1/reschedule')!.body as { scheduledAt: string };
    const moved = new Date(body.scheduledAt);
    expect(moved.getDate()).toBe(target.getDate());
    expect(moved.getHours()).toBe(14);
    expect(moved.getMinutes()).toBe(0);
  });
});
