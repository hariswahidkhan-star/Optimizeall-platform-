import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { FileText, LayoutTemplate, Plus } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Badge,
  Button,
  ButtonLink,
  Checkbox,
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
import { pageKeys, type PageDetail, type PageListItem, type PageStatus, type PageTemplate } from './api';
import './pages.css';

export const statusTone: Record<PageStatus, 'success' | 'warning' | 'neutral'> = { Published: 'success', Draft: 'warning', Archived: 'neutral' };

export function LandingPagesListPage() {
  const [params] = useSearchParams();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(params.get('template') !== null);
  const clients = useClientOptions('pages');
  const queryParams = { search, clientId: filters.client, status: filters.status, page, pageSize: 25 };
  const query = useQuery({
    queryKey: pageKeys.list(queryParams),
    queryFn: () => api.get<PagedResult<PageListItem>>('/agency/pages/landing-pages', { query: queryParams }),
    placeholderData: keepPreviousData,
  });

  const columns: DataTableColumn<PageListItem>[] = [
    {
      id: 'name',
      header: 'Page',
      primary: true,
      cell: (p) => (
        <span className="stack pb-stack-xs">
          <Link to={p.id} className="ui-link">
            {p.name}
          </Link>
          <code className="text-small text-muted">{p.publicPath}</code>
        </span>
      ),
    },
    { id: 'client', header: 'Client', cell: (p) => p.clientName },
    {
      id: 'status',
      header: 'Status',
      cell: (p) => (
        <span className="cluster">
          <Badge tone={statusTone[p.status]}>{p.status}</Badge>
          {p.hasUnpublishedChanges && p.status === 'Published' && <Badge tone="info">Changes</Badge>}
          {p.experimentEnabled && <Badge tone="brand">A/B · {p.variantCount}</Badge>}
        </span>
      ),
    },
    { id: 'updated', header: 'Updated', hideOnMobile: true, cell: (p) => <DateTime value={p.updatedAt} format="relative" /> },
  ];

  return (
    <>
      <PageHeader
        title="Landing pages"
        description="Build conversion pages from blocks, publish immutable versions and A/B test variants."
        actions={
          <>
            <ButtonLink to="templates" variant="secondary" leadingIcon={<LayoutTemplate />}>
              Templates
            </ButtonLink>
            <ButtonLink to="forms" variant="secondary" leadingIcon={<FileText />}>
              Forms
            </ButtonLink>
            <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
              New page
            </Button>
          </>
        }
      />
      <div className="stack">
        <FilterBar
          search={search}
          onSearchChange={(s) => {
            setSearch(s);
            setPage(1);
          }}
          searchLabel="Search landing pages"
          searchPlaceholder="Search name or slug…"
          filters={[
            { id: 'client', label: 'Client', options: (clients.data ?? []).map((c) => ({ value: c.id, label: c.name })) },
            {
              id: 'status',
              label: 'Status',
              options: (['Draft', 'Published', 'Archived'] as const).map((s) => ({ value: s, label: s })),
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
          }}
        />
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Landing pages"
              columns={columns}
              rows={query.data?.items ?? []}
              getRowId={(p) => p.id}
              loading={query.isLoading}
              emptyState={
                <EmptyState
                  icon={<LayoutTemplate />}
                  title="No landing pages yet"
                  description="Start from a template: lead generation, webinar, product launch and more."
                  action={<Button onClick={() => setCreating(true)}>New page</Button>}
                />
              }
            />
            {query.data && query.data.total > 0 && <Pagination page={page} pageSize={25} total={query.data.total} onPageChange={setPage} />}
          </>
        )}
      </div>
      {creating && <CreatePageDialog initialTemplate={params.get('template') ?? undefined} onClose={() => setCreating(false)} />}
    </>
  );
}

export function CreatePageDialog({ initialTemplate, onClose }: { initialTemplate?: string; onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const clients = useClientOptions('pages');
  const templates = useQuery({ queryKey: pageKeys.templates, queryFn: () => api.get<PageTemplate[]>('/agency/pages/templates'), staleTime: 10 * 60_000 });
  const [form, setForm] = useState({ clientAccountId: '', name: '', slug: '', templateKey: initialTemplate ?? '', createForm: true });
  const template = templates.data?.find((t) => t.key === form.templateKey);
  const create = useMutation({
    mutationFn: () =>
      api.post<PageDetail>('/agency/pages/landing-pages', {
        clientAccountId: form.clientAccountId || null,
        name: form.name,
        slug: form.slug || null,
        templateKey: form.templateKey || null,
        createForm: form.createForm,
      }),
    onSuccess: (page) => {
      toast.success('Page created', 'Edit the blocks, then publish when ready.');
      void queryClient.invalidateQueries({ queryKey: pageKeys.all });
      navigate(`/agency/pages/${page.id}`);
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
      title="New landing page"
      description="Pages start as drafts. Nothing is public until you publish."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="pb-create-page" loading={create.isPending}>
            Create page
          </Button>
        </>
      }
    >
      <form id="pb-create-page" className="stack" onSubmit={submit} noValidate>
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
        <FormField label="Page name" required error={firstError(errors, 'name')}>
          <Input value={form.name} onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} />
        </FormField>
        <FormField label="URL slug" optional hint="Generated from the name when empty." error={firstError(errors, 'slug')}>
          <Input value={form.slug} onChange={(e) => setForm((f) => ({ ...f, slug: e.target.value.toLowerCase() }))} />
        </FormField>
        <FormField label="Template" hint={template?.description ?? 'A blank page with a single hero block.'}>
          <Select
            value={form.templateKey}
            onChange={(e) => setForm((f) => ({ ...f, templateKey: e.target.value }))}
            options={[{ value: '', label: 'Blank page' }, ...(templates.data ?? []).map((t) => ({ value: t.key, label: `${t.name} (${t.category})` }))]}
          />
        </FormField>
        {template?.formTemplateKey && (
          <Checkbox
            label="Create the template’s form"
            description="Adds a new form for this client and places it on the page."
            checked={form.createForm}
            onChange={(e) => setForm((f) => ({ ...f, createForm: e.target.checked }))}
          />
        )}
      </form>
    </Dialog>
  );
}
