import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Eye, EyeOff, Pencil, RotateCcw, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  Dialog,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  PageHeader,
  Select,
  Tabs,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { fieldErrors, firstError } from '../seo/common';
import { pageKeys, type FormTemplateAdmin, type PageTemplateAdmin } from './api';
import './pages.css';

const READ_ONLY =
  'Changing the agency template library needs the settings.manage permission (an administrator).';

/** The agency's landing-page and form template library: edit, hide/show, reset built-ins, delete agency templates. */
export function TemplateLibraryPage() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.SettingsManage);
  return (
    <>
      <PageHeader
        title="Template library"
        description="Built-in templates can be edited, hidden or reset; templates saved from your own pages and forms can also be deleted. Pages and forms created earlier are not changed."
        breadcrumbs={[
          { label: 'Landing pages', to: '/agency/pages' },
          { label: 'Templates', to: '/agency/pages/templates' },
          { label: 'Library' },
        ]}
      />
      {!canEdit && (
        <Alert tone="info" title="Read only">
          {READ_ONLY}
        </Alert>
      )}
      <Tabs
        label="Template library"
        tabs={[
          { id: 'pages', label: 'Page templates', content: <PageTemplatesPanel canEdit={canEdit} /> },
          { id: 'forms', label: 'Form templates', content: <FormTemplatesPanel canEdit={canEdit} /> },
        ]}
      />
    </>
  );
}

function StateBadges({ t }: { t: { isActive: boolean; isCustom: boolean; isCustomized: boolean } }) {
  return (
    <span className="cluster">
      {t.isActive ? <Badge tone="success">Shown</Badge> : <Badge>Hidden</Badge>}
      {t.isCustom ? (
        <Badge tone="brand">Agency</Badge>
      ) : t.isCustomized ? (
        <Badge tone="info">Edited</Badge>
      ) : (
        <Badge>Built-in</Badge>
      )}
    </span>
  );
}

function PageTemplatesPanel({ canEdit }: { canEdit: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<PageTemplateAdmin | null>(null);
  const [deleting, setDeleting] = useState<PageTemplateAdmin | null>(null);
  const list = useQuery({
    queryKey: pageKeys.adminTemplates,
    queryFn: () => api.get<PageTemplateAdmin[]>('/agency/pages/admin/templates'),
  });
  const formTemplates = useQuery({
    queryKey: pageKeys.adminFormTemplates,
    queryFn: () => api.get<FormTemplateAdmin[]>('/agency/pages/admin/form-templates'),
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: pageKeys.adminTemplates });
    void queryClient.invalidateQueries({ queryKey: pageKeys.templates });
  };
  const update = useMutation({
    mutationFn: ({ t, patch }: { t: PageTemplateAdmin; patch: Partial<PageTemplateAdmin> }) =>
      api.put<PageTemplateAdmin>(`/agency/pages/admin/templates/${encodeURIComponent(t.key)}`, {
        ...pageBody(t),
        ...patch,
      }),
    onSuccess: (t) => {
      toast.success(t.isActive ? 'Template shown' : 'Template hidden', t.name);
      refresh();
    },
    onError: (e) => toast.error('Not changed', errorMessage(e)),
  });
  const reset = useMutation({
    mutationFn: (t: PageTemplateAdmin) =>
      api.post<PageTemplateAdmin>(`/agency/pages/admin/templates/${encodeURIComponent(t.key)}/reset`),
    onSuccess: (t) => {
      toast.success('Template reset to the built-in version', t.name);
      refresh();
    },
    onError: (e) => toast.error('Not reset', errorMessage(e)),
  });
  if (list.isError) return <ErrorState error={list.error} onRetry={() => void list.refetch()} />;
  const columns: DataTableColumn<PageTemplateAdmin>[] = [
    {
      id: 'name',
      header: 'Template',
      primary: true,
      cell: (t) => (
        <span className="stack pb-stack-xs">
          <strong>{t.name}</strong>
          <span className="text-small text-muted">{t.description}</span>
        </span>
      ),
    },
    { id: 'category', header: 'Category', cell: (t) => t.category },
    { id: 'blocks', header: 'Blocks', align: 'right', hideOnMobile: true, cell: (t) => t.blockCount },
    { id: 'used', header: 'Pages using it', align: 'right', hideOnMobile: true, cell: (t) => t.pagesUsing },
    { id: 'state', header: 'State', cell: (t) => <StateBadges t={t} /> },
  ];
  return (
    <div className="stack">
      <p className="text-muted">
        To add a template, open a landing page and choose “Save as template” in the pages list.
      </p>
      <DataTable
        caption="Page templates"
        columns={columns}
        rows={list.data ?? []}
        getRowId={(t) => t.key}
        rowLabel={(t) => t.name}
        loading={list.isLoading}
        rowActions={(t) => [
          {
            id: 'edit',
            label: 'Edit details',
            icon: <Pencil />,
            disabled: !canEdit,
            description: canEdit ? undefined : READ_ONLY,
            onSelect: () => setEditing(t),
          },
          {
            id: 'toggle',
            label: t.isActive ? 'Hide from the gallery' : 'Show in the gallery',
            icon: t.isActive ? <EyeOff /> : <Eye />,
            disabled: !canEdit,
            onSelect: () => update.mutate({ t, patch: { isActive: !t.isActive } }),
          },
          ...(!t.isCustom
            ? [
                {
                  id: 'reset',
                  label: 'Reset to built-in',
                  icon: <RotateCcw />,
                  disabled: !canEdit || !t.isCustomized,
                  onSelect: () => reset.mutate(t),
                },
              ]
            : [
                {
                  id: 'delete',
                  label: 'Delete',
                  icon: <Trash2 />,
                  danger: true,
                  disabled: !canEdit,
                  onSelect: () => setDeleting(t),
                },
              ]),
        ]}
        emptyState={<EmptyState compact headingLevel={3} title="No templates" />}
      />
      {editing && (
        <PageTemplateDialog
          template={editing}
          formTemplates={formTemplates.data ?? []}
          onClose={() => setEditing(null)}
          onSaved={refresh}
        />
      )}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this template?"
        description={
          deleting
            ? `“${deleting.name}” is removed from the gallery. Pages created from it are not affected.`
            : undefined
        }
        confirmLabel="Delete template"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/pages/admin/templates/${encodeURIComponent(deleting.key)}`);
          toast.success('Template deleted');
          refresh();
        }}
      />
    </div>
  );
}

const pageBody = (t: PageTemplateAdmin) => ({
  name: t.name,
  category: t.category,
  description: t.description,
  metaTitle: t.metaTitle,
  metaDescription: t.metaDescription,
  formTemplateKey: t.formTemplateKey,
  sortOrder: t.sortOrder,
  isActive: t.isActive,
  concurrencyStamp: t.concurrencyStamp,
});

export function PageTemplateDialog({
  template,
  formTemplates,
  onClose,
  onSaved,
}: {
  template: PageTemplateAdmin;
  formTemplates: FormTemplateAdmin[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    ...pageBody(template),
    formTemplateKey: template.formTemplateKey ?? '',
    sortOrder: String(template.sortOrder),
  });
  const save = useMutation({
    mutationFn: () =>
      api.put(`/agency/pages/admin/templates/${encodeURIComponent(template.key)}`, {
        ...form,
        formTemplateKey: form.formTemplateKey || null,
        sortOrder: Number(form.sortOrder) || 0,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  return (
    <Dialog
      open
      onClose={onClose}
      title="Edit page template"
      description="The layout comes from the page it was saved from; build a new page and save it as a template to change the blocks."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="pb-template-edit" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form
        id="pb-template-edit"
        className="stack"
        onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}
      >
        {save.isError && (
          <Alert tone="danger" title="Could not save">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField label="Name" required error={firstError(errors, 'name')}>
          <Input
            value={form.name}
            maxLength={150}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
          />
        </FormField>
        <div className="pb-grid-2">
          <FormField label="Category" required error={firstError(errors, 'category')}>
            <Input
              value={form.category}
              maxLength={60}
              onChange={(e) => setForm({ ...form, category: e.target.value })}
            />
          </FormField>
          <FormField label="Sort order" hint="Lower numbers first.">
            <Input
              type="number"
              min={0}
              value={form.sortOrder}
              onChange={(e) => setForm({ ...form, sortOrder: e.target.value })}
            />
          </FormField>
        </div>
        <FormField label="Description" required error={firstError(errors, 'description')}>
          <Textarea
            rows={2}
            maxLength={500}
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
          />
        </FormField>
        <FormField label="Default meta title" required error={firstError(errors, 'metaTitle')}>
          <Input
            value={form.metaTitle}
            maxLength={150}
            onChange={(e) => setForm({ ...form, metaTitle: e.target.value })}
          />
        </FormField>
        <FormField label="Default meta description" required error={firstError(errors, 'metaDescription')}>
          <Textarea
            rows={2}
            maxLength={320}
            value={form.metaDescription}
            onChange={(e) => setForm({ ...form, metaDescription: e.target.value })}
          />
        </FormField>
        <FormField label="Form created with the page" optional error={firstError(errors, 'formTemplateKey')}>
          <Select
            value={form.formTemplateKey}
            placeholder="No form"
            onChange={(e) => setForm({ ...form, formTemplateKey: e.target.value })}
            options={formTemplates.map((f) => ({ value: f.key, label: f.name }))}
          />
        </FormField>
        <Checkbox
          label="Show in the template gallery"
          checked={form.isActive}
          onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
        />
      </form>
    </Dialog>
  );
}

const formBody = (t: FormTemplateAdmin) => ({
  name: t.name,
  description: t.description,
  schema: t.schema,
  submitLabel: t.submitLabel,
  successMessage: t.successMessage,
  consentText: t.consentText,
  autoresponderSubject: t.autoresponderSubject,
  autoresponderBody: t.autoresponderBody,
  sortOrder: t.sortOrder,
  isActive: t.isActive,
  concurrencyStamp: t.concurrencyStamp,
});

function FormTemplatesPanel({ canEdit }: { canEdit: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<FormTemplateAdmin | null>(null);
  const [deleting, setDeleting] = useState<FormTemplateAdmin | null>(null);
  const list = useQuery({
    queryKey: pageKeys.adminFormTemplates,
    queryFn: () => api.get<FormTemplateAdmin[]>('/agency/pages/admin/form-templates'),
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: pageKeys.adminFormTemplates });
    void queryClient.invalidateQueries({ queryKey: pageKeys.formTemplates });
  };
  const toggle = useMutation({
    mutationFn: (t: FormTemplateAdmin) =>
      api.put<FormTemplateAdmin>(`/agency/pages/admin/form-templates/${encodeURIComponent(t.key)}`, {
        ...formBody(t),
        isActive: !t.isActive,
      }),
    onSuccess: (t) => {
      toast.success(t.isActive ? 'Template shown' : 'Template hidden', t.name);
      refresh();
    },
    onError: (e) => toast.error('Not changed', errorMessage(e)),
  });
  const reset = useMutation({
    mutationFn: (t: FormTemplateAdmin) =>
      api.post<FormTemplateAdmin>(`/agency/pages/admin/form-templates/${encodeURIComponent(t.key)}/reset`),
    onSuccess: (t) => {
      toast.success('Template reset to the built-in version', t.name);
      refresh();
    },
    onError: (e) => toast.error('Not reset', errorMessage(e)),
  });
  if (list.isError) return <ErrorState error={list.error} onRetry={() => void list.refetch()} />;
  const columns: DataTableColumn<FormTemplateAdmin>[] = [
    {
      id: 'name',
      header: 'Template',
      primary: true,
      cell: (t) => (
        <span className="stack pb-stack-xs">
          <strong>{t.name}</strong>
          <span className="text-small text-muted">{t.description}</span>
        </span>
      ),
    },
    {
      id: 'fields',
      header: 'Fields',
      align: 'right',
      hideOnMobile: true,
      cell: (t) => t.schema.steps.flatMap((s) => s.fields).filter((f) => f.type !== 'hidden').length,
    },
    { id: 'used', header: 'Forms using it', align: 'right', hideOnMobile: true, cell: (t) => t.formsUsing },
    { id: 'state', header: 'State', cell: (t) => <StateBadges t={t} /> },
  ];
  return (
    <div className="stack">
      <p className="text-muted">
        To add a template, open the Forms list and choose “Save as template” on a form.
      </p>
      <DataTable
        caption="Form templates"
        columns={columns}
        rows={list.data ?? []}
        getRowId={(t) => t.key}
        rowLabel={(t) => t.name}
        loading={list.isLoading}
        rowActions={(t) => [
          {
            id: 'edit',
            label: 'Edit texts',
            icon: <Pencil />,
            disabled: !canEdit,
            description: canEdit ? undefined : READ_ONLY,
            onSelect: () => setEditing(t),
          },
          {
            id: 'toggle',
            label: t.isActive ? 'Hide' : 'Show',
            icon: t.isActive ? <EyeOff /> : <Eye />,
            disabled: !canEdit,
            onSelect: () => toggle.mutate(t),
          },
          ...(!t.isCustom
            ? [
                {
                  id: 'reset',
                  label: 'Reset to built-in',
                  icon: <RotateCcw />,
                  disabled: !canEdit || !t.isCustomized,
                  onSelect: () => reset.mutate(t),
                },
              ]
            : [
                {
                  id: 'delete',
                  label: 'Delete',
                  icon: <Trash2 />,
                  danger: true,
                  disabled: !canEdit,
                  onSelect: () => setDeleting(t),
                },
              ]),
        ]}
        emptyState={<EmptyState compact headingLevel={3} title="No form templates" />}
      />
      {editing && (
        <FormTemplateDialog template={editing} onClose={() => setEditing(null)} onSaved={refresh} />
      )}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this form template?"
        description={
          deleting ? `“${deleting.name}” is removed. Forms created from it are not affected.` : undefined
        }
        confirmLabel="Delete template"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/pages/admin/form-templates/${encodeURIComponent(deleting.key)}`);
          toast.success('Template deleted');
          refresh();
        }}
      />
    </div>
  );
}

export function FormTemplateDialog({
  template,
  onClose,
  onSaved,
}: {
  template: FormTemplateAdmin;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState({
    ...formBody(template),
    consentText: template.consentText ?? '',
    autoresponderSubject: template.autoresponderSubject ?? '',
    autoresponderBody: template.autoresponderBody ?? '',
    sortOrder: String(template.sortOrder),
  });
  const save = useMutation({
    mutationFn: () =>
      api.put(`/agency/pages/admin/form-templates/${encodeURIComponent(template.key)}`, {
        ...form,
        consentText: form.consentText || null,
        autoresponderSubject: form.autoresponderSubject || null,
        autoresponderBody: form.autoresponderBody || null,
        sortOrder: Number(form.sortOrder) || 0,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  return (
    <Dialog
      open
      size="lg"
      onClose={onClose}
      title="Edit form template"
      description="Fields come from the form the template was saved from; save a form as a template to change them."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="pb-form-template-edit" loading={save.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form
        id="pb-form-template-edit"
        className="stack"
        onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}
      >
        {save.isError && (
          <Alert tone="danger" title="Could not save">
            {errorMessage(save.error)}
          </Alert>
        )}
        <div className="pb-grid-2">
          <FormField label="Name" required error={firstError(errors, 'name')}>
            <Input
              value={form.name}
              maxLength={150}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
            />
          </FormField>
          <FormField label="Sort order">
            <Input
              type="number"
              min={0}
              value={form.sortOrder}
              onChange={(e) => setForm({ ...form, sortOrder: e.target.value })}
            />
          </FormField>
        </div>
        <FormField label="Description" required error={firstError(errors, 'description')}>
          <Textarea
            rows={2}
            maxLength={500}
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
          />
        </FormField>
        <div className="pb-grid-2">
          <FormField label="Button label" required error={firstError(errors, 'submitLabel')}>
            <Input
              value={form.submitLabel}
              maxLength={60}
              onChange={(e) => setForm({ ...form, submitLabel: e.target.value })}
            />
          </FormField>
          <FormField label="Success message" required error={firstError(errors, 'successMessage')}>
            <Input
              value={form.successMessage}
              maxLength={1000}
              onChange={(e) => setForm({ ...form, successMessage: e.target.value })}
            />
          </FormField>
        </div>
        <FormField label="Consent text" optional>
          <Textarea
            rows={2}
            maxLength={2000}
            value={form.consentText}
            onChange={(e) => setForm({ ...form, consentText: e.target.value })}
          />
        </FormField>
        <FormField label="Autoresponder subject" optional>
          <Input
            value={form.autoresponderSubject}
            maxLength={200}
            onChange={(e) => setForm({ ...form, autoresponderSubject: e.target.value })}
          />
        </FormField>
        <FormField label="Autoresponder message" optional>
          <Textarea
            rows={4}
            maxLength={5000}
            value={form.autoresponderBody}
            onChange={(e) => setForm({ ...form, autoresponderBody: e.target.value })}
          />
        </FormField>
        <Checkbox
          label="Offer when creating forms"
          checked={form.isActive}
          onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
        />
      </form>
    </Dialog>
  );
}

/** Saves a landing page (variant A) or a form as a reusable agency template. */
export function SaveAsTemplateDialog({
  kind,
  id,
  defaultName,
  onClose,
}: {
  kind: 'page' | 'form';
  id: string;
  defaultName: string;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [name, setName] = useState(defaultName);
  const [category, setCategory] = useState('Custom');
  const [description, setDescription] = useState('');
  const save = useMutation({
    mutationFn: () =>
      api.post(
        kind === 'page'
          ? `/agency/pages/landing-pages/${id}/save-as-template`
          : `/agency/pages/forms/${id}/save-as-template`,
        {
          name,
          category: kind === 'page' ? category : undefined,
          description: description || null,
        },
      ),
    onSuccess: () => {
      toast.success('Saved to the template library', name);
      void queryClient.invalidateQueries({
        queryKey: kind === 'page' ? pageKeys.templates : pageKeys.formTemplates,
      });
      void queryClient.invalidateQueries({ queryKey: ['agency', 'pages', 'admin'] });
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  return (
    <Dialog
      open
      onClose={onClose}
      title={kind === 'page' ? 'Save page as template' : 'Save form as template'}
      description={
        kind === 'page'
          ? 'Variant A of the draft is saved; its form block becomes a placeholder that is filled when a page is created from the template.'
          : 'The fields, texts and autoresponder are saved; recipients, origins and the client are not.'
      }
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="submit"
            form="pb-save-template"
            loading={save.isPending}
            disabled={name.trim().length < 2}
          >
            Save template
          </Button>
        </>
      }
    >
      <form
        id="pb-save-template"
        className="stack"
        onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}
      >
        {save.isError && (
          <Alert tone="danger" title="Could not save">
            {errorMessage(save.error)}
          </Alert>
        )}
        <FormField label="Template name" required error={firstError(errors, 'name')}>
          <Input value={name} maxLength={150} onChange={(e) => setName(e.target.value)} />
        </FormField>
        {kind === 'page' && (
          <FormField label="Category">
            <Input value={category} maxLength={60} onChange={(e) => setCategory(e.target.value)} />
          </FormField>
        )}
        <FormField label="Description" optional>
          <Textarea
            rows={2}
            maxLength={500}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </FormField>
      </form>
    </Dialog>
  );
}
