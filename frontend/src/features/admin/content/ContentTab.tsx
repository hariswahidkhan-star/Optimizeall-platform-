import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowUpDown, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DataTable, type DataTableColumn } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { FilterBar, type FilterDefinition } from '@/components/ui/FilterBar';
import { Pagination } from '@/components/ui/Pagination';
import { SkeletonText } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import { QueryError } from '../shared/common';
import { adminErrorMessage, toDisplayError } from '../shared/errors';
import { ReorderList } from '../shared/ReorderList';
import {
  ContentEditor,
  contentPath,
  contentQueryKey,
  type ContentConfig,
  type ContentItemBase,
} from './ContentEditor';

export interface ContentTabProps<T extends ContentItemBase, D> {
  config: ContentConfig<T, D>;
  /** Plural label, e.g. "Banners". */
  title: string;
  description: ReactNode;
  columns: DataTableColumn<T>[];
  filters?: FilterDefinition[];
  /** Supports POST …/reorder. */
  reorderable?: boolean;
  renderReorderItem?: (item: T) => ReactNode;
}

const PAGE_SIZE = 25;

export function ContentTab<T extends ContentItemBase, D>({
  config,
  title,
  description,
  columns,
  filters = [],
  reorderable,
  renderReorderItem,
}: ContentTabProps<T, D>) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const [values, setValues] = useState<Record<string, string | undefined>>({});
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<T | null>(null);
  const [creating, setCreating] = useState(false);
  const [deleting, setDeleting] = useState<T | null>(null);
  const [reordering, setReordering] = useState(false);

  const query = { search, ...values, page, pageSize: PAGE_SIZE };
  const list = useQuery({
    queryKey: [...contentQueryKey(config.kind), 'list', query],
    queryFn: ({ signal }) => api.get<PagedResult<T>>(contentPath(config.kind), { query, signal }),
    placeholderData: keepPreviousData,
  });

  // The reorder view needs every item, unfiltered, in the current order.
  const all = useQuery({
    queryKey: [...contentQueryKey(config.kind), 'all'],
    queryFn: ({ signal }) =>
      api.get<PagedResult<T>>(contentPath(config.kind), { query: { page: 1, pageSize: 200 }, signal }),
    enabled: !!reorderable && reordering,
  });

  const reorder = useMutation({
    mutationFn: (ids: string[]) =>
      api.post<{ updated: number }>(`${contentPath(config.kind)}/reorder`, { ids }),
    onSuccess: () => {
      toast.success('Order saved', `${title} will appear in the new order.`);
      return queryClient.invalidateQueries({ queryKey: contentQueryKey(config.kind) });
    },
    onError: (error) => toast.error('Order not saved', adminErrorMessage(error)),
  });

  const remove = useMutation({
    mutationFn: (item: T) => api.delete(contentPath(config.kind, item.id)),
    onSuccess: (_, item) => {
      toast.success(`Deleted`, config.label(item));
      return queryClient.invalidateQueries({ queryKey: contentQueryKey(config.kind) });
    },
  });

  return (
    <div className="stack">
      <Card>
        <CardHeader
          headingLevel={2}
          title={title}
          description={description}
          actions={
            <div className="cluster">
              {reorderable && (
                <Button
                  variant="secondary"
                  leadingIcon={<ArrowUpDown />}
                  aria-pressed={reordering}
                  onClick={() => setReordering((v) => !v)}
                >
                  {reordering ? 'Done reordering' : 'Reorder'}
                </Button>
              )}
              <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
                New {config.singular}
              </Button>
            </div>
          }
        />
        <CardBody className="stack">
          {reordering && reorderable ? (
            all.isPending ? (
              <SkeletonText lines={5} />
            ) : all.isError ? (
              <QueryError error={all.error} onRetry={() => void all.refetch()} headingLevel={3} compact />
            ) : (
              <ReorderList
                label={`${title} order`}
                items={all.data.items}
                getId={(item) => item.id}
                getLabel={config.label}
                renderItem={renderReorderItem ?? ((item) => config.label(item))}
                onSave={(ids) => reorder.mutateAsync(ids).catch(() => undefined)}
                saving={reorder.isPending}
              />
            )
          ) : (
            <>
              <FilterBar
                search={search}
                onSearchChange={(value) => {
                  setSearch(value);
                  setPage(1);
                }}
                searchLabel={`Search ${title.toLowerCase()}`}
                filters={filters}
                values={values}
                onFilterChange={(id, value) => {
                  setValues((v) => ({ ...v, [id]: value }));
                  setPage(1);
                }}
                onReset={() => {
                  setSearch('');
                  setValues({});
                  setPage(1);
                }}
              />
              {list.isError ? (
                <QueryError error={list.error} onRetry={() => void list.refetch()} headingLevel={3} compact />
              ) : (
                <>
                  <DataTable
                    caption={title}
                    columns={columns}
                    rows={list.data?.items ?? []}
                    getRowId={(item) => item.id}
                    loading={list.isPending}
                    rowLabel={config.label}
                    rowActions={(item) => [
                      { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditing(item) },
                      {
                        id: 'delete',
                        label: 'Delete',
                        icon: <Trash2 />,
                        danger: true,
                        onSelect: () => setDeleting(item),
                      },
                    ]}
                    emptyState={
                      <EmptyState
                        compact
                        headingLevel={3}
                        title={`No ${title.toLowerCase()} yet`}
                        description={
                          search || Object.values(values).some(Boolean)
                            ? 'Try clearing the filters.'
                            : undefined
                        }
                        action={
                          <Button leadingIcon={<Plus />} onClick={() => setCreating(true)}>
                            New {config.singular}
                          </Button>
                        }
                      />
                    }
                  />
                  {list.data && list.data.total > PAGE_SIZE && (
                    <Pagination
                      page={page}
                      pageSize={PAGE_SIZE}
                      total={list.data.total}
                      onPageChange={setPage}
                      label={`${title} pages`}
                    />
                  )}
                </>
              )}
            </>
          )}
        </CardBody>
      </Card>

      {/* Mounted only while open so every open starts from a fresh draft. */}
      {creating && (
        <ContentEditor key="new" config={config} item={null} open onClose={() => setCreating(false)} />
      )}
      {editing && (
        <ContentEditor
          key={editing.id}
          config={config}
          item={editing}
          open
          onClose={() => setEditing(null)}
        />
      )}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title={`Delete this ${config.singular}?`}
        description={
          deleting
            ? `“${config.label(deleting)}” will be removed for everyone. This can’t be undone.`
            : undefined
        }
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          try {
            await remove.mutateAsync(deleting);
          } catch (error) {
            throw toDisplayError(error);
          }
        }}
      />
    </div>
  );
}
