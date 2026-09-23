import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Eye, RefreshCw } from 'lucide-react';
import { useEffect, useId, useState, type ComponentType, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Dialog } from '@/components/ui/Dialog';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import { adminErrorMessage, isConflict, mapFieldErrors } from '../shared/errors';

export type ContentKind = 'banners' | 'announcements' | 'faqs' | 'onboarding-steps';

export interface ContentItemBase {
  id: string;
  concurrencyStamp: string;
  updatedAt: string;
}

export interface FormProps<D> {
  draft: D;
  setDraft: (next: D) => void;
  errors: Record<string, string[] | string | undefined>;
}

export interface ContentConfig<T extends ContentItemBase, D> {
  kind: ContentKind;
  /** e.g. "banner" */
  singular: string;
  empty: () => D;
  fromItem: (item: T) => D;
  /** Client validation + request body. */
  toRequest: (draft: D) => { body?: Record<string, unknown>; errors: Record<string, string> };
  /** Request field names, for mapping server errors (case-insensitive). */
  fields: readonly string[];
  codeToField?: Record<string, string>;
  label: (item: T) => string;
  Form: ComponentType<FormProps<D>>;
  Preview: ComponentType<{ draft: D }>;
}

export function contentPath(kind: ContentKind, id?: string): string {
  return `/admin/content/${kind}${id ? `/${id}` : ''}`;
}

export function contentQueryKey(kind: ContentKind) {
  return ['admin', 'content', kind] as const;
}

interface EditorProps<T extends ContentItemBase, D> {
  config: ContentConfig<T, D>;
  /** The item being edited, or null to create. */
  item: T | null;
  open: boolean;
  onClose: () => void;
}

/**
 * Create/edit dialog for a content item with a live preview. Updates send the `concurrencyStamp` last read; a 409
 * (someone else saved first) keeps the user's draft and offers to load the latest version.
 */
export function ContentEditor<T extends ContentItemBase, D>({
  config,
  item,
  open,
  onClose,
}: EditorProps<T, D>) {
  const formId = useId();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<D>(() => (item ? config.fromItem(item) : config.empty()));
  const [stamp, setStamp] = useState<string | undefined>(item?.concurrencyStamp);
  const [clientErrors, setClientErrors] = useState<Record<string, string>>({});
  const [showPreview, setShowPreview] = useState(true);
  const [reloaded, setReloaded] = useState(false);

  const save = useMutation({
    mutationFn: (body: Record<string, unknown>) =>
      item
        ? api.put<T>(contentPath(config.kind, item.id), { ...body, concurrencyStamp: stamp })
        : api.post<T>(contentPath(config.kind), body),
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: contentQueryKey(config.kind) });
      toast.success(item ? 'Changes saved' : `${capitalize(config.singular)} created`, config.label(saved));
      onClose();
    },
  });

  const reload = useMutation({
    mutationFn: () => api.get<T>(contentPath(config.kind, item?.id)),
    onSuccess: (latest) => {
      setDraft(config.fromItem(latest));
      setStamp(latest.concurrencyStamp);
      setReloaded(true);
      save.reset();
      void queryClient.invalidateQueries({ queryKey: contentQueryKey(config.kind) });
    },
    onError: (error) => toast.error('Couldn’t load the latest version', adminErrorMessage(error)),
  });

  useEffect(() => {
    if (!open) return;
    setDraft(item ? config.fromItem(item) : config.empty());
    setStamp(item?.concurrencyStamp);
    setClientErrors({});
    setReloaded(false);
    save.reset();
    // Reset only when the dialog opens for an item.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, item?.id]);

  const isDuplicateKey = isApiError(save.error) && save.error.code === 'content.duplicate_key';
  // Any other 409 on save means the stamp is stale (concurrency.conflict).
  const conflict = isConflict(save.error) && !isDuplicateKey;
  const server = conflict
    ? { fields: {}, form: null }
    : mapFieldErrors(save.error, config.fields, config.codeToField);
  const errors: Record<string, string[] | string | undefined> = { ...server.fields, ...clientErrors };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const { body, errors: found } = config.toRequest(draft);
    setClientErrors(found);
    if (!body || Object.keys(found).length > 0) return;
    save.mutate(body);
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="lg"
      title={item ? `Edit ${config.singular}` : `New ${config.singular}`}
      dismissible={!save.isPending}
      footer={
        <>
          <Button
            variant="ghost"
            leadingIcon={<Eye />}
            aria-pressed={showPreview}
            onClick={() => setShowPreview((v) => !v)}
          >
            {showPreview ? 'Hide preview' : 'Show preview'}
          </Button>
          <Button variant="secondary" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={save.isPending} disabled={conflict}>
            {item ? 'Save changes' : `Create ${config.singular}`}
          </Button>
        </>
      }
    >
      <div className={showPreview ? 'admin-editor admin-editor--preview' : 'admin-editor'}>
        <form id={formId} className="stack" onSubmit={submit} noValidate>
          {conflict && (
            <Alert
              tone="warning"
              role="alert"
              title="Someone else changed this item"
              actions={
                <Button
                  size="sm"
                  variant="secondary"
                  leadingIcon={<RefreshCw />}
                  loading={reload.isPending}
                  onClick={() => reload.mutate()}
                >
                  Load latest version
                </Button>
              }
            >
              Your changes weren’t saved because the item was updated after you opened it. Load the latest
              version, then reapply your edits. (Copy anything you want to keep first — loading replaces this
              form.)
            </Alert>
          )}
          {reloaded && !conflict && (
            <Alert tone="info" role="status">
              Loaded the latest version. Reapply your edits and save again.
            </Alert>
          )}
          {server.form && (
            <Alert tone="danger" role="alert" title={server.form.title}>
              {server.form.details.length > 0 && (
                <ul>
                  {server.form.details.map((d) => (
                    <li key={d}>{d}</li>
                  ))}
                </ul>
              )}
            </Alert>
          )}
          <config.Form draft={draft} setDraft={setDraft} errors={errors} />
        </form>
        {showPreview && (
          <section className="admin-editor__preview" aria-label="Preview">
            <p className="eyebrow">Preview</p>
            <config.Preview draft={draft} />
          </section>
        )}
      </div>
    </Dialog>
  );
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
