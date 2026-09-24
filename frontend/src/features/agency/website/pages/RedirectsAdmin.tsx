import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  ErrorState,
  FilterBar,
  PageHeader,
  Pagination,
  useToast,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { type Paged, type SiteRedirect, W } from '../api';
import { type Errors, TextField, toErrors } from '../shared/fields';
import '../website.css';

const CONTENT_LABEL: Record<string, string> = {
  page: 'Page',
  post: 'Blog post',
  service: 'Service',
  'service-line': 'Service line',
  'case-study': 'Case study',
  industry: 'Industry',
  'landing-page': 'Landing page',
};

const redirectsKey = ['agency', 'website', 'redirects'] as const;

/** Adds a manual redirect (site.manage). The API follows a target that is itself redirected and refuses loops. */
function AddRedirectDialog({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const client = useQueryClient();
  const [fromPath, setFromPath] = useState('');
  const [toPath, setToPath] = useState('');
  const [errors, setErrors] = useState<Errors>({});
  const save = useMutation({
    mutationFn: () => api.post<SiteRedirect>(`${W}/redirects`, { fromPath, toPath }),
    onSuccess: async (r) => {
      toast.success('Redirect added', `${r.fromPath} → ${r.toPath}`);
      await client.invalidateQueries({ queryKey: redirectsKey });
      onClose();
    },
    onError: (e) => setErrors(isApiError(e) ? toErrors(e.errors) : {}),
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  return (
    <Dialog
      open
      onClose={onClose}
      title="Add redirect"
      description="Visitors and search engines asking for the old address are sent to the new one with a permanent (301) redirect."
      dismissible={!save.isPending}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="add-redirect-form" loading={save.isPending}>
            Add redirect
          </Button>
        </>
      }
    >
      <form id="add-redirect-form" className="cms-form" onSubmit={submit} noValidate>
        <TextField
          label="Old address"
          required
          value={fromPath}
          onChange={setFromPath}
          error={errors.fromPath}
          placeholder="/old-page"
          hint="A path on this site, e.g. /old-page or /blog/old-post."
        />
        <TextField
          label="New address"
          required
          value={toPath}
          onChange={setToPath}
          error={errors.toPath}
          placeholder="/new-page"
          hint="Where visitors should land, e.g. /about-us."
        />
        {save.isError && !Object.keys(errors).length && (
          <Alert tone="danger" title="Couldn't add the redirect">
            {errorMessage(save.error)}
          </Alert>
        )}
      </form>
    </Dialog>
  );
}

/**
 * Agency → Website → Redirects: permanent redirects of old public addresses. Renaming the slug of live content records
 * one automatically; staff can add their own (e.g. after a site migration) and delete any.
 */
export function RedirectsAdminPage() {
  const toast = useToast();
  const client = useQueryClient();
  const [search, setSearch] = useState('');
  const [source, setSource] = useState<string | undefined>();
  const [page, setPage] = useState(1);
  const [adding, setAdding] = useState(false);
  const [deleting, setDeleting] = useState<SiteRedirect | null>(null);
  const query = useQuery({
    queryKey: [...redirectsKey, search, source, page],
    queryFn: () =>
      api.get<Paged<SiteRedirect>>(`${W}/redirects`, {
        query: { search: search || undefined, source, page, pageSize: 50 },
      }),
    placeholderData: (previous) => previous,
  });
  return (
    <div className="cms-page">
      <PageHeader
        title="Redirects"
        description="Old addresses that now live elsewhere. When you change the slug of a published page, post, service, service line, case study, industry or landing page, its old address is redirected here automatically, so links and search rankings keep working."
        actions={
          <Button leadingIcon={<Plus />} onClick={() => setAdding(true)}>
            Add redirect
          </Button>
        }
      />
      <FilterBar
        search={search}
        onSearchChange={(v) => {
          setSearch(v);
          setPage(1);
        }}
        searchLabel="Search addresses"
        filters={[
          {
            id: 'source',
            label: 'Source',
            options: [
              { value: 'Automatic', label: 'Automatic' },
              { value: 'Manual', label: 'Manual' },
            ],
          },
        ]}
        values={{ source }}
        onFilterChange={(_, v) => {
          setSource(v);
          setPage(1);
        }}
        onReset={() => {
          setSource(undefined);
          setSearch('');
        }}
      />
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <>
          <DataTable
            caption="Redirects"
            rows={query.data?.items ?? []}
            loading={query.isLoading}
            getRowId={(r) => r.id}
            rowLabel={(r) => r.fromPath}
            columns={[
              { id: 'from', header: 'Old address', primary: true, cell: (r) => <code>{r.fromPath}</code> },
              { id: 'to', header: 'Redirects to', cell: (r) => <code>{r.toPath}</code> },
              {
                id: 'source',
                header: 'Source',
                cell: (r) =>
                  r.source === 'Automatic' ? (
                    <Badge tone="info">
                      {r.contentType
                        ? `${CONTENT_LABEL[r.contentType] ?? r.contentType} renamed`
                        : 'Automatic'}
                    </Badge>
                  ) : (
                    <Badge tone="neutral">Manual</Badge>
                  ),
              },
              {
                id: 'created',
                header: 'Added',
                cell: (r) => <DateTime value={r.createdAt} format="datetime" />,
                hideOnMobile: true,
              },
            ]}
            rowActions={(r) => [
              { id: 'delete', label: 'Delete redirect…', danger: true, onSelect: () => setDeleting(r) },
            ]}
            emptyState={
              <p className="text-muted">
                No redirects yet. They appear here when the address of published content changes.
              </p>
            }
          />
          {query.data && query.data.total > query.data.pageSize && (
            <Pagination
              page={page}
              pageSize={query.data.pageSize}
              total={query.data.total}
              onPageChange={setPage}
            />
          )}
        </>
      )}
      {adding && <AddRedirectDialog onClose={() => setAdding(false)} />}
      <ConfirmDialog
        open={!!deleting}
        onClose={() => setDeleting(null)}
        title={`Delete the redirect from ${deleting?.fromPath ?? ''}?`}
        description="Visitors to the old address will see “page not found” again. This can't be undone."
        confirmLabel="Delete redirect"
        tone="danger"
        onConfirm={async () => {
          await api.delete(`${W}/redirects/${deleting!.id}`);
          toast.success('Redirect deleted', deleting!.fromPath);
          setDeleting(null);
          await client.invalidateQueries({ queryKey: redirectsKey });
        }}
      />
    </div>
  );
}
