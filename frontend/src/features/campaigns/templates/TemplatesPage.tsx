import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, ArchiveRestore, LayoutTemplate, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  ErrorState,
  FilterBar,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { truncate } from '@/lib/format/text';
import { qk } from '../api/queries';
import type { PostTemplate, SocialPlatform, TemplateInput } from '../api/types';
import { fieldError, fieldErrorsFrom, type FieldErrorMap } from '../shared/formErrors';
import { platformOptions } from '../shared/labels';
import '../campaigns.css';

const BODY_MAX = 5000;
const HASHTAGS_MAX = 500;

export function TemplatesPage() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [includeArchived, setIncludeArchived] = useState(false);
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<PostTemplate | 'new' | null>(null);
  const [removing, setRemoving] = useState<PostTemplate | null>(null);

  const params = {
    search,
    platform: filters.platform,
    languageCode: filters.language,
    includeArchived,
    page,
    pageSize: 25,
    sort: 'updatedAt',
    desc: true,
  };
  const query = useQuery({
    queryKey: qk.templates(params),
    queryFn: () => api.get<PagedResult<PostTemplate>>('/marketing/templates', { query: params }),
    placeholderData: keepPreviousData,
  });

  const toggleArchive = useMutation({
    mutationFn: (t: PostTemplate) =>
      api.put<PostTemplate>(`/marketing/templates/${t.id}`, {
        name: t.name,
        platform: t.platform,
        body: t.body,
        hashtags: t.hashtags,
        languageCode: t.languageCode,
        isArchived: !t.isArchived,
      } satisfies TemplateInput),
    onSuccess: (t) => {
      toast.success(t.isArchived ? 'Template archived' : 'Template restored', t.name);
      void queryClient.invalidateQueries({ queryKey: ['manage', 'templates'] });
    },
    onError: (err) => toast.error('Could not update the template', errorMessage(err)),
  });

  const columns: DataTableColumn<PostTemplate>[] = [
    {
      id: 'name',
      header: 'Template',
      primary: true,
      cell: (t) => (
        <span className="stack mg-stack-xs">
          <span className="mg-strong">{t.name}</span>
          <span className="text-small text-muted">{truncate(t.body, 90)}</span>
        </span>
      ),
    },
    { id: 'platform', header: 'Platform', cell: (t) => t.platform ?? 'Any' },
    { id: 'language', header: 'Language', cell: (t) => t.languageCode ?? '—' },
    {
      id: 'usage',
      header: 'Used by',
      align: 'right',
      cell: (t) => `${t.usageCount} ${t.usageCount === 1 ? 'asset' : 'assets'}`,
    },
    {
      id: 'status',
      header: 'Status',
      cell: (t) =>
        t.isArchived ? <Badge tone="neutral">Archived</Badge> : <Badge tone="success">Active</Badge>,
    },
    {
      id: 'updated',
      header: 'Updated',
      hideOnMobile: true,
      cell: (t) => <DateTime value={t.updatedAt} format="relative" />,
    },
  ];

  return (
    <>
      <PageHeader
        title="Templates"
        description="Reusable approved post copy. Link templates to campaign assets and calendar entries."
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setEditing('new')}>
            New template
          </Button>
        }
      />
      <div className="stack">
        <FilterBar
          search={search}
          onSearchChange={(s) => {
            setSearch(s);
            setPage(1);
          }}
          searchLabel="Search templates"
          searchPlaceholder="Search name, body or hashtags…"
          filters={[
            { id: 'platform', label: 'Platform', options: platformOptions },
            {
              id: 'language',
              label: 'Language',
              options: [...new Set((query.data?.items ?? []).map((t) => t.languageCode).filter(Boolean))].map(
                (l) => ({
                  value: l!,
                  label: l!,
                }),
              ),
            },
          ]}
          values={filters}
          onFilterChange={(id, value) => {
            setFilters((f) => ({ ...f, [id]: value }));
            setPage(1);
          }}
          onReset={() => {
            setSearch('');
            setFilters({});
            setPage(1);
          }}
          actions={
            <Checkbox
              label="Show archived"
              checked={includeArchived}
              onChange={(e) => {
                setIncludeArchived(e.target.checked);
                setPage(1);
              }}
            />
          }
        />
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Post templates"
              columns={columns}
              rows={query.data?.items ?? []}
              getRowId={(t) => t.id}
              rowLabel={(t) => t.name}
              loading={query.isLoading}
              rowActions={(t) => [
                { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditing(t) },
                {
                  id: 'archive',
                  label: t.isArchived ? 'Restore' : 'Archive',
                  icon: t.isArchived ? <ArchiveRestore /> : <Archive />,
                  onSelect: () => toggleArchive.mutate(t),
                },
                {
                  id: 'delete',
                  label: 'Delete…',
                  icon: <Trash2 />,
                  danger: true,
                  onSelect: () => setRemoving(t),
                },
              ]}
              emptyState={
                <EmptyState
                  icon={<LayoutTemplate />}
                  headingLevel={2}
                  title="No templates"
                  description="Create approved copy participants and campaign assets can reuse."
                />
              }
            />
            {query.data && query.data.total > 0 && (
              <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />
            )}
          </>
        )}
      </div>
      {editing && (
        <TemplateDialog
          template={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void queryClient.invalidateQueries({ queryKey: ['manage', 'templates'] });
          }}
        />
      )}
      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        tone="danger"
        title="Delete this template?"
        description={
          removing && removing.usageCount > 0
            ? `“${removing.name}” is used by ${removing.usageCount} campaign asset(s), so it will be archived instead of deleted.`
            : 'Unused templates are deleted. Templates referenced by assets or calendar entries are archived instead.'
        }
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!removing) return;
          const result = await api.delete<{ deleted: boolean; archived: boolean }>(
            `/marketing/templates/${removing.id}`,
          );
          toast.success(
            result.deleted ? 'Template deleted' : 'Template archived',
            result.archived ? 'It is still referenced, so it was archived instead of deleted.' : undefined,
          );
          await queryClient.invalidateQueries({ queryKey: ['manage', 'templates'] });
        }}
      />
    </>
  );
}

function TemplateDialog({
  template,
  onClose,
  onSaved,
}: {
  template: PostTemplate | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const toast = useToast();
  const [form, setForm] = useState<TemplateInput>({
    name: template?.name ?? '',
    platform: template?.platform ?? null,
    body: template?.body ?? '',
    hashtags: template?.hashtags ?? '',
    languageCode: template?.languageCode ?? '',
    isArchived: template?.isArchived ?? false,
  });
  const [errors, setErrors] = useState<FieldErrorMap>({});
  const [formError, setFormError] = useState<string | null>(null);
  const set = (patch: Partial<TemplateInput>) => setForm((f) => ({ ...f, ...patch }));

  const save = useMutation({
    mutationFn: (body: TemplateInput) =>
      template
        ? api.put<PostTemplate>(`/marketing/templates/${template.id}`, body)
        : api.post<PostTemplate>('/marketing/templates', body),
    onSuccess: (t) => {
      toast.success(template ? 'Template saved' : 'Template created', t.name);
      onSaved();
    },
    onError: (err) => {
      const mapped = fieldErrorsFrom(err);
      setErrors(mapped);
      setFormError(Object.keys(mapped).length ? null : errorMessage(err));
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const local: FieldErrorMap = {};
    if (form.name.trim().length < 2) local.name = ['Enter a name (at least 2 characters).'];
    if (!form.body.trim()) local.body = ['Enter the post text.'];
    setErrors(local);
    if (Object.keys(local).length) return;
    save.mutate({
      ...form,
      name: form.name.trim(),
      hashtags: form.hashtags?.trim() || null,
      languageCode: form.languageCode?.trim() || null,
    });
  };

  const total = form.body.length + (form.hashtags ? form.hashtags.length + 1 : 0);

  return (
    <Dialog
      open
      onClose={onClose}
      title={template ? 'Edit template' : 'New template'}
      size="lg"
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="template-form" loading={save.isPending}>
            {template ? 'Save template' : 'Create template'}
          </Button>
        </>
      }
    >
      <form id="template-form" className="stack" onSubmit={submit} noValidate>
        {formError && (
          <Alert tone="danger" role="alert">
            {formError}
          </Alert>
        )}
        <FormField label="Name" required error={fieldError(errors, 'name')}>
          <Input value={form.name} maxLength={150} onChange={(e) => set({ name: e.target.value })} />
        </FormField>
        <div className="mg-grid mg-grid--2">
          <FormField label="Platform" optional error={fieldError(errors, 'platform')}>
            <Select
              value={form.platform ?? ''}
              options={[{ value: '', label: 'Any platform' }, ...platformOptions]}
              onChange={(e) => set({ platform: (e.target.value || null) as SocialPlatform | null })}
            />
          </FormField>
          <FormField
            label="Language"
            optional
            hint="BCP 47, e.g. en or en-GB"
            error={fieldError(errors, 'languageCode')}
          >
            <Input
              value={form.languageCode ?? ''}
              maxLength={10}
              onChange={(e) => set({ languageCode: e.target.value })}
            />
          </FormField>
        </div>
        <FormField
          label="Body"
          required
          hint={
            <span aria-live="polite">
              {form.body.length}/{BODY_MAX} characters
            </span>
          }
          error={fieldError(errors, 'body')}
        >
          <Textarea
            value={form.body}
            rows={7}
            maxLength={BODY_MAX}
            onChange={(e) => set({ body: e.target.value })}
          />
        </FormField>
        <FormField
          label="Hashtags"
          optional
          hint={`${(form.hashtags ?? '').length}/${HASHTAGS_MAX} · e.g. #spring #drop`}
          error={fieldError(errors, 'hashtags')}
        >
          <Input
            value={form.hashtags ?? ''}
            maxLength={HASHTAGS_MAX}
            onChange={(e) => set({ hashtags: e.target.value })}
          />
        </FormField>
        <p className="text-small text-muted">Post length with hashtags: {total} characters.</p>
        {template && (
          <Checkbox
            label="Archived"
            description="Archived templates can't be linked to new calendar entries."
            checked={form.isArchived}
            onChange={(e) => set({ isArchived: e.target.checked })}
          />
        )}
      </form>
    </Dialog>
  );
}
