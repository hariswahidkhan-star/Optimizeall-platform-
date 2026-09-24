import { Bookmark, Pencil } from 'lucide-react';
import { useState } from 'react';
import { Button, Checkbox, ConfirmDialog, FormField, Input, useToast } from '@/components/ui';
import { FormDialog } from '@/features/agency/billing/components/FormDialog';
import { billingErrorMessage } from '@/features/agency/billing/lib';
import { useDeleteView, useSaveView, useSavedViews, useUpdateView } from '../api/hooks';
import type { SavedView } from '../api/types';

/**
 * Saved list views: apply one, save the current filters (optionally shared with the team), and rename, share, update
 * or remove your own views.
 */
export function SavedViewsBar({
  entity,
  filters,
  onApply,
}: {
  entity: 'contacts' | 'companies' | 'deals';
  filters: Record<string, string | undefined>;
  onApply: (filters: Record<string, string>) => void;
}) {
  const views = useSavedViews(entity);
  const saveView = useSaveView();
  const updateView = useUpdateView(entity);
  const deleteView = useDeleteView(entity);
  const toast = useToast();
  const [viewName, setViewName] = useState('');
  const [shareNew, setShareNew] = useState(false);
  const [editing, setEditing] = useState<SavedView | null>(null);
  const [editName, setEditName] = useState('');
  const [editShared, setEditShared] = useState(false);
  const [replaceFilters, setReplaceFilters] = useState(false);
  const [removing, setRemoving] = useState<SavedView | null>(null);
  const current = Object.fromEntries(Object.entries(filters).filter(([, v]) => v)) as Record<string, string>;

  return (
    <div className="crm-row">
      <div className="crm-actions" aria-label="Saved views" role="group">
        {(views.data ?? []).map((v) => (
          <span key={v.id} className="crm-actions">
            <Button size="sm" variant="secondary" leadingIcon={<Bookmark />} onClick={() => onApply(v.filters)}>
              {v.name}
              {v.shared && <span className="crm-muted"> (shared)</span>}
            </Button>
            {v.mine && (
              <Button
                size="sm"
                variant="ghost"
                leadingIcon={<Pencil />}
                onClick={() => {
                  setEditing(v);
                  setEditName(v.name);
                  setEditShared(v.shared);
                  setReplaceFilters(false);
                }}
              >
                Edit<span className="visually-hidden"> view {v.name}</span>
              </Button>
            )}
          </span>
        ))}
      </div>
      <form
        className="crm-actions"
        onSubmit={async (e) => {
          e.preventDefault();
          if (!viewName.trim()) return;
          try {
            await saveView.mutateAsync({ name: viewName.trim(), entity, filters: current, shared: shareNew });
            setViewName('');
            toast.success('View saved');
          } catch (error) {
            toast.error('Couldn’t save the view', billingErrorMessage(error));
          }
        }}
      >
        <FormField label="Save current filters as" hideLabel>
          <Input size="sm" placeholder="View name" value={viewName} maxLength={100} onChange={(e) => setViewName(e.target.value)} />
        </FormField>
        <Checkbox label="Share with team" checked={shareNew} onChange={(e) => setShareNew(e.target.checked)} />
        <Button size="sm" type="submit" variant="secondary" disabled={!viewName.trim()}>
          Save view
        </Button>
      </form>
      <FormDialog
        open={editing !== null}
        onClose={() => setEditing(null)}
        title="Edit saved view"
        submitLabel="Save"
        canSubmit={!!editName.trim()}
        onSubmit={async () => {
          if (!editing) return;
          await updateView.mutateAsync({ id: editing.id, name: editName.trim(), shared: editShared, filters: replaceFilters ? current : undefined });
          toast.success('View updated');
        }}
      >
        <FormField label="Name" required>
          <Input value={editName} maxLength={100} onChange={(e) => setEditName(e.target.value)} />
        </FormField>
        <Checkbox label="Share with the team" checked={editShared} onChange={(e) => setEditShared(e.target.checked)} />
        <Checkbox label="Replace its filters with the current filters" checked={replaceFilters} onChange={(e) => setReplaceFilters(e.target.checked)} />
        <Button
          variant="ghost"
          onClick={() => {
            setRemoving(editing);
            setEditing(null);
          }}
        >
          Remove this view
        </Button>
      </FormDialog>
      <ConfirmDialog
        open={removing !== null}
        onClose={() => setRemoving(null)}
        title={`Remove the view “${removing?.name ?? ''}”?`}
        description="The records aren’t affected; only the saved filters are removed."
        tone="danger"
        confirmLabel="Remove"
        onConfirm={async () => {
          if (removing) await deleteView.mutateAsync(removing.id);
        }}
      />
    </div>
  );
}
