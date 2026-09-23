import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ExternalLink, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { Alert, Button, ConfirmDialog, DataTable, type DataTableColumn, Drawer, ErrorState, PageHeader, useToast } from '@/components/ui';
import { errorMessage, isApiError } from '@/lib/api/errors';
import { type Errors, toErrors } from './fields';
import '../website.css';

export interface ResourceFormProps<D> {
  draft: D;
  setDraft: (draft: D) => void;
  errors: Errors;
}

export interface ResourcePageProps<Row, Detail, Draft> {
  title: string;
  description: string;
  singular: string;
  queryKey: readonly unknown[];
  list: () => Promise<Row[]>;
  columns: DataTableColumn<Row>[];
  getId: (row: Row) => string;
  rowLabel: (row: Row) => string;
  /** Loads the full record for editing (defaults to the row itself). */
  load?: (row: Row) => Promise<Detail>;
  toDraft: (detail: Detail | null) => Draft;
  save: (draft: Draft, existing: Detail | null) => Promise<unknown>;
  remove?: (row: Row) => Promise<unknown>;
  publicUrl?: (row: Row) => string | null;
  Form: (props: ResourceFormProps<Draft>) => ReactNode;
  actions?: ReactNode;
  emptyText?: string;
}

/**
 * Generic CMS list + editor: a data table of records, a drawer editor for create/edit (server validation errors shown
 * on the fields, 409 conflicts explained), and delete with confirmation.
 */
export function ResourcePage<Row, Detail = Row, Draft = unknown>(props: ResourcePageProps<Row, Detail, Draft>) {
  const { title, description, singular, queryKey, list, columns, getId, rowLabel, load, toDraft, save, remove, publicUrl, Form, actions, emptyText } = props;
  const toast = useToast();
  const client = useQueryClient();
  const query = useQuery({ queryKey, queryFn: list });
  const [editing, setEditing] = useState<{ detail: Detail | null; draft: Draft } | null>(null);
  const [errors, setErrors] = useState<Errors>({});
  const [deleting, setDeleting] = useState<Row | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const open = async (row: Row | null) => {
    setErrors({});
    setLoadError(null);
    if (!row) {
      setEditing({ detail: null, draft: toDraft(null) });
      return;
    }
    try {
      const detail = load ? await load(row) : (row as unknown as Detail);
      setEditing({ detail, draft: toDraft(detail) });
    } catch (e) {
      setLoadError(errorMessage(e));
    }
  };

  const saveMutation = useMutation({
    mutationFn: () => save(editing!.draft, editing!.detail),
    onSuccess: async () => {
      toast.success(editing?.detail ? `${singular} saved` : `${singular} created`);
      setEditing(null);
      await client.invalidateQueries({ queryKey });
    },
    onError: (e) => {
      if (isApiError(e)) setErrors(toErrors(e.errors));
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    saveMutation.mutate();
  };

  const tableColumns: DataTableColumn<Row>[] = [
    ...columns,
    {
      id: 'actions',
      header: <span className="visually-hidden">Actions</span>,
      align: 'right',
      cell: (row) => (
        <div className="cms-row-actions">
          {publicUrl?.(row) && (
            <a href={publicUrl(row)!} target="_blank" rel="noopener noreferrer" className="ui-button ui-button--ghost ui-button--sm">
              <ExternalLink aria-hidden="true" width={16} height={16} />
              <span className="visually-hidden">View {rowLabel(row)} on the site (opens in a new tab)</span>
            </a>
          )}
          <Button size="sm" variant="secondary" leadingIcon={<Pencil />} onClick={() => void open(row)}>
            Edit<span className="visually-hidden"> {rowLabel(row)}</span>
          </Button>
          {remove && (
            <Button size="sm" variant="ghost" leadingIcon={<Trash2 />} onClick={() => setDeleting(row)}>
              <span className="visually-hidden">Delete {rowLabel(row)}</span>
            </Button>
          )}
        </div>
      ),
    },
  ];

  return (
    <div className="cms-page">
      <PageHeader
        title={title}
        description={description}
        actions={
          <>
            {actions}
            <Button leadingIcon={<Plus />} onClick={() => void open(null)}>
              New {singular.toLowerCase()}
            </Button>
          </>
        }
      />
      {loadError && (
        <Alert tone="danger" title="Couldn't open the editor" onDismiss={() => setLoadError(null)}>
          {loadError}
        </Alert>
      )}
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <DataTable
          caption={title}
          columns={tableColumns}
          rows={query.data ?? []}
          getRowId={getId}
          rowLabel={rowLabel}
          loading={query.isLoading}
          emptyState={<p className="text-muted">{emptyText ?? `No ${title.toLowerCase()} yet.`}</p>}
        />
      )}

      <Drawer open={!!editing} onClose={() => setEditing(null)} title={editing?.detail ? `Edit ${singular.toLowerCase()}` : `New ${singular.toLowerCase()}`} side="right" className="cms-drawer">
        {editing && (
          <form className="cms-form" onSubmit={submit} noValidate>
            <Form draft={editing.draft} setDraft={(draft) => setEditing((e) => (e ? { ...e, draft } : e))} errors={errors} />
            {saveMutation.isError && (
              <Alert tone="danger" title={isApiError(saveMutation.error) && saveMutation.error.status === 409 ? 'Conflict' : "Couldn't save"}>
                {errorMessage(saveMutation.error)}
              </Alert>
            )}
            <div className="cms-form__actions">
              <Button type="submit" loading={saveMutation.isPending}>
                Save
              </Button>
              <Button type="button" variant="ghost" onClick={() => setEditing(null)}>
                Cancel
              </Button>
            </div>
          </form>
        )}
      </Drawer>

      {remove && (
        <ConfirmDialog
          open={!!deleting}
          onClose={() => setDeleting(null)}
          title={`Delete ${deleting ? rowLabel(deleting) : ''}?`}
          description="This can't be undone. Consider unpublishing instead."
          confirmLabel="Delete"
          tone="danger"
          onConfirm={async () => {
            await remove(deleting!);
            toast.success(`${singular} deleted`);
            setDeleting(null);
            await client.invalidateQueries({ queryKey });
          }}
        />
      )}
    </div>
  );
}
