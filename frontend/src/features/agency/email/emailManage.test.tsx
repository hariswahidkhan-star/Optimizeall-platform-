import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { json, makeUser, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { AutomationListItem } from './api/types';
import { RenameKeyDialog, TagsFieldsPage } from './audience/TagsFieldsPage';
import { AutomationsPage } from './automations/AutomationsPage';

const staff = makeUser({
  id: 'staff-1',
  roles: ['ContentCreator'],
  permissions: ['email.manage', 'email.send'],
  timeZone: 'UTC',
});

describe('email tags & fields', () => {
  it('lists tags and fields with usage and has no axe violations', async () => {
    mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/email/tags': () => json(200, [{ key: 'vip', contacts: 12, referencedBy: 2 }]),
      'GET /agency/email/fields': () => json(200, [{ key: 'plan', contacts: 4, referencedBy: 0 }]),
    });
    const { container } = renderWithApp(<TagsFieldsPage />);
    const tags = await screen.findByRole('table', { name: 'Tags' });
    expect(await within(tags).findByText('vip')).toBeInTheDocument();
    expect(within(tags).getByText('12')).toBeInTheDocument();
    expect(await screen.findByText('plan')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('renames a tag, warns about segments that mention it and shows validation errors', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    let attempt = 0;
    const { calls } = mockFetch({
      'POST /agency/email/tags/rename': () =>
        ++attempt === 1
          ? problem(400, 'validation.failed', 'Tags are 1–50 letters.', {
              errors: { to: ['Tags are 1–50 letters, digits, spaces, dashes or underscores.'] },
            })
          : json(200, { from: 'vip', to: 'gold', changed: 3, merged: 1, referencedBy: 2 }),
    });
    const { container } = renderWithApp(
      <RenameKeyDialog
        kind="tags"
        item={{ key: 'vip', contacts: 4, referencedBy: 2 }}
        onClose={() => {}}
        onSaved={onSaved}
      />,
      { withAuth: false },
    );
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/2 segment\(s\) or journey\(s\) use “vip”/)).toBeInTheDocument();
    const input = within(dialog).getByLabelText(/New name/);
    await user.clear(input);
    await user.type(input, '!!');
    await user.click(within(dialog).getByRole('button', { name: 'Rename' }));
    expect(
      await within(dialog).findByText('Tags are 1–50 letters, digits, spaces, dashes or underscores.'),
    ).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);

    await user.clear(input);
    await user.type(input, 'gold');
    await user.click(within(dialog).getByRole('button', { name: 'Rename' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(calls.filter((c) => c.method === 'POST').at(-1)?.body).toEqual({
      clientAccountId: null,
      from: 'vip',
      to: 'gold',
    });
  });
});

describe('journeys list actions', () => {
  it('explains why a journey with history cannot be deleted and duplicates on request', async () => {
    const user = userEvent.setup();
    const journey: AutomationListItem = {
      id: 'j1',
      clientAccountId: null,
      name: 'Welcome',
      status: 'Paused',
      trigger: 'ListSubscribed',
      active: 0,
      completed: 5,
      exited: 1,
      updatedAt: '2026-09-01T00:00:00Z',
    };
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(staff)),
      'GET /agency/email/automations': () => json(200, [journey]),
      'POST /agency/email/automations/j1/duplicate': () =>
        json(200, { ...journey, id: 'j2', name: 'Welcome (copy)', status: 'Draft', steps: [] }),
    });
    renderWithApp(<AutomationsPage />, {
      route: '/agency/email/automations',
      path: '/agency/email/automations',
      routes: [{ path: '/agency/email/automations/:id', element: <p>Editor</p> }],
    });
    await user.click(await screen.findByRole('button', { name: /Actions for Welcome/ }));
    const del = await screen.findByRole('menuitem', { name: /Delete/ });
    expect(del).toHaveAttribute('aria-disabled', 'true');
    expect(del).toHaveTextContent('Contacts entered it — archive it to keep its statistics.');
    await user.click(screen.getByRole('menuitem', { name: /Duplicate/ }));
    await waitFor(() =>
      expect(
        calls.some((c) => c.method === 'POST' && c.path === '/agency/email/automations/j1/duplicate'),
      ).toBe(true),
    );
    expect(await screen.findByText('Editor')).toBeInTheDocument();
  });
});
