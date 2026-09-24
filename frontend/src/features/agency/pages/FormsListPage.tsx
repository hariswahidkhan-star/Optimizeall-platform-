import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, ArchiveRestore, BookmarkPlus, CopyPlus, FileText, Inbox, Pencil, Plus } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Badge,
  Button,
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
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import type { PagedResult } from '@/lib/api/types';
import { Permissions } from '@/lib/auth/permissions';
import { useAuth } from '@/lib/auth/useAuth';
import { fieldErrors, firstError, useClientOptions } from '../seo/common';
import { pageKeys, type FormDetail, type FormListItem, type FormStatus, type FormTemplateInfo } from './api';
import { SaveAsTemplateDialog } from './TemplateLibraryPage';
import './pages.css';

export const formStatusTone: Record<FormStatus, 'success' | 'warning' | 'neutral'> = { Active: 'success', Draft: 'warning', Archived: 'neutral' };

export function FormsListPage() {
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [savingTemplate, setSavingTemplate] = useState<FormListItem | null>(null);
  const [archiving, setArchiving] = useState<FormListItem | null>(null);
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const canSaveTemplates = hasPermission(Permissions.SettingsManage);
  const duplicate = useMutation({
    mutationFn: (f: FormListItem) => api.post<{ id: string; name: string }>(`/agency/pages/forms/${f.id}/duplicate`),
    onSuccess: (copy) => {
      toast.success('Form duplicated', `“${copy.name}” is a draft: activate it in the builder.`);
      void queryClient.invalidateQueries({ queryKey: pageKeys.all });
      navigate(copy.id);
    },
    onError: (e) => toast.error('Could not duplicate the form', errorMessage(e)),
  });
  const restore = useMutation({
    mutationFn: (f: FormListItem) => api.post(`/agency/pages/forms/${f.id}/restore`),
    onSuccess: () => {
      toast.success('Form restored as a draft', 'Activate it in the builder to accept submissions again.');
      void queryClient.invalidateQueries({ queryKey: pageKeys.all });
    },
    onError: (e) => toast.error('Could not restore the form', errorMessage(e)),
  });
  const clients = useClientOptions('pages');
  const params = { search, clientId: filters.client, status: filters.status, page, pageSize: 25 };
  const query = useQuery({
    queryKey: pageKeys.forms(params),
    queryFn: () => api.get<PagedResult<FormListItem>>('/agency/pages/forms', { query: params }),
    placeholderData: keepPreviousData,
  });

  const columns: DataTableColumn<FormListItem>[] = [
    {
      id: 'name',
      header: 'Form',
      primary: true,
      cell: (f) => (
        <Link to={f.id} className="ui-link">
          {f.name}
        </Link>
      ),
    },
    { id: 'client', header: 'Client', cell: (f) => f.clientName },
    { id: 'status', header: 'Status', cell: (f) => <Badge tone={formStatusTone[f.status]}>{f.status}</Badge> },
    { id: 'fields', header: 'Fields', align: 'right', hideOnMobile: true, cell: (f) => `${f.fieldCount} in ${f.stepCount} step${f.stepCount === 1 ? '' : 's'}` },
    {
      id: 'subs',
      header: 'Submissions',
      align: 'right',
      cell: (f) => (
        <Link to={`${f.id}/submissions`} className="ui-link tabular" aria-label={`${f.submissions} submissions for ${f.name}`}>
          {f.submissions}
        </Link>
      ),
    },
    { id: 'last', header: 'Last submission', hideOnMobile: true, cell: (f) => (f.lastSubmissionAt ? <DateTime value={f.lastSubmissionAt} format="relative" /> : '—') },
  ];

  return (
    <>
      <PageHeader
        title="Forms"
        description="Multi-step forms with conditional logic, spam protection and consent capture. Use them on landing pages or embed them anywhere."
        breadcrumbs={[{ label: 'Landing pages', to: '/agency/pages' }, { label: 'Forms' }]}
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
            New form
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
          searchLabel="Search forms"
          searchPlaceholder="Search forms…"
          filters={[
            { id: 'client', label: 'Client', options: (clients.data ?? []).map((c) => ({ value: c.id, label: c.name })) },
            { id: 'status', label: 'Status', options: (['Active', 'Draft', 'Archived'] as const).map((s) => ({ value: s, label: s })) },
          ]}
          values={filters}
          onFilterChange={(id, value) => {
            setFilters((f) => ({ ...f, [id]: value }));
            setPage(1);
          }}
          onReset={() => {
            setSearch('');
            setFilters({});
          }}
        />
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Forms"
              columns={columns}
              rows={query.data?.items ?? []}
              getRowId={(f) => f.id}
              rowLabel={(f) => f.name}
              loading={query.isLoading}
              rowActions={(f) => [
                { id: 'open', label: 'Open builder', icon: <Pencil />, to: f.id },
                { id: 'submissions', label: 'Submissions', icon: <Inbox />, to: `${f.id}/submissions` },
                { id: 'duplicate', label: 'Duplicate', icon: <CopyPlus />, onSelect: () => duplicate.mutate(f) },
                {
                  id: 'template',
                  label: 'Save as template',
                  icon: <BookmarkPlus />,
                  disabled: !canSaveTemplates,
                  description: canSaveTemplates ? undefined : 'Needs the settings.manage permission.',
                  onSelect: () => setSavingTemplate(f),
                },
                f.status === 'Archived'
                  ? { id: 'restore', label: 'Restore as draft', icon: <ArchiveRestore />, onSelect: () => restore.mutate(f) }
                  : { id: 'archive', label: 'Archive', icon: <Archive />, danger: true, onSelect: () => setArchiving(f) },
              ]}
              emptyState={
                <EmptyState
                  icon={<FileText />}
                  title="No forms yet"
                  description="Start from a contact, quote request, newsletter or webinar template."
                  action={<Button onClick={() => setCreating(true)}>New form</Button>}
                />
              }
            />
            {query.data && query.data.total > 0 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
          </>
        )}
      </div>
      {savingTemplate && <SaveAsTemplateDialog kind="form" id={savingTemplate.id} defaultName={savingTemplate.name} onClose={() => setSavingTemplate(null)} />}
      <ConfirmDialog
        open={archiving !== null}
        onClose={() => setArchiving(null)}
        tone="danger"
        title="Archive this form?"
        description="It stops accepting submissions (pages using it show nothing in its place). Submissions are kept; you can restore it."
        confirmLabel="Archive form"
        onConfirm={async () => {
          if (!archiving) return;
          await api.delete(`/agency/pages/forms/${archiving.id}`);
          toast.success('Form archived');
          void queryClient.invalidateQueries({ queryKey: pageKeys.all });
        }}
      />
      {creating && <CreateFormDialog onClose={() => setCreating(false)} />}
    </>
  );
}

function CreateFormDialog({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const clients = useClientOptions('pages');
  const templates = useQuery({ queryKey: pageKeys.formTemplates, queryFn: () => api.get<FormTemplateInfo[]>('/agency/pages/form-templates'), staleTime: 10 * 60_000 });
  const [form, setForm] = useState({ clientAccountId: '', name: '', templateKey: 'contact' });
  const create = useMutation({
    mutationFn: () => api.post<FormDetail>('/agency/pages/forms', { clientAccountId: form.clientAccountId || null, name: form.name, templateKey: form.templateKey || null }),
    onSuccess: (created) => {
      toast.success('Form created');
      void queryClient.invalidateQueries({ queryKey: pageKeys.all });
      navigate(`/agency/pages/forms/${created.id}`);
    },
  });
  const errors = fieldErrors(create.error);
  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };
  return (
    <Dialog
      open
      onClose={onClose}
      title="New form"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="pb-create-form" loading={create.isPending}>
            Create form
          </Button>
        </>
      }
    >
      <form id="pb-create-form" className="stack" onSubmit={submit} noValidate>
        {create.isError && Object.keys(errors).length === 0 && (
          <p role="alert" className="pb-error">
            {(create.error as Error).message}
          </p>
        )}
        <FormField label="Client" required error={firstError(errors, 'clientAccountId')}>
          <Select
            value={form.clientAccountId}
            placeholder="Choose a client"
            onChange={(e) => setForm((f) => ({ ...f, clientAccountId: e.target.value }))}
            options={(clients.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
          />
        </FormField>
        <FormField label="Form name" required error={firstError(errors, 'name')}>
          <Input value={form.name} onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} />
        </FormField>
        <FormField label="Start from" hint={templates.data?.find((t) => t.key === form.templateKey)?.description}>
          <Select
            value={form.templateKey}
            onChange={(e) => setForm((f) => ({ ...f, templateKey: e.target.value }))}
            options={(templates.data ?? [{ key: 'contact', name: 'Contact' } as FormTemplateInfo]).map((t) => ({ value: t.key, label: t.name }))}
          />
        </FormField>
      </form>
    </Dialog>
  );
}
