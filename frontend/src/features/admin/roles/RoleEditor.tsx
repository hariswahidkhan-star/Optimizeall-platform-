import { Search, ShieldPlus } from 'lucide-react';
import { useEffect, useId, useMemo, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/Checkbox';
import { Dialog } from '@/components/ui/Dialog';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Textarea } from '@/components/ui/Textarea';
import { useToast } from '@/components/ui/toastContext';
import { mapFieldErrors } from '../shared/errors';
import {
  mixesClientAndStaff,
  useSaveRole,
  type CustomRole,
  type PermissionArea,
  type PermissionCatalog,
  type PermissionInfo,
} from './api';

const FIELDS = ['name', 'description', 'permissions'] as const;
const CODE_TO_FIELD = {
  'roles.name_taken': 'name',
  'roles.name_reserved': 'name',
  'roles.invalid_name': 'name',
  'roles.cannot_grant_unheld': 'permissions',
  'roles.admin_only_permission': 'permissions',
  'roles.client_portal_mixed': 'permissions',
  'roles.client_staff_conflict': 'permissions',
};

function matches(p: PermissionInfo, area: string, term: string): boolean {
  if (!term) return true;
  return [p.key, p.label, p.description, area].some((text) => text.toLowerCase().includes(term));
}

/** One area of the catalog: a "select all" checkbox (grantable permissions only) and a checkbox per permission. */
function PermissionGroup({
  area,
  visible,
  selected,
  onChange,
  readOnly,
}: {
  area: PermissionArea;
  visible: PermissionInfo[];
  selected: ReadonlySet<string>;
  onChange: (keys: string[], checked: boolean) => void;
  readOnly: boolean;
}) {
  const headingId = useId();
  const selectable = visible.filter((p) => p.granted);
  const selectedCount = area.permissions.filter((p) => selected.has(p.key)).length;
  const allSelected = selectable.length > 0 && selectable.every((p) => selected.has(p.key));
  const someSelected = selectable.some((p) => selected.has(p.key));

  return (
    <fieldset className="admin-fieldset roles-area" aria-labelledby={headingId}>
      <div className="roles-area__header">
        <h3 id={headingId} className="roles-area__title">
          {area.area}
          <span className="text-small text-muted roles-area__count">
            {selectedCount} of {area.permissions.length} selected
          </span>
        </h3>
        {!readOnly && selectable.length > 0 && (
          <Checkbox
            label={`Select all in ${area.area}`}
            checked={allSelected}
            indeterminate={!allSelected && someSelected}
            onChange={(e) => onChange(selectable.map((p) => p.key), e.target.checked)}
          />
        )}
      </div>
      <div className="roles-area__grid">
        {visible.map((p) => (
          <Checkbox
            key={p.key}
            checked={selected.has(p.key)}
            disabled={readOnly || (!p.granted && !selected.has(p.key))}
            onChange={(e) => onChange([p.key], e.target.checked)}
            label={
              <span className="roles-permission__label">
                {p.label} <code className="roles-permission__key">{p.key}</code>
              </span>
            }
            description={
              <>
                {p.description}
                {(p.sensitive || p.adminOnly || !p.granted) && (
                  <span className="roles-permission__badges">
                    {p.sensitive && <Badge tone="warning">Sensitive</Badge>}
                    {p.adminOnly && <Badge tone="danger">Admins only</Badge>}
                    {!p.granted && <Badge tone="neutral">You can’t grant this</Badge>}
                  </span>
                )}
              </>
            }
          />
        ))}
      </div>
    </fieldset>
  );
}

/** Grouped, searchable permission checkboxes (controlled). */
export function PermissionPicker({
  catalog,
  selected,
  onSelectedChange,
  readOnly = false,
  error,
}: {
  catalog: PermissionCatalog;
  selected: ReadonlySet<string>;
  onSelectedChange: (next: Set<string>) => void;
  readOnly?: boolean;
  error?: string[];
}) {
  const [search, setSearch] = useState('');
  const term = search.trim().toLowerCase();
  const errorId = useId();
  const groups = catalog.areas
    .map((area) => ({ area, visible: area.permissions.filter((p) => matches(p, area.area, term)) }))
    .filter((g) => g.visible.length > 0);

  const change = (keys: string[], checked: boolean) => {
    const next = new Set(selected);
    for (const key of keys) {
      if (checked) next.add(key);
      else next.delete(key);
    }
    onSelectedChange(next);
  };

  return (
    <div className="stack roles-picker" aria-describedby={error?.length ? errorId : undefined}>
      <FormField label="Search permissions" hideLabel>
        <Input
          type="search"
          placeholder="Search permissions"
          leading={<Search />}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </FormField>
      <p className="text-small text-muted" aria-live="polite">
        {selected.size} permission{selected.size === 1 ? '' : 's'} selected
      </p>
      {error && error.length > 0 && (
        <p id={errorId} className="ui-field__error" role="alert">
          {error.join(' ')}
        </p>
      )}
      {groups.length === 0 ? (
        <p className="text-muted">No permissions match “{search}”.</p>
      ) : (
        groups.map((g) => (
          <PermissionGroup
            key={g.area.area}
            area={g.area}
            visible={g.visible}
            selected={selected}
            onChange={change}
            readOnly={readOnly}
          />
        ))
      )}
    </div>
  );
}

/** Create or edit a custom role. */
export function RoleEditorDialog({
  open,
  onClose,
  role,
  catalog,
}: {
  open: boolean;
  onClose: () => void;
  role: CustomRole | null;
  catalog: PermissionCatalog;
}) {
  const formId = useId();
  const toast = useToast();
  const save = useSaveRole();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [clientErrors, setClientErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!open) return;
    setName(role?.name ?? '');
    setDescription(role?.description ?? '');
    setSelected(new Set(role?.permissions ?? []));
    setClientErrors({});
    save.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reset only when the dialog opens for a role
  }, [open, role]);

  const server = useMemo(() => mapFieldErrors(save.error, FIELDS, CODE_TO_FIELD), [save.error]);
  const errorFor = (field: string): string[] | undefined =>
    clientErrors[field] ? [clientErrors[field]] : server.fields[field];
  const mixed = mixesClientAndStaff([...selected]);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const errors: Record<string, string> = {};
    if (name.trim().length < 2) errors.name = 'Enter a name of at least 2 characters.';
    if (selected.size === 0) errors.permissions = 'Choose at least one permission.';
    else if (mixed) errors.permissions = 'The client portal can’t be combined with staff permissions.';
    setClientErrors(errors);
    if (Object.keys(errors).length > 0) return;
    save.mutate(
      {
        id: role?.id,
        input: {
          name: name.trim(),
          description: description.trim() || null,
          permissions: [...selected].sort(),
          concurrencyStamp: role?.concurrencyStamp,
        },
      },
      {
        onSuccess: (saved) => {
          toast.success(role ? 'Role updated' : 'Role created', `${saved.role.name} is saved. Changes apply right away.`);
          onClose();
        },
      },
    );
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="lg"
      icon={<ShieldPlus />}
      title={role ? `Edit ${role.name}` : 'New role'}
      description="People with this role get these permissions in addition to their built-in roles. Changes apply on their next request."
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={save.isPending}>
            {role ? 'Save role' : 'Create role'}
          </Button>
        </>
      }
    >
      <form id={formId} className="stack" onSubmit={submit} noValidate aria-label="Role">
        {server.form && (
          <Alert tone="danger" role="alert" title={server.form.title}>
            {server.form.details.length > 0 && (
              <ul>
                {server.form.details.map((d) => (
                  <li key={d}>{d}</li>
                ))}
              </ul>
            )}
          </Alert>
        )}
        <FormField label="Name" required error={errorFor('name')}>
          <Input value={name} maxLength={80} onChange={(e) => setName(e.target.value)} />
        </FormField>
        <FormField label="Description" optional error={errorFor('description')}>
          <Textarea rows={2} maxLength={2000} value={description} onChange={(e) => setDescription(e.target.value)} />
        </FormField>
        {mixed && (
          <Alert tone="warning" title="Client and staff permissions can’t be mixed">
            The client portal permission is for client users only. Remove it or the staff permissions.
          </Alert>
        )}
        <section aria-label="Permissions">
          <h2 className="ui-field__label">Permissions</h2>
          {!catalog.callerIsAdmin && (
            <p className="text-small text-muted">
              You can only grant permissions you hold yourself. Some permissions can only be granted by administrators.
            </p>
          )}
          <PermissionPicker
            catalog={catalog}
            selected={selected}
            onSelectedChange={(next) => {
              setClientErrors((current) => ({ ...current, permissions: '' }));
              setSelected(next);
            }}
            error={errorFor('permissions')?.filter(Boolean)}
          />
        </section>
      </form>
    </Dialog>
  );
}
