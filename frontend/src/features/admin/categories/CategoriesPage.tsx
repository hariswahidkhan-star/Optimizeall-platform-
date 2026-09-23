import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { FolderTree, Pencil, Plus, Power, Trash2 } from 'lucide-react';
import { useEffect, useId, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable } from '@/components/ui/DataTable';
import { Dialog } from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { PageHeader } from '@/components/ui/PageHeader';
import { Switch } from '@/components/ui/Switch';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { formatNumber } from '@/lib/format/money';
import type { Category, CategoryDeleteResult } from '../api/types';
import { orNull, QueryError } from '../shared/common';
import { mapFieldErrors, toDisplayError } from '../shared/errors';
import { TextAreaField, TextField } from '../content/fields';

const KEY = ['admin', 'categories'] as const;
const FIELDS = ['name', 'slug', 'description', 'icon', 'sortOrder', 'isActive'] as const;
const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

interface Draft {
  name: string;
  slug: string;
  description: string;
  icon: string;
  sortOrder: string;
  isActive: boolean;
}

function toDraft(c: Category | null): Draft {
  return {
    name: c?.name ?? '',
    slug: c?.slug ?? '',
    description: c?.description ?? '',
    icon: c?.icon ?? '',
    sortOrder: String(c?.sortOrder ?? 0),
    isActive: c?.isActive ?? true,
  };
}

function toBody(d: Draft) {
  return {
    name: d.name.trim(),
    slug: orNull(d.slug),
    description: orNull(d.description),
    icon: orNull(d.icon),
    sortOrder: Number(d.sortOrder || '0'),
    isActive: d.isActive,
  };
}

function CategoryDialog({
  category,
  open,
  onClose,
}: {
  category: Category | null;
  open: boolean;
  onClose: () => void;
}) {
  const formId = useId();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<Draft>(toDraft(category));
  const [clientErrors, setClientErrors] = useState<Record<string, string>>({});

  const save = useMutation({
    mutationFn: (body: ReturnType<typeof toBody>) =>
      category
        ? api.put<Category>(`/admin/campaign-categories/${category.id}`, body)
        : api.post<Category>('/admin/campaign-categories', body),
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: KEY });
      toast.success(category ? 'Category saved' : 'Category created', saved.name);
      onClose();
    },
  });

  useEffect(() => {
    if (open) {
      setDraft(toDraft(category));
      setClientErrors({});
      save.reset();
    }
    // Reset only when opened.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, category?.id]);

  const server = mapFieldErrors(save.error, FIELDS, { 'category.slug_taken': 'slug' });
  const errors = { ...server.fields, ...clientErrors };
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => setDraft({ ...draft, [key]: value });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const found: Record<string, string> = {};
    const name = draft.name.trim();
    if (name.length < 2 || name.length > 100) found.name = 'Use 2–100 characters.';
    if (draft.slug.trim() && !SLUG.test(draft.slug.trim()))
      found.slug = 'Lower-case letters, digits and single dashes.';
    const sort = Number(draft.sortOrder || '0');
    if (!Number.isInteger(sort)) found.sortOrder = 'Use a whole number.';
    setClientErrors(found);
    if (Object.keys(found).length === 0) save.mutate(toBody(draft));
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={category ? `Edit ${category.name}` : 'New category'}
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={save.isPending}>
            {category ? 'Save category' : 'Create category'}
          </Button>
        </>
      }
    >
      <form id={formId} className="stack" onSubmit={submit} noValidate>
        {server.form && (
          <Alert tone="danger" role="alert" title={server.form.title}>
            {server.form.details.join(' ')}
          </Alert>
        )}
        <TextField
          label="Name"
          required
          maxLength={100}
          value={draft.name}
          onChange={(v) => set('name', v)}
          error={errors.name}
        />
        <TextField
          label="Slug"
          optional
          maxLength={100}
          value={draft.slug}
          onChange={(v) => set('slug', v)}
          error={errors.slug}
          hint="Used in URLs. Leave empty to generate it from the name."
        />
        <TextAreaField
          label="Description"
          optional
          maxLength={500}
          rows={3}
          value={draft.description}
          onChange={(v) => set('description', v)}
          error={errors.description}
        />
        <div className="admin-form-grid">
          <TextField
            label="Icon name"
            optional
            maxLength={50}
            value={draft.icon}
            onChange={(v) => set('icon', v)}
            error={errors.icon}
            hint="A Lucide icon name, e.g. cpu, gamepad-2, shirt."
          />
          <TextField
            label="Sort order"
            type="number"
            value={draft.sortOrder}
            onChange={(v) => set('sortOrder', v)}
            error={errors.sortOrder}
            hint="Lower numbers show first."
          />
        </div>
        <Switch
          checked={draft.isActive}
          onCheckedChange={(v) => set('isActive', v)}
          label="Active"
          description="Inactive categories are hidden from participants and can’t be chosen for new campaigns."
        />
      </form>
    </Dialog>
  );
}

export function CategoriesPage() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<Category | null>(null);
  const [creating, setCreating] = useState(false);
  const [deleting, setDeleting] = useState<Category | null>(null);
  const [toggling, setToggling] = useState<Category | null>(null);

  const categories = useQuery({
    queryKey: KEY,
    queryFn: ({ signal }) => api.get<Category[]>('/admin/campaign-categories', { signal }),
  });

  const remove = useMutation({
    mutationFn: (c: Category) => api.delete<CategoryDeleteResult>(`/admin/campaign-categories/${c.id}`),
    onSuccess: (result, c) => {
      void queryClient.invalidateQueries({ queryKey: KEY });
      if (result?.deactivated)
        toast.info(
          'Category deactivated instead',
          `${c.name} is used by ${pluralCampaigns(result.campaignCount)}, so it was deactivated rather than deleted.`,
        );
      else toast.success('Category deleted', c.name);
    },
  });

  const toggle = useMutation({
    mutationFn: (c: Category) =>
      api.put<Category>(`/admin/campaign-categories/${c.id}`, {
        ...toBody(toDraft(c)),
        isActive: !c.isActive,
      }),
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: KEY });
      toast.success(saved.isActive ? 'Category activated' : 'Category deactivated', saved.name);
    },
  });

  const rows = [...(categories.data ?? [])].sort(
    (a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name),
  );

  return (
    <>
      <PageHeader
        title="Campaign categories"
        description="Categories organise campaigns for participants. Inactive categories stay on existing campaigns but are hidden from browsing."
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
            New category
          </Button>
        }
      />
      <Card>
        <CardBody>
          {categories.isError ? (
            <QueryError error={categories.error} onRetry={() => void categories.refetch()} />
          ) : (
            <DataTable
              caption="Campaign categories"
              rows={rows}
              loading={categories.isPending}
              getRowId={(c) => c.id}
              rowLabel={(c) => c.name}
              columns={[
                {
                  id: 'name',
                  header: 'Category',
                  primary: true,
                  cell: (c) => (
                    <div className="admin-cell-stack">
                      <strong>{c.name}</strong>
                      <code className="text-small">{c.slug}</code>
                    </div>
                  ),
                },
                { id: 'icon', header: 'Icon', cell: (c) => (c.icon ? <code>{c.icon}</code> : '—') },
                { id: 'sortOrder', header: 'Order', align: 'right', cell: (c) => c.sortOrder },
                {
                  id: 'campaigns',
                  header: 'Campaigns',
                  align: 'right',
                  cell: (c) => formatNumber(c.campaignCount),
                },
                {
                  id: 'status',
                  header: 'Status',
                  cell: (c) =>
                    c.isActive ? (
                      <Badge tone="success">Active</Badge>
                    ) : (
                      <Badge tone="neutral">Inactive</Badge>
                    ),
                },
              ]}
              rowActions={(c) => [
                { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditing(c) },
                {
                  id: 'toggle',
                  label: c.isActive ? 'Deactivate' : 'Activate',
                  icon: <Power />,
                  onSelect: () => setToggling(c),
                },
                {
                  id: 'delete',
                  label: 'Delete',
                  icon: <Trash2 />,
                  danger: true,
                  onSelect: () => setDeleting(c),
                },
              ]}
              emptyState={
                <EmptyState
                  icon={<FolderTree />}
                  headingLevel={2}
                  title="No categories yet"
                  action={
                    <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
                      New category
                    </Button>
                  }
                />
              }
            />
          )}
        </CardBody>
      </Card>

      {creating && <CategoryDialog key="new" category={null} open onClose={() => setCreating(false)} />}
      {editing && (
        <CategoryDialog key={editing.id} category={editing} open onClose={() => setEditing(null)} />
      )}
      <ConfirmDialog
        open={toggling !== null}
        onClose={() => setToggling(null)}
        title={toggling?.isActive ? `Deactivate ${toggling.name}?` : `Activate ${toggling?.name ?? ''}?`}
        description={
          toggling?.isActive
            ? 'Participants will no longer see this category and it can’t be used for new campaigns. Existing campaigns keep it.'
            : 'Participants will see this category again.'
        }
        tone={toggling?.isActive ? 'danger' : 'primary'}
        confirmLabel={toggling?.isActive ? 'Deactivate' : 'Activate'}
        onConfirm={async () => {
          if (!toggling) return;
          try {
            await toggle.mutateAsync(toggling);
          } catch (error) {
            throw toDisplayError(error);
          }
        }}
      />
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title={`Delete ${deleting?.name ?? 'category'}?`}
        description={
          deleting && deleting.campaignCount > 0
            ? `It is used by ${pluralCampaigns(deleting.campaignCount)}, so it will be deactivated instead of deleted.`
            : 'The category is not used by any campaign and will be removed permanently.'
        }
        confirmLabel={deleting && deleting.campaignCount > 0 ? 'Deactivate' : 'Delete'}
        onConfirm={async () => {
          if (!deleting) return;
          try {
            await remove.mutateAsync(deleting);
          } catch (error) {
            throw toDisplayError(error);
          }
        }}
      />
    </>
  );
}

function pluralCampaigns(n: number): string {
  return `${formatNumber(n)} campaign${n === 1 ? '' : 's'}`;
}
