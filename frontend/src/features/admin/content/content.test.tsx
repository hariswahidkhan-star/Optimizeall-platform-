import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { problem } from '@/test/fetchMock';
import { axeViolations } from '@/test/render';
import type { Banner } from '../api/types';
import { json, mockAdminApi, renderAdmin } from '../test/helpers';
import { ContentPage } from './ContentPage';

function banner(overrides: Partial<Banner> = {}): Banner {
  return {
    id: 'b1',
    title: 'Welcome back',
    body: 'New campaigns every week.',
    imageUrl: null,
    ctaLabel: 'Browse',
    ctaUrl: '/app/campaigns',
    audience: 'Everyone',
    countryCode: null,
    languageCode: null,
    startsAt: null,
    endsAt: null,
    sortOrder: 10,
    isActive: true,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    concurrencyStamp: 'stamp-1',
    ...overrides,
  };
}

const page = (items: Banner[]) =>
  json(200, { items, total: items.length, page: 1, pageSize: 25, totalPages: 1 });

describe('Content — banners', () => {
  it('sends the concurrency stamp and, on 409, keeps the draft and offers to load the latest version', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({
      'GET /admin/content/banners': () => page([banner()]),
      'PUT /admin/content/banners/b1': () =>
        problem(409, 'concurrency.conflict', 'This item was changed by someone else.'),
      'GET /admin/content/banners/b1': () =>
        json(200, banner({ title: 'Welcome back (edited elsewhere)', concurrencyStamp: 'stamp-2' })),
    });
    renderAdmin(<ContentPage />);

    await user.click(await screen.findByRole('button', { name: 'Actions for Welcome back' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Edit' }));
    const dialog = await screen.findByRole('dialog', { name: 'Edit banner' });
    const title = within(dialog).getByLabelText(/Title/);
    await user.clear(title);
    await user.type(title, 'My edit');
    // The preview follows the draft.
    expect(within(dialog).getByTestId('banner-preview')).toHaveTextContent('My edit');
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }));

    const put = calls.find((c) => c.method === 'PUT');
    expect(put?.body).toMatchObject({ title: 'My edit', concurrencyStamp: 'stamp-1' });
    expect(await within(dialog).findByText('Someone else changed this item')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Save changes' })).toBeDisabled();
    expect(title).toHaveValue('My edit');

    await user.click(within(dialog).getByRole('button', { name: 'Load latest version' }));
    await waitFor(() => expect(title).toHaveValue('Welcome back (edited elsewhere)'));
    expect(within(dialog).getByRole('button', { name: 'Save changes' })).toBeEnabled();
  });

  it('opens an empty form for each new banner', async () => {
    const user = userEvent.setup();
    mockAdminApi({
      'GET /admin/content/banners': () => page([banner()]),
      'POST /admin/content/banners': (req) =>
        json(201, banner({ id: 'b2', title: (req.body as Banner).title })),
    });
    renderAdmin(<ContentPage />);
    await screen.findByText('Welcome back');

    await user.click(screen.getByRole('button', { name: 'New banner' }));
    let dialog = await screen.findByRole('dialog', { name: 'New banner' });
    await user.type(within(dialog).getByLabelText(/Title/), 'First');
    await user.type(within(dialog).getByLabelText(/Body/), 'Some body');
    await user.click(within(dialog).getByRole('button', { name: 'Create banner' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'New banner' }));
    dialog = await screen.findByRole('dialog', { name: 'New banner' });
    expect(within(dialog).getByLabelText(/Title/)).toHaveValue('');
    expect(within(dialog).getByLabelText(/Body/)).toHaveValue('');
  });

  it('rejects unsafe links before calling the API', async () => {
    const user = userEvent.setup();
    const { calls } = mockAdminApi({ 'GET /admin/content/banners': () => page([]) });
    renderAdmin(<ContentPage />);
    await user.click((await screen.findAllByRole('button', { name: 'New banner' }))[0]!);
    const dialog = await screen.findByRole('dialog', { name: 'New banner' });
    await user.type(within(dialog).getByLabelText(/Title/), 'Hi');
    await user.type(within(dialog).getByLabelText(/Button label/), 'Go');
    await user.type(within(dialog).getByLabelText(/Button link/), 'javascript:alert(1)');
    await user.click(within(dialog).getByRole('button', { name: 'Create banner' }));
    expect(within(dialog).getByLabelText(/Button link/)).toHaveAttribute('aria-invalid', 'true');
    expect(calls.some((c) => c.method === 'POST' && c.path.startsWith('/admin/content'))).toBe(false);
  });

  it('maps PascalCase server field errors onto the form', async () => {
    const user = userEvent.setup();
    mockAdminApi({
      'GET /admin/content/banners': () => page([]),
      'POST /admin/content/banners': () =>
        json(400, {
          title: 'One or more validation errors occurred.',
          errors: { CtaUrl: ['Use an https:// link.'] },
        }),
    });
    renderAdmin(<ContentPage />);
    await user.click((await screen.findAllByRole('button', { name: 'New banner' }))[0]!);
    const dialog = await screen.findByRole('dialog', { name: 'New banner' });
    await user.type(within(dialog).getByLabelText(/Title/), 'Hi');
    await user.click(within(dialog).getByRole('button', { name: 'Create banner' }));
    expect(await within(dialog).findByText('Use an https:// link.')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/Button link/)).toHaveAttribute('aria-invalid', 'true');
  });

  it('reorders with the keyboard and saves the new order', async () => {
    const user = userEvent.setup();
    const items = [
      banner(),
      banner({ id: 'b2', title: 'Second', sortOrder: 20 }),
      banner({ id: 'b3', title: 'Third', sortOrder: 30 }),
    ];
    const { calls } = mockAdminApi({
      'GET /admin/content/banners': () => page(items),
      'POST /admin/content/banners/reorder': () => json(200, { updated: 3 }),
    });
    renderAdmin(<ContentPage />);
    await screen.findByText('Welcome back');
    await user.click(screen.getByRole('button', { name: 'Reorder' }));

    const handle = await screen.findByRole('button', { name: /Reorder Third, position 3 of 3/ });
    handle.focus();
    await user.keyboard(' ');
    expect(handle).toHaveAttribute('aria-pressed', 'true');
    await user.keyboard('{ArrowUp}{ArrowUp} ');
    expect(await screen.findByText(/Third dropped at position 1 of 3/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Reorder Third, position 1 of 3/ })).toHaveFocus();

    const list = screen.getByRole('list', { name: 'Banners order' });
    expect(
      within(list)
        .getAllByRole('listitem')
        .map((li) => li.textContent),
    ).toEqual([
      expect.stringContaining('Third'),
      expect.stringContaining('Welcome back'),
      expect.stringContaining('Second'),
    ]);
    await user.click(screen.getByRole('button', { name: 'Save order' }));
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/admin/content/banners/reorder')?.body).toEqual({
        ids: ['b3', 'b1', 'b2'],
      }),
    );
  });

  it('Escape cancels a keyboard move', async () => {
    const user = userEvent.setup();
    mockAdminApi({
      'GET /admin/content/banners': () => page([banner(), banner({ id: 'b2', title: 'Second' })]),
    });
    renderAdmin(<ContentPage />);
    await screen.findByText('Welcome back');
    await user.click(screen.getByRole('button', { name: 'Reorder' }));
    const handle = await screen.findByRole('button', { name: /Reorder Second, position 2 of 2/ });
    handle.focus();
    await user.keyboard(' {ArrowUp}{Escape}');
    expect(screen.getByRole('button', { name: /Reorder Second, position 2 of 2/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save order' })).toBeDisabled();
  });

  it('has no axe violations with the editor open', async () => {
    const user = userEvent.setup();
    mockAdminApi({ 'GET /admin/content/banners': () => page([banner()]) });
    const { baseElement } = renderAdmin(<ContentPage />);
    await screen.findByText('Welcome back');
    expect(await axeViolations(baseElement)).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'New banner' }));
    await screen.findByRole('dialog');
    expect(await axeViolations(baseElement)).toEqual([]);
  });
});
