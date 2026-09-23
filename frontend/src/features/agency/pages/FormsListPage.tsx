import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { FileText, Plus } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Badge,
  Button,
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
import type { PagedResult } from '@/lib/api/types';
import { fieldErrors, firstError, useClientOptions } from '../seo/common';
import { pageKeys, type FormDetail, type FormListItem, type FormStatus, type FormTemplateInfo } from './api';
import './pages.css';

export const formStatusTone: Record<FormStatus, 'success' | 'warning' | 'neutral'> = { Active: 'success', Draft: 'warning', Archived: 'neutral' };

export function FormsListPage() {
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
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
              loading={query.isLoading}
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
