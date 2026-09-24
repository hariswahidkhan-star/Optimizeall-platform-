import { Eye, Lock, Pencil, Plus, ShieldCheck, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable } from '@/components/ui/DataTable';
import { DateTime } from '@/components/ui/DateTime';
import { Dialog } from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { FormField } from '@/components/ui/FormField';
import { PageHeader } from '@/components/ui/PageHeader';
import { Select } from '@/components/ui/Select';
import { SkeletonText } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { formatNumber } from '@/lib/format/money';
import { QueryError } from '../shared/common';
import { toDisplayError } from '../shared/errors';
import {
  useDeleteRole,
  usePermissionCatalog,
  useRolesOverview,
  type BuiltInRole,
  type CustomRole,
  type PermissionCatalog,
} from './api';
import { RoleEditorDialog } from './RoleEditor';
import './roles.css';

/** Read-only list of a role's permissions grouped by catalog area. */
export function PermissionList({ permissions, catalog }: { permissions: readonly string[]; catalog: PermissionCatalog }) {
  const held = new Set(permissions);
  const groups = catalog.areas
    .map((a) => ({ area: a.area, items: a.permissions.filter((p) => held.has(p.key)) }))
    .filter((g) => g.items.length > 0);
  if (groups.length === 0) return <p className="text-muted">No permissions.</p>;
  return (
    <div className="stack">
      {groups.map((g) => (
        <section key={g.area} aria-label={g.area}>
          <h3 className="roles-area__title">{g.area}</h3>
          <ul className="roles-readonly-list">
            {g.items.map((p) => (
              <li key={p.key}>
                <span className="roles-permission__label">
                  {p.label} <code className="roles-permission__key">{p.key}</code>
                </span>
                <span className="text-small text-muted">{p.description}</span>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

function ViewRoleDialog({
  role,
  catalog,
  onClose,
}: {
  role: { name: string; permissions: string[]; builtIn: boolean } | null;
  catalog: PermissionCatalog;
  onClose: () => void;
}) {
  return (
    <Dialog
      open={!!role}
      onClose={onClose}
      size="lg"
      icon={<Lock />}
      title={role ? `${role.name} permissions` : ''}
      description={
        role?.builtIn
          ? 'Built-in roles are defined by the platform and can’t be changed here. Create a custom role to grant a different set.'
          : 'You can’t change this role because it includes permissions you can’t grant.'
      }
      footer={
        <Button variant="secondary" onClick={onClose}>
          Close
        </Button>
      }
    >
      {role && <PermissionList permissions={role.permissions} catalog={catalog} />}
    </Dialog>
  );
}

function DeleteRoleDialog({
  role,
  others,
  onClose,
}: {
  role: CustomRole | null;
  others: CustomRole[];
  onClose: () => void;
}) {
  const toast = useToast();
  const remove = useDeleteRole();
  const [reassignTo, setReassignTo] = useState('');
  const inUse = (role?.userCount ?? 0) > 0;
  const targets = others.filter((r) => r.id !== role?.id && r.canManage);

  return (
    <ConfirmDialog
      open={!!role}
      onClose={() => {
        setReassignTo('');
        onClose();
      }}
      tone="danger"
      title={role ? `Delete ${role.name}?` : ''}
      description={
        inUse
          ? undefined
          : 'Nobody has this role. Deleting it can’t be undone.'
      }
      confirmLabel="Delete role"
      confirmText={inUse ? role?.name : undefined}
      onConfirm={async () => {
        if (!role) return;
        try {
          await remove.mutateAsync({ id: role.id, confirm: inUse, reassignTo: inUse ? reassignTo || null : null });
        } catch (error) {
          throw toDisplayError(error);
        }
        toast.success('Role deleted', `${role.name} was deleted.`);
        setReassignTo('');
      }}
    >
      {inUse && role && (
        <>
          <Alert tone="warning" title={`${formatNumber(role.userCount)} ${role.userCount === 1 ? 'person has' : 'people have'} this role`}>
            They lose its permissions on their next request unless you move them to another role.
          </Alert>
          <FormField label="Move them to" optional hint="Only roles you can assign are listed.">
            <Select
              value={reassignTo}
              onChange={(e) => setReassignTo(e.target.value)}
              options={[
                { value: '', label: 'Nobody (just remove the role)' },
                ...targets.map((r) => ({ value: r.id, label: r.name })),
              ]}
            />
          </FormField>
        </>
      )}
    </ConfirmDialog>
  );
}

export function RolesPage() {
  const overview = useRolesOverview();
  const catalog = usePermissionCatalog();
  const [editing, setEditing] = useState<{ role: CustomRole | null } | null>(null);
  const [viewing, setViewing] = useState<{ name: string; permissions: string[]; builtIn: boolean } | null>(null);
  const [deleting, setDeleting] = useState<CustomRole | null>(null);

  const header = (
    <PageHeader
      title="Roles & permissions"
      description="Built-in roles come with the platform. Custom roles bundle exactly the permissions you choose; a person’s access is everything their built-in and custom roles grant."
      breadcrumbs={[{ label: 'Admin', to: '/admin' }, { label: 'Roles & permissions' }]}
      actions={
        <Button leadingIcon={<Plus />} onClick={() => setEditing({ role: null })} disabled={!catalog.data}>
          New role
        </Button>
      }
    />
  );

  if (overview.isPending || catalog.isPending) {
    return (
      <>
        {header}
        <Card>
          <CardBody>
            <SkeletonText lines={8} />
          </CardBody>
        </Card>
      </>
    );
  }
  if (overview.isError || catalog.isError) {
    const error = overview.error ?? catalog.error;
    return (
      <>
        {header}
        <QueryError
          error={error}
          onRetry={() => {
            void overview.refetch();
            void catalog.refetch();
          }}
        />
      </>
    );
  }

  const { builtIn, custom } = overview.data;
  const cat = catalog.data;

  return (
    <>
      {header}
      <Card as="section" aria-labelledby="custom-roles-heading">
        <CardHeader
          title="Custom roles"
          titleId="custom-roles-heading"
          description="Assign them to people from their user page. Changes apply right away, without signing out."
        />
        <CardBody flush>
          <DataTable<CustomRole>
            caption="Custom roles"
            rows={custom}
            getRowId={(r) => r.id}
            emptyState={
              <EmptyState
                compact
                headingLevel={3}
                icon={<ShieldCheck />}
                title="No custom roles yet"
                description="Create one to give someone exactly the access they need, e.g. “CRM viewer”."
              />
            }
            columns={[
              {
                id: 'name',
                header: 'Role',
                primary: true,
                cell: (r) => (
                  <div className="admin-cell-stack">
                    <span className="roles-name">{r.name}</span>
                    {r.description && <span className="text-small text-muted admin-clamp">{r.description}</span>}
                  </div>
                ),
              },
              {
                id: 'permissions',
                header: 'Permissions',
                align: 'right',
                cell: (r) => formatNumber(r.permissions.length),
              },
              { id: 'users', header: 'People', align: 'right', cell: (r) => formatNumber(r.userCount) },
              {
                id: 'updated',
                header: 'Updated',
                hideOnMobile: true,
                cell: (r) => <DateTime value={r.updatedAt} />,
              },
              {
                id: 'actions',
                header: <span className="visually-hidden">Actions</span>,
                align: 'right',
                cell: (r) =>
                  r.canManage ? (
                    <div className="cluster roles-actions">
                      <Button
                        size="sm"
                        variant="secondary"
                        leadingIcon={<Pencil />}
                        aria-label={`Edit ${r.name}`}
                        onClick={() => setEditing({ role: r })}
                      >
                        Edit
                      </Button>
                      <Button
                        size="sm"
                        variant="danger"
                        leadingIcon={<Trash2 />}
                        aria-label={`Delete ${r.name}`}
                        onClick={() => setDeleting(r)}
                      >
                        Delete
                      </Button>
                    </div>
                  ) : (
                    <Button
                      size="sm"
                      variant="secondary"
                      leadingIcon={<Eye />}
                      aria-label={`View ${r.name}`}
                      onClick={() => setViewing({ name: r.name, permissions: r.permissions, builtIn: false })}
                    >
                      View
                    </Button>
                  ),
              },
            ]}
          />
        </CardBody>
      </Card>

      <Card as="section" aria-labelledby="builtin-roles-heading">
        <CardHeader
          title="Built-in roles"
          titleId="builtin-roles-heading"
          description="Read-only. Change a person’s built-in roles from their user page (signs them out)."
        />
        <CardBody flush>
          <DataTable<BuiltInRole>
            caption="Built-in roles"
            rows={builtIn}
            getRowId={(r) => r.name}
            columns={[
              {
                id: 'name',
                header: 'Role',
                primary: true,
                cell: (r) => (
                  <span className="cluster admin-tight">
                    <span className="roles-name">{r.label}</span>
                    <Badge tone="neutral" icon={<Lock />}>
                      Built-in
                    </Badge>
                  </span>
                ),
              },
              {
                id: 'permissions',
                header: 'Permissions',
                align: 'right',
                cell: (r) => formatNumber(r.permissions.length),
              },
              { id: 'users', header: 'People', align: 'right', cell: (r) => formatNumber(r.userCount) },
              {
                id: 'actions',
                header: <span className="visually-hidden">Actions</span>,
                align: 'right',
                cell: (r) => (
                  <Button
                    size="sm"
                    variant="secondary"
                    leadingIcon={<Eye />}
                    aria-label={`View ${r.label} permissions`}
                    onClick={() => setViewing({ name: r.label, permissions: r.permissions, builtIn: true })}
                  >
                    View
                  </Button>
                ),
              },
            ]}
          />
        </CardBody>
      </Card>

      <RoleEditorDialog
        open={!!editing}
        role={editing?.role ?? null}
        catalog={cat}
        onClose={() => setEditing(null)}
      />
      <ViewRoleDialog role={viewing} catalog={cat} onClose={() => setViewing(null)} />
      <DeleteRoleDialog role={deleting} others={custom} onClose={() => setDeleting(null)} />
    </>
  );
}
