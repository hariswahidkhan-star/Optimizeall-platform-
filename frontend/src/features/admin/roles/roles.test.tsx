import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { problem } from '@/test/fetchMock';
import { axeViolations } from '@/test/render';
import { ADMIN_PERMISSIONS, json, mockAdminApi, renderAdmin } from '../test/helpers';
import type { CustomRole, PermissionCatalog, RolesOverview } from './api';
import { RolesPage } from './RolesPage';
import { UserCustomRolesSection } from './UserCustomRolesSection';

const MANAGER_PERMISSIONS = [...ADMIN_PERMISSIONS, 'roles.manage', 'crm.view', 'crm.manage'];

const catalog: PermissionCatalog = {
  callerIsAdmin: false,
  areas: [
    {
      area: 'Administration',
      permissions: [
        { key: 'users.view', label: 'View users', description: 'Read the user directory.', sensitive: false, adminOnly: false, granted: true },
        { key: 'roles.manage', label: 'Manage custom roles', description: 'Only admins can grant this.', sensitive: true, adminOnly: true, granted: false },
        { key: 'audit.view', label: 'View audit log', description: 'Read the audit log.', sensitive: false, adminOnly: false, granted: true },
      ],
    },
    {
      area: 'CRM & sales',
      permissions: [
        { key: 'crm.view', label: 'View CRM', description: 'Contacts, companies and deals.', sensitive: false, adminOnly: false, granted: true },
        { key: 'crm.manage', label: 'Manage CRM', description: 'Create and edit CRM records.', sensitive: false, adminOnly: false, granted: true },
        { key: 'contracts.manage', label: 'Manage contracts', description: 'Draft and send contracts.', sensitive: false, adminOnly: false, granted: false },
      ],
    },
    {
      area: 'Client portal',
      permissions: [
        { key: 'client.portal', label: 'Client portal', description: 'Client users only.', sensitive: false, adminOnly: false, granted: true },
      ],
    },
  ],
};

function role(overrides: Partial<CustomRole> = {}): CustomRole {
  return {
    id: 'r1',
    name: 'CRM viewer',
    description: 'Read-only CRM access',
    permissions: ['crm.view'],
    isSystem: false,
    userCount: 0,
    createdAt: '2026-09-01T00:00:00Z',
    createdByUserId: 'admin-1',
    createdByName: 'Platform Admin',
    updatedAt: '2026-09-01T00:00:00Z',
    concurrencyStamp: 'stamp-1',
    canManage: true,
    ...overrides,
  };
}

const overview = (custom: CustomRole[] = [role()]): RolesOverview => ({
  builtIn: [
    { name: 'Finance', label: 'Finance', permissions: ['audit.view', 'users.view'], userCount: 2 },
    { name: 'Admin', label: 'Admin', permissions: ['audit.view', 'crm.view', 'roles.manage', 'users.view'], userCount: 1 },
  ],
  custom,
});

function mockRoles(routes: Parameters<typeof mockAdminApi>[0] = {}, custom?: CustomRole[]) {
  return mockAdminApi(
    {
      'GET /admin/roles': () => json(200, overview(custom)),
      'GET /admin/roles/catalog': () => json(200, catalog),
      ...routes,
    },
    MANAGER_PERMISSIONS,
  );
}

const renderRoles = () => renderAdmin(<RolesPage />, '/admin/roles', '/admin/roles');

describe('Roles & permissions page', () => {
  it('lists custom and read-only built-in roles', async () => {
    mockRoles();
    const { container } = renderRoles();
    const custom = await screen.findByRole('table', { name: 'Custom roles' });
    expect(within(custom).getByText('CRM viewer')).toBeInTheDocument();
    const builtIn = screen.getByRole('table', { name: 'Built-in roles' });
    expect(within(builtIn).getAllByText('Built-in')).toHaveLength(2);
    expect(within(builtIn).queryByRole('button', { name: /Edit/ })).not.toBeInTheDocument();

    await userEvent.setup().click(within(builtIn).getByRole('button', { name: 'View Finance permissions' }));
    const dialog = await screen.findByRole('dialog', { name: 'Finance permissions' });
    expect(within(dialog).getByText('View audit log')).toBeInTheDocument();
    expect(within(dialog).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('creates a role from grouped, searchable permission checkboxes', async () => {
    const user = userEvent.setup();
    const { calls } = mockRoles({
      'POST /admin/roles': (req) => json(201, { role: role({ id: 'r2', name: 'Sales assistant', permissions: (req.body as { permissions: string[] }).permissions }), holders: [] }),
    });
    renderRoles();
    await user.click(await screen.findByRole('button', { name: 'New role' }));
    const dialog = await screen.findByRole('dialog', { name: 'New role' });

    // Grouped by area with descriptions; permissions the caller can't grant are disabled and labelled.
    const crm = within(dialog).getByRole('group', { name: /CRM & sales/ });
    expect(within(crm).getByText('Contacts, companies and deals.')).toBeInTheDocument();
    expect(within(crm).getByRole('checkbox', { name: /Manage contracts/ })).toBeDisabled();
    expect(within(dialog).getByRole('checkbox', { name: /Manage custom roles/ })).toBeDisabled();
    expect(within(dialog).getAllByText('Admins only').length).toBeGreaterThan(0);

    // "Select all" in an area selects only what the caller may grant.
    await user.click(within(crm).getByRole('checkbox', { name: 'Select all in CRM & sales' }));
    expect(within(crm).getByRole('checkbox', { name: /View CRM/ })).toBeChecked();
    expect(within(crm).getByRole('checkbox', { name: /Manage CRM/ })).toBeChecked();
    expect(within(crm).getByRole('checkbox', { name: /Manage contracts/ })).not.toBeChecked();

    // Search narrows the list across areas.
    await user.type(within(dialog).getByRole('searchbox', { name: 'Search permissions' }), 'audit');
    expect(within(dialog).getByRole('checkbox', { name: /View audit log/ })).toBeInTheDocument();
    expect(within(dialog).queryByRole('checkbox', { name: /View CRM/ })).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('checkbox', { name: /View audit log/ }));
    await user.clear(within(dialog).getByRole('searchbox', { name: 'Search permissions' }));

    await user.type(within(dialog).getByLabelText(/Name/), 'Sales assistant');
    await user.click(within(dialog).getByRole('button', { name: 'Create role' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/admin/roles')).toBe(true));
    expect(calls.find((c) => c.method === 'POST' && c.path === '/admin/roles')?.body).toEqual({
      name: 'Sales assistant',
      description: null,
      permissions: ['audit.view', 'crm.manage', 'crm.view'],
    });
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'New role' })).not.toBeInTheDocument());
  });

  it('validates the name and blocks mixing the client portal with staff permissions', async () => {
    const user = userEvent.setup();
    const { calls } = mockRoles();
    renderRoles();
    await user.click(await screen.findByRole('button', { name: 'New role' }));
    const dialog = await screen.findByRole('dialog', { name: 'New role' });
    await user.click(within(dialog).getByRole('button', { name: 'Create role' }));
    expect(within(dialog).getByText('Enter a name of at least 2 characters.')).toBeInTheDocument();
    expect(within(dialog).getByText('Choose at least one permission.')).toBeInTheDocument();

    await user.type(within(dialog).getByLabelText(/Name/), 'Hybrid');
    await user.click(within(dialog).getByRole('checkbox', { name: /^Client portal/ }));
    await user.click(within(dialog).getByRole('checkbox', { name: /View CRM/ }));
    expect(within(dialog).getByText('Client and staff permissions can’t be mixed')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Create role' }));
    expect(within(dialog).getByText('The client portal can’t be combined with staff permissions.')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST' && c.path === '/admin/roles')).toBe(false);
  });

  it('edits a role with its concurrency stamp and shows server guardrail errors', async () => {
    const user = userEvent.setup();
    const { calls } = mockRoles({
      'PUT /admin/roles/r1': () =>
        problem(403, 'roles.cannot_grant_unheld', "You can't grant permissions you don't hold yourself: ledger.adjust."),
    });
    renderRoles();
    await user.click(await screen.findByRole('button', { name: 'Edit CRM viewer' }));
    const dialog = await screen.findByRole('dialog', { name: 'Edit CRM viewer' });
    expect(within(dialog).getByLabelText(/Name/)).toHaveValue('CRM viewer');
    expect(within(dialog).getByRole('checkbox', { name: /View CRM/ })).toBeChecked();
    await user.click(within(dialog).getByRole('checkbox', { name: /Manage CRM/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Save role' }));
    expect(await within(dialog).findByText(/You can't grant permissions you don't hold yourself/)).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      name: 'CRM viewer',
      description: 'Read-only CRM access',
      permissions: ['crm.manage', 'crm.view'],
      concurrencyStamp: 'stamp-1',
    });
  });

  it('offers only a read-only view of roles the caller cannot manage', async () => {
    mockRoles({}, [role({ id: 'r9', name: 'Finance plus', permissions: ['payouts.finalize'], canManage: false })]);
    renderRoles();
    expect(await screen.findByRole('button', { name: 'View Finance plus' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit Finance plus' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete Finance plus' })).not.toBeInTheDocument();
  });

  it('deletes an assigned role only after confirmation, optionally moving its holders', async () => {
    const user = userEvent.setup();
    const { fn } = mockRoles(
      { 'DELETE /admin/roles/r1': () => new Response(null, { status: 204 }) },
      [role({ userCount: 3 }), role({ id: 'r2', name: 'Sales assistant', permissions: ['crm.manage'] })],
    );
    renderRoles();
    await user.click(await screen.findByRole('button', { name: 'Delete CRM viewer' }));
    const dialog = await screen.findByRole('alertdialog', { name: /Delete CRM viewer/ });
    expect(within(dialog).getByText('3 people have this role')).toBeInTheDocument();
    const confirm = within(dialog).getByRole('button', { name: 'Delete role' });
    expect(confirm).toBeDisabled(); // the role name must be typed
    await user.selectOptions(within(dialog).getByLabelText(/Move them to/), 'r2');
    await user.type(within(dialog).getByLabelText(/Type/), 'CRM viewer');
    await user.click(confirm);
    await waitFor(() =>
      expect(fn.mock.calls.map(([url]) => String(url))).toContainEqual(
        expect.stringMatching(/\/admin\/roles\/r1\?confirm=true&reassignTo=r2$/),
      ),
    );
  });
});

describe('Custom roles on the user page', () => {
  it('assigns and unassigns roles immediately and disables roles the caller cannot grant', async () => {
    const user = userEvent.setup();
    const { calls } = mockRoles(
      {
        'PUT /admin/roles/r2/users/u-42': () => json(200, { userId: 'u-42', roles: [], effectivePermissions: [] }),
        'DELETE /admin/roles/r1/users/u-42': () => json(200, { userId: 'u-42', roles: [], effectivePermissions: [] }),
      },
      [role(), role({ id: 'r2', name: 'Sales assistant' }), role({ id: 'r3', name: 'Finance plus', canManage: false })],
    );
    renderAdmin(
      <UserCustomRolesSection
        userId="u-42"
        displayName="Sara Khan"
        assigned={[{ id: 'r1', name: 'CRM viewer', assignedAt: '2026-09-01T00:00:00Z' }]}
      />,
    );
    const group = await screen.findByRole('group', { name: 'Custom roles for Sara Khan' });
    expect(within(group).getByRole('checkbox', { name: /CRM viewer/ })).toBeChecked();
    expect(within(group).getByRole('checkbox', { name: /Finance plus/ })).toBeDisabled();

    await user.click(within(group).getByRole('checkbox', { name: /Sales assistant/ }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/admin/roles/r2/users/u-42')).toBe(true));
    await waitFor(() => expect(within(group).getByRole('checkbox', { name: /CRM viewer/ })).toBeEnabled());
    await user.click(within(group).getByRole('checkbox', { name: /CRM viewer/ }));
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/admin/roles/r1/users/u-42')).toBe(true));
  });

  it('shows the server refusal (e.g. client/staff conflict) inline', async () => {
    const user = userEvent.setup();
    mockRoles({
      'PUT /admin/roles/r1/users/u-42': () =>
        problem(409, 'roles.client_staff_conflict', "A user can't hold the client portal permission together with staff permissions."),
    });
    renderAdmin(<UserCustomRolesSection userId="u-42" displayName="Sara Khan" assigned={[]} />);
    await user.click(await screen.findByRole('checkbox', { name: /CRM viewer/ }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/client portal permission together with staff/);
  });

  it('is read-only without roles.manage', async () => {
    const { calls } = mockAdminApi({});
    renderAdmin(
      <UserCustomRolesSection
        userId="u-42"
        displayName="Sara Khan"
        assigned={[{ id: 'r1', name: 'CRM viewer', assignedAt: '2026-09-01T00:00:00Z' }]}
      />,
    );
    expect(await screen.findByText('CRM viewer')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(calls.some((c) => c.path.startsWith('/admin/roles'))).toBe(false);
  });
});
