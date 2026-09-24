import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { getPortal } from '../portals';
import { PortalLayout } from './PortalLayout';

const staff = (permissions: string[]) => () =>
  json(200, session(makeUser({ roles: ['AccountManager'], permissions })));

const results = {
  query: 'acme',
  groups: [
    {
      type: 'clients',
      label: 'Clients',
      items: [{ id: 'c1', title: 'Acme Ltd', subtitle: 'Retail · Active', url: '/agency/clients/c1' }],
    },
    {
      type: 'users',
      label: 'Users',
      // The caller can't open the admin portal, so this hit is hidden.
      items: [{ id: 'u9', title: 'Acme Admin', subtitle: 'a@acme.test', url: '/admin/users/u9' }],
    },
    {
      type: 'invoices',
      label: 'Invoices',
      items: [
        { id: 'i1', title: 'INV-001', subtitle: 'Acme Ltd · Issued', url: '/agency/billing/invoices/i1' },
      ],
    },
  ],
};

function renderAgency(permissions = ['clients.view', 'crm.view', 'billing.view']) {
  const mock = mockFetch({
    'POST /auth/refresh': staff(permissions),
    'GET /search': () => json(200, results),
  });
  const utils = renderWithApp(<PortalLayout portal={getPortal('agency')} />, {
    route: '/agency',
    path: '/agency/*',
    routes: [
      { path: '/agency/clients/:id', element: <p>client page</p> },
      { path: '/agency/billing/invoices/:id', element: <p>invoice page</p> },
    ],
  });
  return { ...mock, ...utils };
}

describe('CommandPalette', () => {
  it('opens with Ctrl+K, searches records the user may open and navigates with the keyboard', async () => {
    const user = userEvent.setup();
    const { calls, baseElement, router } = renderAgency();
    await screen.findByRole('button', { name: 'Search' });

    await user.keyboard('{Control>}k{/Control}');
    const dialog = await screen.findByRole('dialog', { name: 'Search' });
    const input = within(dialog).getByRole('combobox', { name: 'Search pages and records' });
    expect(input).toHaveFocus();
    // Page shortcuts before typing.
    expect(within(dialog).getByRole('group', { name: 'Go to' })).toBeInTheDocument();

    await user.type(input, 'a');
    expect(within(dialog).getByRole('status')).toHaveTextContent('Type at least 2 characters');
    expect(calls.some((c) => c.path === '/search')).toBe(false);

    await user.type(input, 'cme');
    const clients = await within(dialog).findByRole('group', { name: 'Clients' });
    expect(within(clients).getByRole('option', { name: /Acme Ltd/ })).toBeInTheDocument();
    expect(within(dialog).queryByRole('option', { name: /Acme Admin/ })).not.toBeInTheDocument();
    await waitFor(() => expect(within(dialog).getByRole('status')).toHaveTextContent(/\d+ results?\./));
    expect(calls.find((c) => c.path === '/search')).toBeTruthy();
    expect(await axeViolations(baseElement)).toEqual([]);

    // The first option is active; ArrowDown moves to the next and Enter opens it.
    const options = within(dialog).getAllByRole('option');
    expect(input).toHaveAttribute('aria-activedescendant', options[0]!.id);
    await user.keyboard('{ArrowDown}');
    expect(input).toHaveAttribute('aria-activedescendant', options[1]!.id);
    expect(options[1]).toHaveAttribute('aria-selected', 'true');

    await user.keyboard('{ArrowUp}{Enter}');
    await waitFor(() => expect(router.state.location.pathname).toBe('/agency/clients/c1'));
    expect(screen.queryByRole('dialog', { name: 'Search' })).not.toBeInTheDocument();
  });

  it('filters page shortcuts by name and closes with Escape', async () => {
    const user = userEvent.setup();
    renderAgency();
    await user.click(await screen.findByRole('button', { name: 'Search' }));
    const dialog = await screen.findByRole('dialog', { name: 'Search' });
    await user.type(within(dialog).getByRole('combobox'), 'invoic');
    const pages = within(dialog).getByRole('group', { name: 'Go to' });
    expect(
      within(pages)
        .getAllByRole('option')
        .every((o) => /invoic/i.test(o.textContent ?? '')),
    ).toBe(true);
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Search' })).not.toBeInTheDocument());
  });

  it('offers only page shortcuts, without calling GET /search, to staff with no searchable permission', async () => {
    // Regression: a content-only admin (content.manage) got a 403 from /search on every keystroke.
    const user = userEvent.setup();
    const { calls } = (() => {
      const mock = mockFetch({
        'POST /auth/refresh': staff(['content.manage']),
        'GET /search': () => json(403, { code: 'search.forbidden' }),
      });
      renderWithApp(<PortalLayout portal={getPortal('admin')} />, { route: '/admin', path: '/admin/*' });
      return mock;
    })();
    await user.click(await screen.findByRole('button', { name: 'Search' }));
    const dialog = await screen.findByRole('dialog', { name: 'Search' });
    await user.type(within(dialog).getByRole('combobox'), 'Content');
    const pages = await within(dialog).findByRole('group', { name: 'Go to' });
    expect(within(pages).getByRole('option', { name: /Content/ })).toBeInTheDocument();
    // Let the search debounce (250 ms) elapse: a record search would have been sent by now.
    await new Promise((resolve) => setTimeout(resolve, 600));
    expect(calls.some((c) => c.path === '/search')).toBe(false);
    expect(within(dialog).getByRole('status')).toHaveTextContent(/\d+ results?\./);
  });

  it('is not offered in portals without staff search', async () => {
    mockFetch({ 'POST /auth/refresh': staff(['participant.portal']) });
    renderWithApp(<PortalLayout portal={getPortal('participant')} />, { route: '/app', path: '/app/*' });
    await screen.findByRole('button', { name: /Account menu/ });
    expect(screen.queryByRole('button', { name: 'Search' })).not.toBeInTheDocument();
  });
});
